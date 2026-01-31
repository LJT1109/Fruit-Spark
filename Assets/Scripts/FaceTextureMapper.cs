using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;

[System.Serializable]
public class FaceMapping
{
    public int targetPersonId;
    public Renderer renderer;
}

public class FaceTextureMapper : MonoBehaviour
{
    [Header("References")]
    public UdpReceiver udpReceiver;
    public List<FaceMapping> mappings; // List of mappings (Person ID -> Renderer)
    
    [Header("UI Control")]
    public Dropdown cameraDropdown; // Assign a UI Dropdown if you want runtime control

    [Header("Settings")]
    public string webCamName = ""; // Leave empty for default
    public int cameraIndex = 0;
    public bool useCameraIndex = true; // Priority: Index > Name
    public bool flipVertical = false; // Adjust if face is upside down
    public bool mirrorX = true; // Python detects on mirrored image, so we must invert X to sample from raw Webcam
    public Texture2D defaultTexture; // Fallback image if no face is found
    public Texture2D maskTexture; // Optional mask (e.g., Circle)

    [Header("Debug")]
    public bool debugMode = false;

    // Static reference to share webcam among multiple instances
    private static WebCamTexture sharedWebCam;
    private static int referenceCount = 0;

    void Start()
    {
        if (udpReceiver == null) udpReceiver = FindObjectOfType<UdpReceiver>();
        
        // Initialize Mask for all renderers
        if (maskTexture != null && mappings != null)
        {
            foreach (var mapping in mappings)
            {
                if (mapping.renderer != null)
                {
                    mapping.renderer.material.SetTexture("_MaskTex", maskTexture);
                }
            }
        }

        InitializeWebCam();
    }

