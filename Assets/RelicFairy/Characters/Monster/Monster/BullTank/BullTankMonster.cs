
using UnityEngine;
using UnityEngine.AI;

namespace RelicFairy.Monster
{
/// <summary>
/// 중간 엘리트 보스 「황소 탱크」(10-12 · 기획/RelicFairy_중간엘리트보스_황소탱크_설계_20261012.md) — 가볍게: 기본 공격 2 + 주력 1.
///   ① 곤봉 내려찍기(Attack01) — 4 m 안 · 앞 부채꼴.
///   ② 도약 내려찍기(Jump) — 6~14 m · 이륙 순간 착지 원을 굳힌다.
///   ③ 돌진(Attack02 · 주력) — 5 m 밖 · 직선 줄 · 벽(NavMesh 경계)에 박히면 기절(받는 피해 +30%).
/// 예고는 공용 <see cref="MonsterTelegraph"/>(10-12) — 뜨는 순간 방향 · 자리를 굳히고, 앞줄이 끝에 닿는 순간이 판정이다.
/// 일반 몬스터 틀(등급 Elite · 포이즈 붙음 · 접두어 없음 — <see cref="MonsterSpawner.SpawnMidBossAsync"/>로만 나온다).
/// 깨어나면 포효 → 상단 체력바(「정예 보스」), 처치는 공용 사망 디졸브.
/// 예고 = 판정(모양 · 크기 · 시점) · 앞섬 내려찍기 0.7 · 도약 0.8 · 돌진 0.8초(기준 ≥ 0.45) · 이펙트는 행동하는 순간에만.
/// </summary>
public partial class BullTankMonster : MonsterBase
{
    // ── Constants ──────────────────────────────────────────────
    public const string Kind = "BullTank";
    private const float ChargeLead      = 0.8f;    // 돌진 — 따라 돌다가 달리기 이만큼 전에 방향을 굳히고 줄 예고(공용 예고 권장 0.6~1.0초)
    private const float FirstActDelay   = 1.0f;
    private const int   MaxRepeat       = 2;       // 같은 패턴 연속 상한
    private const string ExposedStatus  = "vulnerable";
    private static readonly Color DustTint = new(0.62f, 0.5f, 0.38f, 1f);

    // ── 애니 상태(전용 컨트롤러 — BullTankBuilderEditor가 만든다) ──
    private const string AnimRoar       = "Angry";
    private const string AnimSwing      = "Attack01";
    private const string AnimChargeIn   = "Charge_Start";
    private const string AnimChargeAim  = "Charge_Loop";
    private const string AnimRunIn      = "Run_Start";
    private const string AnimRunLoop    = "Run_Loop";
    private const string AnimChargeEnd  = "Charge_End";
    private const string AnimJumpIn     = "Jump_Start";
    private const string AnimJumpAir    = "Jump_Loop";
    private const string AnimJumpLand   = "Jump_End";
    private const string AnimIdle       = "Idle";
    private const string RootBone       = "DeformationSystem";   // 클립 루트(가져오기 루트 모션 노드가 비어 수평 이동이 몸에 남는다)

    // ── Serialized ─────────────────────────────────────────────
    [SerializeField] private string _configAddress = "BullTank/BullTankConfig";

    [Header("깨어남")]
    [SerializeField] private float _roarSeconds = 1.67f;   // Angry 클립(60 fps) — 입력 잠금 · 카메라 컷 없음

    [Header("① 내려찍기")]
    [SerializeField] private float _swingRange     = 4f;
    [SerializeField] private float _swingRadius    = 3.2f;
    [SerializeField] private float _swingAngle     = 90f;
    [SerializeField] private float _swingHitTime   = 0.85f;   // 곤봉 정점(클립 1.5초) — 실측 0.84~0.87초(10-14 정예 애니 점검 · 0.70은 휘두르기 전에 맞았다)
    [SerializeField] private float _swingDuration  = 1.40f;   // 클립 1.5초 중 회복 끝에서 끊는다
    [SerializeField] private float _swingCooldown  = 2.5f;
    [SerializeField] private float _swingDamageMul = 1.1f;

