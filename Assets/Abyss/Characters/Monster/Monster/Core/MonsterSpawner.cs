using Abyss.Monster;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

/// <summary>티어 스포너의 등급 매칭 모드.</summary>
public enum GradeMatchMode
{
    /// <summary>targetGrade 이하의 등급을 모두 포함 (Boss는 항상 제외).</summary>
    AtMost,
    /// <summary>targetGrade와 정확히 일치하는 등급만 (Boss는 항상 제외).</summary>
    Exact,
}

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

    [Tooltip("이 스포너가 누적으로 소환할 최대 마릿수. 0 이하면 무제한 (기존 동작).\n" +
             "이 값에 도달하면 스폰 루프가 종료됨 — 이미 살아있는 몬스터는 유지.")]
    [SerializeField, Min(0)] private int maxTotalSpawns = 0;

    [Tooltip("소환 시도 간격 (초)")]
    [SerializeField] private float spawnInterval = 3f;

    [Tooltip("스포너 중심으로부터 소환 반경 (유닛)")]
    [SerializeField] private float spawnRadius = 10f;

    [Tooltip("NavMesh 유효 위치 샘플링 허용 오차")]
    [SerializeField] private float navMeshSampleDistance = 2f;

    [Header("스폰 연출 이펙트 (선택)")]
    [Tooltip("몬스터 스폰 위치에 재생할 VFX의 Addressable 키. 비어있으면 연출 없음.")]
    [SerializeField] private string spawnEffectAddressKey;

    [Tooltip("VFX 인스턴스 자동 정리 시간(초). 루프는 런타임에 강제 off되며 이 시간 후 ReleaseInstance 수행.")]
    [SerializeField, Min(0.1f)] private float spawnEffectDuration = 2f;

    [Tooltip("VFX 바닥 위치 보정. 스폰 위치 y에 더해져 파티클 피봇이 묻히는 것을 방지.")]
    [SerializeField] private float spawnEffectYOffset = 0f;

    [Tooltip("VFX 프리팡 원본 크기 대비 배율. 1=원본, 0.5=절반.")]
    [SerializeField, Min(0.01f)] private float spawnEffectScale = 0.5f;

    [Header("몬스터 등장 디졸브 (선택)")]
    [Tooltip("몬스터 스폰 직후 디졸브 연출 지속 시간(초). 0 이하면 디졸브 미사용.\n" +
             "너무 짧으면 눈에 안 띄니 0.8~1.5 권장.")]
    [SerializeField, Min(0f)] private float spawnDissolveDuration = 1.2f;

    [Header("풀 그룹 필터 (primary)")]
    [Tooltip("true면 Start 시점에 현재 진행 중인 챕터 번호(ChapterId+1)를 allowedPoolGroups에 자동 주입.\n" +
             "수동으로 특정 그룹만 지정하고 싶으면 false로 설정.")]
    [SerializeField] private bool autoApplyChapterGroup = true;

    [Tooltip("이 방에서 소환 허용할 풀 그룹 번호. 몬스터의 poolTags 중 하나라도 이 목록에 있으면 스폰됨.\n" +
             "autoApplyChapterGroup=true면 Start에서 현재 챕터로 덮어쓰여짐.")]
    [SerializeField] private List<int> allowedPoolGroups;

    [Header("등급 필터 (티어 스포너)")]
    [Tooltip("AtMost: targetGrade 이하 등급을 모두 허용 (Common 스포너, Rare 스포너 등).\n" +
             "Exact:  targetGrade와 동일한 등급만 허용 (Elite 전용 스포너 등).\n" +
             "※ Boss 등급은 어떤 모드에서도 항상 제외됨 — 보스는 전용 소환 연출을 사용합니다.")]
    [SerializeField] private GradeMatchMode gradeMode = GradeMatchMode.AtMost;

    [Tooltip("기준 등급. gradeMode와 조합되어 스폰 대상 범위를 정함.\n" +
             "예) AtMost + Elite → Common/Rare/Elite 허용, Exact + Rare → Rare만 허용.")]
    [SerializeField] private MonsterGrade targetGrade = MonsterGrade.Common;

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
    // 이 스포너가 이번 방 수명 동안 누적으로 스폰한 마릿수 (maxTotalSpawns와 비교)
    private int _totalSpawned;

    // ── Properties / Events ─────────────────────────────────

    /// <summary>Inspector에 설정된 누적 스폰 상한. 0 이하 = 무제한.
    /// 방 클리어 카운터가 Σ로 합산해 킬 목표 수를 계산할 때 사용.</summary>
    public int MaxTotalSpawns => maxTotalSpawns;

    /// <summary>몬스터가 실제로 스폰된 직후 발행. (풀에서 꺼낸 MonsterBase 인스턴스 전달)
    /// RoomClearController가 몬스터 OnDied를 체이닝하는 데 사용.</summary>
    public event System.Action<MonsterBase> OnMonsterSpawned;

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

    /// <summary>외부(MapBuilder/Bootstrapper)에서 스포너 설정을 주입.
    /// Start() 전에 호출되어야 함 — 프리팹 Instantiate 직후가 안전.
    /// gradeMode는 AtMost로 고정됨 (등급 상한 해석).</summary>
    public void Configure(MonsterGrade maxGrade, int totalCount)
    {
        targetGrade    = maxGrade;
        gradeMode      = GradeMatchMode.AtMost;
        maxTotalSpawns = Mathf.Max(0, totalCount);
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
        // 여러 스포너가 같은 프레임에 Start()되어도 첫 스폰 시점이 균등하게 분산되도록
        // [0, spawnInterval) 범위 지터 추가 — 초기 스폰 폭주 방지.
        try
        {
            float initialJitter = UnityEngine.Random.Range(0f, spawnInterval);
            await UniTask.Delay(
                System.TimeSpan.FromSeconds(initialJitter),
                cancellationToken: destroyCancellationToken);
        }
        catch (System.OperationCanceledException) { return; }

        while (true)
        {
            try
            {
                await UniTask.Delay(
                    System.TimeSpan.FromSeconds(spawnInterval),
                    cancellationToken: destroyCancellationToken);
            }
            catch (System.OperationCanceledException) { return; }

            PurgeReturnedMonsters();

            // 누적 상한 도달 시 루프 종료 (이미 살아있는 몬스터는 유지)
            if (maxTotalSpawns > 0 && _totalSpawned >= maxTotalSpawns)
            {
                Debug.Log($"[MonsterSpawner:{name}] 누적 스폰 상한 {maxTotalSpawns}마리 도달 — 스폰 루프 종료.");
                return;
            }

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
                int passGrade = 0;
                int passElem  = 0;
                int passAll   = 0;
                if (spawnTable.entries != null)
                {
                    foreach (var e in spawnTable.entries)
                    {
                        if (!e.enabled || string.IsNullOrEmpty(e.addressableKey)) continue;
                        bool p1 = PassesPoolGroupFilter(e);
                        bool p2 = PassesGradeFilter(e);
                        bool p3 = PassesElementFilter(e);
                        if (p1) passPool++;
                        if (p2) passGrade++;
                        if (p3) passElem++;
                        if (p1 && p2 && p3) passAll++;
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
                    $"  · gradeMode/target  = {gradeMode} / {targetGrade}\n" +
                    $"  · allowedElements   = {elems}\n" +
                    $"  · 테이블 엔트리 총 {total}개 (풀통과 {passPool} / 등급통과 {passGrade} / 원소통과 {passElem} / 모두통과 {passAll})\n" +
                    $"  → SpawnTable Inspector에서 Auto-Populate를 눌러 grade/poolTags가 채워졌는지 확인하세요.", this);
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

        _spawnedMonsters.Add(monster);
        _totalSpawned++;

        // 외부 수명주기 구독자(RoomClearController 등)에게 통지 — OnDied 체이닝 기회 제공
        OnMonsterSpawned?.Invoke(monster);

        // 스폰 연출 이펙트 (fire-and-forget — 몬스터 루프는 블로킹하지 않음)
        PlaySpawnEffectAsync(spawnPos).Forget();

        // 원소 적용은 반드시 렌더러 머티리얼이 최종 상태일 때 수행해야 색상·속성 UI가 안정적.
        // 디졸브가 활성이면 렌더러 머티리얼이 일시 교체되므로, 디졸브 완료 콜백에서 원소 적용.
        //
        // 풀 재사용 Race 방어: 몬스터가 디졸브 중 사망·반환되어 다른 방에서 재사용된 상태라면,
        // 뒤늦게 firing되는 onComplete가 새 인스턴스의 원소를 덮어쓰는 사고가 발생할 수 있다.
        // 캡처한 GenerationId로 동일 lifecycle인지 검증한다.
        var capturedMonster = monster;
        int capturedGen = capturedMonster != null ? capturedMonster.GenerationId : -1;
        if (spawnDissolveDuration > 0f && monster != null)
        {
            DissolveEffect.PlayAppear(
                monster.gameObject,
                spawnDissolveDuration,
                onComplete: () =>
                {
                    if (capturedMonster == null) return;
                    if (!capturedMonster.gameObject.activeInHierarchy) return;
                    if (capturedMonster.GenerationId != capturedGen) return; // 풀 재사용 후라면 무시
                    capturedMonster.SetRandomNativeElement();
                });
        }
        else
        {
            monster.SetRandomNativeElement();
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>엔트리가 이 방의 모든 필터(풀 그룹 AND 등급 AND 원소)를 통과하는지.
    /// 각 필터 리스트가 비어있으면 해당 필터는 통과로 간주.</summary>
    private bool PassesAllFilters(SpawnEntry entry)
    {
        return PassesPoolGroupFilter(entry) && PassesGradeFilter(entry) && PassesElementFilter(entry);
    }

    /// <summary>엔트리의 grade가 이 스포너의 설정 모드 + targetGrade 범위 안에 있는지.
    /// Boss 등급은 모드와 무관하게 항상 제외 (보스 전용 소환 연출 사용).</summary>
    private bool PassesGradeFilter(SpawnEntry entry)
    {
        if (entry.grade == MonsterGrade.Boss) return false;

        return gradeMode switch
        {
            GradeMatchMode.AtMost => entry.grade <= targetGrade,
            GradeMatchMode.Exact  => entry.grade == targetGrade,
            _                     => true,
        };
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

    /// <summary>지정 위치에 스폰 VFX를 1회 재생.
    /// 모든 ParticleSystem은 루프 off로 강제 전환, spawnEffectDuration 후 ReleaseInstance.
    /// Addressable 키 비어있거나 로드 실패 시 조용히 무시.</summary>
    private async UniTaskVoid PlaySpawnEffectAsync(Vector3 pos)
    {
        if (string.IsNullOrEmpty(spawnEffectAddressKey)) return;

        GameObject fx;
        try
        {
            fx = await Managers.AddressableManager.InstantiateAsync(spawnEffectAddressKey);
        }
        catch (System.OperationCanceledException) { return; }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[MonsterSpawner] SpawnEffect '{spawnEffectAddressKey}' 로드 실패: {e.Message}", this);
            return;
        }

        if (fx == null) return;

        fx.transform.SetPositionAndRotation(
            new Vector3(pos.x, pos.y + spawnEffectYOffset, pos.z),
            Quaternion.identity);

        if (spawnEffectScale != 1f)
            fx.transform.localScale *= spawnEffectScale;

        // 루프 강제 off — 프리팹 설정 실수 방지 + 정리 시점 일관성 확보
        var particles = fx.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
        {
            var ps = particles[i];
            var main = ps.main;
            if (main.loop)
            {
                main.loop = false;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                ps.Play(true);
            }
        }

        try
        {
            await UniTask.Delay(
                System.TimeSpan.FromSeconds(spawnEffectDuration),
                cancellationToken: destroyCancellationToken);
        }
        catch (System.OperationCanceledException)
        {
            if (fx != null) Managers.AddressableManager.ReleaseInstance(fx);
            return;
        }

        if (fx != null)
            Managers.AddressableManager.ReleaseInstance(fx);
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