    void InitializeWebCam()
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
             Debug.LogError("FaceTextureMapper: No Webcam found!");
             return;
        }

        // Priority: Functionality requested by user
        // Check for OBS camera and set it as default if found
        for (int i = 0; i < devices.Length; i++)
        {
            if (devices[i].name.Contains("OBS"))
            {
                cameraIndex = i;
                useCameraIndex = true;
                break;
            }
        }

        // Setup Dropdown if assigned
        if (cameraDropdown != null)
        {
            cameraDropdown.ClearOptions();
            List<string> options = new List<string>();
            int currentSelection = 0;
            for (int i = 0; i < devices.Length; i++)
            {
                string deviceName = devices[i].name;
                options.Add($"[{i}] {deviceName}");
                
                if (useCameraIndex && i == cameraIndex) currentSelection = i;
                else if (!useCameraIndex && deviceName == webCamName) currentSelection = i;
            }
            cameraDropdown.AddOptions(options);
            cameraDropdown.value = currentSelection;
            cameraDropdown.onValueChanged.AddListener(OnCameraDropdownChanged);
        }

        StartWebCamInternal();
    }
    
    public void OnCameraDropdownChanged(int index)
    {
        cameraIndex = index;
        useCameraIndex = true;
        // Restart Camera
        StopWebCamInternal();
        StartWebCamInternal();
    }

    void StartWebCamInternal()
    {
        if (sharedWebCam != null && sharedWebCam.isPlaying) return; // Already playing? Or perhaps check if device changed.

        // If we want to switch cameras, we must stop the old one first.
        // Since it is static, this affects all instances. 
        // We assume valid logic here for single camera usage across multiple renderers.
        if (sharedWebCam != null) 
        {
             // Check if it's the correct device
             // For simplicity, let's just use what's configured.
        }

        WebCamDevice[] devices = WebCamTexture.devices;
        string deviceName = "";

        if (useCameraIndex)
        {
            if (cameraIndex >= 0 && cameraIndex < devices.Length)
            {
                deviceName = devices[cameraIndex].name;
            }
            else
            {
                Debug.LogWarning($"FaceTextureMapper: Camera Index {cameraIndex} out of range [0, {devices.Length-1}]. Using default.");
                deviceName = devices[0].name;
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(webCamName))
            {
                foreach (var d in devices)
                {
                    if (d.name == webCamName) { deviceName = d.name; break; }
                }
            }
            if (string.IsNullOrEmpty(deviceName)) deviceName = devices[0].name;
        }

        if (sharedWebCam != null && sharedWebCam.deviceName != deviceName)
        {
            sharedWebCam.Stop();
            sharedWebCam = null;
        }

        if (sharedWebCam == null)
        {
            sharedWebCam = new WebCamTexture(deviceName);
        }
        
        if (!sharedWebCam.isPlaying)
            sharedWebCam.Play();
            
        Debug.Log($"FaceTextureMapper: Using webcam: {deviceName}");
        referenceCount++;
    }
    
    void StopWebCamInternal()
    {
         if (sharedWebCam != null)
         {
             sharedWebCam.Stop();
             sharedWebCam = null; 
             // Note: referenceCount logic is tricky with switching. 
             // For simplicity in this mono-manager style, we just manage the static instance directly.
         }
    }


    [ContextMenu("List Cameras")]
    public void ListCameras()
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        Debug.Log($"--- Available Cameras ({devices.Length}) ---");
        for (int i = 0; i < devices.Length; i++)
        {
            Debug.Log($"[{i}] {devices[i].name} (FrontFacing: {devices[i].isFrontFacing})");
        }
    }

    void Update()
    {
        if (mappings == null) return;
        
        // Ensure shared webcam is valid
        bool webcamReady = (sharedWebCam != null && sharedWebCam.isPlaying);

        foreach (var mapping in mappings)
        {
            if (mapping.renderer == null) continue;

            bool hasFace = false;
            PersonData targetPerson = null;

            if (udpReceiver != null && udpReceiver.latestPosePacket != null)
            {
                 foreach (var p in udpReceiver.latestPosePacket.people)
                 {
                     if (p.id == mapping.targetPersonId) 
                     {
                         targetPerson = p;
                         break;
                     }
                 }
            }

            if (targetPerson != null && targetPerson.faceRect != null && targetPerson.faceRect.Length == 4)
            {
                hasFace = true;
            }
            
            if (debugMode && hasFace)
            {
                Debug.Log($"ID {mapping.targetPersonId}: Face Found {targetPerson.faceRect[0]},{targetPerson.faceRect[1]}");
            }

            if (hasFace && webcamReady)
            {
                // 1. Ensure Webcam Texture
                if (mapping.renderer.material.mainTexture != sharedWebCam)
                    mapping.renderer.material.mainTexture = sharedWebCam;

                // 2. Apply Face Rect
                float x = targetPerson.faceRect[0];
                float y = targetPerson.faceRect[1];
                float w = targetPerson.faceRect[2];
                float h = targetPerson.faceRect[3];

                // Handle Mirroring
                if (mirrorX)
                {
                    x = 1.0f - (x + w);
                }

                // --- Aspect Ratio Correction ---
                if (sharedWebCam.width > 0 && sharedWebCam.height > 0)
                {
                    float camW = sharedWebCam.width;
                    float camH = sharedWebCam.height;

                    float pxW = w * camW;
                    float pxH = h * camH;
                    float targetSize = Mathf.Max(pxW, pxH);

                    float newW = targetSize / camW;
                    float newH = targetSize / camH;

                    x -= (newW - w) * 0.5f;
                    y -= (newH - h) * 0.5f;

                    w = newW;
                    h = newH;
                }

                // Unity Y Calculation
                float unityY = 1.0f - (y + h);
                if (flipVertical) unityY = y;

                // Apply
                mapping.renderer.material.mainTextureScale = new Vector2(w, h);
                mapping.renderer.material.mainTextureOffset = new Vector2(x, unityY);
            }
            else
            {
                // Fallback
                if (defaultTexture != null)
                {
                    if (mapping.renderer.material.mainTexture != defaultTexture)
                    {
                        mapping.renderer.material.mainTexture = defaultTexture;
                        mapping.renderer.material.mainTextureScale = new Vector2(1, 1);
                        mapping.renderer.material.mainTextureOffset = new Vector2(0, 0);
                    }
                }
                else
                {
                   if (mapping.renderer.material.mainTexture == sharedWebCam)
                   {
                       mapping.renderer.material.mainTexture = null; 
                   }
                }
            }
        }
    }

    void OnDestroy()
    {
        referenceCount--;
        if (referenceCount <= 0 && sharedWebCam != null)
        {
            sharedWebCam.Stop();
            sharedWebCam = null;
        }
    }
}
