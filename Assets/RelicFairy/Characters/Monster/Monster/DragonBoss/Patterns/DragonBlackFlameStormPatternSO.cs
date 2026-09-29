using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DL1 「흑염 폭풍」 — 2페이지(심연) 일반 패턴. 검은 불 브레스 두 줄.
///   줄마다: 조준(앞부분은 플레이어를 따라 돌고, 마지막 <c>_lockSeconds</c>는 고정 · 판정 색 — 09-28 규칙 R3)
///   → 발사(줄 전체 즉시 판정) → 지나간 줄은 몇 초간 불타는 띠(<see cref="DragonAbyssFlameStrip"/>, 동시 최대 2줄).
/// 지상 · 공중 어디서든 쓴다(공중이면 그 자리 체공). 거리 제한 없음 — 줄이 바닥 끝까지 닿는다(R6).
/// </summary>
[CreateAssetMenu(fileName = "DragonBlackFlameStormPattern",
    menuName = "RelicFairy/Boss/Dragon/BlackFlameStormPattern")]
public class DragonBlackFlameStormPatternSO : BossPatternSO
{
    [Header("쿨다운")]
    [SerializeField] private float _cooldown = 16f;

    [Header("줄")]
    [SerializeField] private int   _lineCount      = 2;
    [Tooltip("조준 전체 시간 (초) — 마지막 LockSeconds 동안은 조준을 멈춘다")]
    [SerializeField] private float _aimSeconds     = 1.2f;
    [Tooltip("판정 전 조준을 멈추고 판정 색으로 바꾸는 시간 (초) — R3: 0.4 이상")]
    [SerializeField] private float _lockSeconds    = 0.4f;
    [Tooltip("조준 중 플레이어를 따라 도는 속도 (도/초)")]
    [SerializeField] private float _trackDegPerSec = 120f;
    [Tooltip("한 줄을 쏜 뒤 다음 줄 조준까지 (초)")]
    [SerializeField] private float _lineGap        = 0.35f;
    [SerializeField] private float _lineWidth      = 3f;
    [Tooltip("줄 최대 길이 — 바닥 경계가 더 가까우면 경계까지")]
    [SerializeField] private float _maxLength      = 80f;

    [Header("피해")]
    [Tooltip("발사 판정 = 공격력 × 이 값 (강)")]
    [SerializeField] private float _hitDamageMult   = 1.4f;
    [Tooltip("불타는 띠가 남는 시간 (초)")]
    [SerializeField] private float _stripSeconds    = 6f;
    [Tooltip("띠 안 0.5초마다 공격력 × 이 값 (약)")]
    [SerializeField] private float _stripDamageMult = 0.12f;

    [Header("이펙트")]
    [Tooltip("발사 순간 줄을 따라 번지는 검은 불 (Hit 3 black fire)")]
    [SerializeField] private GameObject _waveVfxPrefab;
    [SerializeField] private float      _waveVfxScale   = 1.4f;
    [SerializeField] private float      _waveSpacing    = 3f;
    [Tooltip("입에서 줄 끝까지 불이 번지는 속도 (m/s, 시각 전용 — 판정은 발사 순간)")]
    [SerializeField] private float      _waveSpeed      = 45f;
    [Tooltip("입 앞 섬광 (Flash 3 black fire)")]
    [SerializeField] private GameObject _mouthVfxPrefab;
    [SerializeField] private float      _mouthVfxScale  = 1.5f;
    [Tooltip("불타는 띠를 따라 늘어놓는 불꽃 — 비우면 바닥 띠만")]
    [SerializeField] private GameObject _stripVfxPrefab;
    [SerializeField] private float      _stripVfxScale   = 0.8f;
    [SerializeField] private float      _stripVfxSpacing = 4f;
    [SerializeField] private AudioClip  _breathSfx;

    [Header("애니메이션")]
    [SerializeField] private string _aimStateName  = "UAttack FireBreath L";
    [SerializeField] private string _fireStateName = "UAttack FireBreath Loop";
    [Tooltip("공중에서 쓸 때 체공 모션")]
    [SerializeField] private string _airStateName  = "Airborne_Hover";
    [SerializeField] private float  _endPoseSeconds = 0.5f;

