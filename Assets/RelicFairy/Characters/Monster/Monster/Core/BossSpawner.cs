using RelicFairy.Monster;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 보스 방 전용 스포너. B 타일에 배치되며 방 진입 직후 보스 1마리를 소환한다.
/// MonsterSpawnTableSO에서 Boss 등급 엔트리만 선택한다.
/// RoomClearController가 OnMonsterSpawned를 통해 보스 사망을 추적한다.
/// </summary>
public class BossSpawner : MonoBehaviour
{
    [Header("스폰 테이블")]
    [Tooltip("Boss 등급 엔트리가 포함된 MonsterSpawnTableSO. 'Create > Abyss > Monster > Spawn Table'로 생성.")]
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

    // ── Properties / Events ─────────────────────────────────

    /// <summary>항상 1 (보스는 1마리). RoomClearController가 킬 목표 합산에 사용.</summary>
    public int MaxTotalSpawns => 1;

    /// <summary>보스가 스폰된 직후 발행. RoomClearController가 OnDied를 체이닝하는 데 사용.</summary>
    public event System.Action<MonsterBase> OnMonsterSpawned;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Lifecycle
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void Start()
    {
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

        MonsterBase boss = null;
        try
        {
            boss = await Managers.ObjectPooler.SpawnAsync<MonsterBase>(
                entry.addressableKey,
                ObjectPoolerManager.PoolType.Monster,
                transform.position,
                Quaternion.identity);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[BossSpawner] '{entry.addressableKey}' 스폰 예외: {ex.Message}", this);
            return;
        }

        if (boss == null)
        {
            Debug.LogWarning($"[BossSpawner] '{entry.addressableKey}' 스폰 실패.", this);
            return;
        }

        OnMonsterSpawned?.Invoke(boss);

        PlaySpawnEffectAsync(transform.position).Forget();

        var capturedBoss = boss;
        int capturedGen = capturedBoss.GenerationId;
        if (spawnDissolveDuration > 0f)
        {
            DissolveEffect.PlayAppear(
                boss.gameObject,
                spawnDissolveDuration,
                onComplete: () =>
                {
                    if (capturedBoss == null) return;
                    if (!capturedBoss.gameObject.activeInHierarchy) return;
                    if (capturedBoss.GenerationId != capturedGen) return;
                    capturedBoss.SetRandomNativeElement();
                },
                activationToken: boss.ActivationToken);
        }
        else
        {
            boss.SetRandomNativeElement();
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
