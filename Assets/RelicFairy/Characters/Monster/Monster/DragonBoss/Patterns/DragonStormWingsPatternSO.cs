using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// Boss pattern: hover in place → 평행 두 줄 예고(플레이어 양옆 · 가운데 안전 길 · 10-22 E3 D4) →
/// wing-strike animation + spawn WindBlast in each lane → (연계) 공중 물기 첫 입이 가운데 길을 노린다(D2).
/// </summary>
[CreateAssetMenu(fileName = "DragonStormWingsPattern",
    menuName = "RelicFairy/Boss/Dragon/StormWingsPattern")]
public class DragonStormWingsPatternSO : BossPatternSO
{
    [Header("비행")]
    [SerializeField] private float  _hoverHeight         = 5f;
    [SerializeField] private string _takeoffStateName    = "Takeoff";
    [SerializeField] private string _hoverStateName      = "URFlyStand";
    [SerializeField] private string _attackStateName     = "UAttackWindHighStart";
    [SerializeField] private string _landingStateName    = "Landing";

    [Header("경고 장판")]
    [SerializeField] private float  _warningDuration     = 2f;
    [Tooltip("한 줄 폭 (m) — 두 줄이 가운데 안전 길 양옆에 선다(10-22 E3 D4)")]
    [SerializeField] private float  _warningWidth        = 8f;
    [Tooltip("두 줄 사이 안전 길 폭 (m) — 예고 순간 플레이어 자리가 한가운데")]
    [SerializeField] private float  _laneGap             = 3f;
    [SerializeField] private float  _warningLength       = 14f;
    [SerializeField] private Color  _warningColor        = new Color(0.65f, 0.3f, 1.0f, 0.45f);

    [Header("공격")]
    [SerializeField] private float  _attackAnimDuration  = 1.8f;
    [SerializeField] private int    _attackDamage        = 25;
    [SerializeField] private float  _damageMultiplier    = 1.5f;
    [SerializeField] private GameObject _windBlastPrefab;
    [Tooltip("날개치기 발동 시 재생할 사운드")]
    [SerializeField] private AudioClip _wingStrikeSfx;
    [Tooltip("경고 장판 생성 후 사운드를 재생하기까지 지연 시간(초)")]
    [SerializeField] private float _wingStrikeSfxDelay = 1f;

    [Header("상태이상")]
    [SerializeField] private PlayerStatusEffectSO _statusEffect;

    [Header("EndPose (반격 창)")]
    [SerializeField] private float  _endPoseDuration     = 0.4f;

    [Header("원소")]
    [SerializeField] private DragonBossBlackboard.DragonElement _element = DragonBossBlackboard.DragonElement.Thunder;

    [Header("쿨다운")]
    [SerializeField] private float  _cooldown            = 18f;

    public DragonBossBlackboard.DragonElement Element => _element;
    public float  HoverHeight        => _hoverHeight;
    public string TakeoffStateName   => _takeoffStateName;
    public string HoverStateName     => _hoverStateName;
    public string AttackStateName    => _attackStateName;
    public string LandingStateName   => _landingStateName;
    public float  WarningDuration    => _warningDuration;
    public float  WarningWidth       => _warningWidth;
    public float  LaneGap            => Mathf.Max(0.5f, _laneGap);
    public float  WarningLength      => _warningLength;
    public Color  WarningColor       => _warningColor;
    public float  AttackAnimDuration => _attackAnimDuration;
    public int    AttackDamage       => _attackDamage;
    public float  DamageMultiplier   => _damageMultiplier;
    public GameObject WindBlastPrefab => _windBlastPrefab;
    public AudioClip  WingStrikeSfx   => _wingStrikeSfx;
    public float  WingStrikeSfxDelay => _wingStrikeSfxDelay;
    public PlayerStatusEffectSO StatusEffect => _statusEffect;
    public float  EndPoseDuration    => _endPoseDuration;
    public float  Cooldown           => _cooldown;

