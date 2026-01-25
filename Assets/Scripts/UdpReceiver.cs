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

    void UpdateSkeleton()
    {
        if (latestPosePacket == null) return;
        // 範例：處理最新收到的資料
        // Debug.Log($"Received data for {latestPosePacket.people.Count} people");
    }

    void OnApplicationQuit()
    {
        isRunning = false;
        if (client != null) client.Close();
        if (receiveThread != null) receiveThread.Abort();
    }
}
