using UnityEngine;

/// <summary>
/// 6속성 → 실제 VFX 프리팹 매핑 데이터. 정적 데이터 SO(런타임 상태 없음).
///
/// <see cref="ElementVfxPlayer"/>가 Addressable("ElementVfxRegistry")로 1회 로드해 참조하는 프리팹을
/// 풀링 재생한다. 프리팹은 직접 참조라 레지스트리 하나만 Addressable로 두면 번들에 함께 포함된다.
///
/// <para><b>칸은 역할별로 나눈다</b>(10-01). 예전엔 「버스트」 한 칸(버프 방패 문장)을 발동 표시 · 타격 · 광역 폭발에
/// 다 써서 폭발이 폭발로 보이지 않았다. 광역 · 장판 칸은 프리팹의 <b>공칭 반경</b>(배율 1에서 보이는 반경)을 같이 적는다 —
/// 판정 반경 ÷ 공칭 반경이 배율이다(보이는 범위 = 맞는 범위).</para>
/// 채우기: 메뉴 RelicFairy/Debug/속성 이펙트 목록 채우기.
/// </summary>
[CreateAssetMenu(fileName = "ElementVfxRegistry", menuName = "RelicFairy/Combat/Element VFX Registry")]
public sealed class ElementVfxRegistry : ScriptableObject
{
    [System.Serializable]
    private struct Entry
    {
        public RuneElement element;
        [Tooltip("장판(GroundField) 전용 — 바닥에 깔리는 영역 이펙트")]
        public GameObject statusAura;
        [Tooltip("statusAura가 배율 1에서 보이는 반경(m). 장판 반경 ÷ 이 값 = 배율")]
        public float auraRadius;
        [Tooltip("적 몸에 붙는 상태 이펙트 — 화상/독/빙결 등. 비우면 장판 칸으로 폴백")]
        public GameObject statusBody;
        [Tooltip("단계 발동 표시 — 플레이어 몸에 1회(버프 문장)")]
        public GameObject burst;
        [Tooltip("타격 1회 — 맞은 적 자리(체인 · 표식 폭발 등 점 타격)")]
        public GameObject impact;
        [Tooltip("타격 이펙트를 발밑 기준에서 올리는 높이(m) — 구형 폭발이 바닥에 반쯤 박히지 않게")]
        public float impactHeight;
        [Tooltip("광역 폭발 1회 — 반경이 있는 폭발(작열 · 빙하 · 광폭발 등)")]
        public GameObject area;
        [Tooltip("area가 배율 1에서 보이는 반경(m). 판정 반경 ÷ 이 값 = 배율")]
        public float areaRadius;
        [Tooltip("area를 거두는 시각(초). 0이면 기본(2초)")]
        public float areaLife;
    }

    [SerializeField] private Entry[] entries = new Entry[0];

    private bool Find(RuneElement element, out Entry entry)
    {
        for (int i = 0; i < entries.Length; i++)
            if (entries[i].element == element) { entry = entries[i]; return true; }
        entry = default;
        return false;
    }

    /// <summary>장판 영역 이펙트와 공칭 반경. 없으면 null.</summary>
    public GameObject GetAura(RuneElement element, out float nominalRadius)
    {
        nominalRadius = 1f;
        if (!Find(element, out var e)) return null;
        if (e.auraRadius > 0.01f) nominalRadius = e.auraRadius;
        return e.statusAura;
    }

    /// <summary>장판 영역 이펙트. 없으면 null.</summary>
    public GameObject GetAura(RuneElement element) => GetAura(element, out _);

    /// <summary>적 몸에 붙는 상태 이펙트. 미지정이면 장판 칸으로 폴백.</summary>
    public GameObject GetStatusBody(RuneElement element)
    {
        if (!Find(element, out var e)) return null;
        return e.statusBody != null ? e.statusBody : e.statusAura;
    }

    /// <summary>단계 발동 표시(플레이어 몸). 없으면 null.</summary>
    public GameObject GetBurst(RuneElement element)
        => Find(element, out var e) ? e.burst : null;

    /// <summary>점 타격 이펙트와 올리는 높이. 칸이 비었으면 발동 표시로 폴백(높이 0).</summary>
    public GameObject GetImpact(RuneElement element, out float lift)
    {
        lift = 0f;
        if (!Find(element, out var e)) return null;
        if (e.impact == null) return e.burst;
        lift = e.impactHeight;
        return e.impact;
    }

    /// <summary>광역 폭발 이펙트 · 공칭 반경 · 거두는 시각. 없으면 null.</summary>
    public GameObject GetArea(RuneElement element, out float nominalRadius, out float life)
    {
        nominalRadius = 1f; life = 0f;
        if (!Find(element, out var e) || e.area == null) return null;
        if (e.areaRadius > 0.01f) nominalRadius = e.areaRadius;
        life = e.areaLife;
        return e.area;
    }
}