    private DragonStormWingsState _runtimeState;

    public override void Initialize(BossPatternContext ctx)
        => _runtimeState = new DragonStormWingsState(this);

    public override void OnRecycled() => _runtimeState?.Reset();

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        return ctx.Blackboard is DragonBossBlackboard bb
               && bb.BodyState == BodyState.Airborne
               && bb.LeapCooldown <= 0f;
    }

    public override SpecialStateBase GetRuntimeState() => _runtimeState;
}

// ────────────────────────────────────────────────────────────────────────────
// Runtime state
// ────────────────────────────────────────────────────────────────────────────

internal sealed class DragonStormWingsState : FullLockState<DragonStormWingsPatternSO>
{
    private enum Phase { Takeoff, Hover, Warning, Attack, EndPose, Landing, Done }

    private const float WindBlastAt = 0.4f;   // 날개치기 클립에서 돌풍(판정)이 나가는 비율
    private const int   LaneCount           = 2;    // 평행 두 줄(10-22 E3 D4)
    private const float SafeLaneKeepSeconds = 4f;   // 가운데 안전 길을 이어지는 공중 물기가 노릴 수 있는 시간(연계 · D2)

    private Phase   _phase;
    private float   _timer;
    private Vector3 _hoverPos;
    private int     _takeoffHash;
    private int     _landingHash;

    // 경고 장판 — 평행 두 줄(10-22 E3 D4)
    private readonly GameObject[] _guides      = new GameObject[LaneCount];
    private readonly Vector3[]    _laneCenters = new Vector3[LaneCount];
    private readonly float[]      _laneLengths = new float[LaneCount];
    private Vector3    _warnCenter;
    private Vector3    _safeOrigin;   // 가운데 안전 길 시작점(보스 발밑) — 이어지는 공중 물기가 노린다(D2)
    private Quaternion _warnRotation;
    private Vector3    _toPlayer;
    private bool       _windBlastSpawned;
    private bool       _wingStrikeSfxPlayed;

    /// <summary>예고 채움 시간 = 예고 + 날개를 쳐 돌풍이 나가기까지 — 다 차는 순간이 판정(10-08 점검: 다 찬 뒤 0.72초에 맞았다).</summary>
    private float FillSeconds => Data.WarningDuration + Data.AttackAnimDuration * WindBlastAt;

    internal DragonStormWingsState(DragonStormWingsPatternSO data) : base(data) { }

    internal void Reset()
    {
        _phase = Phase.Done;
        _timer = 0f;
        DestroyWarning();
    }

    // ── FSM ──────────────────────────────────────────────────────────────────

    public override void Enter(MonsterContext ctx)
    {
        GameCameraController.Instance?.DeactivateDragonTopDownView(0.8f);
        _phase            = Phase.Hover;
        _timer            = 0f;
        _windBlastSpawned = false;
        _wingStrikeSfxPlayed = false;
        _takeoffHash      = Animator.StringToHash(Data.TakeoffStateName);
        _landingHash      = Animator.StringToHash(Data.LandingStateName);
        _hoverPos         = ctx.Transform.position;
        _hoverPos.y       = Mathf.Max(ctx.Transform.position.y, ctx.Runtime.SpawnPosition.y + Data.HoverHeight);

        PlayAnim(ctx, Data.HoverStateName);

    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Takeoff:  UpdateTakeoff(ctx);  break;
            case Phase.Hover:    UpdateHover(ctx);    break;
            case Phase.Warning:  UpdateWarning(ctx);  break;
            case Phase.Attack:   UpdateAttack(ctx);   break;
            case Phase.EndPose:  UpdateEndPose(ctx);  break;
            case Phase.Landing:  UpdateLanding(ctx);  break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        if ((ctx.Monster as IBoss)?.Blackboard is DragonBossBlackboard exitBb)
            exitBb.LeapCooldown = Data.Cooldown;
        DestroyWarning();
    }

