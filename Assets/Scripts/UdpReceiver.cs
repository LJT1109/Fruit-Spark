using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;

public class UdpReceiver : MonoBehaviour
{
    [Header("Network Settings")]
    public int port = 5005;

    [Header("Movement Settings")]
    public float xMultiplier = 10.0f; // Scale usage of normalized center (0-1) to Unity world units
    public float xOffset = 0.0f;      // Offset to center the movement
    public bool flipX = true;         // Flip horizontal direction
    public float moveSpeed = 5.0f;    // Smoothing speed

    [Header("Face Settings")]
    [Range(0.1f, 3.0f)]
    public float faceScale = 1.0f; // Scale factor for the face rect (1.0 = original, <1 = zoom in, >1 = zoom out)

    private Thread receiveThread;
    private UdpClient client;
    private bool isRunning = true;

    // 用於線程安全地存放最新資料
    private byte[] lastPacketData = null;
    private readonly object lockObject = new object();
    private bool newDataAvailable = false;

    public PosePacket latestPosePacket;

    void Start()
    {
        latestPosePacket = new PosePacket();
        latestPosePacket.people = new List<PersonData>();

        receiveThread = new Thread(new ThreadStart(ReceiveData));
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

    private void ReceiveData()
    {
        try 
        {
            client = new UdpClient(port);
            IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, port);
            Debug.Log($"UDP Receiver started on port {port}");

            while (isRunning)
            {
                try
                {
                    byte[] data = client.Receive(ref anyIP);

                    lock (lockObject)
                    {
                        lastPacketData = data;
                        newDataAvailable = true;
                    }
                }
                catch (SocketException e)
                {
                    if (isRunning) Debug.LogError($"UDP Receive Socket Error: {e}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"UDP Receive Error: {e}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to start UDP Client: {e}");
        }
    }

    void Update()
    {
        if (newDataAvailable)
        {
            byte[] dataToProcess = null;
            lock (lockObject)
            {
                dataToProcess = lastPacketData;
                newDataAvailable = false;
            }

            if (dataToProcess != null)
            {
                ParseBinaryPacket(dataToProcess);
                UpdateSkeleton();
            }
        }
    }

    private void ParseBinaryPacket(byte[] data)
    {
        try
        {
            using (var stream = new System.IO.MemoryStream(data))
            using (var reader = new System.IO.BinaryReader(stream))
            {
                int numPeople = reader.ReadInt32();
                
                // Reuse list or clear it
                latestPosePacket.people.Clear();

                for (int i = 0; i < numPeople; i++)
                {
                    PersonData person = new PersonData();
                    person.id = reader.ReadInt32();
                    float cx = reader.ReadSingle();
                    float cy = reader.ReadSingle();
                    person.center = new float[] { cx, cy };
                    
                    // Read Face Rect (x, y, w, h)
                    float fx = reader.ReadSingle();
                    float fy = reader.ReadSingle();
                    float fw = reader.ReadSingle();
                    float fh = reader.ReadSingle();
                    person.faceRect = new float[] { fx, fy, fw, fh };

                    // Read Shoulder Center (sx, sy, sv)
                    float sx = reader.ReadSingle();
                    float sy = reader.ReadSingle();
                    float sv = reader.ReadSingle();
                    person.shoulderCenter = new float[] { sx, sy };
                    person.shoulderVisibility = sv;
                    
                    int numLandmarks = reader.ReadInt32();
                    person.landmarks_3d = new List<Landmark>(numLandmarks);

                    for (int j = 0; j < numLandmarks; j++)
                    {
                        Landmark lm = new Landmark();
                        lm.x = reader.ReadSingle();
                        lm.y = reader.ReadSingle();
                        lm.z = reader.ReadSingle();
                        lm.visibility = reader.ReadSingle();
                        person.landmarks_3d.Add(lm);
                    }
                    latestPosePacket.people.Add(person);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Binary Parse Error: {e}");
        }
    }

    [System.Serializable]
    public class CharacterBinding
    {
        public GameObject characterRoot;
        public Renderer faceRenderer;
        public int materialIndex = 0;
    }

    [Header("Visualization")]
    public List<CharacterBinding> characterBindings;

    void UpdateSkeleton()
    {
        if (latestPosePacket == null || latestPosePacket.people == null) return;

        int peopleCount = latestPosePacket.people.Count;

        if (characterBindings != null)
        {
            for (int i = 0; i < characterBindings.Count; i++)
            {
                var binding = characterBindings[i];
                if (binding != null && binding.characterRoot != null)
                {
                    bool shouldActive = i < peopleCount;
                    if (binding.characterRoot.activeSelf != shouldActive)
                    {
                        binding.characterRoot.SetActive(shouldActive);
                    }

                    if (shouldActive)
                    {
                        PersonData person = latestPosePacket.people[i];
                        
                        // 1. Update Position based on Center
                        // 1. Update Position based on Priority: Shoulders > Face > Center
                        // Default to Generic Center
                        float targetNormalizedX = 0.5f;
                        bool hasValidX = false;

                        if (person.center != null && person.center.Length >= 2)
                        {
                            targetNormalizedX = person.center[0];
                            hasValidX = true;
                        }

                        // Try Face Center
                        if (person.faceRect != null && person.faceRect.Length >= 4 && person.faceRect[2] > 0)
                        {
                            // faceRect = [x, y, w, h]
                            // Face Center X = x + w/2
                            float faceCenterX = person.faceRect[0] + person.faceRect[2] * 0.5f;
                            targetNormalizedX = faceCenterX;
                            hasValidX = true;
                        }

                        // Try Shoulder Center (Highest Priority as per request)
                        // Using a visibility threshold, e.g., 0.5
                        if (person.shoulderCenter != null && person.shoulderCenter.Length >= 2 && person.shoulderVisibility > 0.5f)
                        {
                            targetNormalizedX = person.shoulderCenter[0];
                            hasValidX = true;
                        }

                        if (hasValidX)
                        {
                            float centered = targetNormalizedX - 0.5f;
                            if (flipX) centered = -centered;
                            float targetX = (centered * xMultiplier) + xOffset;

                            Vector3 currentPos = binding.characterRoot.transform.position;
                            Vector3 targetPos = new Vector3(targetX, currentPos.y, currentPos.z);
                            binding.characterRoot.transform.position = Vector3.Lerp(currentPos, targetPos, Time.deltaTime * moveSpeed);
                        }

                        // 2. Update Face Texture
                        if (binding.faceRenderer != null && person.faceRect != null && person.faceRect.Length >= 4)
                        {
                            WebCamTexture webcamTex = WebcamSender.Instance ? WebcamSender.Instance.GetWebCamTexture() : null;
                            if (webcamTex != null)
                            {
                                Material faceMat = binding.faceRenderer.material; // Automatically instances
                                if (faceMat.mainTexture != webcamTex)
                                {
                                    faceMat.mainTexture = webcamTex;
                                }

                                float fx = person.faceRect[0];
                                float fy = person.faceRect[1];
                                float fw = person.faceRect[2];
                                float fh = person.faceRect[3];

                                if (fw > 0 && fh > 0)
                                {
                                    // Unity UV (0,0) is bottom-left. CV (0,0) is top-left.
                                    
                                    // Apply Scale (Zoom)
                                    // Current Center
                                    float fcx = fx + fw * 0.5f;
                                    float fcy = fy + fh * 0.5f;

                                    // New Width/Height based on scale
                                    // If we want to "Zoom In" to the face on the mesh, we actually need to taking a SMALLER chunk of the webcam image.
                                    // So Scale < 1.0 means crop smaller area (Zoom in), Scale > 1.0 means crop larger area (Zoom out).
                                    // Wait, usually users think: "Make the face bigger on screen". 
                                    // But here we are defining UVs. 
                                    // If we want the face to appear bigger, we probably can't change the mesh size here easily without scaling the object.
                                    // The request says "adjust face rect".
                                    // Let's assume:
                                    // faceScale 1.2 means -> Capture 20% MORE area around the face (Zoom Out / Show more context)
                                    // faceScale 0.8 means -> Capture 20% LESS area (Zoom In / Close up)
                                    
                                    float newW = fw * faceScale;
                                    float newH = fh * faceScale;
                                    
                                    // Recalculate Top-Left (fx, fy) based on new size and center
                                    float newFx = fcx - newW * 0.5f;
                                    float newFy = fcy - newH * 0.5f;

                                    // Clamp to 0..1 to avoid repeating texture (optional, but safe)
                                    // newFx = Mathf.Clamp01(newFx);
                                    // newFy = Mathf.Clamp01(newFy);
                                    // newW = Mathf.Clamp(newW, 0, 1 - newFx);
                                    // newH = Mathf.Clamp(newH, 0, 1 - newFy);

                                    // Tiling = (w, h)
                                    // Offset:
                                    // X = fx
                                    // Y = 1.0 - (fy + fh)  (Since fy is top, fy+fh is bottom edge in CV coords)
                                    
                                    faceMat.mainTextureScale = new Vector2(newW, newH);
                                    faceMat.mainTextureOffset = new Vector2(newFx, 1.0f - (newFy + newH));
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    void OnApplicationQuit()
    {
        isRunning = false;
        if (client != null) client.Close();
        if (receiveThread != null) receiveThread.Abort();
    }
}
