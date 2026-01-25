using UnityEngine;
using System.Collections.Generic;

public class HumanoidPoseMapper : MonoBehaviour
{
    [Header("References")]
    public UdpReceiver udpReceiver;
    public Animator animator;

    [Header("Settings")]
    public int targetPersonId = 0; // Which person to track (0-based index from MP)
    [Range(0.1f, 10f)]
    public float movementScale = 1.0f;
    public bool enableSmoothing = true;
    public bool flipHorizontal = false;
    
    [Header("One Euro Filter Settings")]
    public float minCutoff = 1.0f;
    public float beta = 0.0f;

    // Internal state
    private Dictionary<int, OneEuroFilter3> landmarkFilters = new Dictionary<int, OneEuroFilter3>();
    private Dictionary<HumanBodyBones, Quaternion> initialRotations = new Dictionary<HumanBodyBones, Quaternion>();
    private Dictionary<HumanBodyBones, Vector3> initialBoneDirections = new Dictionary<HumanBodyBones, Vector3>();
    private Vector3 initialShoulderRight; // Vector from L Shoulder to R Shoulder
    private Transform hips;
    private Transform root; // Character root
    private Vector3 initialHipsPosition;
    
    [Header("Orientation Adjustment")]
    public Vector3 hipsRotationOffset = new Vector3(0, 90, 0); // User suggested 90 deg offset logic

    [Header("Visibility Control")]
    [Range(0f, 1f)]
    public float minLandmarkVisibility = 0.5f;
    public float autoHideTimeout = 0.5f;

    private float lastDataTime;
    private bool isVisible = true;
    private Renderer[] childRenderers;

    // Mapping definition: Bone -> (StartLandmark, EndLandmark)
    // MediaPipe Landmark IDs:
    // 11-12: Shoulders (L, R)
    // 13-14: Elbows (L, R)
    // 15-16: Wrists (L, R)
    // 23-24: Hips (L, R)
    // 25-26: Knees (L, R)
    // 27-28: Ankles (L, R)
    private struct BoneMapping
    {
        public HumanBodyBones bone;
        public int startIdx;
        public int endIdx;
        public HumanBodyBones? childBone; // For direction calculation if needed

        public BoneMapping(HumanBodyBones b, int s, int e, HumanBodyBones? child = null)
        {
            bone = b; startIdx = s; endIdx = e; childBone = child;
        }
    }

    private List<BoneMapping> mappings = new List<BoneMapping>();

    void Start()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (udpReceiver == null) udpReceiver = FindObjectOfType<UdpReceiver>();

        // 1. Define Mappings (Include child bones for direction calculation)
        
        // Right Arm (Unity) <- Left Arm (MP) (Note: Code uses LeftUpperArm, mapping to MP Left 11-13. This is NOT mirroring Left-to-Right)
        mappings.Add(new BoneMapping(HumanBodyBones.RightUpperArm, 11, 13, HumanBodyBones.RightLowerArm));
        mappings.Add(new BoneMapping(HumanBodyBones.RightLowerArm, 13, 15, HumanBodyBones.RightHand));
        
        // Left Arm (Unity) <- Right Arm (MP)
        mappings.Add(new BoneMapping(HumanBodyBones.LeftUpperArm, 12, 14, HumanBodyBones.LeftLowerArm));
        mappings.Add(new BoneMapping(HumanBodyBones.LeftLowerArm, 14, 16, HumanBodyBones.LeftHand));

        // Right Leg (Unity) <- Left Leg (MP)
        mappings.Add(new BoneMapping(HumanBodyBones.RightUpperLeg, 23, 25, HumanBodyBones.RightLowerLeg));
        mappings.Add(new BoneMapping(HumanBodyBones.RightLowerLeg, 25, 27, HumanBodyBones.RightFoot));

        // Left Leg (Unity) <- Right Leg (MP)
        mappings.Add(new BoneMapping(HumanBodyBones.LeftUpperLeg, 24, 26, HumanBodyBones.LeftLowerLeg));
        mappings.Add(new BoneMapping(HumanBodyBones.LeftLowerLeg, 26, 28, HumanBodyBones.LeftFoot));

        // --- New Mappings for Torso & Head ---
        // Virtual Landmarks: 33 = MidShoulder, 34 = MidHip
        // Spine: MidShoulder -> MidHip (Controls Uprightness)
        // Note: Unity Spine is usually lower, Chest is upper. Let's map Spine.
        mappings.Add(new BoneMapping(HumanBodyBones.Spine, 34, 33, HumanBodyBones.Chest)); 
        
        // Head: Nose -> MidShoulder (Controls Head direction relative to body)
        // Or better: MidShoulder -> Nose
        mappings.Add(new BoneMapping(HumanBodyBones.Head, 33, 0)); 

