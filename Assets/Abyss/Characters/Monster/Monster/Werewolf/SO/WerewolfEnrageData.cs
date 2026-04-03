using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 웨어울프 광폭화 특수 상태 데이터 SO.
/// HP 40% 이하 도달 시 1회 발동 — 짧은 포효 연출 후 영구적으로 속도·공격력 증가 + 붉은 Tint 유지.
/// </summary>
[CreateAssetMenu(fileName = "WerewolfEnrageData", menuName = "Lee/Monster/Special/WerewolfEnrageData")]
public class WerewolfEnrageData : SpecialStateDataBase
{
    [Tooltip("광폭화 발동 HP 비율 (0~1). 이 비율 이하로 떨어지면 1회 발동.")]
    [Range(0f, 1f)]
    public float hpThreshold = 0.4f;

    [Tooltip("광폭화 연출 잠금 시간 (초). 이 시간 동안 이동·피격 불가.")]
    public float lockDuration = 1.5f;

    [Tooltip("광폭화 포효 애니메이션 상태 이름")]
    public string enrageStateName = "Taunting";

    [Tooltip("광폭화 후 영구 이동 속도 배율")]
    public float speedMultiplier = 1.8f;

    [Tooltip("광폭화 후 영구 공격력 배율")]
    public float attackMultiplier = 1.5f;

    public override SpecialStateBase CreateState() => new WerewolfEnrageState(this);
}
