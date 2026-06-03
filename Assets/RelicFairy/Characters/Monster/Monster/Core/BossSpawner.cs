using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 보스 방 전용 스포너. B 타일에 배치되며 방 진입 직후 보스 1마리를 소환한다.
/// MonsterSpawnTableSO에서 Boss 등급 엔트리만 선택한다.
/// RoomClearController가 OnMonsterSpawned를 통해 보스 사망을 추적한다.
/// </summary>
public class BossSpawner : MonoBehaviour
{
    [Header("스폰 테이블")]
    [Tooltip("Boss 등급 엔트리가 포함된 MonsterSpawnTableSO. 'Create > RelicFairy > Monster > Spawn Table'로 생성.")]
    [SerializeField] private MonsterSpawnTableSO spawnTable;

    [Header("소환 설정")]
    [Tooltip("방 진입 후 보스 소환까지 대기 시간(초). 분위기 빌드업용.")]
    [SerializeField, Min(0f)] private float spawnDelay = 1.5f;

    [Header("스폰 연출 이펙트 (선택)")]
    [Tooltip("보스 소환 위치에 재생할 VFX의 Addressable 키. 비어있으면 연출 없음.")]
    [SerializeField] private string spawnEffectAddressKey;

    [Tooltip("VFX 인스턴스 자동 정리 시간(초).")]
    [SerializeField, Min(0.1f)] private float spawnEffectDuration = 2f;

    [Tooltip("VFX 바닥 위치 보정 오프셋.")]
    [SerializeField] private float spawnEffectYOffset;

    [Tooltip("VFX 프리팹 크기 배율.")]
    [SerializeField, Min(0.01f)] private float spawnEffectScale = 1f;

    [Header("보스 등장 디졸브 (선택)")]
    [Tooltip("보스 등장 디졸브 지속 시간(초). 0 이하면 미사용.")]
    [SerializeField, Min(0f)] private float spawnDissolveDuration = 1.5f;
    [Tooltip("디졸브 엣지 색상 (시인성 아웃라인).")]
    [SerializeField] private Color spawnOutlineColor = new Color(0f, 2.4f, 3f, 1f);

    [Header("직접 배치 보스 (선택)")]
    [Tooltip("씬에 직접 배치된 보스. 설정 시 spawnTable 대신 이 오브젝트를 활성화한다. 보스는 비활성 상태로 씬에 배치해야 한다.")]
    [SerializeField] private MonsterBase placedBoss;

    [Header("외부 트리거 연동")]
    [Tooltip("true면 Start()에서 자동 소환하지 않고 Trigger() 호출을 기다린다. BossRoomController 연출 후 소환 시 사용.")]
    [SerializeField] private bool waitForExternalTrigger;

    // ── Private ─────────────────────────────────────────────
    private bool      _spawned;
    private GameObject _spawnedBossGO;

    // ── Properties / Events ─────────────────────────────────

    /// <summary>항상 1 (보스는 1마리). RoomClearController가 킬 목표 합산에 사용.</summary>
    public int MaxTotalSpawns => 1;

    /// <summary>스폰 완료된 보스 인스턴스. 스폰 전에는 null. BossRoomController 소급 연결에 사용.</summary>
    public MonsterBase SpawnedBoss { get; private set; }

    /// <summary>보스가 스폰된 직후 발행. RoomClearController가 OnDied를 체이닝하는 데 사용.</summary>
    public event System.Action<MonsterBase> OnMonsterSpawned;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Lifecycle
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void Start()
    {
        if (waitForExternalTrigger) return;
        TrySpawn();
    }

    /// <summary>BossRoomController가 연출 완료 후 호출 — 보스 소환/활성화 시작.</summary>
    public void Trigger() => TrySpawn();

