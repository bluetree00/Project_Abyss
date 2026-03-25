using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 달팽이 중갑 방어 특수 상태 데이터 SO.
/// SnailConfig 의 specialStates 리스트에 추가한다.
/// </summary>
[CreateAssetMenu(fileName = "SnailShellData", menuName = "Lee/Monster/Special/SnailShellData")]
public class SnailShellData : SpecialStateDataBase
{
    [Tooltip("방어 상태 발동 주기 (초)")]
    public float interval          = 15f;

    [Tooltip("방어 지속 시간 (초)")]
    public float duration          = 4f;

    [Tooltip("방어 중 받는 데미지 배율 (0~1). 0.5 = 50% 감소.")]
    [Range(0f, 1f)]
    public float damageMultiplier  = 0.5f;

    public override SpecialStateBase CreateState() => new SnailShellState(this);
}
