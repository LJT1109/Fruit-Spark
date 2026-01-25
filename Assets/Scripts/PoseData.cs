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
    public List<Landmark> landmarks_3d;
}

[Serializable]
public class PosePacket
{
    public List<PersonData> people;
}
