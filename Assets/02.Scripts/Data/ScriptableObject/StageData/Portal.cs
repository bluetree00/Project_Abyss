using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Portal
{
    public string portalName; // 포탈 이름
    public string currentStage; // 현재 스테이지 이름
    public List<string> connectedStages; // 연결된 스테이지들 (가중치와 관련된 선택 가능)
}
