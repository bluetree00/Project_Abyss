using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 낫 해골 — LichSkeletonSummonPattern이 소환하는 일반 몬스터(연출·UX 시나리오 §12-2).
/// 리치 모델에서 의상·책을 OnInitialized에서 끄고 <b>낫은 남긴</b> 작은 사신. 애니메이션은 리치 것을 그대로 쓴다.
/// 등장: 바닥 아래에서 riseSeconds 동안 떠오른다(그동안 이동·공격 없음, 맞기는 함).
/// 죽음: 뼛가루(SkeletonDeath 칸).
/// NavMesh가 없는 환경(테스트씬 등)에서도 플레이어를 직접 추적한다.
/// </summary>
public class LichSkeletonMonster : MonsterBase
{
    // ── 상수 ─────────────────────────────────────────────
    public const string PrefabAddress = "LichSkeleton/LichSkeleton";
    private const int   MaxConcurrent = 8;     // 동시 생존 상한 — 초과 시 가장 오래된 비봉인 해골 정리
    private const float Lifetime      = 25f;   // 개체 수명(초). 봉인 해골은 면제.
    private const float FallCullDepth = 12f;   // 스폰 높이보다 이만큼 아래로 떨어지면 정리 (지형 붕괴 대비)
    private const float RiseSeconds   = 0.8f;  // 바닥에서 떠오르는 시간 — 그동안 이동·공격 없음
    private const float RiseDepth     = 1.8f;  // 바닥 아래 이만큼에서 시작
    private const float DefaultReach  = 2.4f;  // 공격 모양이 부채꼴이 아닐 때 베기 이펙트 반경

    // ── MonsterBase 추상 멤버 ─────────────────────────────
    protected override string ConfigAddress   => "LichSkeleton/LichSkeletonConfig";
    protected override string DataAddress     => string.Empty;
    protected override string HeadBoneName    => null;
    protected override float  HPBarHeadOffset => 0.2f;

    // ── Private ────────────────────────────────────────────
    private static readonly HashSet<string> HiddenObjectNames = new()
    {
        "SK_BookOpen Equip",
        "Bookss",
        "Clothing",
        "SkirtSeparate",
        "HoodDown",
        "HoodUp",
    };

    // 살아있는 해골 레지스트리 — 동시 상한·수명·전투 종료 정리에 사용.
    private static readonly List<LichSkeletonMonster> Live = new();

    private float _spawnTime;
    private bool  _lifetimeExpired;
    private float _spawnY;
    private bool  _fellOut;
    private float _riseTimer;
    private bool  _hitWasDealt;   // 공격 판정 순간 감지(이번 공격의 판정이 났는가)
    private bool  _nameCleared;   // 머리 위 이름을 지웠다(스폰마다 한 번)

    // ── 수명주기 ──────────────────────────────────────────

    protected override void OnEnable()
    {
        base.OnEnable();
        _spawnTime       = Time.time;
        _lifetimeExpired = false;
        // 풀러가 SetActive 전에 위치를 확정하므로 여기서 스폰 높이를 캡처해도 안전하다.
        _spawnY          = transform.position.y;
        _fellOut         = false;
        _riseTimer       = RiseSeconds;
        _hitWasDealt     = false;
        _nameCleared     = false;
        transform.position += Vector3.down * RiseDepth;   // 바닥 아래에서 떠오른다
        Live.Add(this);
        EnforceCap();
        OnDied += HandleDied;
    }

    protected override void OnDisable()
    {
        OnDied -= HandleDied;
        Live.Remove(this);
        base.OnDisable();
    }

    protected override void Update()
    {
        if (_riseTimer > 0f)
        {
            TickRise();
            return;   // 떠오르는 동안 FSM 정지 — 이동·공격 없음
        }
        base.Update();
        if (_lifetimeExpired) return;
        if (_runtime != null && _runtime.IsDead) return;
        ClearNameLabel();
        TickSlashVfx();

        // 구멍으로 떨어진 개체 정리 — SkeletonDirectChaseState의 직선 이동은 사라진 바닥 위를 그대로 지난다.
        if (!_fellOut && transform.position.y < _spawnY - FallCullDepth)
        {
            _fellOut = true;
            HandleFellOutOfArena();
            return;
        }

        if (Lifetime > 0f && Time.time - _spawnTime >= Lifetime)
        {
            _lifetimeExpired = true;
            // 봉인 해골은 수명 면제 — 시간 초과로 사라지면 SealBreaker가 영구 무적(소프트락)된다.
            if (TryGetComponent<SealSkeletonMarker>(out _)) return;
            Cull(this);
        }
    }

    // ── FSM 오버라이드 ────────────────────────────────────

    protected override void RegisterStates()
    {
        base.RegisterStates();
        // NavMesh 없을 때도 직접 이동하는 ChaseState로 교체
        _fsm.RegisterAs<ChaseState>(new SkeletonDirectChaseState());
    }

