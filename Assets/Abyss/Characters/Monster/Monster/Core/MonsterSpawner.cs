using Abyss.Monster;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Lee 몬스터 스포너.
///
/// 빈 오브젝트에 부착 후 Inspector에서 설정:
///   - spawnTable      : LeeMonsterSpawnTableSO 에셋 (어셈블리 스캔으로 엔트리 자동 발견)
///   - maxMonsterCount : 동시 유지할 최대 마릿수
///   - spawnRadius     : 스포너 중심 기준 소환 반경
///   - spawnInterval   : 한 마리 소환 시도 간격 (초)
///
/// 생성: ObjectPoolerManager → Addressables 에서 프리팹 로드 후 풀 관리
/// 반환: 몬스터 사망 시 LeeDieState 에서 ObjectPoolerManager.Despawn() 호출
/// </summary>
public class MonsterSpawner : MonoBehaviour
{
    [Header("스폰 테이블")]
    [Tooltip("소환할 몬스터 목록 SO. 'Create > Abyss > Monster > Spawn Table'로 생성.")]
    [SerializeField] private MonsterSpawnTableSO spawnTable;

    [Header("소환 설정")]
    [Tooltip("동시에 살아있는 최대 몬스터 수")]
    [SerializeField] private int maxMonsterCount = 5;

    [Tooltip("소환 시도 간격 (초)")]
    [SerializeField] private float spawnInterval = 3f;

    [Tooltip("스포너 중심으로부터 소환 반경 (유닛)")]
    [SerializeField] private float spawnRadius = 10f;

    [Tooltip("NavMesh 유효 위치 샘플링 허용 오차")]
    [SerializeField] private float navMeshSampleDistance = 2f;

    [Header("풀 그룹 필터 (primary)")]
    [Tooltip("true면 Start 시점에 현재 진행 중인 챕터 번호(ChapterId+1)를 allowedPoolGroups에 자동 주입.\n" +
             "수동으로 특정 그룹만 지정하고 싶으면 false로 설정.")]
    [SerializeField] private bool autoApplyChapterGroup = true;

    [Tooltip("이 방에서 소환 허용할 풀 그룹 번호. 몬스터의 poolTags 중 하나라도 이 목록에 있으면 스폰됨.\n" +
             "autoApplyChapterGroup=true면 Start에서 현재 챕터로 덮어쓰여짐.")]
    [SerializeField] private List<int> allowedPoolGroups;

    [Header("원소 필터 (선택 — 비어있으면 모든 원소 허용)")]
    [Tooltip("특정 원소 테마 방에서 사용. None(무속성)을 허용하려면 명시적으로 추가.")]
    [SerializeField] private List<ElementType> allowedElements;

    // ── 런타임 ─────────────────────────────────────────────

    // 비활성(풀 반환) 또는 null 엔트리는 Purge로 제거
    private readonly List<MonsterBase> _spawnedMonsters = new();
    // 스폰 실패한 키는 다시 시도하지 않음 (런타임 캐시)
    private readonly HashSet<string> _disabledKeys = new();
    // 필터 미스 경고는 1회만 출력
    private bool _filterMissWarned;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 초기화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void Start()
    {
        if (spawnTable == null)
        {
            Debug.LogWarning("[MonsterSpawner] spawnTable이 비어 있습니다. Inspector에서 SO를 할당해주세요.", this);
            return;
        }

        if (autoApplyChapterGroup)
            ApplyCurrentChapterGroup();

        SpawnLoop().Forget();
    }