    public float      Cooldown        => _cooldown;
    public int        LineCount       => Mathf.Max(1, _lineCount);
    public float      AimSeconds      => Mathf.Max(_aimSeconds, _lockSeconds);
    public float      LockSeconds     => _lockSeconds;
    public float      TrackDegPerSec  => _trackDegPerSec;
    public float      LineGap         => _lineGap;
    public float      LineWidth       => _lineWidth;
    public float      MaxLength       => _maxLength;
    public float      HitDamageMult   => _hitDamageMult;
    public float      StripSeconds    => _stripSeconds;
    public float      StripDamageMult => _stripDamageMult;
    public GameObject WaveVfxPrefab   => _waveVfxPrefab;
    public float      WaveVfxScale    => _waveVfxScale;
    public float      WaveSpacing     => Mathf.Max(0.5f, _waveSpacing);
    public float      WaveSpeed       => Mathf.Max(1f, _waveSpeed);
    public GameObject MouthVfxPrefab  => _mouthVfxPrefab;
    public float      MouthVfxScale   => _mouthVfxScale;
    public GameObject StripVfxPrefab  => _stripVfxPrefab;
    public float      StripVfxScale   => _stripVfxScale;
    public float      StripVfxSpacing => _stripVfxSpacing;
    public AudioClip  BreathSfx       => _breathSfx;
    public string     AimStateName    => _aimStateName;
    public string     FireStateName   => _fireStateName;
    public string     AirStateName    => _airStateName;
    public float      EndPoseSeconds  => _endPoseSeconds;

    private DragonBlackFlameStormState _runtimeState;

    public override void Initialize(BossPatternContext ctx) => _runtimeState = new DragonBlackFlameStormState(this);

    public override void OnRecycled() => _runtimeState?.Reset();

    public override bool CanExecute(BossPatternContext ctx)
        => ctx.Ctx.Runtime.PlayerTarget != null
           && ctx.Blackboard is DragonBossBlackboard bb
           && bb.BlackFlameStormCooldown <= 0f;

    /// <summary>후반 연계(원소 합주 → 흑염 폭풍, 2페이지 구성 §9) — 연계는 원소 합주의 일부라 흑염 쿨다운을 보지 않는다.</summary>
    public override bool CanFollowUp(BossPatternContext ctx)
        => ctx.Ctx.Runtime.PlayerTarget != null && ctx.Blackboard is DragonBossBlackboard;

    public override SpecialStateBase GetRuntimeState() => _runtimeState ??= new DragonBlackFlameStormState(this);
}

// ────────────────────────────────────────────────────────────────────────────
// Runtime state
// ────────────────────────────────────────────────────────────────────────────

/// <summary>Aim(추적 → 고정) → Fire → Gap → Aim … → EndPose → 복귀. 중단 불가 · 이동 잠금.</summary>
internal sealed class DragonBlackFlameStormState : FullLockState<DragonBlackFlameStormPatternSO>
{
    private enum Phase { Aim, Gap, EndPose, Done }

    private Phase      _phase;
    private float      _timer;
    private int        _linesFired;
    private bool       _locked;
    private bool       _airborne;
    private Vector3    _hoverPos;
    private Transform  _mouth;
    private bool       _mouthSearched;
    private GameObject _guide;
    private Vector3    _lineOrigin;
    private Vector3    _lineDir;
    private float      _lineLength;

    // 발사 뒤 줄을 따라 번지는 불(시각) — 다음 줄 조준과 겹쳐도 계속 번진다
    private Vector3 _waveOrigin;
    private Vector3 _waveDir;
    private float   _waveLength;
    private float   _waveTraveled;
    private float   _waveNextAt = float.PositiveInfinity;

    internal DragonBlackFlameStormState(DragonBlackFlameStormPatternSO data) : base(data) { }

    internal void Reset()
    {
        _phase      = Phase.Done;
        _waveNextAt = float.PositiveInfinity;
        PatternGuideHelper.SafeDestroy(ref _guide);
    }

