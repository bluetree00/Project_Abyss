using UnityEngine;
using Cysharp.Threading.Tasks;


namespace RelicFairy.Monster
{
/// <summary>
/// 사망 상태.
/// - NavMeshAgent 비활성화, 콜라이더 비활성화
/// - 사망 애니메이션 재생
/// - 3초 후 ObjectPoolerManager에 반환
/// </summary>
public class DieState : IMonsterState
{
    public virtual void Enter(MonsterContext ctx)
    {
        ctx.Runtime.IsDead = true;
        ctx.Monster.HideWorldHPBar();

        // 외부 수명주기 구독자(방 클리어 카운터 등)에 사망 통지
        ctx.Monster.RaiseDied();

        // 골드 코인 드롭
        SpawnGoldDrop(ctx);

        // 정수 조각은 처치 N마리마다 한 번 떨어진다 — 그 자리를 트래커에 알려준다.
        // (QuestEvents.OnMonsterKilled는 이름만 넘겨 위치를 모른다.)
        if (ctx.Monster != null)
            GameRunBootstrapper.Instance?.EssenceTracker?.ReportKillPosition(ctx.Monster.transform.position);

        // 처치 연출 — 막타(킬) 히트스톱 + 사망 위치 VFX 버스트
        //
        // 히트스톱은 <b>플레이어가 직접 때려서 죽였을 때만</b> 건다. 화상·독 틱(Dot)이나 장판·시너지
        // 즉발(Synergy)로 죽은 것까지 걸면, 후반부에 0.09초 프리즈가 0.5초마다 연쇄로 터져
        // 게임 전체가 렉 걸린 것처럼 보인다(타격 입력과 무관한 정지라 손맛에도 기여하지 않는다).
        var fx = ctx.Death;
        if (fx != null)
        {
            bool directHitKill = ctx.Monster.LastDamageKind == DamageKind.Normal;
            if (directHitKill && fx.killHitStopDuration > 0f)
                HitFeelService.KillImpact(fx.killHitStopScale, fx.killHitStopDuration);
            if (!string.IsNullOrEmpty(fx.deathVfxKey))
                SpawnDeathVfx(ctx.Monster.transform.position, fx.deathVfxKey, fx.deathVfxScale).Forget();
        }

        // 이동 중지
        ctx.Agent.enabled = false;

        // Rigidbody 속도 초기화 후 kinematic 전환 (사망 시 밀려남 방지)
        // kinematic 상태에서는 velocity 할당이 경고를 내므로 비활성화 상태에서만 초기화.
        var rb = ctx.Monster.GetComponent<Rigidbody>();
        if (rb != null)
        {
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            rb.isKinematic = true;
        }

        // 콜라이더 비활성화 (루트 + 자식 포함, 더 이상 피격되지 않도록)
        foreach (var col in ctx.Monster.GetComponentsInChildren<Collider>())
            col.enabled = false;

        // 사망 애니메이션 즉시 전환 (dieTrigger 미설정 시 dieStateName으로 fallback)
        string dieAnim = !string.IsNullOrEmpty(ctx.Animation.dieTrigger)
            ? ctx.Animation.dieTrigger
            : ctx.Animation.dieStateName;
        if (ctx.Animator != null && !string.IsNullOrEmpty(dieAnim))
        {
            ctx.Animator.speed = 1f;
            ctx.Animator.CrossFade(dieAnim, 0.1f, 0, 0f);
        }

        // 지연 파괴(+ 소멸 디졸브)
        float despawnDelay     = fx?.despawnDelay     ?? 3f;
        float dissolveDuration = fx?.dissolveDuration ?? 0f;
        DespawnAsync(ctx.Monster, despawnDelay, dissolveDuration).Forget();
    }

    public virtual void Update(MonsterContext ctx) { }

    public virtual void Exit(MonsterContext ctx) { }

    // ── 드롭 ──────────────────────────────────────────────

    private static void SpawnGoldDrop(MonsterContext ctx)
    {
        var config = ctx.Config;
        if (config == null) return;

        var drop = config.drop;
        if (drop == null || drop.coinMax <= 0 || drop.coinValue <= 0) return;

        int min = Mathf.Max(0, drop.coinMin);
        int max = Mathf.Max(min, drop.coinMax);
        int count = UnityEngine.Random.Range(min, max + 1);
        if (count <= 0) return;

        count = Mathf.Max(1, Mathf.RoundToInt(count * GoldScale(config.grade)));

        GoldCoinPickup.SpawnDrops(ctx.Monster.transform.position, count, drop.coinValue);
    }