    private void TrySpawn()
    {
        if (_spawned) return;
        _spawned = true;

        if (placedBoss != null)
        {
            ActivatePlacedBossAsync().Forget();
            return;
        }

        if (spawnTable == null)
        {
            Debug.LogWarning("[BossSpawner] spawnTable이 비어 있습니다. Inspector에서 SO를 할당해주세요.", this);
            return;
        }
        SpawnBossAsync().Forget();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 소환 로직
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 직접 배치 보스 활성화
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private async UniTaskVoid ActivatePlacedBossAsync()
    {
        if (spawnDelay > 0f)
        {
            try
            {
                await UniTask.Delay(
                    System.TimeSpan.FromSeconds(spawnDelay),
                    cancellationToken: destroyCancellationToken);
            }
            catch (System.OperationCanceledException) { return; }
        }

        if (placedBoss == null) return;

        // 씬에 직접 배치된 보스는 이미 활성 상태 — SetActive 불필요
        // RoomClearController가 OnDied를 체이닝할 수 있도록 먼저 알림
        SpawnedBoss = placedBoss;
        OnMonsterSpawned?.Invoke(placedBoss);

        PlaySpawnEffectAsync(placedBoss.transform.position).Forget();

        // IBossEntrance 보스는 자체 등장 연출로 장비 디졸브를 직접 관리 — body 디졸브 스킵
        bool hasOwnEntrance = placedBoss is IBossEntrance;
        if (spawnDissolveDuration > 0f && !hasOwnEntrance)
        {
            DissolveEffect.PlayAppear(
                placedBoss.gameObject,
                spawnDissolveDuration,
                activationToken: placedBoss.ActivationToken,
                edgeColor: spawnOutlineColor);
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 어드레서블 직접 소환 보스
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void OnDestroy()
    {
        // 종료/씬 정리 시 Managers가 먼저 파괴되면 AddressableManager가 null이므로 ?. 가드
        if (_spawnedBossGO != null)
            Managers.AddressableManager?.ReleaseInstance(_spawnedBossGO);
    }

    private async UniTaskVoid SpawnBossAsync()
    {
        if (spawnDelay > 0f)
        {
            try
            {
                await UniTask.Delay(
                    System.TimeSpan.FromSeconds(spawnDelay),
                    cancellationToken: destroyCancellationToken);
            }
            catch (System.OperationCanceledException) { return; }
        }

        var entry = spawnTable.PickRandom(IsBossEntry);
        if (entry == null)
        {
            Debug.LogWarning(
                "[BossSpawner] Boss 등급 엔트리를 찾지 못했습니다. " +
                "SpawnTable에 Boss 등급 엔트리가 있는지 확인하세요.", this);
            return;
        }

        // 보스는 전투 중 단 1마리 — 풀러 대신 어드레서블에서 직접 인스턴스 생성.
        // 풀러 사용 시 프리웜 인스턴스가 공유 ScriptableObject의 BuiltConditions를 덮어써서
        // 마지막 인스턴스의 블랙보드가 캡처되는 버그가 발생한다.
        GameObject bossGO = null;
        try
        {
            bossGO = await Managers.AddressableManager.InstantiateAsync(entry.addressableKey);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[BossSpawner] '{entry.addressableKey}' 소환 예외: {ex.Message}", this);
            return;
        }

        if (bossGO == null)
        {
            Debug.LogWarning($"[BossSpawner] '{entry.addressableKey}' 소환 실패.", this);
            return;
        }

        // 스포너 위치·회전 적용 — 보스가 입장 방향을 바라보도록 Inspector에서 스포너를 정렬해둔다.
        bossGO.transform.SetPositionAndRotation(transform.position, transform.rotation);

        var boss = bossGO.GetComponent<MonsterBase>();
        if (boss == null)
        {
            Debug.LogWarning($"[BossSpawner] '{entry.addressableKey}' 프리팹에 MonsterBase가 없습니다.", this);
            Managers.AddressableManager.ReleaseInstance(bossGO);
            return;
        }

        _spawnedBossGO = bossGO;
        SpawnedBoss = boss;
        OnMonsterSpawned?.Invoke(boss);

        PlaySpawnEffectAsync(transform.position).Forget();

        // IBossEntrance 보스는 자체 등장 연출로 장비 디졸브를 직접 관리 — body 디졸브 스킵
        bool hasOwnEntrance = boss is IBossEntrance;
        if (spawnDissolveDuration > 0f && !hasOwnEntrance)
        {
            DissolveEffect.PlayAppear(
                boss.gameObject,
                spawnDissolveDuration,
                activationToken: boss.ActivationToken,
                edgeColor: spawnOutlineColor);
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private static bool IsBossEntry(SpawnEntry entry) => entry.grade == MonsterGrade.Boss;

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
            Debug.LogWarning($"[BossSpawner] SpawnEffect '{spawnEffectAddressKey}' 로드 실패: {e.Message}", this);
            return;
        }

        if (fx == null) return;

        fx.transform.SetPositionAndRotation(
            new Vector3(pos.x, pos.y + spawnEffectYOffset, pos.z),
            Quaternion.identity);

        if (spawnEffectScale != 1f)
            fx.transform.localScale *= spawnEffectScale;

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

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.35f);
        Gizmos.DrawSphere(transform.position, 1.5f);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, 1.5f);
    }
}