        // Hips (Rotation): Left Hip -> Right Hip (Controls Pelvis rotation)
        // MidHip is position, but rotation comes from the hip-to-hip vector.
        // We will handle Hips rotation MANUALLY in ApplyPose to use Shoulder/Hip fusion or specific logic.
        // mappings.Add(new BoneMapping(HumanBodyBones.Hips, 23, 24)); // Removed, doing manual
        
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        if (hips) initialHipsPosition = hips.position;
        root = transform;

        // 2. Capture Initial State (AFTER defining mappings)
        CaptureInitialState();
        
        childRenderers = GetComponentsInChildren<Renderer>();
        lastDataTime = Time.time;
        SetVisibility(true);
    }

    void CaptureInitialState()
    {
        // Capture Rotations
        foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
        {
            if (bone == HumanBodyBones.LastBone) continue;

            Transform t = animator.GetBoneTransform(bone);
            if (t != null)
            {
                initialRotations[bone] = t.rotation;
            }
        }

        // Capture Directions for mapped bones
        foreach (var map in mappings)
        {
            if (map.childBone.HasValue)
            {
                Transform parent = animator.GetBoneTransform(map.bone);
                Transform child = animator.GetBoneTransform(map.childBone.Value);

                if (parent != null && child != null)
                {
                    // Direction vector in World Space at Start
                    Vector3 dir = (child.position - parent.position).normalized;
                    initialBoneDirections[map.bone] = dir;
                }
            }
        }
        
        // Capture Initial Shoulder Right Vector
        Transform lArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform rArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        if (lArm && rArm) {
            initialShoulderRight = (rArm.position - lArm.position).normalized;
        } else {
             initialShoulderRight = Vector3.right; // Fallback
        }
    }

    void Update()
    {
        if (udpReceiver == null || udpReceiver.latestPosePacket == null) return;

        // Find the person
        PersonData person = null;
        foreach (var p in udpReceiver.latestPosePacket.people)
        {
            if (p.id == targetPersonId) // Simple ID matching
            {
                person = p;
                break;
            }
        }
        
        // If not found by ID, maybe take the first one? 
        if (person == null && udpReceiver.latestPosePacket.people.Count > 0)
        {
            person = udpReceiver.latestPosePacket.people[0];
        }

        if (person != null)
        {
            lastDataTime = Time.time;
            if (!isVisible) SetVisibility(true);
            ApplyPose(person);
        }
        else
        {
            if (isVisible && Time.time - lastDataTime > autoHideTimeout)
            {
                SetVisibility(false);
            }
        }
    }

    void SetVisibility(bool visible)
    {
        isVisible = visible;
        if (childRenderers != null)
        {
            foreach (var r in childRenderers)
            {
                if (r != null) r.enabled = visible;
            }
        }
    }

    float GetLandmarkVisibility(PersonData person, int index)
    {
        // Handle virtual landmarks
        if (index == 33) // MidShoulder -> Avg(11, 12)
            return Mathf.Min(GetLandmarkVisibility(person, 11), GetLandmarkVisibility(person, 12));
        if (index == 34) // MidHip -> Avg(23, 24)
            return Mathf.Min(GetLandmarkVisibility(person, 23), GetLandmarkVisibility(person, 24));
            
        if (index >= 0 && index < person.landmarks_3d.Count)
            return person.landmarks_3d[index].visibility;
            
        return 0f;
    }

    void ApplyPose(PersonData person)
    {
        // 1. Smooth Landmarks
        // Indexes 0-32 are MP. 33=MidShoulder, 34=MidHip
        Vector3[] smoothedLandmarks = new Vector3[35]; 
        for (int i = 0; i < person.landmarks_3d.Count && i < 33; i++)
        {
            float xVal = person.landmarks_3d[i].x;
            if (flipHorizontal) xVal = -xVal;

            Vector3 rawPos = new Vector3(
                xVal, 
                -person.landmarks_3d[i].y, 
                person.landmarks_3d[i].z
            );

            if (enableSmoothing)
            {
                if (!landmarkFilters.ContainsKey(i))
                {
                    landmarkFilters[i] = new OneEuroFilter3(minCutoff, beta);
                }
                // Update params in case they changed in Inspector
                landmarkFilters[i].UpdateParams(minCutoff, beta);
                
                smoothedLandmarks[i] = landmarkFilters[i].Filter(rawPos);
            }
            else
            {
                smoothedLandmarks[i] = rawPos;
            }
        }

        // --- Virtual Landmarks Calculation ---
        // MidShoulder (33) = (LeftShoulder(11) + RightShoulder(12)) / 2
        smoothedLandmarks[33] = (smoothedLandmarks[11] + smoothedLandmarks[12]) * 0.5f;
        
        // MidHip (34) = (LeftHip(23) + RightHip(24)) / 2
        smoothedLandmarks[34] = (smoothedLandmarks[23] + smoothedLandmarks[24]) * 0.5f;

        // 2. Map Body Position (Root Motion)
        // MP coordinates: X (right?), Y (up?), Z (screen?) -> Need to verify convention from Python
        // Python: x=-lm.x (flip), y=lm.y, z=lm.z.
        // Usually MP World Landmarks: Y is up, Z is depth.
        // We calculate Hip center in 3D.
        
        Vector3 hipLeft = smoothedLandmarks[23];
        Vector3 hipRight = smoothedLandmarks[24];
        Vector3 hipCenterMP = (hipLeft + hipRight) * 0.5f;

        // Scale and Offset
        // Assuming character starts at (0,0,0) or we keep it relative
        // For now, let's just apply position relative to initial hips
        // Note: Direct position mapping might be huge if MP scale is different.
        // We use movementScale.
        
        Vector3 targetHipsPos = hipCenterMP * movementScale;
        
        // Adjust for Unity World Space (Optional: might need to swap axes depending on MP output)
        // If Python sends (x, y, z) matching Unity Left-Hand, we just use it.
        // However, usually we want to keep the feet on ground or move relative to start.
        // Let's update Hips LOCAL position or ROOT position?
        // Updating Hips bone directly is better for "in-place" animation, updating Root for world movement.
        
        if (hips != null)
        {
             // Check visibility for Hips Position (23, 24)
             float hipsVis = GetLandmarkVisibility(person, 34); // Virtual MidHip uses 23, 24
             
             if (hipsVis >= minLandmarkVisibility)
             {
                 // Simple approach: Map MP Hip Center Delta to Unity Hip Position
                 // Or just set it if we trust the floor level (MP Y=0 is sometimes mid-body)
                 // Let's try direct assignment relative to initial offset
                 hips.position = initialHipsPosition + targetHipsPos;
             }
             
             
             // --- BODY ORIENTATION CORRECTION (STABLE) ---
             // 1. Calculate Shoulder Vector (Left 11 -> Right 12)
             if (GetLandmarkVisibility(person, 11) >= minLandmarkVisibility && 
                 GetLandmarkVisibility(person, 12) >= minLandmarkVisibility)
             {
                 Vector3 currentShoulderVec = (smoothedLandmarks[12] - smoothedLandmarks[11]).normalized;
                 
                 // 2. Define Up Vector (Lock to World Up to prevent flipping)
                 Vector3 worldUp = Vector3.up;
    
                 // 3. Calculate Forward using Cross Product
                 // Cross(Right, Up) = Forward (Assuming standard Unity coordinates)
                 Vector3 charForward = Vector3.Cross(currentShoulderVec, worldUp).normalized;
    
                 if (charForward.sqrMagnitude > 0.01f)
                 {
                     // 4. Create LookRotation
                     Quaternion targetLookRot = Quaternion.LookRotation(charForward, worldUp);
                     
                     // 5. Apply with Offset
                     // User suggested 90 degrees correction because shoulder vector is horizontal
                    //  hips.rotation = targetLookRot * Quaternion.Euler(hipsRotationOffset); 
                 }
             }
        }

        // 3. Map Rotations
        foreach (var map in mappings)
        {
            Transform bone = animator.GetBoneTransform(map.bone);
            if (bone == null) continue;

            // Check Visibility
            float vStart = GetLandmarkVisibility(person, map.startIdx);
            float vEnd = GetLandmarkVisibility(person, map.endIdx);
            if (vStart < minLandmarkVisibility || vEnd < minLandmarkVisibility) continue;

            Vector3 start = smoothedLandmarks[map.startIdx];
            Vector3 end = smoothedLandmarks[map.endIdx];
            
            // Direction in MP space
            Vector3 targetDir = (end - start).normalized;

            // Apply Rotation
            // We need to know which way the bone points in T-Pose.
            // Typically:
            // Arms point +X (Left) or -X (Right)
            // Legs point -Y
            
            // Instead of hardcoding, we can calculate the INITIAL direction for this bone
            // by looking at the child bone or the mapping definition's intended vector.
            // But we don't have the landmarks for T-Pose at runtime easily unless we assume T-pose.
            
            // Better approach:
            // Calculate the rotation from the INITIAL pose direction to the TARGET direction.
            // This works regardless of whether the avatar started in T-Pose or A-Pose.
            
            if (initialBoneDirections.ContainsKey(map.bone))
            {
                Vector3 initialDir = initialBoneDirections[map.bone];
                
                // Calculate the rotation that takes initialDir to targetDir
                Quaternion rotationFromTo = Quaternion.FromToRotation(initialDir, targetDir);
                
                // Apply this rotation to the initial rotation
                bone.rotation = rotationFromTo * initialRotations[map.bone]; 
            }
        }
    }


}