    /// <summary>현재 런의 챕터 번호(Chapter1 → 1, Chapter5 → 5)를 allowedPoolGroups에 주입.
    /// 세션 없으면 경고 후 수동 설정 유지.</summary>
    private void ApplyCurrentChapterGroup()
    {
        var run = AppBootstrapper.Instance?.CurrentRun;
        if (run == null)
        {
            Debug.LogWarning("[MonsterSpawner] CurrentRun 없음 — 챕터 자동 적용 스킵, 수동 allowedPoolGroups 유지", this);
            return;
        }

        int chapterNum = (int)run.CurrentChapter + 1;
        allowedPoolGroups = new() { chapterNum };
        Debug.Log($"[MonsterSpawner:{name}] 챕터 {run.CurrentChapter} 자동 적용 → allowedPoolGroups=[{chapterNum}]");
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 소환 루프
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private async UniTaskVoid SpawnLoop()
    {
        while (true)
        {
            await UniTask.Delay(
                System.TimeSpan.FromSeconds(spawnInterval),
                cancellationToken: destroyCancellationToken);

            PurgeReturnedMonsters();

            if (_spawnedMonsters.Count < maxMonsterCount)
                await TrySpawnOneAsync();
        }
    }

    /// <summary>풀에 반환(비활성화)됐거나 null인 참조를 리스트에서 제거한다.</summary>
    private void PurgeReturnedMonsters()
    {
        _spawnedMonsters.RemoveAll(m => m == null || !m.gameObject.activeInHierarchy);
    }

    private async UniTask TrySpawnOneAsync()
    {
        SpawnEntry entry = spawnTable.PickRandom(PassesAllFilters);
        if (entry == null)
        {
            if (!_filterMissWarned)
            {
                _filterMissWarned = true;
                int total     = spawnTable.entries != null ? spawnTable.entries.Count : 0;
                int passPool  = 0;
                int passElem  = 0;
                int passBoth  = 0;
                if (spawnTable.entries != null)
                {
                    foreach (var e in spawnTable.entries)
                    {
                        if (!e.enabled || string.IsNullOrEmpty(e.addressableKey)) continue;
                        bool p1 = PassesPoolGroupFilter(e);
                        bool p2 = PassesElementFilter(e);
                        if (p1) passPool++;
                        if (p2) passElem++;
                        if (p1 && p2) passBoth++;
                    }
                }
                string pools = allowedPoolGroups == null || allowedPoolGroups.Count == 0
                    ? "(비어있음=전체허용)"
                    : string.Join(",", allowedPoolGroups);
                string elems = allowedElements == null || allowedElements.Count == 0
                    ? "(비어있음=전체허용)"
                    : string.Join(",", allowedElements);
                Debug.LogWarning($"[MonsterSpawner:{name}] 필터 통과 엔트리 0개 — 스폰 불가.\n" +
                    $"  · allowedPoolGroups = {pools}\n" +
                    $"  · allowedElements   = {elems}\n" +
                    $"  · 테이블 엔트리 총 {total}개 (풀통과 {passPool} / 원소통과 {passElem} / 둘다통과 {passBoth})\n" +
                    $"  → SpawnTable Inspector에서 Auto-Populate를 눌러 poolTags가 채워졌는지 확인하세요.", this);
            }
            return;
        }

        // 잘못된 엔트리(이전에 스폰 실패)는 비활성화 처리해 다음 PickRandom 에서 제외
        if (_disabledKeys.Contains(entry.addressableKey)) return;

        if (!TryGetSpawnPosition(out Vector3 spawnPos)) return;

        MonsterBase monster = null;
        try
        {
            monster = await Managers.ObjectPooler.SpawnAsync<MonsterBase>(
                entry.addressableKey,
                ObjectPoolerManager.PoolType.Monster,
                spawnPos,
                Quaternion.identity);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[MonsterSpawner] '{entry.addressableKey}' 스폰 예외 — 엔트리 비활성화: {ex.Message}", this);
            _disabledKeys.Add(entry.addressableKey);
            return;
        }

        if (monster == null)
        {
            Debug.LogWarning($"[MonsterSpawner] '{entry.addressableKey}' 스폰 실패 — 엔트리 비활성화.", this);
            _disabledKeys.Add(entry.addressableKey);
            return;
        }

        // 방 내 다양성 — 스폰마다 독립 roll로 6원소(None 포함) 중 랜덤 속성 주입
        monster.SetRandomNativeElement();

        _spawnedMonsters.Add(monster);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>엔트리가 이 방의 모든 필터(풀 그룹 AND 원소)를 통과하는지.
    /// 각 필터 리스트가 비어있으면 해당 필터는 통과로 간주.</summary>
    private bool PassesAllFilters(SpawnEntry entry)
    {
        return PassesPoolGroupFilter(entry) && PassesElementFilter(entry);
    }

    /// <summary>엔트리의 poolTags 중 하나라도 allowedPoolGroups에 있으면 통과.
    /// allowedPoolGroups가 비어있으면 모두 통과.
    /// 엔트리 poolTags가 비어있으면(보스 등) 필터가 비어있을 때만 통과.</summary>
    private bool PassesPoolGroupFilter(SpawnEntry entry)
    {
        if (allowedPoolGroups == null || allowedPoolGroups.Count == 0) return true;
        if (entry.poolTags == null || entry.poolTags.Length == 0) return false;

        for (int i = 0; i < entry.poolTags.Length; i++)
            if (allowedPoolGroups.Contains(entry.poolTags[i])) return true;
        return false;
    }

    /// <summary>엔트리가 이 방의 원소 필터를 통과하는지. allowedElements가 비어있으면 항상 통과.</summary>
    private bool PassesElementFilter(SpawnEntry entry)
    {
        if (allowedElements == null || allowedElements.Count == 0) return true;
        return allowedElements.Contains(entry.nativeElement);
    }

    private bool TryGetSpawnPosition(out Vector3 result)
    {
        const int MaxAttempts = 10;

        for (int i = 0; i < MaxAttempts; i++)
        {
            Vector2 rand2D    = Random.insideUnitCircle * spawnRadius;
            Vector3 candidate = transform.position + new Vector3(rand2D.x, 0f, rand2D.y);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, navMeshSampleDistance, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }
        }

        Debug.LogWarning("[MonsterSpawner] 유효한 NavMesh 소환 위치를 찾지 못했습니다.", this);
        result = Vector3.zero;
        return false;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 에디터 Gizmo
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.25f);
        Gizmos.DrawSphere(transform.position, spawnRadius);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, spawnRadius);

        Gizmos.color = Color.yellow;
        foreach (var m in _spawnedMonsters)
        {
            if (m == null) continue;
            Gizmos.DrawLine(transform.position, m.transform.position);
        }
    }
}