    // ── Takeoff ───────────────────────────────────────────────────────────────

    private void UpdateTakeoff(MonsterContext ctx)
    {
        FacePlayer(ctx);

        // Takeoff 클립이 제자리로 변경됨 → 코드에서 Y 상승 처리
        float targetY = ctx.Runtime.SpawnPosition.y + Data.HoverHeight;
        Vector3 p = ctx.Transform.position;
        p.y = Mathf.MoveTowards(p.y, targetY, ctx.Stat.moveSpeed * 2f * Time.deltaTime);
        ctx.Transform.position = p;

        if (!IsAnimNearEnd(ctx, _takeoffHash)) return;

        Vector3 pos = ctx.Transform.position;
        pos.y = targetY;
        ctx.Transform.position = pos;
        _hoverPos = pos;

        var bb = GetDragonBB(ctx);
        if (bb != null) bb.IsAirborne = true;

        _phase = Phase.Hover;
        _timer = 0f;
        PlayAnim(ctx, Data.HoverStateName);
    }

    // ── Hover ─────────────────────────────────────────────────────────────────

    private void UpdateHover(MonsterContext ctx)
    {
        ctx.Transform.position = _hoverPos;
        FacePlayer(ctx);

        // 1프레임 호버 후 경고 장판 생성
        _phase = Phase.Warning;
        _timer = 0f;
        CreateWarning(ctx);
    }

    // ── Warning ───────────────────────────────────────────────────────────────

    private void CreateWarning(MonsterContext ctx)
    {
        Color c = DragonAbyssFx.WithAlpha(PatternGuideHelper.AttackFill, Data.WarningColor.a);   // (10-23 가이드 색 통일) 속성 색은 돌풍 이펙트만 — 에셋 색은 알파만

        // 바닥 기준 위치 — 지형 z-fighting 방지용 0.3f 오프셋
        float groundY = DragonPatternFloorUtils.GetFloorY(ctx.Transform.position, ctx.Runtime.SpawnPosition.y) + 0.3f;
        Vector3 bossFloor = new Vector3(
            ctx.Transform.position.x, groundY, ctx.Transform.position.z);

        // 플레이어 방향 (수평 고정)
        if (ctx.Runtime.PlayerTarget != null)
        {
            _toPlayer = ctx.Runtime.PlayerTarget.position - bossFloor;
            _toPlayer.y = 0f;
        }
        else
        {
            _toPlayer = ctx.Transform.forward;
            _toPlayer.y = 0f;
        }
        if (_toPlayer.sqrMagnitude < 0.01f) _toPlayer = ctx.Transform.forward;
        _toPlayer.Normalize();

        // 경고장판이 항상 바닥 끝까지 닿도록 fallback을 룸 최대 크기로 보정
        float maxLength = Mathf.Max(Data.WarningLength, DragonPatternFloorUtils.GetRoomMaxExtent());
        float midLength = DragonPatternFloorUtils.DistanceToFloorEdge(bossFloor, _toPlayer, maxLength);

        _warnCenter   = bossFloor + _toPlayer * (midLength * 0.5f);   // 날갯짓 소리 자리 — 가운데 길 한가운데
        _warnRotation = Quaternion.LookRotation(_toPlayer, Vector3.up);
        _safeOrigin   = bossFloor;

        // 보스 → 플레이어 줄 양옆에 평행 두 줄(가운데 LaneGap은 안전 길) · 줄마다 바닥 끝까지 · 폭 WarningWidth — 속성 색으로 차오른다(10-22 E3 D4)
        Vector3 side   = Vector3.Cross(Vector3.up, _toPlayer);
        float   offset = (Data.LaneGap + Data.WarningWidth) * 0.5f;
        for (int i = 0; i < LaneCount; i++)
        {
            Vector3 origin = bossFloor + side * (i == 0 ? -offset : offset);
            float   length = Mathf.Max(1f, DragonPatternFloorUtils.DistanceToFloorEdge(origin, _toPlayer, maxLength));
            _laneLengths[i] = length;
            _laneCenters[i] = origin + _toPlayer * (length * 0.5f);
            _guides[i] = PatternGuideHelper.Prepare(
                PatternGuideHelper.Beam(origin, _toPlayer, length, Data.WarningWidth, c), c);
        }
    }

