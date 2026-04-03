using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 성난 버섯 포자 폭발 특수 상태 데이터 SO.
/// HP 50% 이하일 때 반복 발동 — 즉시 범위 포자 폭발.
/// </summary>
[CreateAssetMenu(fileName = "MushroomAngrySporeData", menuName = "Lee/Monster/Special/MushroomAngrySporeData")]
public class MushroomAngrySporeData : SpecialStateDataBase
{
    [Tooltip("발동 HP 비율 (0~1). 이 비율 이하일 때 반복 발동.")]
    [Range(0f, 1f)]
    public float hpThreshold = 0.5f;

    [Tooltip("포자 폭발 애니메이션 상태 이름")]
    public string sporeStateName = "Mushroom_Attack03Angry";

    [Tooltip("폭발 연출 지속 시간 (초)")]
    public float sporeDuration = 1.5f;

    [Tooltip("포자 폭발 반경 (m)")]
    public float sporeRadius = 4f;

    [Tooltip("포자 폭발 데미지")]
    public float sporeDamage = 10f;

    [Tooltip("포자 폭발 시 재생할 이펙트 프리팹 (null이면 생략)")]
    public GameObject sporeEffectPrefab;

    public override SpecialStateBase CreateState() => new MushroomAngrySporeBlastState(this);
}
