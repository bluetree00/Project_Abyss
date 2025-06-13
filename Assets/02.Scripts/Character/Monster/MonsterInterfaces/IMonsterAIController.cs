using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IMonsterAIController
{
    void InitAI(MonsterController monster);
    void TickAI(); // 매 프레임 갱신 (Update 등에서 호출)
    void OnEnterCombat(); // 전투 시작 시
    void OnExitCombat();  // 전투 종료 시
}