    public override void Enter(MonsterContext ctx)
    {
        GameCameraController.Instance?.DeactivateDragonTopDownView(0.8f);
        if (!_mouthSearched)
        {
            _mouth         = FindBone(ctx.Transform, "Jaw");
            _mouthSearched = true;
        }

        _airborne   = DragonAbyssFx.IsAirborne(ctx);
        _hoverPos   = ctx.Transform.position;
        _linesFired = 0;
        _waveNextAt = float.PositiveInfinity;
        if (!_airborne && ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.isStopped = true;

        if (DragonAbyssFx.Blackboard(ctx) is { } bb) bb.BlackFlameStormCooldown = Data.Cooldown;
        StartAim(ctx);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        if (_airborne) ctx.Transform.position = _hoverPos;
        TickWave();

        switch (_phase)
        {
            case Phase.Aim:     UpdateAim(ctx); break;
            case Phase.Gap:     if (_timer >= Data.LineGap) StartAim(ctx); break;
            case Phase.EndPose: UpdateEndPose(ctx); break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        PatternGuideHelper.SafeDestroy(ref _guide);
        _waveNextAt = float.PositiveInfinity;
        _phase      = Phase.Done;
    }

    // ── Aim ──────────────────────────────────────────────────────────────────

    private void StartAim(MonsterContext ctx)
    {
        _phase  = Phase.Aim;
        _timer  = 0f;
        _locked = false;
        DragonAbyssFx.PlayAnim(ctx, _airborne ? Data.AirStateName : Data.AimStateName, 0.12f);

        Color c = DragonAbyssFx.WithAlpha(DragonAbyssFx.Abyss, 0.85f);
        ResolveLine(ctx);
        _guide = PatternGuideHelper.Prepare(
            PatternGuideHelper.Beam(_lineOrigin, _lineDir, _lineLength, Data.LineWidth, c), c);
    }

    private void UpdateAim(MonsterContext ctx)
    {
        float trackUntil = Data.AimSeconds - Data.LockSeconds;
        if (_timer < trackUntil)
        {
            // 추적 — 몸을 돌리고 줄을 다시 놓는다
            if (ctx.Runtime.PlayerTarget != null)
                DragonAbyssFx.Face(ctx, ctx.Runtime.PlayerTarget.position - ctx.Transform.position, Data.TrackDegPerSec);
            ResolveLine(ctx);
            PatternGuideHelper.PlaceBeam(_guide, _lineOrigin, _lineDir, _lineLength, Data.LineWidth);
        }
        else if (!_locked)
        {
            // R3 — 판정 LockSeconds 전부터 조준을 멈추고 판정 색으로
            _locked = true;
            PatternGuideHelper.Arm(_guide);
        }
        PatternGuideHelper.SetProgress(_guide, _timer / Data.AimSeconds);

        if (_timer < Data.AimSeconds) return;
        Fire(ctx);
    }

    /// <summary>줄 = 입 아래 바닥점에서 몸 정면으로 바닥 끝까지.</summary>
    private void ResolveLine(MonsterContext ctx)
    {
        Vector3 mouth = _mouth != null ? _mouth.position : ctx.Transform.position + ctx.Transform.forward * 2f;
        _lineDir    = DragonAbyssFx.FlatDir(Vector3.zero, ctx.Transform.forward, Vector3.forward);
        _lineOrigin = DragonAbyssFx.OnFloor(ctx, mouth);
        _lineLength = Mathf.Max(1f, DragonPatternFloorUtils.DistanceToFloorEdge(_lineOrigin, _lineDir, Data.MaxLength));
    }

    // ── Fire ─────────────────────────────────────────────────────────────────

    private void Fire(MonsterContext ctx)
    {
        DragonAbyssFx.PlayAnim(ctx, _airborne ? Data.AirStateName : Data.FireStateName, 0.05f);
        Managers.Sound?.PlayEffectAt(Data.BreathSfx, _lineOrigin);
        Vector3 mouth = _mouth != null ? _mouth.position : ctx.Transform.position + Vector3.up * 2f;
        DragonAbyssFx.Burst(Data.MouthVfxPrefab, mouth, Quaternion.LookRotation(_lineDir), Data.MouthVfxScale);

        var player = DragonAbyssFx.Player(ctx);
        if (player != null
            && DragonAbyssFx.InStrip(player.transform.position, _lineOrigin, _lineDir, _lineLength, Data.LineWidth * 0.5f))
            DragonAbyssFx.HitPlayer(ctx, Data.HitDamageMult, HitWeight.Heavy);   // 브레스 직격 — 강

        DragonAbyssFlameStrip.Create(ctx.Monster, _lineOrigin, _lineDir, _lineLength, Data.LineWidth,
                                     Data.StripSeconds, Data.StripDamageMult,
                                     DragonAbyssFx.WithAlpha(DragonAbyssFx.Abyss, 0.6f),
                                     Data.StripVfxPrefab, Data.StripVfxScale, Data.StripVfxSpacing);
        PatternGuideHelper.SafeDestroy(ref _guide);

        _waveOrigin   = _lineOrigin;
        _waveDir      = _lineDir;
        _waveLength   = _lineLength;
        _waveTraveled = 0f;
        _waveNextAt   = Data.WaveVfxPrefab != null ? 0f : float.PositiveInfinity;

        _linesFired++;
        _timer = 0f;
        _phase = _linesFired < Data.LineCount ? Phase.Gap : Phase.EndPose;
    }

    private void TickWave()
    {
        if (_waveNextAt > _waveLength) return;
        _waveTraveled += Data.WaveSpeed * Time.deltaTime;
        while (_waveNextAt <= _waveTraveled && _waveNextAt <= _waveLength)
        {
            DragonAbyssFx.Burst(Data.WaveVfxPrefab, _waveOrigin + _waveDir * _waveNextAt,
                                Quaternion.LookRotation(_waveDir), Data.WaveVfxScale);
            _waveNextAt += Data.WaveSpacing;
        }
    }

    // ── EndPose ──────────────────────────────────────────────────────────────

    private void UpdateEndPose(MonsterContext ctx)
    {
        // 마지막 줄의 불이 끝까지 번질 때까지는 숨을 뿜는 자세로 선다(판정은 이미 끝났다 — 시각만)
        if (_timer < Data.EndPoseSeconds || _waveNextAt <= _waveLength) return;
        _phase = Phase.Done;
        DragonAbyssFx.ReturnToCombat(ctx);
    }

    private static Transform FindBone(Transform root, string boneName)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == boneName) return t;
        return null;
    }
}
}
