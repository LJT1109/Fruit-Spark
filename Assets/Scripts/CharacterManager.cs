using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class CharacterManager : MonoBehaviour
{
    [System.Serializable]
    public class CharacterBinding
    {
        public string characterName = "Character";
        public GameObject rootObject;
        public int currentTrackingId = -1;
        
        [Header("Bone Mapping (Optional for Auto-Rig)")]
        // If rootObject has an Animator, we can auto-map. Otherwise, assign manually.
        public Transform nose;
        public Transform leftEye, rightEye;
        public Transform leftEar, rightEar;
        public Transform leftShoulder, rightShoulder;
        public Transform leftElbow, rightElbow;
        public Transform leftWrist, rightWrist;
        public Transform leftHip, rightHip;
        public Transform leftKnee, rightKnee;
        public Transform leftAnkle, rightAnkle;

        // Internal quick-access list
        public Transform[] orderedJoints; 
    }

    [Header("Configuration")]
    [Tooltip("Assign your 5 character models here.")]
    public List<CharacterBinding> characters = new List<CharacterBinding>();

    [Header("Display Settings")]
    [SerializeField] RawImage displayReference; // For coordinate mapping (UI space)
    [SerializeField] float depthScale = 10f; // For simple Z-depth estimation if needed

    private void Start()
    {
        // 1. Validate or Initialize Characters
        if (characters.Count == 0)
        {
            Debug.LogWarning("No characters assigned in CharacterManager. Creating debug primitives.");
            CreateDebugPrimitives();
        }

        // 2. Setup Joints for each character
        foreach (var charBinding in characters)
        {
            SetupCharacter(charBinding);
        }
    }

    private void CreateDebugPrimitives()
    {
        // Fallback: Create 5 simple stick-figure style characters using UI if reference exists
        if (displayReference != null)
        {
            for (int i = 0; i < 5; i++)
            {
                var binding = new CharacterBinding();
                binding.characterName = $"Debug_Char_{i}";
                
                GameObject go = new GameObject(binding.characterName);
                go.transform.SetParent(displayReference.transform, false);
                binding.rootObject = go;
                
                // We won't fully rigor it here to save space, but relying on SetupCharacter to warn/fail gracefully
                // actually, let's just make simple transforms so it doesn't crash
                binding.orderedJoints = new Transform[17];
                for(int j=0; j<17; j++) 
                {
                    GameObject joint = new GameObject($"Joint_{j}");
                    joint.transform.SetParent(go.transform);
                    var img = joint.AddComponent<Image>();
                    img.color = Color.red; 
                    joint.GetComponent<RectTransform>().sizeDelta = new Vector2(10,10);
                    binding.orderedJoints[j] = joint.transform;
                }
                
                characters.Add(binding);
            }
        }
    }

    private void SetupCharacter(CharacterBinding binding)
    {
        if (binding.rootObject == null) return;
        
        // Ensure root is disabled initially
        binding.rootObject.SetActive(false);

        // Array to hold the 17 standard MoveNet keypoints
        binding.orderedJoints = new Transform[17];

        // Try to auto-fill from Animator if available and fields are empty
        Animator anim = binding.rootObject.GetComponent<Animator>();
        if (anim != null && anim.isHuman)
        {
            // Auto-map using Unity HumanBodyBones
            // Note: MoveNet 17 points are specific. We map to closest Humanoid bone.
            binding.orderedJoints[0] = binding.nose ? binding.nose : anim.GetBoneTransform(HumanBodyBones.Head); // Approx
            binding.orderedJoints[1] = binding.leftEye ? binding.leftEye : anim.GetBoneTransform(HumanBodyBones.LeftEye);
            binding.orderedJoints[2] = binding.rightEye ? binding.rightEye : anim.GetBoneTransform(HumanBodyBones.RightEye);
            // Ears are not standard in Humanoid, skip or use head
            binding.orderedJoints[3] = binding.leftEar ? binding.leftEar : anim.GetBoneTransform(HumanBodyBones.Head);
            binding.orderedJoints[4] = binding.rightEar ? binding.rightEar : anim.GetBoneTransform(HumanBodyBones.Head);
            
            binding.orderedJoints[5] = binding.leftShoulder ? binding.leftShoulder : anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            binding.orderedJoints[6] = binding.rightShoulder ? binding.rightShoulder : anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            binding.orderedJoints[7] = binding.leftElbow ? binding.leftElbow : anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            binding.orderedJoints[8] = binding.rightElbow ? binding.rightElbow : anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            binding.orderedJoints[9] = binding.leftWrist ? binding.leftWrist : anim.GetBoneTransform(HumanBodyBones.LeftHand);
            binding.orderedJoints[10] = binding.rightWrist ? binding.rightWrist : anim.GetBoneTransform(HumanBodyBones.RightHand);
            
            binding.orderedJoints[11] = binding.leftHip ? binding.leftHip : anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            binding.orderedJoints[12] = binding.rightHip ? binding.rightHip : anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            binding.orderedJoints[13] = binding.leftKnee ? binding.leftKnee : anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            binding.orderedJoints[14] = binding.rightKnee ? binding.rightKnee : anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            binding.orderedJoints[15] = binding.leftAnkle ? binding.leftAnkle : anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            binding.orderedJoints[16] = binding.rightAnkle ? binding.rightAnkle : anim.GetBoneTransform(HumanBodyBones.RightFoot);
        }
        else
        {
            // Manual Assignment Fallback
            binding.orderedJoints[0] = binding.nose;
            binding.orderedJoints[1] = binding.leftEye;
            binding.orderedJoints[2] = binding.rightEye;
            binding.orderedJoints[3] = binding.leftEar;
            binding.orderedJoints[4] = binding.rightEar;
            binding.orderedJoints[5] = binding.leftShoulder;
            binding.orderedJoints[6] = binding.rightShoulder;
            binding.orderedJoints[7] = binding.leftElbow;
            binding.orderedJoints[8] = binding.rightElbow;
            binding.orderedJoints[9] = binding.leftWrist;
            binding.orderedJoints[10] = binding.rightWrist;
            binding.orderedJoints[11] = binding.leftHip;
            binding.orderedJoints[12] = binding.rightHip;
            binding.orderedJoints[13] = binding.leftKnee;
            binding.orderedJoints[14] = binding.rightKnee;
            binding.orderedJoints[15] = binding.leftAnkle;
            binding.orderedJoints[16] = binding.rightAnkle;
        }
    }

    public void UpdateCharacterState(int id, Vector2[] normalizedPositions, float[] scores, float scoreThreshold)
    {
        // 1. Find assigned character or assign new one
        CharacterBinding target = null;

        // Try to find existing assignment
        foreach (var c in characters)
        {
            if (c.currentTrackingId == id)
            {
                target = c;
                break;
            }
        }

        // If not found, look for free slot
        if (target == null)
        {
            foreach (var c in characters)
            {
                if (c.currentTrackingId == -1) // Free
                {
                    target = c;
                    target.currentTrackingId = id;
                    break;
                }
            }
        }

        if (target == null) return; // No available characters

        // 2. Activate and Update
        if (!target.rootObject.activeSelf) 
            target.rootObject.SetActive(true);

        // Update positions
        RectTransform refRect = displayReference != null ? displayReference.rectTransform : null;
        
        for (int i = 0; i < 17; i++)
        {
            if (i >= target.orderedJoints.Length) break;
            Transform jointT = target.orderedJoints[i];
            
            if (jointT == null) continue;

            float score = scores[i];
            // If score is too low, maybe hide joint? Or just keep last position.
            // For 3D meshes, we can't 'hide' a joint, just ignore update.
            
            if (score >= scoreThreshold)
            {
                Vector2 nPos = normalizedPositions[i];
                
                // Helper to position logic depending on Type (RectTransform vs Transform)
                PositionJoint(jointT, nPos, refRect);
            }
        }
    }

    private void PositionJoint(Transform joint, Vector2 normalizedPos, RectTransform displayRect)
    {
        if (displayRect != null && joint is RectTransform jointRT)
        {
            // UI Overlay Mode
            // MoveNet: (0,0) is TOP-Left usually per typical image standards, but Unity RenderTextures can be flipped.
            // PoseEstimator sends x (0..1), y (0..1).
            // Let's assume PoseEstimator normalized it correctly where (0,0) is Top-Left visually.
            
            float w = displayRect.rect.width;
            float h = displayRect.rect.height;

            // Map 0..1 to -w/2 .. w/2
            float uiX = (normalizedPos.x - 0.5f) * w;
            float uiY = (0.5f - normalizedPos.y) * h; // Inverted Y for UI

            jointRT.anchoredPosition = new Vector2(uiX, uiY);
        }
        else
        {
            // 3D / World Mode
            // Simplest Approach: Map to viewport coordinates of main camera
            Camera cam = Camera.main;
            if (cam != null)
            {
                // y needs to be inverted if 0 is top. Unity Viewport 0 is bottom.
                float viewX = normalizedPos.x;
                float viewY = 1.0f - normalizedPos.y; 
                
                float zDepth = 10f; // Distance from camera
                // If the root object is placed in the scene, maybe maintain its Z?
                // Ideally, user places the character at a specific Z.
                // We'll use the joint's current Z or root's Z.
                
                Vector3 currentPos = joint.position;
                Vector3 targetPoint = cam.ViewportToWorldPoint(new Vector3(viewX, viewY, depthScale));
                
                // Smoothly interact or direct set
                joint.position = targetPoint;
            }
        }
    }

    public void HideInactive(HashSet<int> activeIds)
    {
        foreach (var c in characters)
        {
            if (c.currentTrackingId != -1 && !activeIds.Contains(c.currentTrackingId))
            {
                c.rootObject.SetActive(false);
                c.currentTrackingId = -1;
            }
        }
    }
}
