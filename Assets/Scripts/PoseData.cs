using System;
using System.Collections.Generic;

[Serializable]
public class Landmark
{
    public float x;
    public float y;
    public float z;
    public float visibility;
}

[Serializable]
public class PersonData
{
    public int id;
    public float[] center;
    public float[] size; // [width, height] (normalized 0-1)
    public float[] faceRect; // [x, y, w, h] (normalized 0-1)
    public float[] shoulderCenter; // [x, y] (normalized 0-1)
    public float shoulderVisibility;
    public List<Landmark> landmarks_3d;
}

[Serializable]
public class PosePacket
{
    public List<PersonData> people;
}
