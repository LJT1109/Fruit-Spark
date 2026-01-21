using UnityEngine;
using UnityEngine.UI;
using Unity.InferenceEngine;
using System.Collections.Generic;
using System.Linq;

public class PoseEstimator : MonoBehaviour
{
    [Header("Model Settings")]
    [SerializeField] ModelAsset modelAsset;
    [Range(0f, 1f)] public float scoreThreshold = 0.3f;

    [Header("Input Settings")]
    [SerializeField] WebCamController webCamController;
    [SerializeField] RawImage displayImage; 

    [Header("Character Management")]
    [SerializeField] CharacterManager characterManager;

    [Header("Tracking Settings")]
    [SerializeField] float maxMatchingDistance = 0.15f; // Normalized units
    [SerializeField] float bodyTimeout = 0.5f;

    [Header("Smoothing Settings")]
    [SerializeField] float filterMinCutoff = 1.0f;
    [SerializeField] float filterBeta = 5.0f;
    [SerializeField] float filterDCutoff = 1.0f;

    Model runtimeModel;
    Worker worker;

    // Preprocessing resources
    RenderTexture inputRT;
    Texture2D inputTex;
    int[] inputData;

    // Tracking Data Structure
    class TrackedBody
    {
        public int id;
        public Vector2[] smoothedKeypoints = new Vector2[17];
        public float[] keypointScores = new float[17];
        public float lastSeenTime;
        
        public OneEuroFilter[] filters;

        public TrackedBody(int newId, float minCutoff, float beta, float dCutoff)
        {
            id = newId;
            smoothedKeypoints = new Vector2[17];
            keypointScores = new float[17];
            filters = new OneEuroFilter[17];
            for (int i = 0; i < 17; i++)
            {
                filters[i] = new OneEuroFilter(minCutoff, beta, dCutoff);
            }
        }

        public void Update(Vector2[] rawPositions, float[] scores, float time)
        {
            lastSeenTime = time;
            for (int i = 0; i < 17; i++)
            {
                // Only filter if score is good, or keep using last prediction?
                // For simplicity, filter everything, but maybe rely on previous if score is low?
                // Moves tend to be jittery if we update with low score garbage.
                
                keypointScores[i] = scores[i];
                smoothedKeypoints[i] = filters[i].Filter(rawPositions[i], time);
            }
        }
    }

    struct DetectedBody
    {
        public Vector2[] keypoints; // Normalized (x, y)
        public float[] scores;
        public float totalScore;
    }

    List<TrackedBody> trackedBodies = new List<TrackedBody>();
    int nextId = 0;

    void Start()
    {
        if (webCamController == null) webCamController = FindObjectOfType<WebCamController>();
        if (displayImage == null && webCamController != null) displayImage = webCamController.DisplayImage;
        if (characterManager == null) characterManager = FindObjectOfType<CharacterManager>();

        if (modelAsset == null)
        {
            Debug.LogError("ModelAsset is not assigned!");
            return;
        }

        runtimeModel = ModelLoader.Load(modelAsset);
        // We use GPUCompute, but input will be CPU int tensor to satisfy model requirement if needed
        worker = new Worker(runtimeModel, BackendType.GPUCompute);
    }