    private void UpdateWarning(MonsterContext ctx)
    {
        ctx.Transform.position = _hoverPos;

        if (!_wingStrikeSfxPlayed && _timer >= Data.WingStrikeSfxDelay)
        {
            _wingStrikeSfxPlayed = true;
            Managers.Sound?.PlayEffectAt(Data.WingStrikeSfx, _warnCenter);
        }

        SetGuideProgress(_timer / FillSeconds);

        if (_timer < Data.WarningDuration) return;

        for (int i = 0; i < LaneCount; i++)
            PatternGuideHelper.Arm(_guides[i]);   // 날개를 치는 동안 판정 색으로 — 채움은 돌풍이 나가는 순간까지 이어지고, 나가면 치운다
        SetGuideProgress(_timer / FillSeconds);

        _phase = Phase.Attack;
        _timer = 0f;
        PlayAnim(ctx, Data.AttackStateName);
    }

    // ── Attack ───────────────────────────────────────────────────────────────

    private void UpdateAttack(MonsterContext ctx)
    {
        ctx.Transform.position = _hoverPos;

        SetGuideProgress((Data.WarningDuration + _timer) / FillSeconds);   // 돌풍이 나간 뒤엔 가이드가 없어 아무 일도 안 한다
        if (!_windBlastSpawned && _timer >= Data.AttackAnimDuration * WindBlastAt)
        {
            _windBlastSpawned = true;
            SpawnWindBlast(ctx);
            DestroyWarning();
        }

        if (_timer < Data.AttackAnimDuration) return;

        _phase = Phase.EndPose;
        _timer = 0f;
    }

    private void UpdateEndPose(MonsterContext ctx)
    {
        ctx.Transform.position = _hoverPos;
        if (_timer < Data.EndPoseDuration) return;

        _phase = Phase.Done;
        ctx.Monster.ChangeState<AttackReadyState>();
    }

    // ── Landing ───────────────────────────────────────────────────────────────

    private void UpdateLanding(MonsterContext ctx)
    {
        // 착지 지점의 Y는 Spawn Y가 아닌 실제 바닥 높이를 사용 —
        // 그렇지 않으면 NavMeshAgent.Warp이 바닥과 어긋난 위치에서 실패해 착지 후 이동이 안 됨
        float targetY = DragonPatternFloorUtils.GetFloorY(ctx.Transform.position, ctx.Runtime.SpawnPosition.y);
        Vector3 pos   = ctx.Transform.position;
        pos.y = Mathf.MoveTowards(pos.y, targetY, ctx.Stat.moveSpeed * 2f * Time.deltaTime);
        ctx.Transform.position = pos;

        if (!IsAnimNearEnd(ctx, _landingHash)) return;

        RestoreAgent(ctx);
        _phase = Phase.Done;
        ctx.Monster.ChangeState<ChaseState>();
    }

    // ── WindBlast 스폰 ────────────────────────────────────────────────────────

    private void SpawnWindBlast(MonsterContext ctx)
    {
        // 줄마다 돌풍 — 판정 = 예고 두 줄 그대로(가운데 안전 길엔 없다) · 한 번만 맞는다(10-22 E3 D4)
        Color elementBase = DragonBossVisualHelper.GetElementColor(Data.Element);
        Color tint = new Color(elementBase.r, elementBase.g, elementBase.b, 1f);
        bool  hit  = false;
        for (int i = 0; i < LaneCount; i++)
        {
            SpawnLaneBlast(_laneCenters[i], _laneLengths[i], tint);
            if (!hit) hit = TryHitLane(ctx, _laneCenters[i], _laneLengths[i]);
        }

        // 이어지는 공중 물기(연계)가 노릴 가운데 안전 길 — 날개를 피해 머문 줄(10-22 E3 D2)
        var bb = GetDragonBB(ctx);
        if (bb == null) return;
        bb.SafeLaneOrigin = _safeOrigin;
        bb.SafeLaneDir    = _toPlayer;
        bb.SafeLaneUntil  = Time.time + SafeLaneKeepSeconds;
    }

