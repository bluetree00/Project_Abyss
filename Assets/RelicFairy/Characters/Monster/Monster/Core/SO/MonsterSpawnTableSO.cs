using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>스폰 필터 — 허용할 원소 집합. 비어있으면 모든 원소 허용.</summary>
public delegate bool SpawnEntryFilter(SpawnEntry entry);

/// <summary>
/// 몬스터 스폰 테이블 ScriptableObject.
///
/// 스포너가 참조하는 유일한 SO.
/// 각 엔트리는 몬스터 프리팹 + 스폰 가중치로 구성.
///
/// Editor에서 "Auto-Populate" 버튼으로 어셈블리 스캔 후 엔트리 행을 자동 추가.
/// (프리팹 슬롯은 이후 한 번만 채워주면 모든 스포너에 공유됨)
///
/// 새 몬스터 추가 시:
///   1) MonsterClass : LeeMonsterBase + public const string PrefabAddress 작성
///   2) 프리팹 생성
///   3) 이 SO에서 Auto-Populate → 생긴 행에 프리팹 드래그
/// </summary>
[CreateAssetMenu(
    fileName = "MonsterSpawnTable",
    menuName  = "RelicFairy/Monster/Spawn Table")]
public class MonsterSpawnTableSO : ScriptableObject
{
    [Tooltip("소환 가능한 몬스터 목록. 가중치가 높을수록 더 자주 선택됩니다.")]
    public List<SpawnEntry> entries = new();

    // ── 런타임 헬퍼 ───────────────────────────────────────

    /// <summary>가중치 기반 랜덤으로 엔트리를 하나 반환한다. 유효 엔트리가 없으면 null.</summary>
    public SpawnEntry PickRandom() => PickRandom(null);

    /// <summary>필터 통과 엔트리 중에서만 가중치 랜덤.
    /// filter=null 이면 모든 유효 엔트리 대상. 통과 엔트리가 없으면 null 반환.</summary>
    public SpawnEntry PickRandom(SpawnEntryFilter filter)
    {
        float total = 0f;
        foreach (var e in entries)
        {
            if (!IsSelectable(e, filter)) continue;
            total += Mathf.Max(0f, e.weight);
        }

        if (total <= 0f) return null;

        float roll       = UnityEngine.Random.Range(0f, total);
        float cumulative = 0f;

        foreach (var e in entries)
        {
            if (!IsSelectable(e, filter)) continue;
            cumulative += Mathf.Max(0f, e.weight);
            if (roll < cumulative) return e;
        }

        return null;
    }

    private static bool IsSelectable(SpawnEntry e, SpawnEntryFilter filter)
    {
        if (e == null || !e.enabled || string.IsNullOrEmpty(e.addressableKey)) return false;
        if (filter != null && !filter(e)) return false;
        return true;
    }
}

/// <summary>스폰 테이블 한 행.</summary>
[Serializable]
public class SpawnEntry
{
    [Tooltip("표시 이름 (식별용, Auto-Populate로 자동 채워짐)")]
    public string displayName;

    [Tooltip("소환할 몬스터 Addressable 주소 (Auto-Populate로 자동 채워짐)")]
    public string addressableKey;

    [Tooltip("몬스터의 등급. 티어 스포너가 이 값으로 필터링. (Auto-Populate가 MonsterConfigSO.grade에서 자동 채움)\n" +
             "Boss 등급은 일반 스포너에서 자동 제외됨 — 보스는 전용 소환 연출 사용.")]
    public MonsterGrade grade = MonsterGrade.Common;

    [Tooltip("몬스터의 네이티브 원소. 스포너 원소 필터와 매칭됨. (Auto-Populate로 자동 채워짐)")]
    public ElementType nativeElement = ElementType.None;

    [Tooltip("몬스터가 속한 풀 그룹 번호 목록. 스포너의 Allowed Pool Groups와 교집합이 있으면 스폰. " +
             "(Auto-Populate가 MONSTER_ELEMENT_STAT_DATA의 monster_pool_tag에서 자동 채움)")]
    public int[] poolTags;

    [Tooltip("스폰 가중치. 높을수록 더 자주 선택됨 (1 이상 권장)")]
    [Min(0f)]
    public float weight = 1f;

    [Tooltip("false로 설정하면 이 몬스터는 소환 대상에서 제외됨")]
    public bool enabled = true;
}
