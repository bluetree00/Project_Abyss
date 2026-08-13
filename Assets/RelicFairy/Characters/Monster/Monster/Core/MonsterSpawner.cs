using RelicFairy.Monster;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

/// <summary>웨이브 모드에서 한 스포너의 단일 웨이브 설정.</summary>
[System.Serializable]
public struct WaveEntry
{
    [Tooltip("이 웨이브에서 이 스포너가 소환할 몬스터 마릿수.")]
    [Min(0)] public int spawnCount;

    [Tooltip("이 웨이브에서 허용할 최대 등급 상한. 스폰 테이블 필터에 적용됨.")]
    public MonsterGrade maxGrade;
}

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
    /// <summary>동시 생존 상한(<see cref="MonsterBudget"/>)에 걸렸을 때 자리를 다시 확인하는 주기(초).</summary>
    private const float BudgetPollInterval = 0.25f;

    // ── 스폰 지점 여유 검사 ────────────────────────────────
    // NavMesh는 장식(나무·기둥·석상)을 <b>담고 있지 않다</b> —
    // DecorationHandler가 모든 장식에 NavMeshModifier.ignoreFromBuild=true를 붙이고,
    // 게다가 장식은 PostBuild(=NavMesh 베이크 이후)에 생성된다.
    // 그래서 NavMesh.SamplePosition은 장식 한가운데도 "유효한 바닥"이라고 답한다
    // → 몬스터가 장애물 위/속에 박힌 채 소환된다. 물리로 한 번 더 거른다.
    /// <summary>스폰 여유 반경(m). 몬스터 몸 반경 근사.</summary>
    private const float SpawnClearRadius = 0.5f;
    /// <summary>검사 구의 중심 높이(m). 바닥 블록 윗면(≈0.1)보다 위라 바닥에 걸리지 않는다.</summary>
    private const float SpawnClearHeight = 0.9f;
    /// <summary>막힘으로 볼 레이어 — Default(0, 장식) + Wall(8). Ground(3)·Player(6)·Monster(7)는 제외.
    /// (MapBuilder 규약: 벽=8, 바닥=3 / DecorationHandler가 만드는 장식=0)</summary>
    private const int SpawnBlockMask = (1 << 0) | (1 << 8);

    [Header("스폰 테이블")]
    [Tooltip("소환할 몬스터 목록 SO. 'Create > RelicFairy > Monster > Spawn Table'로 생성.")]
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
    [Tooltip("디졸브 엣지 색상 (시인성 아웃라인). 모든 몬스터 공용으로 적용됩니다.")]
    [SerializeField] private Color spawnOutlineColor = new Color(0f, 2.4f, 3f, 1f);

    [Tooltip("true면 Elite 등급 이상에만 스폰 디졸브를 적용해 일반 몹의 머티리얼 비용을 줄인다. " +
             "기본값 false = 현행(모든 몹 디졸브).")]
    [SerializeField] private bool spawnDissolveEliteAndAbove = false;

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

    [Header("웨이브 모드 설정 (비어있으면 자동 루프 모드)")]
    [Tooltip("단일 웨이브의 스폰 그룹 목록. 각 항목 = (등급 상한 × 마릿수).\n" +
             "예) [Common×3, Rare×2] → 한 웨이브에서 총 5마리를 연속 스폰.\n" +
             "비어있으면 기존 자동 루프 모드(maxTotalSpawns 기반)로 동작.")]
    [SerializeField] private WaveEntry[] _waveEntries;

    [Tooltip("웨이브 내 몬스터 간 스폰 간격 최소값(초). 한 마리씩 연속으로 등장하는 느낌을 준다.")]
    [SerializeField, Min(0f)] private float _waveSpawnDelayMin = 0.15f;

    [Tooltip("웨이브 내 몬스터 간 스폰 간격 최대값(초). Min~Max 사이 랜덤 대기.")]
    [SerializeField, Min(0f)] private float _waveSpawnDelayMax = 0.4f;

    // ── 런타임 ─────────────────────────────────────────────

    // 비활성(풀 반환) 또는 null 엔트리는 Purge로 제거
    private readonly List<MonsterBase> _spawnedMonsters = new();

    // 내부 필드 경계(월드 AABB). 설정 시 스폰 후보를 이 안으로 제한 — 게이트/복도로 새는 것 차단.
    private bool    _hasFieldBounds;
    private Bounds  _fieldBounds;
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

    /// <summary>이 스포너가 현재 살려두고 있는 몬스터 수. MonsterBudget이 전역 동시 상한을 계산할 때 합산한다.</summary>
    public int AliveCount => _spawnedMonsters.Count;

    /// <summary>웨이브 모드 여부(단일 웨이브). 그룹이 하나라도 있으면 1, 없으면 0(자동 루프 모드).
    /// _waveEntries의 모든 그룹을 하나의 웨이브로 합쳐 연속 스폰한다.</summary>
    public int WaveCount => (_waveEntries != null && _waveEntries.Length > 0) ? 1 : 0;

    /// <summary>몬스터가 실제로 스폰된 직후 발행. (풀에서 꺼낸 MonsterBase 인스턴스 전달)
    /// RoomClearController가 몬스터 OnDied를 체이닝하는 데 사용.</summary>
    public event System.Action<MonsterBase> OnMonsterSpawned;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 초기화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    // 방 전체 동시 적 하드캡(MonsterBudget)이 이 스포너의 생존 수를 합산할 수 있도록 등록한다.
    private void OnEnable()  => MonsterBudget.Register(this);
    private void OnDisable() => MonsterBudget.Unregister(this);

    private void Start()
    {
        if (spawnTable == null)
        {
            Debug.LogWarning("[MonsterSpawner] spawnTable이 비어 있습니다. Inspector에서 SO를 할당해주세요.", this);
            return;
        }

        if (autoApplyChapterGroup)
            ApplyCurrentChapterGroup();

        // 웨이브 모드이면 자동 루프 미실행 — RoomWaveController가 SpawnWaveAsync로 제어
        if (WaveCount > 0) return;

        SpawnLoop().Forget();
    }

    /// <summary>외부(MapBuilder/Bootstrapper)에서 레거시 단일 등급/수량 설정 주입.
    /// Start() 전에 호출되어야 함. gradeMode는 AtMost로 고정.
    /// 웨이브 모드(WaveCount > 0)에서는 totalCount가 무시된다.</summary>
    public void Configure(MonsterGrade maxGrade, int totalCount)
    {
        targetGrade = maxGrade;
        gradeMode   = GradeMatchMode.AtMost;
        if (WaveCount == 0)
            maxTotalSpawns = Mathf.Max(0, totalCount);
    }

    /// <summary>외부(Bootstrapper)에서 CSV 토큰의 세그먼트 배열을 주입. Start() 전에 호출.
    /// 각 세그먼트는 단일 웨이브 안의 스폰 그룹(등급 상한 × 마릿수)으로 취급된다 — 여러 웨이브가 아님.
    /// Inspector의 _waveEntries를 덮어쓴다 — CSV 토큰 기반 데이터가 Inspector 수동 설정보다 우선됨.</summary>
    public void ConfigureWaves(MapDataLoader.WaveCellConfig[] waves)
    {
        if (waves == null || waves.Length == 0) return;

        _waveEntries = new WaveEntry[waves.Length];
        for (int i = 0; i < waves.Length; i++)
            _waveEntries[i] = new WaveEntry { spawnCount = waves[i].count, maxGrade = waves[i].grade };

        // Exact: 토큰의 등급 세그먼트가 정확히 그 등급으로 스폰됨(c=Common, r=Rare, e=Elite).
        // AtMost였을 땐 상한만이라 e를 넣어도 엘리트가 확정 안 됐음 — 엘리트 믹스를 실제로 반영하려면 Exact.
        // 전제: 각 챕터 풀에 c/r/e가 최소 1종씩 존재(현 로스터 충족).
        gradeMode = GradeMatchMode.Exact;

        // Start()보다 먼저 SpawnWaveAsync가 호출될 수 있으므로 챕터 그룹을 여기서 즉시 적용.
        if (autoApplyChapterGroup)
            ApplyCurrentChapterGroup();

        Debug.Log($"[MonsterSpawner:{name}] 단일 웨이브 그룹 {waves.Length}개 주입 — " +
                  string.Join(" + ", System.Array.ConvertAll(waves, w => $"{w.grade}×{w.count}")), this);
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

        // 오프셋 제거: ChapterId.Chapter1=1 → poolTag 1 (태그=챕터, 직관적). 스폰테이블 poolTags도 이 규칙으로 재작성됨.
        int chapterNum = (int)run.CurrentChapter;
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

        // 스폰 여유 검사용 물리 쿼리 대비 — autoSyncTransforms=0이라 1회 동기화가 필요하다.
        Physics.SyncTransforms();

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

            // 스폰 조건 2중:
            //  ① 자기 몫(maxMonsterCount) — 죽으면 보충해 호드를 유지한다("많이 잡는 재미")
            //  ② 방 전체 동시 상한(MonsterBudget) — 스포너가 여러 개일 때 합계가 폭증해
            //     화면이 몹으로 꽉 차는 것을 막는다. 상한에 걸리면 이번 턴은 건너뛰고,
            //     적이 죽어 자리가 나면 다음 턴에 다시 스폰된다(루프는 안 끝냄).
            if (_spawnedMonsters.Count < maxMonsterCount && MonsterBudget.CanSpawn)
                await TrySpawnOneAsync();
        }
    }

    /// <summary>풀에 반환(비활성화)됐거나 null인 참조를 리스트에서 제거한다.</summary>
    private void PurgeReturnedMonsters()
    {
        _spawnedMonsters.RemoveAll(m => m == null || !m.gameObject.activeInHierarchy);
    }

    /// <summary>
    /// 동시 생존 상한(<see cref="MonsterBudget"/>)에 자리가 날 때까지 대기한다. 취소되면 false.
    ///
    /// 매 확인마다 <see cref="PurgeReturnedMonsters"/>를 먼저 부르는 것이 핵심이다 —
    /// <see cref="AliveCount"/>가 곧 예산 사용량인데, 정리가 없으면 죽은 몹이 계속 산 것으로 잡혀
    /// 상한에 한 번 걸리면 <b>영원히 스폰이 재개되지 않는다</b>.
    /// (기존엔 정리 호출이 레거시 <c>SpawnLoop</c> 안에만 있어 웨이브 경로에서는 돌지 않았다.)
    /// </summary>
    private async UniTask<bool> WaitForSpawnBudgetAsync(CancellationToken ct)
    {
        PurgeReturnedMonsters();
        if (MonsterBudget.CanSpawn) return true;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await UniTask.Delay(System.TimeSpan.FromSeconds(BudgetPollInterval), cancellationToken: ct);
            }
            catch (System.OperationCanceledException) { return false; }

            PurgeReturnedMonsters();
            if (MonsterBudget.CanSpawn) return true;
        }
        return false;
    }

    /// <summary>단일 웨이브 스폰. _waveEntries의 모든 그룹(등급 상한 × 마릿수)을 하나의 웨이브로 합쳐
    /// 한 마리씩 랜덤 간격으로 연속 스폰한다. waveIndex는 항상 0(단일 웨이브)이며 무시된다.
    /// 그룹별 maxGrade를 유지하며 ct가 취소되면 즉시 중단. 실제 스폰된 수를 반환.</summary>
    public async UniTask<int> SpawnWaveAsync(int waveIndex, CancellationToken ct)
    {
        if (_waveEntries == null || _waveEntries.Length == 0)
        {
            Debug.LogWarning($"[MonsterSpawner:{name}] SpawnWaveAsync: 웨이브 그룹이 비어 있음", this);
            return 0;
        }

        // 스폰 여유 검사(IsSpawnSpotClear)가 쓰는 물리 쿼리를 위해 트랜스폼을 1회 동기화한다.
        // 프로젝트 설정이 autoSyncTransforms=0이라, PostBuild에서 갓 생성된 장식 콜라이더가
        // 동기화 전에는 쿼리에 안 잡힌다(장식은 움직이지 않으므로 웨이브당 1회면 충분).
        Physics.SyncTransforms();

        // 이 웨이브 동안 targetGrade를 그룹별로 일시 교체 — TrySpawnOneAsync의 PassesGradeFilter에 반영됨.
        // SpawnWaveAsync는 같은 인스턴스에 대해 순차 실행되므로 동시성 문제 없음.
        var prevGrade = targetGrade;

        // 챕터 수량 배율(ChapterDataSO.monsterCountScale) — 높은 챕터일수록 웨이브 마릿수 증가.
        float countScale = AppBootstrapper.Instance?.CurrentRun?.CurrentMonsterCountScale ?? 1f;

        // 총 스폰 대상 수 — 마지막 한 마리 뒤에는 대기하지 않도록 카운트다운에 사용.
        int remaining = 0;
        foreach (var g in _waveEntries) remaining += ScaleCount(g.spawnCount, countScale);

        int spawned = 0;
        for (int g = 0; g < _waveEntries.Length; g++)
        {
            targetGrade = _waveEntries[g].maxGrade; // 이 그룹의 등급 상한(AtMost)
            int count   = ScaleCount(_waveEntries[g].spawnCount, countScale);

            for (int i = 0; i < count; i++)
            {
                if (ct.IsCancellationRequested) { targetGrade = prevGrade; return spawned; }

                // 방 전체 동시 상한(MonsterBudget)을 여기서도 지킨다.
                // 예전엔 이 경로에 검사가 없어, 절차생성 방(전부 웨이브 모드)에서는 상한이 사실상 죽어 있었다
                // — 정예방 32마리가 한꺼번에 살아 있었다. 자리가 날 때까지 기다렸다가 이어서 소환한다.
                if (!await WaitForSpawnBudgetAsync(ct)) { targetGrade = prevGrade; return spawned; }

                if (await TrySpawnOneAsync()) spawned++;
                remaining--;

                // 마지막 한 마리 뒤에는 대기 생략 — 스폰 완료를 지체시키지 않음.
                if (remaining <= 0) continue;

                float delay = UnityEngine.Random.Range(_waveSpawnDelayMin, _waveSpawnDelayMax);
                try
                {
                    await UniTask.Delay(System.TimeSpan.FromSeconds(delay), cancellationToken: ct);
                }
                catch (System.OperationCanceledException) { targetGrade = prevGrade; return spawned; }
            }
        }

        targetGrade = prevGrade; // 복원
        return spawned;
    }

    /// <summary>
    /// 이 스포너의 spawnTable에 등록된 모든 몬스터 풀을 미리 채운다.
    /// RoomWaveController가 첫 웨이브 전에 호출해 스폰 시 프레임 드랍을 방지한다.
    /// </summary>
    public async UniTask PrewarmPoolsAsync(int sizePerMonster, CancellationToken ct)
    {
        if (spawnTable?.entries == null) return;

        foreach (var entry in spawnTable.entries)
        {
            if (ct.IsCancellationRequested) return;
            if (!entry.enabled || string.IsNullOrEmpty(entry.addressableKey)) continue;
            if (entry.grade == MonsterGrade.Boss) continue; // 보스는 BossSpawner가 별도 처리

            await Managers.ObjectPooler.PrewarmAsync(
                entry.addressableKey,
                ObjectPoolerManager.PoolType.Monster,
                sizePerMonster,
                ct);
        }
    }

    private async UniTask<bool> TrySpawnOneAsync()
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
                int passAll   = 0;
                if (spawnTable.entries != null)
                {
                    foreach (var e in spawnTable.entries)
                    {
                        if (!e.enabled || string.IsNullOrEmpty(e.addressableKey)) continue;
                        bool p1 = PassesPoolGroupFilter(e);
                        bool p2 = PassesGradeFilter(e);
                        if (p1) passPool++;
                        if (p2) passGrade++;
                        if (p1 && p2) passAll++;
                    }
                }
                string pools = allowedPoolGroups == null || allowedPoolGroups.Count == 0
                    ? "(비어있음=전체허용)"
                    : string.Join(",", allowedPoolGroups);
                Debug.LogWarning($"[MonsterSpawner:{name}] 필터 통과 엔트리 0개 — 스폰 불가.\n" +
                    $"  · allowedPoolGroups = {pools}\n" +
                    $"  · gradeMode/target  = {gradeMode} / {targetGrade}\n" +
                    $"  · 테이블 엔트리 총 {total}개 (풀통과 {passPool} / 등급통과 {passGrade} / 모두통과 {passAll})\n" +
                    $"  → SpawnTable Inspector에서 Auto-Populate를 눌러 grade/poolTags가 채워졌는지 확인하세요.", this);
            }
            return false;
        }

        // 잘못된 엔트리(이전에 스폰 실패)는 비활성화 처리해 다음 PickRandom 에서 제외
        if (_disabledKeys.Contains(entry.addressableKey)) return false;

        if (!TryGetSpawnPosition(out Vector3 spawnPos)) return false;

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
            return false;
        }

        if (monster == null)
        {
            Debug.LogWarning($"[MonsterSpawner] '{entry.addressableKey}' 스폰 실패 — 엔트리 비활성화.", this);
            _disabledKeys.Add(entry.addressableKey);
            return false;
        }

        _spawnedMonsters.Add(monster);
        _totalSpawned++;

        // 외부 수명주기 구독자(RoomWaveController 등)에게 통지 — OnDied 체이닝 기회 제공
        OnMonsterSpawned?.Invoke(monster);

        // 스폰 연출 이펙트 (fire-and-forget — 몬스터 루프는 블로킹하지 않음)
        PlaySpawnEffectAsync(spawnPos).Forget();

        bool dissolveAllowed = !spawnDissolveEliteAndAbove || entry.grade >= MonsterGrade.Elite;
        if (spawnDissolveDuration > 0f && monster != null && dissolveAllowed)
        {
            DissolveEffect.PlayAppear(
                monster.gameObject,
                spawnDissolveDuration,
                activationToken: monster.ActivationToken,
                edgeColor: spawnOutlineColor);
        }
        return true;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>엔트리가 이 방의 모든 필터(풀 그룹 AND 등급)를 통과하는지.</summary>
    private bool PassesAllFilters(SpawnEntry entry)
    {
        return PassesPoolGroupFilter(entry) && PassesGradeFilter(entry);
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

    /// <summary>지정 위치에 스폰 VFX를 1회 재생.
    /// 모든 ParticleSystem은 루프 off로 강제 전환, spawnEffectDuration 후 ReleaseInstance.
    /// Addressable 키 비어있거나 로드 실패 시 조용히 무시.</summary>
    private async UniTaskVoid PlaySpawnEffectAsync(Vector3 pos)
    {
        if (string.IsNullOrEmpty(spawnEffectAddressKey)) return;

        var spawnPos = new Vector3(pos.x, pos.y + spawnEffectYOffset, pos.z);

        GameObject fx;
        try
        {
            fx = await Managers.ObjectPooler.SpawnAsync(
                spawnEffectAddressKey,
                ObjectPoolerManager.PoolType.Effect,
                spawnPos,
                Quaternion.identity);
        }
        catch (System.OperationCanceledException) { return; }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[MonsterSpawner] SpawnEffect '{spawnEffectAddressKey}' 로드 실패: {e.Message}", this);
            return;
        }

        if (fx == null) return;

        // 풀 인스턴스를 1회용 VFX로 재생 — 수명(spawnEffectDuration) 후 자동 Despawn.
        // 컴포넌트는 첫 스폰 시 자동 부착(프리팹 데이터 변경 없음), 재사용 인스턴스는 그대로 재활용.
        var vfx = fx.GetComponent<PooledOneShotVfx>() ?? fx.AddComponent<PooledOneShotVfx>();
        vfx.Play(spawnEffectDuration, spawnEffectScale);
    }

    /// <summary>방 내부 필드 경계(월드 AABB)를 주입한다. 설정되면 스폰 후보가 이 경계 밖(게이트/복도)으로
    /// 나가지 않도록 클램프 + NavMesh 스냅 후 재검증한다. 미설정(손맵/테스트)이면 기존 동작 유지.</summary>
    public void SetFieldBounds(Bounds worldBounds)
    {
        _fieldBounds    = worldBounds;
        _hasFieldBounds = true;
    }

    private bool TryGetSpawnPosition(out Vector3 result)
    {
        const int MaxAttempts = 10;

        for (int i = 0; i < MaxAttempts; i++)
        {
            Vector2 rand2D    = Random.insideUnitCircle * spawnRadius;
            Vector3 candidate = transform.position + new Vector3(rand2D.x, 0f, rand2D.y);

            // 후보를 방 안으로 투영 — 처음부터 경계 밖으로 안 나가게
            if (_hasFieldBounds)
                candidate = ClampXZ(candidate, _fieldBounds);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, navMeshSampleDistance, NavMesh.AllAreas))
            {
                // NavMesh 스냅이 문틈으로 복도에 붙는 경우 차단 — 경계 안일 때만 채택
                if ((!_hasFieldBounds || ContainsXZ(_fieldBounds, hit.position)) && IsSpawnSpotClear(hit.position))
                {
                    result = hit.position;
                    return true;
                }
            }
        }

        // 폴백 ① 스포너 자기 위치(방 안 보장)
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit selfHit, navMeshSampleDistance, NavMesh.AllAreas)
            && IsSpawnSpotClear(selfHit.position))
        {
            result = selfHit.position;
            return true;
        }

        // 폴백 ② 스포너 둘레를 한 바퀴 훑어 빈 곳을 찾는다.
        //     스포너 셀이 통째로 장식(멀티셀 나무 등) 밑에 깔린 경우가 있어 자기 위치만으로는 못 빠져나온다.
        for (int ring = 1; ring <= 3; ring++)
        {
            float r = spawnRadius * (ring / 3f);
            for (int a = 0; a < 8; a++)
            {
                float rad = a * Mathf.PI * 0.25f;
                Vector3 p = transform.position + new Vector3(Mathf.Cos(rad) * r, 0f, Mathf.Sin(rad) * r);
                if (_hasFieldBounds) p = ClampXZ(p, _fieldBounds);
                if (NavMesh.SamplePosition(p, out NavMeshHit ringHit, navMeshSampleDistance, NavMesh.AllAreas)
                    && (!_hasFieldBounds || ContainsXZ(_fieldBounds, ringHit.position))
                    && IsSpawnSpotClear(ringHit.position))
                {
                    result = ringHit.position;
                    return true;
                }
            }
        }

        Debug.LogWarning("[MonsterSpawner] 장애물에 막히지 않은 소환 위치를 찾지 못했습니다 — " +
                         "스포너 주변이 장식으로 덮여 있는지 확인하세요.", this);
        result = Vector3.zero;
        return false;
    }

    /// <summary>
    /// 그 자리에 몬스터가 들어갈 실물 여유가 있는가.
    ///
    /// NavMesh만 믿으면 안 된다 — 장식은 <see cref="Unity.AI.Navigation.NavMeshModifier"/>
    /// <c>ignoreFromBuild=true</c>로 베이크에서 빠지고, 생성 시점도 NavMesh 베이크 이후(PostBuild)라
    /// <c>NavMesh.SamplePosition</c>이 나무·기둥 한가운데를 멀쩡한 바닥으로 답한다.
    /// 그래서 물리로 한 번 더 거른다.
    /// </summary>
    private static bool IsSpawnSpotClear(Vector3 pos)
        => !Physics.CheckSphere(pos + Vector3.up * SpawnClearHeight, SpawnClearRadius,
                                SpawnBlockMask, QueryTriggerInteraction.Ignore);

    /// <summary>X·Z만 경계 안으로 클램프(Y는 유지).</summary>
    private static Vector3 ClampXZ(Vector3 p, Bounds b)
        => new Vector3(Mathf.Clamp(p.x, b.min.x, b.max.x), p.y, Mathf.Clamp(p.z, b.min.z, b.max.z));

    /// <summary>X·Z 경계 포함 여부(Y 무시).</summary>
    private static bool ContainsXZ(Bounds b, Vector3 p)
        => p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z;

    /// <summary>챕터 수량 배율을 적용한 스폰 마릿수. baseCount>0이면 최소 1 보장(반올림).</summary>
    private static int ScaleCount(int baseCount, float scale)
        => baseCount <= 0 ? 0 : Mathf.Max(1, Mathf.RoundToInt(baseCount * scale));

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