    /// <summary>한 줄 돌풍 이펙트 — 줄 한가운데(지면 기준)에서 줄 길이만큼 · 속성 색.</summary>
    private void SpawnLaneBlast(Vector3 center, float length, Color tint)
    {
        if (Data.WindBlastPrefab == null) return;

        var go = BossEffectPool.SpawnOneShot(Data.WindBlastPrefab, center, _warnRotation, fallbackLifetime: 3f);
        if (go == null) return;
        go.transform.localScale = new Vector3(Data.WarningWidth, Data.WarningWidth * 0.5f, length);

        // 속성 색상 적용
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(tint);
        }
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var mat in r.materials)
            {
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
                if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     tint);
                if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", tint);
            }
        }
    }

    /// <summary>한 줄 판정 — 예고 직사각(폭 WarningWidth · 길이 length) 안의 플레이어. 맞혔으면 true.</summary>
    private bool TryHitLane(MonsterContext ctx, Vector3 center, float length)
    {
        Vector3 hitCenter = center;
        hitCenter.y = DragonPatternFloorUtils.GetFloorY(center, ctx.Runtime.SpawnPosition.y) + 1f;
        var hits = Physics.OverlapBox(
            hitCenter,
            new Vector3(Data.WarningWidth * 0.5f, 1.5f, length * 0.5f),
            _warnRotation);
        foreach (var col in hits)
        {
            var player = col.GetComponent<PlayerController>()
                      ?? col.GetComponentInParent<PlayerController>();
            if (player == null) continue;
            player.TakeDamage(Mathf.RoundToInt(ctx.Config.stat.attackPower * Data.DamageMultiplier), ctx.Monster.gameObject);   // 공격자 = 화룡(10-08 점검)
            Data.StatusEffect?.Apply(player);
            return true;
        }
        return false;
    }

    // ── 경고 장판 정리 ────────────────────────────────────────────────────────

    private void DestroyWarning()
    {
        for (int i = 0; i < LaneCount; i++)
            PatternGuideHelper.SafeDestroy(ref _guides[i]);
    }

    private void SetGuideProgress(float t)
    {
        for (int i = 0; i < LaneCount; i++)
            PatternGuideHelper.SetProgress(_guides[i], t);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        int hash = Animator.StringToHash(stateName);
        if (ctx.Animator.HasState(0, hash))
            ctx.Animator.CrossFade(stateName, 0.12f, 0, 0f);
    }

    private static bool IsAnimNearEnd(MonsterContext ctx, int hash)
    {
        if (ctx.Animator == null) return true;
        if (!ctx.Animator.HasState(0, hash)) return true;
        if (ctx.Animator.IsInTransition(0)) return false;
        var info = ctx.Animator.GetCurrentAnimatorStateInfo(0);
        if (info.shortNameHash != hash) return true;
        return info.normalizedTime >= 0.9f;
    }

    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.01f)
            ctx.Transform.rotation = Quaternion.Slerp(
                ctx.Transform.rotation, Quaternion.LookRotation(dir), 8f * Time.deltaTime);
    }

    private static void RestoreAgent(MonsterContext ctx)
        => DragonPatternFloorUtils.SnapToFloorAndRestoreAgent(ctx);

    private static DragonBossBlackboard GetDragonBB(MonsterContext ctx)
        => (ctx.Monster as DragonBossMonster)?.DragonBlackboard;
}
}
