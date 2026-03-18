using System;
using System.Collections.Generic;

[Serializable]
public class RoomDataRoot
{
    public List<RoomData> rooms;
}

[Serializable]
public class RoomData
{
    public string roomId;
    public string name;
    public string category;   // "Combat"/"Battle"/"Event"/"Shop"/...
    public int difficulty;
    public int weight;
    public string prefab;     // Addressables Key
    public List<string> tags;
}
