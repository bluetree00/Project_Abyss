using System;
using System.Collections.Generic;
using UnityEngine;

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
    menuName  = "Abyss/Monster/Spawn Table")]
public class LeeMonsterSpawnTableSO : ScriptableObject
{
    [Tooltip("소환 가능한 몬스터 목록. 가중치가 높을수록 더 자주 선택됩니다.")]
    public List<LeeSpawnEntry> entries = new();

    // ── 런타임 헬퍼 ───────────────────────────────────────

    /// <summary>가중치 기반 랜덤으로 엔트리를 하나 반환한다. 유효 엔트리가 없으면 null.</summary>
    public LeeSpawnEntry PickRandom()
    {
        float total = 0f;
        foreach (var e in entries)
            if (e.enabled && e.prefab != null)
                total += Mathf.Max(0f, e.weight);

        if (total <= 0f) return null;

        float roll       = UnityEngine.Random.Range(0f, total);
        float cumulative = 0f;

        foreach (var e in entries)
        {
            if (!e.enabled || e.prefab == null) continue;
            cumulative += Mathf.Max(0f, e.weight);
            if (roll < cumulative) return e;
        }

        return null;
    }
}

/// <summary>스폰 테이블 한 행.</summary>
[Serializable]
public class LeeSpawnEntry
{
    [Tooltip("표시 이름 (식별용, Auto-Populate로 자동 채워짐)")]
    public string displayName;

    [Tooltip("소환할 몬스터 프리팹")]
    public GameObject prefab;

    [Tooltip("스폰 가중치. 높을수록 더 자주 선택됨 (1 이상 권장)")]
    [Min(0f)]
    public float weight = 1f;

    [Tooltip("false로 설정하면 이 몬스터는 소환 대상에서 제외됨")]
    public bool enabled = true;
}
