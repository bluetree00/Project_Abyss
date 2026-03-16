using System.Collections;
using System.Collections.Generic;
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

    // null 엔트리 = 사망 후 Destroy된 몬스터
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

        StartCoroutine(SpawnLoop());
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 소환 루프
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private IEnumerator SpawnLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(spawnInterval);

            PurgeDeadMonsters();

            if (_spawnedMonsters.Count < maxMonsterCount)
                TrySpawnOne();
        }
    }

    private void PurgeDeadMonsters()
    {
        _spawnedMonsters.RemoveAll(m => m == null);
    }

    private void TrySpawnOne()
    {
        LeeSpawnEntry entry = spawnTable.PickRandom();
        if (entry == null) return;

        if (!TryGetSpawnPosition(out Vector3 spawnPos)) return;

        GameObject go      = Instantiate(entry.prefab, spawnPos, Quaternion.identity);
        var        monster = go.GetComponent<LeeMonsterBase>();

        if (monster == null)
        {
            Debug.LogWarning($"[LeeMonsterSpawner] '{entry.prefab.name}'에 LeeMonsterBase 컴포넌트가 없습니다.", this);
            Destroy(go);
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
