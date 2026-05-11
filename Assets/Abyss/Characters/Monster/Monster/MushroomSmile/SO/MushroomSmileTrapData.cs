using Abyss.Monster;
using UnityEngine;

/// <summary>
/// 머쉬룸스마일 자폭 트랩 특수 상태 데이터 SO.
/// 플레이어 접근 시 폭발 애니메이션을 재생하고 범위 데미지 후 즉시 사망한다.
/// MushroomSmileConfig 의 specialStates 리스트에 추가한다.
/// </summary>
[CreateAssetMenu(fileName = "MushroomSmileTrapData", menuName = "Lee/Monster/Special/MushroomSmileTrapData")]
public class MushroomSmileTrapData : SpecialStateDataBase
{
    [Tooltip("식물 위장 중 재생할 애니메이션 스테이트 이름")]
    public string idleStateName = "Mushroom_IdlePlant";

    [Tooltip("폭발 연출 애니메이션 스테이트 이름")]
    public string burstStateName = "Mushroom_Attack01Smile";

    [Tooltip("전투 개시까지의 감지 거리 (m)")]
    public float activateRange = 2.5f;

    [Tooltip("폭발 범위 반경 (m)")]
    public float burstRadius = 4.0f;

    [Tooltip("폭발 데미지")]
    public float burstDamage = 25f;

    [Tooltip("애니메이션 시작 후 실제 데미지 적용까지의 딜레이 (초)")]
    public float burstDelay = 0.4f;

    [Tooltip("폭발 시 재생할 이펙트 프리팹 (null이면 생략)")]
    public GameObject explosionEffectPrefab;

    [Tooltip("폭발 이펙트 스케일 배율")]
    public float explosionEffectScale = 1f;

    public override SpecialStateBase CreateState() => new MushroomSmileTrapBurstState(this);
}