    [Header("② 도약")]
    [SerializeField] private float _leapMinDist    = 6f;
    [SerializeField] private float _leapMaxDist    = 14f;
    [SerializeField] private float _leapTakeoff    = 0.55f;   // 웅크림(Jump_Start 0.58초) → 이륙
    [SerializeField] private float _leapAir        = 0.8f;    // 공중(이륙 순간 착지 원이 생겨 착지까지 차오른다)
    [SerializeField] private float _leapHeight     = 3.0f;
    [SerializeField] private float _leapRadius     = 3.0f;
    [SerializeField] private float _leapRecover    = 1.0f;    // Jump_End 1.07초
    [SerializeField] private float _leapCooldown   = 8f;
    [SerializeField] private float _leapDamageMul  = 1.6f;

    [Header("③ 돌진(주력)")]
    [SerializeField] private float _chargeMinDist  = 5f;
    [SerializeField] private float _chargeIntro    = 0.57f;   // 발 구름(Charge_Start 0.57초)
    [SerializeField] private float _chargeAim      = 1.2f;    // 조준 — 끝 0.45초 전에 굳힘
    [SerializeField] private float _chargeRunIn    = 0.18f;   // Run_Start 0.18초
    [SerializeField] private float _chargeSpeed    = 16f;
    [SerializeField] private float _chargeMaxLen   = 18f;
    [SerializeField] private float _chargeWidth    = 2.2f;
    [SerializeField] private float _chargeHitPad   = 0.3f;
    [SerializeField] private float _chargeStun     = 3.8f;    // 벽에 박힘 — Attack02_End(1.9초) 뒤 비틀대는 대기로 이어 붙인다
    [SerializeField] private float _chargeEndClip  = 1.9f;
    [SerializeField] private float _chargeStop     = 1.0f;    // 벽 없이 끝까지 — 짧은 정지
    [SerializeField] private float _chargeCooldown = 7f;
    [SerializeField] private float _chargeDamageMul = 1.9f;
    [SerializeField] private float _stunDamageAmp  = 0.3f;    // 기절 동안 받는 피해 +30%(취약 아이콘)

    [Header("겉모습")]
    [Tooltip("악몽 모드 몸 재질(검은 몸 — 83 배치 설계)")]
    [SerializeField] private Material _nightmareBodyMaterial;
    [SerializeField] private float    _poiseScale = 3f;   // 정예 포이즈 6타 × 3 = 18타(6타면 약 2초마다 휘청 — 샌드백 위험 · 83 배치 설계)

    [Header("소리(없으면 건너뜀)")]
    [SerializeField] private AudioClip _roarSfx;
    [SerializeField] private AudioClip _swingSfx;
    [SerializeField] private AudioClip _landSfx;
    [SerializeField] private AudioClip _stompSfx;
    [SerializeField] private AudioClip _wallSfx;
    [Tooltip("빌린 소리를 낮춰 황소 몸집으로 — 숲 · 화룡과 같은 소리로 들리지 않게(매번 ±4% 흔들림)")]
    [SerializeField] private float _sfxPitch = 0.85f;

    // ── Private ────────────────────────────────────────────────
    private enum Pattern { None, Swing, Leap, Charge }

    private bool    _awake;
    private float   _nextActAt;
    private float   _swingReadyAt;
    private float   _leapReadyAt;
    private float   _chargeReadyAt;
    private Pattern _last;
    private int     _repeat;

    private Renderer[]   _bodyRenderers;   // 몸 색 재질(M_BullTank_URP_*)을 쓰는 렌더러
    private Material[][] _bodyOriginals;

    private RoarState   _roarState;
    private SwingState  _swingState;
    private LeapState   _leapState;
    private ChargeState _chargeState;

    // ── MonsterBase ────────────────────────────────────────────
    protected override string ConfigAddress   => _configAddress;
    protected override string DataAddress     => string.Empty;
    protected override float  HPBarHeadOffset => 0.4f;