    /// <summary>
    /// 등급별 골드 배율. 등급 간 낙차를 만들되(정예 1마리 = Common 4마리) <b>상점 충당률을 무너뜨리지 않는</b> 선.
    ///
    /// 왜 배율이 필요한가: MonsterConfigSO 36개 중 30개에 drop 블록이 없어 C# 기본값(5~8)으로 역직렬화되고,
    /// 값이 있는 6개도 5개가 같은 5~8이다. 즉 <b>슬라임과 흑기사와 보스가 같은 골드</b>를 준다.
    ///
    /// ⚠️ 계수는 <b>런의 실제 등급 구성</b>에 묶여 있다. 현재 구성은 Common 31% / Rare 38% / Elite 32%
    /// (룸풀 토큰 기준)이고, 이 값이면 가중평균 0.93 → 런당 약 2,200골드 → 상점 진열가 3,351 대비
    /// <b>충당률 66%</b>다. 진열의 3분의 2만 살 수 있어야 "무엇을 포기할까"가 성립한다.
    /// 룸풀 토큰의 등급 비중을 바꾸면 <b>여기도 함께 재계산해야 한다</b> —
    /// 구 구성(C60/R28/E12) 기준 계수를 새 구성에 그대로 쓰면 가중평균이 1.42로 튀어 충당률 101%가 된다(실측).
    /// </summary>
    private static float GoldScale(MonsterGrade grade) => grade switch
    {
        MonsterGrade.Rare  => 0.8f,
        MonsterGrade.Elite => 1.6f,
        MonsterGrade.Boss  => 5f,
        _                  => 0.4f,
    };

    // ── 처치 VFX ──────────────────────────────────────────

    private static async UniTaskVoid SpawnDeathVfx(Vector3 pos, string key, float scale)
    {
        var pooler = Managers.ObjectPooler;
        if (pooler == null) return;

        GameObject go;
        try
        {
            go = await pooler.SpawnAsync(
                key, ObjectPoolerManager.PoolType.Effect, pos, Quaternion.identity);
        }
        catch (System.OperationCanceledException) { return; }
        if (go == null) return;

        var ps = go.GetComponent<ParticleSystem>() ?? go.GetComponentInChildren<ParticleSystem>();
        float lifetime = ps != null
            ? ps.main.duration + ps.main.startLifetimeMultiplier + 0.3f
            : 2f;

        var vfx = go.GetComponent<PooledOneShotVfx>() ?? go.AddComponent<PooledOneShotVfx>();
        vfx.Play(lifetime, Mathf.Max(0.001f, scale));
    }

    // ── 비동기 파괴 ────────────────────────────────────────

    private static async UniTaskVoid DespawnAsync(MonsterBase monster, float despawnDelay, float dissolveDuration)
    {
        if (monster == null) return;

        // destroyCancellationToken(파괴) + ActivationToken(OnDisable=풀 반환) 링크.
        // 디졸브 진행 중 외부 Despawn(방 클리어 등)으로 풀 반환되면 ActivationToken 취소 →
        // 디졸브가 onDespawn 미호출로 중단 → 이중 Despawn 레이스 차단. 머티리얼 복원은 디졸브 finally가 담당.
        using var cts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(
            monster.destroyCancellationToken, monster.ActivationToken);
        var ct = cts.Token;

        try
        {
            await UniTask.Delay(
                System.TimeSpan.FromSeconds(despawnDelay),
                cancellationToken: ct);
        }
        catch (System.OperationCanceledException) { return; }

        if (monster == null) return;

        if (dissolveDuration > 0f)
        {
            await DissolveEffect.PlayDeathDissolveAsync(
                monster.gameObject, dissolveDuration,
                () => { if (monster != null) Managers.ObjectPooler.Despawn(monster.gameObject); },
                ct);
        }
        else
        {
            Managers.ObjectPooler.Despawn(monster.gameObject);
        }
    }
}
}
