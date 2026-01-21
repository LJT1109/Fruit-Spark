using UnityEngine;
using UnityEngine.UI;
using Unity.InferenceEngine;
using System.Collections.Generic;

public class PoseEstimator : MonoBehaviour
{
    [Header("Model Settings")]
    [SerializeField] ModelAsset modelAsset;
    [Range(0f, 1f)] public float scoreThreshold = 0.3f;

    [Header("Input Settings")]
    [SerializeField] WebCamController webCamController;
    [SerializeField] RawImage displayImage; 

    [Header("Visualization Settings")]
    [SerializeField] Color jointColor = Color.red;
    [SerializeField] Color boneColor = Color.green;
    [SerializeField] float jointSize = 10f;
    [SerializeField] float boneWidth = 2f;

    Model runtimeModel;
    Worker worker;

    // Preprocessing resources
    RenderTexture inputRT;
    Texture2D inputTex;
    int[] inputData;

    // Data for 5 people
    class Person
    {
        public GameObject root;
        public RectTransform[] joints = new RectTransform[17];
        public RectTransform[] bones = new RectTransform[16];
    }
    Person[] people = new Person[5];

    readonly int[,] boneConnections = new int[,]
    {
        {0, 1}, {0, 2}, {1, 3}, {2, 4},       
        {5, 6}, {5, 7}, {7, 9},               
        {6, 8}, {8, 10},                      
        {5, 11}, {6, 12},                     
        {11, 12},                             
        {11, 13}, {13, 15},                   
        {12, 14}, {14, 16}                    
    };

    void Start()
    {
        if (webCamController == null) webCamController = FindObjectOfType<WebCamController>();
        if (displayImage == null && webCamController != null) displayImage = webCamController.DisplayImage;

        if (modelAsset == null)
        {
            Debug.LogError("ModelAsset is not assigned!");
            return;
        }

        runtimeModel = ModelLoader.Load(modelAsset);
        // We use GPUCompute, but input will be CPU int tensor to satisfy model requirement
        worker = new Worker(runtimeModel, BackendType.GPUCompute);

        InitializeSkeletons();
    }

