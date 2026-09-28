using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 죽음의 기사 2페이지 KL4 「왕좌의 인력」(09-28 설계 확정 §5) — 멀어진 플레이어를 끌어오는 패턴(R2).
///
/// 흐름:
///  Enter            → 기사(유리벽 너머)에서 플레이어로 회색 사슬 · 기사 앞 세 줄에 회색 예고가 차오르기 시작
///  0 ~ pullSeconds  → 사슬이 플레이어를 유리벽 앞 한가운데로 끌어당긴다(pullSpeed m/s — 달리기보다 느려
///                     반대로 달리면 버틴다). 회피(대시)를 쓰면 사슬이 끊어진다.
///  pullSeconds      → 끌기 끝 · 예고가 판정 색으로 굳는다(베기 lockSeconds 전)
///  +lockSeconds     → 기사 앞 세 줄(유리벽 쪽부터)을 벤다
///
/// 끌기는 위치를 조금씩 옮길 뿐 플레이어 입력 · 속도는 그대로 둔다(벽 · 기둥 쪽이면 옮기지 않는다).
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/DeathKnight/Page2/DK_KL4_ThronePull", fileName = "DK_KL4_ThronePull")]
public class DKThronePullPatternSO : BossPatternSO
{
    [Header("사슬")]
    [Tooltip("끌어당기는 시간 — 설계 1.2초")]
    public float pullSeconds = 1.2f;
    [Tooltip("끌어당기는 속도(m/s). 플레이어 달리기(8)보다 느려야 반대로 달리면 버틴다")]
    public float pullSpeed   = 6.5f;
    [Tooltip("목표(유리벽 앞 가운데)에 이만큼 가까우면 더 끌지 않는다(m)")]
    public float stopDistance = 0.8f;
    public float chainWidth  = 0.45f;
    [Tooltip("사슬이 기사 몸에 닿는 높이 · 플레이어 몸에 닿는 높이")]
    public float knightChainHeight = 1.3f;
    public float playerChainHeight = 1.0f;

    [Header("베기")]
    [Tooltip("끌기가 끝나고 베기까지 — 판정 색으로 굳어 있는 시간. R3: 0.4 이상")]
    public float lockSeconds  = 0.4f;
    [Tooltip("기사 앞 몇 줄을 베는가")]
    [Min(1)] public int frontRows = 3;
    [Tooltip("공격 모션(Attack2)에서 칼이 내려오는 순간 — 베기보다 이만큼 먼저 모션을 건다")]
    public float swingLead    = 0.35f;
    public float recoveryTime = 0.6f;

    [Header("VFX · 사운드")]
    [Tooltip("줄 베기 이펙트 (Sword Slash 15)")]
    public GameObject slashVfxPrefab;
    public AudioClip  chainSfx;
    public AudioClip  chainBreakSfx;
    public AudioClip  bigSlashSfx;

    [Header("데미지")]
    public float damageMultiplier    = 1.4f;
    public float knockbackMultiplier = 1.2f;

    private DKThronePullState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new DKThronePullState(this);
    public override void OnRecycled()                      => _state = new DKThronePullState(this);

    public override bool CanExecute(BossPatternContext ctx)
        => ctx.Ctx.Runtime.PlayerTarget != null && ctx.Ctx.Monster is DeathKnightBossMonster;

    public override SpecialStateBase GetRuntimeState() => _state;
}

public class DKThronePullState : FullLockState<DKThronePullPatternSO>
{
    private const string ChannelAnim = "Idle2";
    private const string SlashAnim   = "Attack2";
    private const float  SweepSkin   = 0.05f;

    private DKPage2Zone   _zone;
    private LichChainLine _chain;
    private GameObject    _band;
    private float         _timer;
    private bool          _pulling;
    private bool          _locked;
    private bool          _swung;
    private bool          _slashed;

    public DKThronePullState(DKThronePullPatternSO data) : base(data) { }

    private float SlashTime => Data.pullSeconds + Data.lockSeconds;