    public override bool   IsEliteBoss => true;
    // 챕터 이름(10-16 사용자 「제대로 된 이름 · 챕터별 개성」) — 봉인기 「가시뿔 황소」 · 해방기부터 「사슬 끊은 가시뿔 황소」(머리 위는 영문 — 09-27)
    public override string BossName    => StoryProgress.IsLiberated ? "사슬 끊은 " + ThemeNameKo(_theme) : ThemeNameKo(_theme);
    public override string DisplayName => StoryProgress.IsLiberated ? "Unchained " + ThemeNameEn(_theme) : ThemeNameEn(_theme);
    protected override string StaggerStateName => AnimChargeEnd;   // 휘청 = 벽에 박힌 비틀거림(Hit 0.5초로는 1.5초를 못 채운다 · 10-14 정예 애니 점검)

    // ── Lifecycle ──────────────────────────────────────────────
    protected override void OnEnable()
    {
        _awake = false;
        _last = Pattern.None;
        _repeat = 0;
        _swingReadyAt = _leapReadyAt = _chargeReadyAt = 0f;
        MonsterRootXZLock.Attach(gameObject, RootBone);   // 클립의 수평 이동을 지운다 — 몸이 자리에서 최대 10.7 m 앞으로 나갔다(10-14)
        ResolveTheme();
        BeginKit();
        ApplyEraBody();
        base.OnEnable();
        if (Poise != null) Poise.MaxScale = _poiseScale;   // OnEnable의 포이즈 초기화 뒤에
    }

    protected override void Update()
    {
        base.Update();
        if (_runtime == null || _runtime.IsDead || IsCcActive || IsInSpecialState) return;
        if (!IsInState<ChaseState>()) return;

        if (!_awake) { Wake(); return; }
        if (Time.time >= _nextActAt) TryAct();
    }

    protected override void OnDisable()
    {
        EndKit();
        UnbindBossHudIfBound();
        base.OnDisable();
    }

    // 첫 스폰은 OnEnable 때 설정이 아직 없어 정예 포이즈가 없다 — 초기화 뒤에 다시 건다(10-12 실측)
    protected override void OnInitialized()
    {
        base.OnInitialized();
        if (Poise != null) Poise.MaxScale = _poiseScale;
    }

    // ── 추적 · 공격 훅 — 일반 공격은 쓰지 않고 패턴 셋만 ──
    public override bool ShouldEnterAttackReady(MonsterContext ctx) => false;
    public override bool ShouldGiveUpChase(MonsterContext ctx)      => !_awake && base.ShouldGiveUpChase(ctx);

    protected override void OnFatalDamage()
    {
        EndKit();   // 쓰러진 뒤 남은 강화(예고 뒤 일격 · 불길)가 치지 않게
        UnbindBossHudIfBound();
        base.OnFatalDamage();
    }

    // ── Private Methods ────────────────────────────────────────

    private void Wake()
    {
        _awake = true;
        _nextActAt = Time.time + _roarSeconds + FirstActDelay;
        BindBossHud();
        _roarState ??= new RoarState(this);
        ChangeState(_roarState);
        Debug.Log($"[BullTank] 깨어남 — {name}");
    }

    private void TryAct()
    {
        var player = PlayerPos(out bool ok);
        if (!ok) return;
        float d = Flat(player - transform.position).magnitude;
        float now = Time.time;

        bool canCharge = d >= _chargeMinDist && now >= _chargeReadyAt && Allowed(Pattern.Charge);
        bool canLeap   = d >= _leapMinDist && d <= _leapMaxDist && now >= _leapReadyAt && Allowed(Pattern.Leap);
        Pattern pick = Pattern.None;
        if (d <= _swingRange && now >= _swingReadyAt && Allowed(Pattern.Swing)) pick = Pattern.Swing;
        else if (canCharge && canLeap) pick = _last == Pattern.Charge ? Pattern.Leap : Pattern.Charge;   // 둘 다 되면 번갈아(10-12 실측: 돌진만 나와 도약 0회)
        else if (canCharge) pick = Pattern.Charge;
        else if (canLeap)   pick = Pattern.Leap;
        if (pick == Pattern.None) return;

        _repeat = pick == _last ? _repeat + 1 : 1;
        _last = pick;
        switch (pick)
        {
            case Pattern.Swing:  _swingReadyAt  = now + _swingCooldown;  ChangeState(_swingState  ??= new SwingState(this));  break;
            case Pattern.Leap:   _leapReadyAt   = now + _leapCooldown;   ChangeState(_leapState   ??= new LeapState(this));   break;
            case Pattern.Charge: _chargeReadyAt = now + _chargeCooldown; ChangeState(_chargeState ??= new ChargeState(this)); break;
        }
    }

