using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 슬라임 HP 회복 특수 상태 데이터 SO.
/// SlimeConfig 의 specialStates 리스트에 추가한다.
/// </summary>
[CreateAssetMenu(fileName = "SlimeRegenData", menuName = "Lee/Monster/Special/SlimeRegenData")]
public class SlimeRegenData : SpecialStateDataBase
{
    [Tooltip("회복 상태 발동 주기 (초)")]
    public float interval   = 20f;

    [Tooltip("회복 지속 시간 (초)")]
    public float duration   = 3f;

    [Tooltip("초당 회복량 (HP)")]
    public int   healPerSec = 5;

    public override SpecialStateBase CreateState() => new SlimeRegenState(this);
}