    void InitializeSkeletons()
    {
        if (displayImage == null) return;

        for (int i = 0; i < 5; i++)
        {
            var p = new Person();
            GameObject personGO = new GameObject($"Person_{i}");
            personGO.transform.SetParent(displayImage.transform, false);
            
            RectTransform personRT = personGO.AddComponent<RectTransform>();
            personRT.anchorMin = Vector2.zero;
            personRT.anchorMax = Vector2.one;
            personRT.offsetMin = Vector2.zero;
            personRT.offsetMax = Vector2.zero;

            p.root = personGO;

            for (int j = 0; j < 17; j++)
            {
                GameObject joint = new GameObject($"Joint_{j}");
                joint.transform.SetParent(personGO.transform, false);
                
                Image img = joint.AddComponent<Image>();
                img.color = jointColor;
                
                RectTransform rt = joint.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(jointSize, jointSize);
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                
                p.joints[j] = rt;
                joint.SetActive(false);
            }

            int numBones = boneConnections.GetLength(0);
            p.bones = new RectTransform[numBones];
            for (int k = 0; k < numBones; k++)
            {
                GameObject bone = new GameObject($"Bone_{k}");
                bone.transform.SetParent(personGO.transform, false);

                Image img = bone.AddComponent<Image>();
                img.color = boneColor;

                RectTransform rt = bone.GetComponent<RectTransform>();
                rt.pivot = new Vector2(0, 0.5f); 
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);

                p.bones[k] = rt;
                bone.SetActive(false);
            }

            p.root.SetActive(false);
            people[i] = p;
        }
    }

    void Update()
    {
        if (webCamController == null || webCamController.WebCamTexture == null || !webCamController.WebCamTexture.isPlaying) return;

        // 1. Prepare input data on CPU as INT array [0-255]
        int width = 256;
        int height = 256;

        if (inputRT == null) inputRT = new RenderTexture(width, height, 0);
        if (inputTex == null) inputTex = new Texture2D(width, height, TextureFormat.RGB24, false);
        if (inputData == null) inputData = new int[width * height * 3];

        // Blit webcam to scaled RT. This handles resizing.
        Graphics.Blit(webCamController.WebCamTexture, inputRT);

        // Readback
        RenderTexture.active = inputRT;
        inputTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        inputTex.Apply();
        RenderTexture.active = null;

        Color32[] pixels = inputTex.GetPixels32();

        // Convert to NHWC Int32 [1, H, W, 3] which is the required shape (1, d0, d1, 3)
        // Texture2D is bottom-left origin. Model usually top-left. We flip Y.
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Source: bottom-up from standard Unity texture
                // We want row 0 to be the visual top, so we read from height-1-y
                int srcIdx = (height - 1 - y) * width + x; 
                Color32 c = pixels[srcIdx];

                // Dest: NHWC (Batch, Height, Width, Channel)
                // Index = (y * W + x) * 3 + c
                int dstPixelIdx = (y * width + x) * 3;
                
                inputData[dstPixelIdx] = c.r;
                inputData[dstPixelIdx + 1] = c.g;
                inputData[dstPixelIdx + 2] = c.b;
            }
        }

        // Create Tensor with NHWC shape: (1, height, width, 3)
        // MoveNet multipose input is [1, 256, 256, 3] of int32
        using Tensor inputTensor = new Tensor<int>(new TensorShape(1, height, width, 3), inputData);
        
        worker.Schedule(inputTensor);

        using var outputTensor = worker.PeekOutput() as Tensor<float>;
        using var cpuTensor = outputTensor.ReadbackAndClone(); 
        
        float[] results = cpuTensor.DownloadToArray();
        DrawSkeletons(results);
    }

    void DrawSkeletons(float[] data)
    {
        if (displayImage == null) return;
        RectTransform displayRect = displayImage.rectTransform;
        float width = displayRect.rect.width;
        float height = displayRect.rect.height;

        // Output: [1, 6, 56] 
        for (int i = 0; i < 5; i++)
        {
            int offset = i * 56;
            if (offset + 55 >= data.Length) break;

            float personScore = data[offset + 55]; // 56th element

            if (personScore < scoreThreshold)
            {
                people[i].root.SetActive(false);
                continue;
            }

            people[i].root.SetActive(true);

            // Joints
            for (int k = 0; k < 17; k++)
            {
                int kIdx = offset + k * 3;
                float y = data[kIdx];     // Normalized 0..1
                float x = data[kIdx + 1]; // Normalized 0..1
                float s = data[kIdx + 2];

                RectTransform joint = people[i].joints[k];
                if (s < scoreThreshold)
                {
                    joint.gameObject.SetActive(false);
                }
                else
                {
                    joint.gameObject.SetActive(true);
                    
                    // x is 0..1 (left to right)
                    // y is 0..1 (top to bottom usually)
                    // Unity UI (0,0) center.
                    float uiX = (x - 0.5f) * width;
                    float uiY = (0.5f - y) * height; // Invert Y for UI
                    
                    joint.anchoredPosition = new Vector2(uiX, uiY);
                }
            }

            // Bones
            int boneCount = boneConnections.GetLength(0);
            for (int b = 0; b < boneCount; b++)
            {
                int start = boneConnections[b, 0];
                int end = boneConnections[b, 1];
                var j1 = people[i].joints[start];
                var j2 = people[i].joints[end];

                if (j1.gameObject.activeSelf && j2.gameObject.activeSelf)
                {
                    var bone = people[i].bones[b];
                    bone.gameObject.SetActive(true);
                    Vector2 p1 = j1.anchoredPosition;
                    Vector2 p2 = j2.anchoredPosition;
                    Vector2 dir = p2 - p1;
                    bone.sizeDelta = new Vector2(dir.magnitude, boneWidth);
                    bone.anchoredPosition = p1;
                    bone.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
                }
                else
                {
                    people[i].bones[b].gameObject.SetActive(false);
                }
            }
        }
    }

    void OnDisable()
    {
        worker?.Dispose();
        if (inputRT) Destroy(inputRT);
        if (inputTex) Destroy(inputTex);
    }
}
