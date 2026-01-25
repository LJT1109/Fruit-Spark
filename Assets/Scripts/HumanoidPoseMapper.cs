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
    private Transform hips;
    private Transform root; // Character root
    private Vector3 initialHipsPosition;

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
        
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        if (hips) initialHipsPosition = hips.position;
        root = transform;

        // 2. Capture Initial State (AFTER defining mappings)
        CaptureInitialState();
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
            ApplyPose(person);
        }
    }

    void ApplyPose(PersonData person)
    {
        // 1. Smooth Landmarks
        Vector3[] smoothedLandmarks = new Vector3[33];
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
             // Simple approach: Map MP Hip Center Delta to Unity Hip Position
             // Or just set it if we trust the floor level (MP Y=0 is sometimes mid-body)
             // Let's try direct assignment relative to initial offset
             hips.position = initialHipsPosition + targetHipsPos;
        }

        // 3. Map Rotations
        foreach (var map in mappings)
        {
            Transform bone = animator.GetBoneTransform(map.bone);
            if (bone == null) continue;

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
