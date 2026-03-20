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
public class LeeMonsterSpawner : MonoBehaviour
{
    [Header("스폰 테이블")]
    [Tooltip("소환할 몬스터 목록 SO. 'Create > Abyss > Monster > Spawn Table'로 생성.")]
    [SerializeField] private LeeMonsterSpawnTableSO spawnTable;

    [Header("소환 설정")]
    [Tooltip("동시에 살아있는 최대 몬스터 수")]
    [SerializeField] private int maxMonsterCount = 5;

    [Tooltip("소환 시도 간격 (초)")]
    [SerializeField] private float spawnInterval = 3f;

    [Tooltip("스포너 중심으로부터 소환 반경 (유닛)")]
    [SerializeField] private float spawnRadius = 10f;

    [Tooltip("NavMesh 유효 위치 샘플링 허용 오차")]
    [SerializeField] private float navMeshSampleDistance = 2f;

    // ── 런타임 ─────────────────────────────────────────────

    // 비활성(풀 반환) 또는 null 엔트리는 Purge로 제거
    private readonly List<LeeMonsterBase> _spawnedMonsters = new();

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 초기화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void Start()
    {
        if (spawnTable == null)
        {
            Debug.LogWarning("[LeeMonsterSpawner] spawnTable이 비어 있습니다. Inspector에서 SO를 할당해주세요.", this);
            return;
        }

        SpawnLoop().Forget();
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
        LeeSpawnEntry entry = spawnTable.PickRandom();
        if (entry == null) return;

        if (!TryGetSpawnPosition(out Vector3 spawnPos)) return;

        var monster = await Managers.ObjectPooler.SpawnAsync<LeeMonsterBase>(
            entry.addressableKey,
            ObjectPoolerManager.PoolType.Monster,
            spawnPos,
            Quaternion.identity);

        if (monster == null)
        {
            Debug.LogWarning($"[LeeMonsterSpawner] '{entry.addressableKey}' 스폰 실패.", this);
            return;
        }

        _spawnedMonsters.Add(monster);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

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

        Debug.LogWarning("[LeeMonsterSpawner] 유효한 NavMesh 소환 위치를 찾지 못했습니다.", this);
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
