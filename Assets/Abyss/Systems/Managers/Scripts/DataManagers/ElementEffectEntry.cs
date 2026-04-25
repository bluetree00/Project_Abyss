using System.Collections.Generic;

/// <summary>
/// 뒤끝 ELEMENT_EFFECT_DATA 차트 1행.
/// element 키로 조회 — 각 원소에 1개 효과.
/// </summary>
[System.Serializable]
public class ElementEffectEntry
{
    public string element;              // Fire / Water / Grass / Earth / Lightning
    public string effect_id;           // burn / wet / poison / petrify / shock
    public string effect_type;         // DoT / SlowAndAmplify / PoisonDoT / Petrify / Chain
    public float  duration;            // 효과 지속 시간 (초)
    public float  magnitude_a;         // 주요 수치 (DoT 비율, 슬로우 배율 등)
    public float  magnitude_b;         // 보조 수치 (체인 거리, 슬로우 시 누적 증폭 등)
    public float  tick_interval;       // DoT 틱 간격 (0이면 틱 없음)
    public float  counter_multiplier;  // 상성 유리 원소 피격 시 누적 배율
    public float  reverse_multiplier;  // 상성 불리 원소 피격 시 누적 배율
    public float  same_multiplier;     // 동일 원소 피격 시 누적 배율
    public float  activation_gauge;    // 이 원소의 발동 임계치 (몬스터 max_accumulation 배율과 곱해 사용)
    public string vfx_key;             // VFX Addressable 키
    public string sfx_key;             // SFX 키
    public string description;
    public int    stat_version;
}

[System.Serializable]
public class ElementEffectEntryCollection
{
    public List<ElementEffectEntry> effects;
}
