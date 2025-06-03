using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IMonsterAIController
{
    void Initialize();
    void Tick();  // 매 프레임 실행
}