    public override void Enter(MonsterContext ctx)
    {
        _zone    = (ctx.Monster as DeathKnightBossMonster)?.Page2Zone;
        _timer   = 0f;
        _locked  = false;
        _swung   = false;
        _slashed = false;
        _pulling = false;

        DKPage2Zone.StopAgent(ctx);
        DKPage2Zone.FacePlayer(ctx);
        DKPage2Zone.PlayAnim(ctx, ChannelAnim);
        if (_zone == null) return;

        // 기사 앞 세 줄 — 가로로 긴 띠 하나(기사를 보고 오른쪽으로 쓸어 간다)
        float   cs      = DKBossRoomContext.CellSize;
        Vector3 east    = new Vector3(_zone.TowardKnight, 0f, 0f);
        int     westCol = _zone.TowardKnight > 0 ? _zone.MinX : _zone.MaxX;
        Vector3 origin  = _zone.CellCenter(westCol, _zone.FrontRowAt(0)) - east * (cs * 0.5f);
        origin.z = (origin.z + _zone.CellCenter(westCol, _zone.FrontRowAt(Data.frontRows - 1)).z) * 0.5f;   // 세 줄 가운데
        _band = PatternGuideHelper.Prepare(
            PatternGuideHelper.Beam(origin, east, _zone.Columns * cs, Data.frontRows * cs, DKPage2Zone.Grey), DKPage2Zone.Grey);

        var player = ctx.Runtime.CachedPlayer;
        if (player != null)
        {
            _chain = LichChainLine.Create(ctx.Transform.position + Vector3.up * Data.knightChainHeight,
                                          player.transform.position + Vector3.up * Data.playerChainHeight,
                                          Data.chainWidth, DKPage2Zone.Grey);
            _chain.Follow(ctx.Transform, Vector3.up * Data.knightChainHeight, player.transform, Vector3.up * Data.playerChainHeight);
            _chain.Slack = 0.02f;
            _chain.Tension(1f);
            _chain.Flash(0.3f);
            _pulling = true;
            if (Data.chainSfx != null) Managers.Sound?.PlayEffectAt(Data.chainSfx, player.transform.position);
        }
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        if (_zone == null) { ctx.Monster.ChangeState<AttackReadyState>(); return; }

        if (_pulling)
        {
            if (_timer >= Data.pullSeconds) ReleaseChain(snapped: false);
            else                            Pull(ctx);
        }

        if (!_locked)
        {
            PatternGuideHelper.SetProgress(_band, _timer / Mathf.Max(0.01f, Data.pullSeconds));
            if (_timer >= Data.pullSeconds)
            {
                _locked = true;
                PatternGuideHelper.Arm(_band);   // R3 — 끌기가 끝나는 순간 판정 색
            }
        }

        if (!_swung && _timer >= SlashTime - Data.swingLead)
        {
            _swung = true;
            DKPage2Zone.PlayAnim(ctx, SlashAnim);
        }

        if (!_slashed && _timer >= SlashTime)
        {
            _slashed = true;
            Slash(ctx);
        }

        if (_slashed && _timer >= SlashTime + Data.recoveryTime)
            ctx.Monster.ChangeState<AttackReadyState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        ReleaseChain(snapped: false);
        PatternGuideHelper.SafeDestroy(ref _band);
        DKPage2Zone.RestoreAgent(ctx);
    }

    // ── 끌기 ───────────────────────────────────────────────

    /// <summary>
    /// 유리벽 앞 한가운데로 한 프레임만큼 옮긴다. 플레이어 속도는 건드리지 않아 반대로 달리면 버틴다.
    /// 회피 중이면 사슬이 끊어진다. 벽 · 기둥이 막으면(스윕) 옮기지 않는다.
    /// </summary>
    private void Pull(MonsterContext ctx)
    {
        var player = ctx.Runtime.CachedPlayer;
        if (player == null) { ReleaseChain(snapped: false); return; }

        if (player.LocoSM != null && player.LocoSM.CurrentId == LocoState.Dodge)
        {
            ReleaseChain(snapped: true);
            return;
        }
        _chain?.Tension(1f);

        var rb = player.Rigid;
        if (rb == null || rb.isKinematic) return;

        Vector3 target = new Vector3(ctx.Transform.position.x, rb.position.y, _zone.FrontLimitZ);
        Vector3 delta  = target - rb.position;
        delta.y = 0f;
        float dist = delta.magnitude;
        if (dist <= Data.stopDistance) return;

        Vector3 dir  = delta / dist;
        float   step = Mathf.Min(Data.pullSpeed * Time.deltaTime, dist - Data.stopDistance);
        if (rb.SweepTest(dir, out _, step + SweepSkin, QueryTriggerInteraction.Ignore)) return;
        rb.position += dir * step;
    }

    private void ReleaseChain(bool snapped)
    {
        _pulling = false;
        if (_chain == null) return;
        if (snapped)
        {
            _chain.Flash(0.2f);
            if (Data.chainBreakSfx != null) Managers.Sound?.PlayEffectAt(Data.chainBreakSfx, _chain.transform.position);
        }
        _chain.Dispose();
        _chain = null;
    }

    // ── 베기 ───────────────────────────────────────────────

    private void Slash(MonsterContext ctx)
    {
        int midCol = (_zone.MinX + _zone.MaxX) / 2;
        for (int i = 0; i < Data.frontRows; i++)
        {
            int row = _zone.FrontRowAt(i);
            _zone.SpawnLineVfx(Data.slashVfxPrefab, _zone.CellCenter(midCol, row), alongX: true, _zone.Columns);
        }
        Managers.Sound?.PlayEffectAt(Data.bigSlashSfx, _zone.CellCenter(midCol, _zone.FrontRowAt(1)), startTime: 0.5f);

        if (DKPage2Zone.TryPlayerCell(ctx, out var pc) && _zone.Contains(pc) && _zone.InFrontRows(pc.y, Data.frontRows))
            DKPage2Zone.HitPlayer(ctx, Data.damageMultiplier, Data.knockbackMultiplier, HitWeight.Heavy, ctx.Transform.position);

        BossImpactFeedback.TriggerHitStop(0.12f);
        BossImpactFeedback.TriggerCameraShake(0.16f, 0.35f);
    }
}
}
