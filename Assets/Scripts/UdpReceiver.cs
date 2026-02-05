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
    // Moved to Settings.cs
    // public float xMultiplier = 10.0f; 
    // public float xOffset = 0.0f;      
    // public bool flipX = true;         
    // public float moveSpeed = 5.0f;    
    
    [Header("Face Settings")]
    [Range(0.1f, 3.0f)]
    // public float faceScale = 1.0f; // Moved to Settings.cs

    [Header("Stability")]
    public float lostTrackingTimeout = 0.5f; // Time in seconds to keep character active after losing tracking

    private Thread receiveThread;
    private UdpClient client;
    private bool isRunning = true;

    // 用於線程安全地存放最新資料
    private byte[] lastPacketData = null;
    private readonly object lockObject = new object();
    private bool newDataAvailable = false;

    public PosePacket latestPosePacket;
    private float[] lastSeenTimes;


    void Start()
    {
        latestPosePacket = new PosePacket();
        latestPosePacket.people = new List<PersonData>();
        
        if (characterBindings != null)
        {
            lastSeenTimes = new float[characterBindings.Count];
        }


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
                    
                    // Read Size (w, h)
                    float bboxW = reader.ReadSingle();
                    float bboxH = reader.ReadSingle();
                    person.size = new float[] { bboxW, bboxH };
                    
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
        if (characterBindings == null) return;

        // Ensure array size matches if bindings changed via Inspector at runtime (unlikely but safe)
        if (lastSeenTimes == null || lastSeenTimes.Length != characterBindings.Count)
        {
            lastSeenTimes = new float[characterBindings.Count];
        }

        // 1. Process received people (Update positions and refresh lastSeenTime)
        foreach (var person in latestPosePacket.people)
        {
            int id = person.id;
            if (id >= 0 && id < characterBindings.Count)
            {
                var binding = characterBindings[id];
                if (binding != null && binding.characterRoot != null)
                {
                    // Activate if hidden
                    if (!binding.characterRoot.activeSelf)
                    {
                        binding.characterRoot.SetActive(true);
                    }

                    // Update timestamp
                    lastSeenTimes[id] = Time.time;

                    // Update Transforms & Face
                    UpdateCharacter(binding, person);
                }
            }
        }

        // 2. Process timeouts (Hide characters that haven't been seen for a while)
        for (int i = 0; i < characterBindings.Count; i++)
        {
            if (characterBindings[i] != null && characterBindings[i].characterRoot != null)
            {
                // If it's active, check if it should time out
                if (characterBindings[i].characterRoot.activeSelf)
                {
                    if (Time.time - lastSeenTimes[i] > lostTrackingTimeout)
                    {
                        characterBindings[i].characterRoot.SetActive(false);
                    }
                }
            }
        }
    }

    void UpdateCharacter(CharacterBinding binding, PersonData person)
    {
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
            if (Settings.Instance != null ? Settings.Instance.flipX : true) centered = -centered;
            float targetX = (centered * (Settings.Instance != null ? Settings.Instance.xMultiplier : 10f)) + (Settings.Instance != null ? Settings.Instance.xOffset : 0f);

            Vector3 currentPos = binding.characterRoot.transform.position;
            Vector3 targetPos = new Vector3(targetX, currentPos.y, currentPos.z);
            binding.characterRoot.transform.position = Vector3.Lerp(currentPos, targetPos, Time.deltaTime * (Settings.Instance != null ? Settings.Instance.moveSpeed : 5f));
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

                    float newW = fw * (Settings.Instance != null ? Settings.Instance.faceScale : 1f);
                    float newH = fh * (Settings.Instance != null ? Settings.Instance.faceScale : 1f);
                    
                    // Recalculate Top-Left (fx, fy) based on new size and center
                    float newFx = fcx - newW * 0.5f;
                    float newFy = fcy - newH * 0.5f;

                    faceMat.mainTextureScale = new Vector2(newW, newH);
                    faceMat.mainTextureOffset = new Vector2(newFx, 1.0f - (newFy + newH));
                }
            }
        }
    }

    void OnDestroy()
    {
        Cleanup();
    }

    void OnApplicationQuit()
    {
        Cleanup();
    }

    private void Cleanup()
    {
        isRunning = false;
        
        if (client != null)
        {
            client.Close();
            client.Dispose();
            client = null;
        }

        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Abort();
            receiveThread = null;
        }
    }
}
