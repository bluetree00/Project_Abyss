using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 효과 아이콘 아트 슬롯(IconKey → Sprite). 표시 전용 레이어 C단계의 "아트 교체 슬롯".
///
/// ■ 이 SO에 IconKey별 실제 스프라이트를 인스펙터에서 채우면 코드 수정 없이 아이콘이 교체된다.
/// ■ <see cref="EffectIconRegistry"/>가 Addressable 키 "UI/EffectIconSet"로 자동 채택한다
///   (에셋이 없으면 런타임 생성 플레이스홀더로 폴백 — 경고/예외 없음).
/// ■ 키 어휘는 <see cref="EffectMetaRegistry"/>의 IconKey와 동일:
///   dmg/atk/def/hp/speed/atkspeed/crit/critdmg/skill/cooldown/gold/luck/heal/
///   lifesteal/poison/freeze/stun/fire/lightning/shield/projectile/range/roll/
///   element/allstats/utility/special/unknown
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Effect/Effect Icon Set", fileName = "EffectIconSet")]
public sealed class EffectIconSetSO : ScriptableObject
{
    [System.Serializable]
    public struct Entry
    {
        [Tooltip("EffectMetaRegistry의 IconKey (예: atk, def, fire ...)")]
        public string key;
        public Sprite sprite;
    }

    [SerializeField] private List<Entry> entries = new();

    private Dictionary<string, Sprite> _lookup;

    /// <summary>IconKey로 스프라이트 조회. 미정의/null이면 false.</summary>
    public bool TryGet(string iconKey, out Sprite sprite)
    {
        sprite = null;
        if (string.IsNullOrEmpty(iconKey)) return false;

        if (_lookup == null) BuildLookup();

        return _lookup.TryGetValue(iconKey, out sprite) && sprite != null;
    }

    private void BuildLookup()
    {
        _lookup = new Dictionary<string, Sprite>();
        foreach (var e in entries)
        {
            if (string.IsNullOrEmpty(e.key) || e.sprite == null) continue;
            _lookup[e.key.ToLowerInvariant()] = e.sprite;
        }
    }

    private void OnDisable() => _lookup = null;   // 인스펙터 편집 후 재빌드 유도
}
