using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>런 공용 이펙트 칸 — 보상 오브젝트 등급 연출 · 이벤트방 놀이가 쓴다. 순서를 바꾸지 말 것(직렬화 값).</summary>
public enum RunFxSlot
{
    None   = 0,
    Pillar = 1,   // 빛기둥 + 바닥 고리
    Burst  = 2,   // 구형 폭발
    Ring   = 3,   // 소용돌이 고리(충격파)
    Orb    = 4,   // 떠 있는 구체(반복)
    Glyph  = 5,   // 바닥 문양(반복) — 10-05 보상에서 걷음(리치 봉인진과 같은 프리팹). 값은 직렬화라 남긴다
    Swirl  = 6,   // 모여드는 소용돌이(반복)
    Beacon = 7,   // 멀리서도 보이는 표지 기둥(반복, 원래 금빛) — 10-05 보상에서 걷음(리치 봉인 완성과 같은 프리팹)
    Portal = 8,   // 세워 두는 소용돌이 문(반복, 원래 빛깔) — 챕터 게이트
    // 보상 등급 기둥(반복, 원래 빛깔 = 등급색) — Vefects 드랍 표시 팩(10-05)
    LootCommon    = 9,
    LootRare      = 10,
    LootEpic      = 11,
    LootLegendary = 12,
}

[Serializable]
public struct RunFxEntry
{
    public RunFxSlot  slot;
    public GameObject prefab;
    [Tooltip("프리팹 크기에 곱할 배율 (0이면 1)")]
    public float      scale;
    [Tooltip("재생 위치 보정")]
    public Vector3    offset;
    [Tooltip("한 번 재생 수명(초). 0이면 파티클 길이로 계산")]
    public float      lifetime;
    [Tooltip("출처 메모")]
    public string     note;
    [Tooltip("빛깔 갈아 끼우기 — 원색이 진한 단색인 팩(Hovl 지도 표지 = 빨강)은 곱하기 틴트로 등급색이 안 나온다. 켜면 밝기는 두고 빛깔만 바꾼다(RunFxRecolor)")]
    public bool       recolor;
}

/// <summary>
/// 런 공용 이펙트 목록 — Addressables 주소 <see cref="RunFx.SetAddress"/>로 불러온다.
/// 리치 목록이 쓰는 프리팹 가운데 여섯만 가리킨다(모든 방에서 쓰므로 리치 목록 전체를 올리지 않는다).
/// 만들기: 메뉴 RelicFairy/Setup/Build Run Fx Set.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Run/Run Fx Set", fileName = "RunFxSet")]
public sealed class RunFxSetSO : ScriptableObject
{
    [SerializeField] private List<RunFxEntry> entries = new();

    [Header("예고 장판 데칼 — 이벤트방 놀이가 쓰는 동안만 PatternGuideHelper에 넣는다")]
    [SerializeField] private Material guideCircle;
    [SerializeField] private Material guideArrow;

    private Dictionary<RunFxSlot, RunFxEntry> _map;

    public IReadOnlyList<RunFxEntry> Entries => entries;
    public Material GuideCircle => guideCircle;
    public Material GuideArrow  => guideArrow;

    public bool TryGet(RunFxSlot slot, out RunFxEntry entry)
    {
        if (_map == null) BuildMap();
        return _map.TryGetValue(slot, out entry) && entry.prefab != null;
    }

    private void BuildMap()
    {
        _map = new Dictionary<RunFxSlot, RunFxEntry>(entries.Count);
        foreach (var e in entries)
            if (e.slot != RunFxSlot.None && e.prefab != null)
                _map[e.slot] = e;
    }

    private void OnDisable() => _map = null;   // 인스펙터 편집 후 다시 만든다

#if UNITY_EDITOR
    /// <summary>만들기 메뉴 전용 — 목록을 통째로 바꾼다.</summary>
    public void EditorSetEntries(List<RunFxEntry> list)
    {
        entries = list;
        _map    = null;
    }

    /// <summary>만들기 메뉴 전용 — 예고 장판 재질.</summary>
    public void EditorSetGuides(Material circle, Material arrow)
    {
        guideCircle = circle;
        guideArrow  = arrow;
    }
#endif
}
