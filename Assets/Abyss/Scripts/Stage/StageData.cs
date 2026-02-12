using System;
using System.Collections.Generic;

[Serializable]
public class StageDataRoot
{
    public List<StageData> stages;
}

[Serializable]
public class StageData
{
    public int stageId;
    public string stageCategory;        // 필요시 사용
    public string normalRoomCategory;   // 필요시 사용
}
