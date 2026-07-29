using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 머쉬룸스마일 몬스터.
/// - 기본 상태: PatrolState를 MushroomSmilePatrolState로 오버라이드 → 식물 위장 대기.
/// - 외부 피격: 항상 무적 (TakeDamage 차단). 트랩은 플레이어 공격으로 파괴되지 않음.
/// - 폭발 조건: 플레이어가 activateRange 이내로 접근 시 자폭 → DieState.
/// </summary>
public class MushroomSmileMonster : MonsterBase
{
    public const string PrefabAddress = "MushroomSmile/MushroomSmile";
    protected override string ConfigAddress => "MushroomSmile/MushroomSmileConfig";
    protected override string DataAddress   => string.Empty;

    // 트랩은 HP 바 불필요 — 무적이라 의미 없고 위장 연출에 방해됨
    protected override bool UseWorldHPBar => false;

    protected override void RegisterStates()
    {
        base.RegisterStates();

        // specialStates[0] 에 설정된 MushroomSmileTrapData 를 읽어 PatrolState 교체
        var trapData = _config.specialStates != null && _config.specialStates.Count > 0
            ? _config.specialStates[0].state as MushroomSmileTrapData
            : null;

        if (trapData != null)
            _fsm.RegisterAs<PatrolState>(new MushroomSmilePatrolState(trapData));
    }

    public override void TakeDamage(float amount, GameObject instigator, float knockbackMultiplier = 1f, bool isCrit = false)
    {
        // 트랩은 외부 공격에 파괴되지 않는다 — 완전 무적
    }
}
