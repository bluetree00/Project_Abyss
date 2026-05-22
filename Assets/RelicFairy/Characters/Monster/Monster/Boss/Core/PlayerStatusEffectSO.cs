using UnityEngine;

namespace RelicFairy.Monster
{
public enum StatusEffectType
{
    Freeze,  // 빙결 — 이동·행동 완전 차단 + 이펙트
    Groggy,  // 그로기 — 시야 축소 비네트 (연속 피격 시 시간 연장)
    Slow,    // 슬로우 — 이동속도 배율 감소 + 이펙트
}

/// <summary>
/// 보스 스킬 피격 시 플레이어에게 적용할 상태이상을 정의하는 ScriptableObject.
/// 이펙트 프리팹은 Inspector에서 직접 할당한다.
/// </summary>
[CreateAssetMenu(fileName = "PlayerStatusEffect", menuName = "RelicFairy/Boss/PlayerStatusEffect")]
public class PlayerStatusEffectSO : ScriptableObject
{
    [Header("상태이상 설정")]
    public StatusEffectType effectType;
    [Tooltip("상태이상 지속 시간 (초)")]
    public float duration = 2f;

    [Header("슬로우 전용")]
    [Range(0f, 1f)]
    [Tooltip("이동속도 배율 — 0이면 완전 정지, 1이면 기본 속도")]
    public float slowScale = 0.3f;

    [Header("이펙트")]
    [Tooltip("플레이어에게 부착할 이펙트 프리팹. null이면 이펙트 없음.")]
    [SerializeField] private GameObject _effectPrefab;
    [Tooltip("이펙트 스케일 (1 = 기본 크기)")]
    [SerializeField] private float _effectScale = 1f;

    public GameObject EffectPrefab => _effectPrefab;
    public float      EffectScale  => _effectScale;

    // ── Public API ──────────────────────────────────────────

    /// <summary>플레이어에게 상태이상을 적용한다. 이펙트가 이미 활성 중이면 지속시간만 초기화.</summary>
    public void Apply(PlayerController player)
    {
        if (player == null) return;

        switch (effectType)
        {
            case StatusEffectType.Freeze: player.ApplyFreeze(duration);                break;
            case StatusEffectType.Groggy: player.ApplyThunderGroggy(duration);         break;
            case StatusEffectType.Slow:   player.ApplySlow(slowScale, duration);       break;
        }

        if (_effectPrefab == null) return;

        string markerName = "StatusEffect_" + effectType;
        var existing = player.transform.Find(markerName);
        if (existing != null)
        {
            existing.GetComponent<StatusEffectInstance>()?.Refresh(duration);
        }
        else
        {
            var go = Object.Instantiate(_effectPrefab,
                player.transform.position, Quaternion.identity, player.transform);
            go.name                    = markerName;
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale    = Vector3.one * _effectScale;
            go.AddComponent<StatusEffectInstance>().Init(duration);
        }
    }
}

/// <summary>
/// 상태이상 이펙트 GO에 부착되어 지속시간을 관리한다.
/// Refresh() 호출 시 남은 시간을 초기 duration으로 재설정한다.
/// </summary>
public sealed class StatusEffectInstance : MonoBehaviour
{
    private float _remaining;

    public void Init(float duration)   => _remaining = duration;
    public void Refresh(float duration) => _remaining = duration;

    private void Update()
    {
        _remaining -= Time.deltaTime;
        if (_remaining <= 0f)
            Destroy(gameObject);
    }
}
}