    private bool Allowed(Pattern p) => p != _last || _repeat < MaxRepeat;

    private static bool IsBodyColour(Material m)
        => m != null && m.name.StartsWith("M_BullTank_URP_")
           && !m.name.Contains("Viking") && !m.name.Contains("Baton") && !m.name.Contains("Bodyguard");

    // 악몽 모드는 검은 몸 — 몸 색 재질만 바꾸고(옷 · 무기는 그대로) 아니면 챕터 톤(10-16). 등장 디졸브가 이 재질을 원본으로 잡는다
    private void ApplyEraBody()
    {
        if (_bodyRenderers == null)
        {
            var list = new System.Collections.Generic.List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (IsBodyColour(m)) { list.Add(r); break; }
            _bodyRenderers = list.ToArray();
            _bodyOriginals = new Material[_bodyRenderers.Length][];
            for (int i = 0; i < _bodyRenderers.Length; i++) _bodyOriginals[i] = _bodyRenderers[i].sharedMaterials;
        }
        bool black = StoryProgress.IsNightmareMode && _nightmareBodyMaterial != null;
        for (int i = 0; i < _bodyRenderers.Length; i++)
        {
            if (_bodyRenderers[i] == null) continue;
            var mats = (Material[])_bodyOriginals[i].Clone();
            for (int k = 0; k < mats.Length; k++)
                if (IsBodyColour(mats[k])) mats[k] = black ? _nightmareBodyMaterial : Tinted(mats[k], _theme);
            _bodyRenderers[i].sharedMaterials = mats;
        }
    }

    private void EndPattern()
    {
        _nextActAt = Time.time + 0.4f;
        ChangeState<ChaseState>();
    }

    private Vector3 PlayerPos(out bool ok)
    {
        var t = _runtime != null ? _runtime.PlayerTarget : null;
        ok = t != null;
        return ok ? t.position : transform.position;
    }

    private int Damage(float mul) => Mathf.Max(1, Mathf.RoundToInt(EffectiveAttackPower * AttackMultiplierNow * mul));

    private void HitPlayer(int dmg, HitWeight weight)
    {
        var pc = _runtime != null ? _runtime.CachedPlayer : null;
        if (pc != null) pc.TakeDamage(dmg, gameObject, false, weight);
    }

    private void Play(string state)
    {
        if (_animator == null || !_animator.HasState(0, Animator.StringToHash(state))) return;
        _animator.CrossFadeInFixedTime(state, 0.12f);
    }

