using UnityEngine;

public class Settings : MonoBehaviour
{
    public static Settings Instance;

    [Header("Humanoid Pose Mapper")]
    [Range(0.1f, 10f)]
    public float movementScale = 1.0f;

    [Header("UDP Receiver Navigation")]
    public float xMultiplier = 10.0f; // Scale usage of normalized center (0-1) to Unity world units
    public float xOffset = 0.0f;      // Offset to center the movement
    public bool flipX = true;         // Flip horizontal direction
    public float moveSpeed = 5.0f;    // Smoothing speed

    [Header("UDP Receiver Face")]
    [Range(0.1f, 3.0f)]
    public float faceScale = 1.0f; // Scale factor for the face rect

    [Header("Webcam Sender Settings")]
    public string serverIP = "127.0.0.1";
    public int serverPort = 5004;
    public int targetWidth = 640;
    public int targetHeight = 360;
    [Range(0, 100)]
    public int jpegQuality = 50;

    [Header("UI Control")]
    public bool showUI = false;

    void Update()
    {
        // Toggle UI with ESC key
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            showUI = !showUI;
        }
    }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            LoadSettings();
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        // Optional: Keep across scenes
        // DontDestroyOnLoad(gameObject);
    }

    void OnApplicationQuit()
    {
        SaveSettings();
    }

    void LoadSettings()
    {
        movementScale = PlayerPrefs.GetFloat("Settings_movementScale", movementScale);
        xMultiplier = PlayerPrefs.GetFloat("Settings_xMultiplier", xMultiplier);
        xOffset = PlayerPrefs.GetFloat("Settings_xOffset", xOffset);
        flipX = PlayerPrefs.GetInt("Settings_flipX", flipX ? 1 : 0) == 1;
        moveSpeed = PlayerPrefs.GetFloat("Settings_moveSpeed", moveSpeed);
        faceScale = PlayerPrefs.GetFloat("Settings_faceScale", faceScale);
        
        serverIP = PlayerPrefs.GetString("Settings_serverIP", serverIP);
        serverPort = PlayerPrefs.GetInt("Settings_serverPort", serverPort);
        targetWidth = PlayerPrefs.GetInt("Settings_targetWidth", targetWidth);
        targetHeight = PlayerPrefs.GetInt("Settings_targetHeight", targetHeight);
        jpegQuality = PlayerPrefs.GetInt("Settings_jpegQuality", jpegQuality);
    }

    public void SaveSettings()
    {
        PlayerPrefs.SetFloat("Settings_movementScale", movementScale);
        PlayerPrefs.SetFloat("Settings_xMultiplier", xMultiplier);
        PlayerPrefs.SetFloat("Settings_xOffset", xOffset);
        PlayerPrefs.SetInt("Settings_flipX", flipX ? 1 : 0);
        PlayerPrefs.SetFloat("Settings_moveSpeed", moveSpeed);
        PlayerPrefs.SetFloat("Settings_faceScale", faceScale);
        
        PlayerPrefs.SetString("Settings_serverIP", serverIP);
        PlayerPrefs.SetInt("Settings_serverPort", serverPort);
        PlayerPrefs.SetInt("Settings_targetWidth", targetWidth);
        PlayerPrefs.SetInt("Settings_targetHeight", targetHeight);
        PlayerPrefs.SetInt("Settings_jpegQuality", jpegQuality);
        
        PlayerPrefs.Save();
    }

    void OnGUI()
    {
        if (!showUI) return;

        // Define a rect for the settings panel
        // Center the settings panel with margins
        float width = 450;
        float height = Screen.height - 100;
        float x = (Screen.width - width) / 2;
        float y = (Screen.height - height) / 2;

        GUILayout.BeginArea(new Rect(x, y, width, height), "Global Settings", GUI.skin.window);

        GUILayout.Space(10);
        GUILayout.Label("Humanoid Pose Mapper", GUI.skin.box);
        
        GUILayout.Label($"Movement Scale: {movementScale:F2}");
        float newMovementScale = GUILayout.HorizontalSlider(movementScale, 0.1f, 10f);
        if (newMovementScale != movementScale)
        {
            movementScale = newMovementScale;
        }

        GUILayout.Space(20);
        GUILayout.Label("UDP Receiver - Navigation", GUI.skin.box);

        GUILayout.Label($"X Multiplier: {xMultiplier:F2}");
        float newXMultiplier = GUILayout.HorizontalSlider(xMultiplier, 0.0f, 20.0f);
        if (newXMultiplier != xMultiplier)
        {
            xMultiplier = newXMultiplier;
        }

        GUILayout.Label($"X Offset: {xOffset:F2}");
        float newXOffset = GUILayout.HorizontalSlider(xOffset, -10.0f, 10.0f);
        if (newXOffset != xOffset)
        {
            xOffset = newXOffset;
        }

        GUILayout.Label($"Move Speed: {moveSpeed:F2}");
        float newMoveSpeed = GUILayout.HorizontalSlider(moveSpeed, 0.1f, 20.0f);
        if (newMoveSpeed != moveSpeed)
        {
            moveSpeed = newMoveSpeed;
        }

        bool newFlipX = GUILayout.Toggle(flipX, "Flip X");
        if (newFlipX != flipX)
        {
            flipX = newFlipX;
        }

        GUILayout.Space(20);
        GUILayout.Label("UDP Receiver - Face", GUI.skin.box);

        GUILayout.Label($"Face Scale: {faceScale:F2}");
        float newFaceScale = GUILayout.HorizontalSlider(faceScale, 0.1f, 3.0f);
        if (newFaceScale != faceScale)
        {
            faceScale = newFaceScale;
        }

        GUILayout.Space(20);
        GUILayout.Label("Webcam Sender", GUI.skin.box);
        GUILayout.Label("Server IP:");
        string newServerIP = GUILayout.TextField(serverIP);
        if (newServerIP != serverIP)
        {
            serverIP = newServerIP;
        }
        
        GUILayout.Label($"Server Port: {serverPort}");
        string portStr = GUILayout.TextField(serverPort.ToString());
        if (int.TryParse(portStr, out int newPort) && newPort != serverPort)
        {
            serverPort = newPort;
        }

        GUILayout.Label($"JPEG Quality: {jpegQuality}");
        int newJpegQuality = (int)GUILayout.HorizontalSlider(jpegQuality, 0f, 100f);
        if (newJpegQuality != jpegQuality)
        {
            jpegQuality = newJpegQuality;
        }
        
        GUILayout.Label($"Target Resize: {targetWidth}x{targetHeight}");
        // Simple preset buttons for common resolutions
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("640x360")) { targetWidth = 640; targetHeight = 360; }
        if (GUILayout.Button("320x180")) { targetWidth = 320; targetHeight = 180; }
        GUILayout.EndHorizontal();

        GUILayout.Space(20);
        GUILayout.Label("Device Settings", GUI.skin.box);
        
        if (WebcamSender.Instance != null && WebcamSender.Instance.devices != null && WebcamSender.Instance.devices.Length > 0)
        {
            // Canvas Toggle
            if (WebcamSender.Instance.uiCanvas != null)
            {
                 bool isCanvasActive = WebcamSender.Instance.uiCanvas.activeSelf;
                 string btnText = isCanvasActive ? "Hide Canvas Object" : "Show Canvas Object";
                 if (GUILayout.Button(btnText))
                 {
                     WebcamSender.Instance.uiCanvas.SetActive(!isCanvasActive);
                 }
            }

            GUILayout.Space(10);
            GUILayout.Label("Select Camera:");
            string[] camNames = new string[WebcamSender.Instance.devices.Length];
            for (int i = 0; i < camNames.Length; i++) camNames[i] = WebcamSender.Instance.devices[i].name;

            int currentCam = WebcamSender.Instance.currentCameraIndex;
            int newCam = GUILayout.SelectionGrid(currentCam, camNames, 1);
            if (newCam != currentCam)
            {
                WebcamSender.Instance.OnCameraSelected(newCam);
            }
            
            GUILayout.Space(10);
            if (WebcamSender.Instance.availableResolutions != null && WebcamSender.Instance.availableResolutions.Length > 0)
            {
                GUILayout.Label("Select Resolution:");
                string[] resNames = new string[WebcamSender.Instance.availableResolutions.Length];
                for (int i = 0; i < resNames.Length; i++)
                {
                    Resolution r = WebcamSender.Instance.availableResolutions[i];
                    resNames[i] = $"{r.width}x{r.height}@{r.refreshRate}Hz";
                }

                int currentRes = WebcamSender.Instance.currentResolutionIndex;
                int newRes = GUILayout.SelectionGrid(currentRes, resNames, 2); // 2 columns
                if (newRes != currentRes)
                {
                    WebcamSender.Instance.OnResolutionSelected(newRes);
                }
            }
            else
            {
                GUILayout.Label("No resolutions available or using Default");
            }
        }
        else
        {
            GUILayout.Label("WebcamSender not ready or no camera found.");
        }

        GUILayout.Space(20);
        if (GUILayout.Button("Reload Scene"))
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }

        GUILayout.EndArea();

        // Save if any change occurred in this GUI pass
        if (GUI.changed)
        {
             // We can defer this or do it here. 
             // For now, let's rely on OnApplicationQuit, or we can just set dirty flags. 
             // But strictly speaking, OnApplicationQuit covers most cases. 
             // Adding SaveSettings here might be heavy if called every frame while dragging slider.
             // So relying on OnApplicationQuit is better for performance.
        }
    }
}