    void Update()
    {
        if (webCamController == null || webCamController.WebCamTexture == null || !webCamController.WebCamTexture.isPlaying) return;

        // 1. Prepare input data
        int width = 256;
        int height = 256;

        if (inputRT == null) inputRT = new RenderTexture(width, height, 0);
        if (inputTex == null) inputTex = new Texture2D(width, height, TextureFormat.RGB24, false);
        if (inputData == null) inputData = new int[width * height * 3];

        Graphics.Blit(webCamController.WebCamTexture, inputRT);

        RenderTexture.active = inputRT;
        inputTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        inputTex.Apply();
        RenderTexture.active = null;

        Color32[] pixels = inputTex.GetPixels32();

        // Convert to NHWC Int32 [1, H, W, 3]
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Flip Y manually for MoveNet input expectation usually
                int srcIdx = (height - 1 - y) * width + x; 
                Color32 c = pixels[srcIdx];

                int dstPixelIdx = (y * width + x) * 3;
                inputData[dstPixelIdx] = c.r;
                inputData[dstPixelIdx + 1] = c.g;
                inputData[dstPixelIdx + 2] = c.b;
            }
        }

        using Tensor inputTensor = new Tensor<int>(new TensorShape(1, height, width, 3), inputData);
        worker.Schedule(inputTensor);

        using var outputTensor = worker.PeekOutput() as Tensor<float>;
        using var cpuTensor = outputTensor.ReadbackAndClone(); 
        
        float[] results = cpuTensor.DownloadToArray();
        ProcessResults(results);
    }

    void ProcessResults(float[] data)
    {
        // 1. specific layout for MoveNet Multipose [1, 6, 56]
        // 56 floats per person: 17 * 3 (y, x, s) = 51 floats.
        // Indices 51-54: bounding box? 55: body score.

        List<DetectedBody> rawDetections = new List<DetectedBody>();

        for (int i = 0; i < 6; i++) // Model outputs up to 6
        {
            int offset = i * 56;
            if (offset + 55 >= data.Length) break;

            float personScore = data[offset + 55];
            if (personScore < scoreThreshold) continue;

            Vector2[] kpts = new Vector2[17];
            float[] scrs = new float[17];

            // Center of mass calculation for quick matching using weighted average
            Vector2 center = Vector2.zero;
            float totalWeight = 0f;

            for (int k = 0; k < 17; k++)
            {
                int kIdx = offset + k * 3;
                float y = data[kIdx];
                float x = data[kIdx + 1];
                float s = data[kIdx + 2];

                kpts[k] = new Vector2(x, y); // Store as (x, y) normalized
                scrs[k] = s;

                // Simple average or use scores
                if (s > scoreThreshold)
                {
                    center += kpts[k];
                    totalWeight += 1f;
                }
            }

            rawDetections.Add(new DetectedBody
            {
                keypoints = kpts,
                scores = scrs,
                totalScore = personScore
            });
        }

        // 2. Tracking: Match Detections to TrackedBodies
        // Simple Greedy Matching based on Euclidean distance of Keypoints
        
        List<DetectedBody> unmatchedDetections = new List<DetectedBody>(rawDetections);
        HashSet<TrackedBody> matchedTrackers = new HashSet<TrackedBody>();

        // Sort detections by score potentially to match best first? 
        // Or track older bodies first.
        
        // We iterate through existing bodies and try to find closest detection
        foreach (var body in trackedBodies)
        {
            float bestDist = float.MaxValue;
            DetectedBody? bestDet = null;
            int bestDetIdx = -1;

            for(int i = 0; i<unmatchedDetections.Count; i++)
            {
                float dist = CalculateDistance(body, unmatchedDetections[i]);
                if (dist < bestDist && dist < maxMatchingDistance)
                {
                    bestDist = dist;
                    bestDet = unmatchedDetections[i];
                    bestDetIdx = i;
                }
            }

            if (bestDet != null)
            {
                // Match Found
                body.Update(bestDet.Value.keypoints, bestDet.Value.scores, Time.time);
                matchedTrackers.Add(body);
                unmatchedDetections.RemoveAt(bestDetIdx);
            }
        }

        // 3. Create new trackers for unmatched detections (if slots available)
        // We limit to 5 tracked characters to match the scene pool
        int activeCount = trackedBodies.Count(b => Time.time - b.lastSeenTime < bodyTimeout);
        
        foreach (var det in unmatchedDetections)
        {
            if (trackedBodies.Count < 5) // Should strict limit? Or allowed to track more but only show 5?
            {
                // Create new
                TrackedBody newBody = new TrackedBody(nextId++, filterMinCutoff, filterBeta, filterDCutoff);
                newBody.Update(det.keypoints, det.scores, Time.time);
                trackedBodies.Add(newBody);
                matchedTrackers.Add(newBody);
            }
            else
            {
                // Try to replace an old/lost tracker?
                // For now, ignore new people if full
            }
        }

        // 4. Prune old trackers
        for (int i = trackedBodies.Count - 1; i >= 0; i--)
        {
            if (Time.time - trackedBodies[i].lastSeenTime > bodyTimeout)
            {
                trackedBodies.RemoveAt(i);
            }
        }

        // 5. Update Character Manager
        HashSet<int> activeIds = new HashSet<int>();
        foreach(var body in trackedBodies)
        {
            if (characterManager != null)
            {
                characterManager.UpdateCharacterState(body.id, body.smoothedKeypoints, body.keypointScores, scoreThreshold);
            }
            activeIds.Add(body.id);
        }

        if (characterManager != null)
        {
            characterManager.HideInactive(activeIds);
        }
    }

    float CalculateDistance(TrackedBody body, DetectedBody det)
    {
        // Average distance between valid keypoints
        float distSum = 0f;
        int count = 0;

        for(int i=0; i<17; i++)
        {
            // If body has a recently valid point index, compare
            // For simplicity, just compare all points (body.smoothedKeypoints should be valid)
            // Or better: use hips/shoulders for tracking (indices 5,6,11,12) for stability
            
            // Using all 17 for robustness
            float d = Vector2.Distance(body.smoothedKeypoints[i], det.keypoints[i]);
            distSum += d;
            count++;
        }

        return count > 0 ? distSum / count : float.MaxValue;
    }

    void OnDisable()
    {
        worker?.Dispose();
        if (inputRT) Destroy(inputRT);
        if (inputTex) Destroy(inputTex);
    }
}
