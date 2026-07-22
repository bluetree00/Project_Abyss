using UnityEngine;

/// <summary>
/// 6속성 → 실제 VFX 프리팹 매핑 데이터. 정적 데이터 SO(런타임 상태 없음).
///
/// 소스: Spells Pack LWRP 세트(Aura=상태 지속, Spell=즉발 버스트).
/// <see cref="ElementVfxPlayer"/>가 Addressable("ElementVfxRegistry")로 1회 로드해 참조하는 프리팹을
/// 풀링 재생한다. 프리팹은 직접 참조라 레지스트리 하나만 Addressable로 두면 번들에 함께 포함된다.
/// </summary>
[CreateAssetMenu(fileName = "ElementVfxRegistry", menuName = "RelicFairy/Combat/Element VFX Registry")]
public sealed class ElementVfxRegistry : ScriptableObject
{
    [System.Serializable]
    private struct Entry
    {
        public RuneElement element;
        [Tooltip("바닥 원반형 오라(Aura_*_LWRP) — 장판(GroundField) 전용")]
        public GameObject statusAura;
        [Tooltip("적 몸에 붙는 상태 이펙트(INab Character Effects) — 화상/독/빙결 등. 비우면 바닥 오라로 폴백")]
        public GameObject statusBody;
        [Tooltip("즉발/버스트 1회 재생")]
        public GameObject burst;
    }

    [SerializeField] private Entry[] entries = new Entry[0];

    /// <summary>바닥 원반형 오라(장판 전용). 없으면 null.</summary>
    public GameObject GetAura(RuneElement element)
    {
        for (int i = 0; i < entries.Length; i++)
            if (entries[i].element == element) return entries[i].statusAura;
        return null;
    }

    /// <summary>적 몸에 붙는 상태 이펙트. 미지정이면 바닥 오라로 폴백(전기 등 몸 이펙트 부재 속성).</summary>
    public GameObject GetStatusBody(RuneElement element)
    {
        for (int i = 0; i < entries.Length; i++)
            if (entries[i].element == element)
                return entries[i].statusBody != null ? entries[i].statusBody : entries[i].statusAura;
        return null;
    }

    /// <summary>속성의 버스트 프리팹. 없으면 null.</summary>
    public GameObject GetBurst(RuneElement element)
    {
        for (int i = 0; i < entries.Length; i++)
            if (entries[i].element == element) return entries[i].burst;
        return null;
    }
}
