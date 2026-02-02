using UnityEngine;
using UnityEngine.UI;
using System.Net;
using System.Net.Sockets;
using System.Collections;

public class WebcamSender : MonoBehaviour
{
    [Header("UI Settings")]
    public RawImage displayImage; // Assign this in the Inspector
    public Dropdown cameraDropdown; // Assign this in the Inspector
    public Dropdown resolutionDropdown; // Assign this in the Inspector
    public GameObject uiCanvas; // Assign the Canvas GameObject here

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (uiCanvas != null)
            {
                uiCanvas.SetActive(!uiCanvas.activeSelf);
            }
        }
    }

    [Header("Network Settings")]
    public string serverIP = "127.0.0.1";
    public int serverPort = 5004;

    [Header("Video Settings")]
    public int targetWidth = 640;
    public int targetHeight = 480;
    [Range(0, 100)]
    public int jpegQuality = 50;

    public static WebcamSender Instance { get; private set; }

    public WebCamTexture GetWebCamTexture()
    {
        return webCamTexture;
    }

    void Awake()
    {
        Instance = this;
    }
    
    [Header("Capture Settings")]
    public int captureWidth = 1920;
    public int captureHeight = 1080;
    public int captureFPS = 30;

    private WebCamTexture webCamTexture;
    private UdpClient udpClient;
    private Texture2D resizedTexture;
    private RenderTexture renderTexture;
    private IPEndPoint remoteEndPoint;
    
    private const string PREF_CAMERA_NAME = "SelectedCameraName";
    private WebCamDevice[] devices;
    private Resolution[] availableResolutions;

    void Start()
    {
        // Initialize UDP Config
        udpClient = new UdpClient();
        remoteEndPoint = new IPEndPoint(IPAddress.Parse(serverIP), serverPort);

        // Initialize Textures for resizing
        renderTexture = new RenderTexture(targetWidth, targetHeight, 24);
        resizedTexture = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);

        // Setup Camera Dropdown and Start Camera
        InitializeCameraDropdown();

        StartCoroutine(SendFrames());
    }

    void InitializeCameraDropdown()
    {
        devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            Debug.LogError("No webcam devices found.");
            return;
        }

        if (cameraDropdown != null)
        {
            cameraDropdown.ClearOptions();
            System.Collections.Generic.List<string> options = new System.Collections.Generic.List<string>();
            
            int selectedIndex = 0;
            string savedCameraName = PlayerPrefs.GetString(PREF_CAMERA_NAME, "");

            for (int i = 0; i < devices.Length; i++)
            {
                options.Add(devices[i].name);
                if (devices[i].name == savedCameraName)
                {
                    selectedIndex = i;
                }
            }

            cameraDropdown.AddOptions(options);
            cameraDropdown.value = selectedIndex;
            cameraDropdown.onValueChanged.AddListener(OnCameraSelected);
        }

        // Start the camera (either saved or default 0)
        // By setting the value, it triggers the OnValueChanged listener (OnCameraSelected)
        // But if the value is already the same (e.g. 0), it might not trigger. 
        // So we manually call OnCameraSelected to ensure resolutions are populated.
        
        // Find correct index for saved camera
        int indexToSelect = 0;
        if (cameraDropdown != null) indexToSelect = cameraDropdown.value;

        OnCameraSelected(indexToSelect);
    }

    public void OnCameraSelected(int index)
    {
        if (devices == null || index < 0 || index >= devices.Length) return;

        string selectedDeviceName = devices[index].name;
        
        // Save preference
        PlayerPrefs.SetString(PREF_CAMERA_NAME, selectedDeviceName);
        PlayerPrefs.Save();

        // Populate resolutions for this camera
        PopulateResolutions(devices[index]);
    }

    void PopulateResolutions(WebCamDevice device)
    {
        if (resolutionDropdown == null)
        {
             // If no dropdown, just start camera with default/inspector settings
             StartCamera(device.name);
             return;
        }

        resolutionDropdown.ClearOptions();
        availableResolutions = device.availableResolutions;

        if (availableResolutions == null || availableResolutions.Length == 0)
        {
            // Fallback if no resolutions reported
            System.Collections.Generic.List<string> options = new System.Collections.Generic.List<string> { "Default" };
            resolutionDropdown.AddOptions(options);
            resolutionDropdown.interactable = false;
            StartCamera(device.name);
        }
        else
        {
            resolutionDropdown.interactable = true;
            System.Collections.Generic.List<string> options = new System.Collections.Generic.List<string>();
            int bestIndex = 0;
            int maxResolution = 0;

            for (int i = 0; i < availableResolutions.Length; i++)
            {
                Resolution res = availableResolutions[i];
                string optionText = $"{res.width}x{res.height} @ {res.refreshRate}Hz";
                options.Add(optionText);

                // Simple logic to find "highest" resolution (product of width*height)
                if (res.width * res.height > maxResolution)
                {
                    maxResolution = res.width * res.height;
                    bestIndex = i;
                }
            }

            resolutionDropdown.AddOptions(options);
            resolutionDropdown.onValueChanged.RemoveAllListeners(); // Remove previous listeners
            resolutionDropdown.onValueChanged.AddListener(OnResolutionSelected);
            
            // Auto-select highest resolution
            resolutionDropdown.value = bestIndex; 
            resolutionDropdown.RefreshShownValue();
            
            // Trigger selection to update settings and start camera
            OnResolutionSelected(bestIndex);
        }
    }

    public void OnResolutionSelected(int index)
    {
        if (devices == null || cameraDropdown == null) return;
        string deviceName = devices[cameraDropdown.value].name;

        if (availableResolutions != null && index >= 0 && index < availableResolutions.Length)
        {
            Resolution res = availableResolutions[index];
            captureWidth = res.width;
            captureHeight = res.height;
            captureFPS = res.refreshRate;
            Debug.Log($"Resolution set to: {captureWidth}x{captureHeight} @ {captureFPS}");
        }

        StartCamera(deviceName);
    }

    public void StartCamera(string deviceName)
    {
        if (webCamTexture != null)
        {
            webCamTexture.Stop();
        }

        webCamTexture = new WebCamTexture(deviceName, captureWidth, captureHeight, captureFPS);
        
        if (displayImage != null)
        {
            displayImage.texture = webCamTexture;
        }
        
        webCamTexture.Play();
        Debug.Log($"Started camera: {deviceName}");
    }

    IEnumerator SendFrames()
    {
        while (true)
        {
            yield return new WaitForEndOfFrame();

            if (webCamTexture != null && webCamTexture.didUpdateThisFrame)
            {
                ProcessAndSendFrame();
            }
        }
    }

    private byte frameId = 0;
    private const int MAX_PACKET_SIZE = 8192; // Reduced to 8KB to avoid 'Message too long' on some OS/Interfaces

    void ProcessAndSendFrame()
    {
        if (udpClient == null) return;

        // 1. Blit Webcam texture to RenderTexture (Resizing)
        Graphics.Blit(webCamTexture, renderTexture);

        // 2. Read pixels from RenderTexture to Texture2D
        RenderTexture.active = renderTexture;
        resizedTexture.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
        resizedTexture.Apply();
        RenderTexture.active = null;

        // 3. Encode to JPG
        byte[] imageBytes = resizedTexture.EncodeToJPG(jpegQuality);

        // 4. Send via UDP with Fragmentation
        try
        {
            frameId++; // Increment frame ID (overflows 255 -> 0 naturally)
            
            int totalBytes = imageBytes.Length;
            int totalPackets = Mathf.CeilToInt((float)totalBytes / MAX_PACKET_SIZE);

            if (totalPackets > 255)
            {
                Debug.LogError("Frame too large to split (max 255 chunks). Decrease Quality.");
                return;
            }

            for (int i = 0; i < totalPackets; i++)
            {
                int start = i * MAX_PACKET_SIZE;
                int length = Mathf.Min(MAX_PACKET_SIZE, totalBytes - start);
                
                // Packet Structure: [ID] [Index] [Total] [Data...]
                byte[] packet = new byte[length + 3];
                packet[0] = frameId;
                packet[1] = (byte)i;
                packet[2] = (byte)totalPackets;
                
                System.Buffer.BlockCopy(imageBytes, start, packet, 3, length);
                
                udpClient.Send(packet, packet.Length, remoteEndPoint);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"UDP Send Error: {e.Message}");
        }
    }

    void OnDestroy()
    {
        if (webCamTexture != null)
        {
            webCamTexture.Stop();
        }
        if (udpClient != null)
        {
            udpClient.Close();
            udpClient = null;
        }
        if (renderTexture != null)
        {
            renderTexture.Release();
        }
    }
}