    private void Sfx(AudioClip clip)
    {
        if (clip != null) Managers.Sound?.PlayEffectAt(clip, transform.position, 1f, _sfxPitch * Random.Range(0.96f, 1.04f));
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    private static readonly RaycastHit[] s_obstacleHits = new RaycastHit[16];

    /// <summary>
    /// 돌진 앞 벽 · 기둥 · 장식까지 거리 — NavMesh에 안 담긴 장애물(메모 「장애물 위 스폰」)을 돌진이 뚫고 넘어갔다(10-16 사용자).
    /// 몸 높이 구(球)로 쏘고(바닥 · 낮은 잔해는 넘는다) 몸 앞 길이만큼 덜 간다. 시작부터 겹친 것은 건너뛴다.
    /// </summary>
    private float ObstacleClear(Vector3 from, Vector3 dir, float maxLen)
    {
        const float CastRadius = 0.8f, CastHeight = 1.3f, FrontPad = 0.7f;
        int mask = LayerMask.GetMask("Wall", "Default");
        int n = Physics.SphereCastNonAlloc(from + Vector3.up * CastHeight, CastRadius, dir, s_obstacleHits, maxLen + FrontPad, mask, QueryTriggerInteraction.Ignore);
        float best = maxLen;
        for (int i = 0; i < n; i++)
        {
            var h = s_obstacleHits[i];
            if (h.collider == null || h.distance <= 0f || h.collider.transform.IsChildOf(transform)) continue;
            best = Mathf.Min(best, h.distance - FrontPad);
        }
        return best;
    }

    private void StopAgent()
    {
        if (_agent == null || !_agent.isActiveAndEnabled || !_agent.isOnNavMesh) return;
        _agent.isStopped = true;
        _agent.ResetPath();
        _agent.velocity = Vector3.zero;   // 남은 추적 속도로 내려찍기 자세가 미끄러졌다(10-14 실측 1.7 m/s)
    }

    private void ResumeAgent()
    {
        if (_agent == null || !_agent.isActiveAndEnabled) return;
        _agent.updatePosition = true;
        if (!_agent.isOnNavMesh) return;
        _agent.Warp(transform.position);
        _agent.isStopped = false;
    }

    private void FaceToward(Vector3 dir, float degPerSec)
    {
        dir = Flat(dir);
        if (dir.sqrMagnitude < 0.0001f) return;
        var want = Quaternion.LookRotation(dir.normalized);
        transform.rotation = degPerSec <= 0f ? want : Quaternion.RotateTowards(transform.rotation, want, degPerSec * Time.deltaTime);
    }

    private NavMeshQueryFilter Filter() => new()
    {
        agentTypeID = _agent != null ? _agent.agentTypeID : 0,
        areaMask    = NavMesh.AllAreas,
    };

    private static void Dust(Vector3 at, float scale) => RunFx.Play(RunFxSlot.Burst, at + Vector3.up * 0.1f, scale, DustTint);

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 깨어남 포효 — 끊기지 않는다(맞기는 함)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private sealed class RoarState : SpecialStateBase
    {
        private readonly BullTankMonster _b;
        private float _t;
        public RoarState(BullTankMonster b) { _b = b; }
        public override SpecialStateConstraint Constraints =>
            SpecialStateConstraint.UnInterruptible | SpecialStateConstraint.MovementLocked;

        public override void Enter(MonsterContext ctx)
        {
            _t = 0f;
            _b.StopAgent();
            _b.Play(AnimRoar);   // 플레이어 쪽으로는 포효하며 돈다(Update) — 시작 즉시 회전은 180°/프레임이었다(10-18 실측)
            _b.Sfx(_b._roarSfx);
        }

        public override void Update(MonsterContext ctx)
        {
            _t += Time.deltaTime;
            var p = _b.PlayerPos(out bool ok);
            if (ok) _b.FaceToward(p - _b.transform.position, RoarTurnRate);
            if (_t >= _b._roarSeconds) ctx.Monster.ChangeState<ChaseState>();
        }

        private const float RoarTurnRate = 300f;   // 포효하며 도는 초당 각도(10-18)

        public override void Exit(MonsterContext ctx) => _b.ResumeAgent();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // ① 곤봉 내려찍기 — 시작 순간 방향을 굳히고 부채꼴 예고(앞줄이 휘두름 정점에 끝에 닿는다)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private sealed class SwingState : SpecialStateBase
    {
        private readonly BullTankMonster _b;
        private float _t;
        private bool  _hit;
        private MonsterTelegraph _tele;
        public SwingState(BullTankMonster b) { _b = b; }
        public override SpecialStateConstraint Constraints =>
            SpecialStateConstraint.UnInterruptible | SpecialStateConstraint.MovementLocked;

        public override void Enter(MonsterContext ctx)
        {
            _t = 0f; _hit = false;
            _b.StopAgent();
            var p = _b.PlayerPos(out bool ok);
            if (ok) _b.FaceToward(p - _b.transform.position, 0f);
            _b.Play(AnimSwing);
            _tele = MonsterTelegraph.Cone(_b.transform.position, _b.transform.forward, _b._swingRadius, _b._swingAngle * 0.5f,
                                          _b._swingHitTime, MonsterTelegraph.Normal);
            Debug.Log($"[BullTank] 내려찍기 예고 시작 t={Time.time:F2}");
            Debug.Log($"[BullTank] 내려찍기 방향 굳힘 t={Time.time:F2}");
        }

        public override void Update(MonsterContext ctx)
        {
            _t += Time.deltaTime;
            if (!_hit && _t >= _b._swingHitTime)
            {
                _hit = true;
                Vector3 me = _b.transform.position;
                var pl = _b.PlayerPos(out bool ok);
                Vector3 to = Flat(pl - me);
                bool inside = ok && to.magnitude <= _b._swingRadius
                              && Vector3.Angle(_b.transform.forward, to) <= _b._swingAngle * 0.5f;
                if (inside) _b.HitPlayer(_b.Damage(_b._swingDamageMul), HitWeight.Medium);
                _b.Sfx(_b._swingSfx);
                // 흙먼지(RunFxSlot.Burst = 구형 폭발 ×1.6)는 뺐다 — 기본 타격이 「이상한 폭발」로 읽혔다(10-22 ⑧) · 궤적만
                _b.SlashFx(me, _b.transform.forward);   // 휘두르는 궤적(10-16 「모션에 맞는 이펙트」)
                _b.KitAfterSwing(me, _b.transform.forward, _b._swingRadius, _b._swingAngle * 0.5f);
                Debug.Log($"[BullTank] 내려찍기 판정 t={Time.time:F2}{(inside ? " — 명중" : "")}");
            }
            if (_t >= _b._swingDuration) _b.EndPattern();
        }

        public override void Exit(MonsterContext ctx)
        {
            if (_tele != null) _tele.Cancel();   // 판정 전에 끊기면 헛예고를 지운다(판정 뒤엔 이미 스스로 사라졌다)
            _tele = null;
            _b.ResumeAgent();
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // ② 도약 내려찍기 — 이륙 순간 착지 자리를 굳히고 원 예고(앞줄이 착지 순간 끝에 닿는다)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private sealed class LeapState : SpecialStateBase
    {
        private enum Phase { Crouch, Air, Recover }
        private readonly BullTankMonster _b;
        private Phase   _phase;
        private float   _t;
        private Vector3 _from, _to;
        private MonsterTelegraph _tele;
        public LeapState(BullTankMonster b) { _b = b; }
        public override SpecialStateConstraint Constraints =>
            SpecialStateConstraint.UnInterruptible | SpecialStateConstraint.MovementLocked;

        public override void Enter(MonsterContext ctx)
        {
            _phase = Phase.Crouch; _t = 0f;
            _b.StopAgent();
            _b.Play(AnimJumpIn);
        }

        public override void Update(MonsterContext ctx)
        {
            _t += Time.deltaTime;
            switch (_phase)
            {
                case Phase.Crouch:
                {
                    var p = _b.PlayerPos(out bool ok);
                    if (ok) _b.FaceToward(p - _b.transform.position, 540f);
                    if (_t < _b._leapTakeoff) break;
                    // 이륙 — 착지 자리를 지금 굳힌다(NavMesh 위로)
                    _from = _b.transform.position;
                    _to = ok ? p : _from;
                    if (NavMesh.SamplePosition(_to, out var hit, 2f, _b.Filter())) _to = hit.position;
                    else _to = _from;
                    _tele = MonsterTelegraph.Circle(_to, _b._leapRadius, _b._leapAir, MonsterTelegraph.Heavy, face: true);
                    if (_b._agent != null) _b._agent.updatePosition = false;
                    Dust(_from, 0.7f);   // 박차고 오르는 흙먼지(10-16)
                    _b.Play(AnimJumpAir);
                    _phase = Phase.Air; _t = 0f;
                    Debug.Log($"[BullTank] 도약 착지 자리 굳힘 t={Time.time:F2}(착지까지 {_b._leapAir:F2}초)");
                    break;
                }
                case Phase.Air:
                {
                    float k = Mathf.Clamp01(_t / _b._leapAir);
                    Vector3 pos = Vector3.Lerp(_from, _to, k) + Vector3.up * (_b._leapHeight * 4f * k * (1f - k));
                    _b.transform.position = pos;
                    _b.FaceToward(_to - _from, 0f);
                    if (k < 1f) break;
                    Land();
                    _phase = Phase.Recover; _t = 0f;
                    break;
                }
                case Phase.Recover:
                    if (_t >= _b._leapRecover) _b.EndPattern();
                    break;
            }
        }

        private void Land()
        {
            _b.transform.position = _to;
            _b.Play(AnimJumpLand);
            var pl = _b.PlayerPos(out bool ok);
            bool inside = ok && Flat(pl - _to).magnitude <= _b._leapRadius;
            if (inside) _b.HitPlayer(_b.Damage(_b._leapDamageMul), HitWeight.Heavy);
            _b.Sfx(_b._landSfx);
            RunFx.Play(RunFxSlot.Ring, _to + Vector3.up * 0.1f, _b._leapRadius / 1.36f, DustTint);   // 착지 충격파가 판정 원(3 m)까지 퍼진다(먼지 0.55는 0.4 m — 10-13 실측)
            BossImpactFeedback.TriggerCameraShake(0.08f, 0.25f);   // 살짝
            Debug.Log($"[BullTank] 도약 판정 t={Time.time:F2}{(inside ? " — 명중" : "")}");
            _b.KitAfterLeap(_to, _b._leapRadius);
        }

        public override void Exit(MonsterContext ctx)
        {
            if (_tele != null) _tele.Cancel();
            _tele = null;
            if (_phase == Phase.Air) _b.transform.position = _to;   // 공중에서 끊기면(사망 등) 착지 자리로
            _b.ResumeAgent();
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // ③ 돌진(주력) — 발 구름 → 조준(따라 돌다가 달리기 0.8초 전에 굳히고 줄 예고) → 달림 → 벽에 박히면 기절
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    private sealed class ChargeState : SpecialStateBase
    {
        private enum Phase { Intro, Aim, RunIn, Run, Stun, Stop }
        private readonly BullTankMonster _b;
        private Phase   _phase;
        private float   _t;
        private Vector3 _dir, _start;
        private float   _len, _moved, _trailAt;
        private bool    _wall, _hit, _locked, _dazed;
        private MonsterTelegraph _tele;
        public ChargeState(BullTankMonster b) { _b = b; }
        public override SpecialStateConstraint Constraints =>
            SpecialStateConstraint.UnInterruptible | SpecialStateConstraint.MovementLocked;

        public override void Enter(MonsterContext ctx)
        {
            _phase = Phase.Intro; _t = 0f; _moved = 0f; _hit = false; _locked = false; _dazed = false;
            _b.StopAgent();
            Aim();
            _b.Play(AnimChargeIn);
            _b.Sfx(_b._stompSfx);
        }

        public override void Update(MonsterContext ctx)
        {
            _t += Time.deltaTime;
            switch (_phase)
            {
                case Phase.Intro:
                    Aim();
                    _b.FaceToward(_dir, 360f);
                    if (_t < _b._chargeIntro) break;
                    _b.Play(AnimChargeAim);
                    _phase = Phase.Aim; _t = 0f;
                    break;

                case Phase.Aim:
                    if (!_locked)
                    {
                        Aim();
                        _b.FaceToward(_dir, 360f);
                        if (_t >= _b._chargeAim + _b._chargeRunIn - ChargeLead)
                        {
                            _locked = true;
                            _b.FaceToward(_dir, 0f);
                            // NavMesh에 없는 벽 · 기둥 · 장식에서도 자른다(굳히는 순간 한 번 — 예고 줄 = 실제 길이 · 박히면 기절)
                            float clear = _b.ObstacleClear(_b.transform.position, _dir, _len);
                            if (clear < _len) { _len = Mathf.Max(1f, clear); _wall = true; }
                            _tele = MonsterTelegraph.Line(_b.transform.position, _dir, _len + _b._chargeWidth * 0.5f, _b._chargeWidth, ChargeLead,   // 끝에서도 몸 반폭만큼 닿는다
                                                          MonsterTelegraph.Heavy, face: true);
                            Debug.Log($"[BullTank] 돌진 예고 시작 t={Time.time:F2} 길이 {_len:F1} m{(_wall ? " · 벽" : "")}");
                            Debug.Log($"[BullTank] 돌진 방향 굳힘 t={Time.time:F2}");
                        }
                    }
                    if (_t < _b._chargeAim) break;
                    _b.Play(AnimRunIn);
                    _phase = Phase.RunIn; _t = 0f;
                    break;

                case Phase.RunIn:
                    if (_t < _b._chargeRunIn) break;
                    _b.Play(AnimRunLoop);
                    _phase = Phase.Run; _t = 0f;
                    _start = _b.transform.position; _trailAt = 0f;
                    Debug.Log($"[BullTank] 돌진 판정 시작 t={Time.time:F2}");
                    break;

                case Phase.Run:
                {
                    float step = Mathf.Min(_b._chargeSpeed * Time.deltaTime, _len - _moved);
                    Vector3 before = _b.transform.position;
                    if (_b._agent != null && _b._agent.isOnNavMesh) _b._agent.Move(_dir * step);
                    float moved = Flat(_b.transform.position - before).magnitude;
                    _moved += moved;
                    if (_t >= _trailAt) { _trailAt = _t + 0.15f; _b.TrailFx(_b.transform.position - _dir * 0.8f); }   // 발밑 흙먼지 자국(10-16)
                    TryHit();
                    bool done = _moved >= _len - 0.05f;
                    bool blocked = moved < step * 0.3f;
                    if (!done && !blocked) break;
                    if (_wall || blocked) BeginStun(); else BeginStop();
                    break;
                }

                case Phase.Stun:
                    if (!_dazed && _t >= _b._chargeEndClip) { _dazed = true; _b.Play(AnimIdle); }   // 클립이 끝나도 기절은 이어진다
                    if (_t >= _b._chargeStun) _b.EndPattern();
                    break;

                case Phase.Stop:
                    if (_t >= _b._chargeStop) _b.EndPattern();
                    break;
            }
        }

        public override void Exit(MonsterContext ctx)
        {
            if (_tele != null) _tele.Cancel();
            _tele = null;
            _b.ResumeAgent();
        }

        private void Aim()
        {
            Vector3 from = _b.transform.position;
            var p = _b.PlayerPos(out bool ok);
            Vector3 to = ok ? Flat(p - from) : _b.transform.forward;
            _dir = to.sqrMagnitude > 0.01f ? to.normalized : _b.transform.forward;
            _len = _b._chargeMaxLen;
            _wall = false;
            if (_b._agent != null && _b._agent.isOnNavMesh
                && NavMesh.Raycast(from, from + _dir * _b._chargeMaxLen, out var hit, _b.Filter()))
            {
                _len  = Mathf.Max(1f, hit.distance - 0.4f);   // 경계(벽 · 장애물 · 허공 가장자리)에서 자른다 — 예고 줄 = 실제 돌진 길이
                _wall = true;
            }
        }

        private void TryHit()
        {
            if (_hit) return;
            var p = _b.PlayerPos(out bool ok);
            if (!ok || Flat(p - _b.transform.position).magnitude > _b._chargeWidth * 0.5f + _b._chargeHitPad) return;
            _hit = true;
            _b.HitPlayer(_b.Damage(_b._chargeDamageMul), HitWeight.Heavy);
            Debug.Log($"[BullTank] 돌진 명중 t={Time.time:F2}");
        }

        private void BeginStun()
        {
            _phase = Phase.Stun; _t = 0f;
            _b.Play(AnimChargeEnd);
            _b.Sfx(_b._wallSfx);
            Dust(_b.transform.position + _dir * 1.2f, 0.45f);
            BossImpactFeedback.TriggerCameraShake(0.06f, 0.2f);   // 살짝
            _b.ApplyDamageTakenAmp(_b._stunDamageAmp, _b._chargeStun, ExposedStatus);   // 체력바 아래 「취약」 아이콘
            Debug.Log($"[BullTank] 돌진 벽 충돌 — 기절 {_b._chargeStun:F1}초 t={Time.time:F2}");
            _b.KitAfterCharge(_start, _b.transform.position, _b._chargeWidth);
        }

        private void BeginStop()
        {
            _phase = Phase.Stop; _t = 0f;
            _b.Play(AnimIdle);
            Debug.Log($"[BullTank] 돌진 끝(벽 없음) t={Time.time:F2}");
            _b.KitAfterCharge(_start, _b.transform.position, _b._chargeWidth);
        }
    }
}
}