    // NavMesh 유무와 관계없이 detectionRange 기준으로 감지
    public override bool ShouldStartChase(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || IsPlayerDead()) return false;
        return ctx.Runtime.DistToPlayer <= ctx.Detection.detectionRange;
    }

    public override bool ShouldGiveUpChase(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || IsPlayerDead()) return true;
        return ctx.Runtime.DistToPlayer > ctx.Detection.chaseGiveUpRange;
    }

    // ── OnInitialized ─────────────────────────────────────

    protected override void OnInitialized()
    {
        // 의상·무기 비활성화 — 뼈대(Phase2 스타일) 노출
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (HiddenObjectNames.Contains(t.name))
                t.gameObject.SetActive(false);
        }
    }

    // ── 내부 메서드 ────────────────────────────────────────

    /// <summary>바닥에서 떠오른다 — 빠르게 솟았다가 끝에 살짝 느려진다.</summary>
    private void TickRise()
    {
        _riseTimer -= Time.deltaTime;
        float k    = 1f - Mathf.Clamp01(_riseTimer / RiseSeconds);
        float ease = 1f - (1f - k) * (1f - k);
        Vector3 p  = transform.position;
        p.y        = _spawnY - RiseDepth * (1f - ease);
        transform.position = p;
    }

    /// <summary>
    /// 머리 위 이름을 지운다(HP바는 남긴다) — 해골이 여럿 몰리면 「리치 해골」 이름표가 겹쳐 화면을 덮었다(09-19).
    /// HP바는 스폰 뒤 비동기로 붙고, 풀에서 다시 켜질 때 이름을 다시 쓰므로 붙은 뒤 한 번 지운다.
    /// </summary>
    private void ClearNameLabel()
    {
        if (_nameCleared || HpBar == null) return;
        HpBar.SetMonsterInfo(string.Empty);
        _nameCleared = true;
    }

    /// <summary>
    /// 낫 베기 이펙트 — 공격 판정이 나는 순간(AttackHitDealt가 켜질 때) 몸 방향 부채꼴 크기로.
    /// 공용 부채꼴 공격(MonsterConeAttackSO)은 이펙트를 내지 않아 해골 베기가 맨몸으로 보였다(09-19 사용자 지적).
    /// </summary>
    private void TickSlashVfx()
    {
        bool dealt = _runtime != null && _runtime.AttackHitDealt;
        if (dealt && !_hitWasDealt)
        {
            float reach = _config?.stat?.attackShape is MonsterConeAttackSO cone ? cone.range : DefaultReach;
            LichPatternUtil.SlashVfx(transform.position, transform.forward, reach, LichSwing.RightToLeft);
            LichSfx.Play(LichSfxSlot.ScytheSwing, transform.position, 0.45f);
        }
        _hitWasDealt = dealt;
    }

    private void HandleDied(MonsterBase _)
    {
        LichVfx.Play(LichVfxSlot.SkeletonDeath, transform.position + Vector3.up * 0.8f, Quaternion.identity);
        LichSfx.Play(LichSfxSlot.Collapse, transform.position, 0.35f);
    }

    /// <summary>
    /// 아레나 밖(구멍)으로 떨어진 해골 처리.
    ///
    /// 봉인 해골을 그냥 풀에 반납하면 MonsterBase.OnDied가 발행되지 않아
    /// SealBreaker의 남은 봉인 수가 줄지 않는다. sealActiveTime 기본값이 0(제한 없음)이라
    /// 리치가 영구 무적으로 남는다 — 수명 면제와 같은 이유다. 정상 사망 경로를 태워 봉인을 해제한다.
    /// </summary>
    private void HandleFellOutOfArena()
    {
        if (TryGetComponent<SealSkeletonMarker>(out _))
        {
            // TakeDamage → OnFatalDamage → DieState → RaiseDied → SealSkeletonMarker가 봉인 해제
            TakeDamage(999999f, null);
            return;
        }

        Cull(this);
    }

    // ── 정적 관리 ──────────────────────────────────────────

    /// <summary>전투 종료(보스 퇴각·사망) 시 생존 해골을 모두 정리한다.</summary>
    public static void DespawnAll()
    {
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            var s = Live[i];
            if (s != null) Managers.ObjectPooler.Despawn(s.gameObject);
        }
        Live.Clear();
    }

    /// <summary>동시 상한 초과 시 가장 오래된 비봉인 해골부터 정리.</summary>
    private static void EnforceCap()
    {
        int idx = 0;
        while (Live.Count > MaxConcurrent && idx < Live.Count)
        {
            var s = Live[idx];
            if (s == null)                                    { Live.RemoveAt(idx); continue; }
            if (s.TryGetComponent<SealSkeletonMarker>(out _)) { idx++; continue; } // 봉인 해골 면제
            Cull(s);
        }
    }

    private static void Cull(LichSkeletonMonster s)
    {
        Live.Remove(s);
        if (s != null) Managers.ObjectPooler.Despawn(s.gameObject);
    }
}

/// <summary>
/// NavMesh 없는 환경에서도 Transform을 직접 이동시키는 ChaseState.
/// NavMesh가 있으면 기존 NavMeshAgent 경로 탐색으로 동작한다.
/// </summary>
public class SkeletonDirectChaseState : ChaseState
{
    public override void Update(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Monster.IsPlayerDead())
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            base.Update(ctx);
            return;
        }

        // NavMesh 없음 — 직접 이동
        if (ctx.Monster.ShouldGiveUpChase(ctx))
        {
            ctx.Monster.ChangeState<PatrolState>();
            return;
        }

        if (ctx.Monster.ShouldEnterAttackReady(ctx))
        {
            ctx.Monster.ChangeState<AttackReadyState>();
            return;
        }

        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
        {
            float speed = ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier;
            ctx.Transform.position += dir.normalized * speed * Time.deltaTime;
            ctx.Transform.rotation = Quaternion.Slerp(
                ctx.Transform.rotation,
                Quaternion.LookRotation(dir),
                Time.deltaTime * 10f);
        }

        KeepChaseAnimation(ctx);
    }
}
}
