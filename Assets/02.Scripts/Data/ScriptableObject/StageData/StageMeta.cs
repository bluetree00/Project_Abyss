using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StageMeta : MonoBehaviour
{
    public string stageName;
    public StageManager.StageType stageType;
    public int weight; // 초기 값, 이후 MST 생성 시 덮어씀
}

