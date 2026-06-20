using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
[CreateAssetMenu(fileName = "DragonBreathSweepPattern",
    menuName = "RelicFairy/Boss/Dragon/BreathSweepPattern")]
public class DragonBreathSweepPatternSO : BossPatternSO
{
    [Header("Air Animation")]
    [SerializeField] private string _airChaseLeftStateName = "AirChaseLeft";
    [SerializeField] private string _airChaseRightStateName = "AirChaseRight";

    [Header("Flight")]
    [SerializeField] private float _hideHeight = 6f;
    [SerializeField] private float _flySpeed = 14f;

    [Header("Warning")]
    [SerializeField] private int _warningRowCount = 15;
    [SerializeField] private float _warningDuration = 2f;
    [SerializeField] private Color _warningColor = new Color(1.0f, 0.35f, 0.1f, 0.45f);

    [Header("Cinematic")]
    [SerializeField] private int _sweepCount = 3;
    [SerializeField] private float _previewDuration = 1f;
    [SerializeField] private float _previewTimeScale = 0.25f;
    [SerializeField] private float _cameraReturnDuration = 1.2f;
    [Tooltip("첫 sweep에서 슬로우+탑뷰를 유지하는 레인 비율 (0~1). 높을수록 더 오래 탑뷰 유지.")]
    [SerializeField] private float _slowReleaseRatio = 0.15f;

    [Header("Follow Light")]
    [SerializeField] private float _lightHeightOffset = 2f;
    [SerializeField] private float _lightIntensity = 20f;
    [SerializeField] private float _lightRange = 30f;
    [SerializeField] private float _lightSpotAngle = 60f;
    [SerializeField] private Color _lightColor = new Color(1f, 0.85f, 0.6f);

    [Header("Flame Breath")]
    [SerializeField] private GameObject _flameBreathPrefab;
    [Tooltip("매 sweep 시작 시 재생할 브레스 사운드")]
    [SerializeField] private AudioClip _breathSfx;
    [SerializeField] private float _breathDownAngle = 45f;
    [Tooltip("탑뷰 슬로우 연출 시 사용할 각도. 0에 가까울수록 수평에 가까워 위에서 보임")]
    [SerializeField] private float _breathPreviewAngle = 8f;
    [SerializeField] private float _flameBreathBaseLength = 10f;
    [SerializeField] private float _flameBreathBaseWidth = 5f;
    [SerializeField] private float _flameBreathScale = 1f;
    [SerializeField] private float _breathHorizReach = 0f;

    [Header("Flame Tsunami")]
    [SerializeField] private GameObject _flameTsunamiPrefab;
    [SerializeField] private int _tsunamiColInterval = 3;
    [SerializeField] private float _tsunamiScale = 0.2f;
    [Tooltip("바닥에 남는 불길 이펙트가 생성될 때 재생할 사운드")]
    [SerializeField] private AudioClip _residualFireSfx;

    [Header("Damage")]
    [SerializeField] private int _breathDamage = 20;
    [SerializeField] private int _tsunamiDamage = 10;
    [SerializeField] private float _tsunamiDuration = 5f;
    [SerializeField] private float _tsunamiTickInterval = 1f;
    [Tooltip("브레스 파티클 도달 지연 보정: 기하학 계산이 비주얼보다 빨리 잡히는 경우 증가 (셀 단위)")]
    [SerializeField] private float _tsunamiLagCells = 1f;
    [SerializeField] private LayerMask _breathBlockMask;

    [Header("Screen Fire (피해 중 화면 이펙트)")]
    [Tooltip("브레스/화염 피해를 받는 동안 표시할 화면 전체 이펙트 프리팹")]
    [SerializeField] private GameObject _screenFireEffectPrefab;
    [Tooltip("마지막 피해 후 이펙트가 사라지기까지의 유예 시간(초)")]
    [SerializeField] private float _screenFireGraceDuration = 1.5f;

    [Header("Scorch Marks")]
    [Tooltip("직접 지정한 텍스처. 없으면 절차적 생성 사용.")]
    [SerializeField] private Texture2D _scorchTexture;
    [Tooltip("열당 스폰할 그을림 쿼드 수")]
    [SerializeField] private int   _scorchClusterCount = 3;
    [SerializeField] private float _scorchDuration     = 9f;
    [Tooltip("스케일 범위 (셀 크기 배수)")]
    [SerializeField] private float _scorchScaleMin     = 0.8f;
    [SerializeField] private float _scorchScaleMax     = 2.2f;
    [Tooltip("진행 방향 수직 분산 (셀 단위)")]
    [SerializeField] private float _scorchPerpJitter   = 0.55f;

    [Header("Flame Scatter")]
    [Tooltip("열당 추가 시각 불 이펙트 수 (데미지 판정 1개 + 여기서 지정한 수만큼 시각 전용 추가)")]
    [SerializeField] private int   _fireVisualCount    = 1;
    [SerializeField] private float _fireScaleMin       = 0.13f;
    [SerializeField] private float _fireScaleMax       = 0.27f;

    [Header("Cooldown")]
    [SerializeField] private float _cooldown = 22f;

    public string AirChaseLeftStateName => _airChaseLeftStateName;
    public string AirChaseRightStateName => _airChaseRightStateName;
    public float HideHeight => _hideHeight;
    public float FlySpeed => _flySpeed;
    public int WarningRowCount => _warningRowCount;
    public float WarningDuration => _warningDuration;
    public Color WarningColor => _warningColor;
    public int SweepCount => _sweepCount;
    public float PreviewDuration => _previewDuration;
    public float PreviewTimeScale => _previewTimeScale;
    public float CameraReturnDuration => _cameraReturnDuration;
    public float SlowReleaseRatio     => _slowReleaseRatio;
    public float LightHeightOffset => _lightHeightOffset;
    public float LightIntensity => _lightIntensity;
    public float LightRange => _lightRange;
    public float LightSpotAngle => _lightSpotAngle;
    public Color LightColor => _lightColor;
    public GameObject FlameBreathPrefab => _flameBreathPrefab;
    public AudioClip  BreathSfx         => _breathSfx;
    public float BreathDownAngle        => _breathDownAngle;
    public float BreathPreviewAngle     => _breathPreviewAngle;
    public float BreathHorizReach => _breathHorizReach;
    public float FlameBreathScale => _flameBreathScale;
    public GameObject FlameTsunamiPrefab => _flameTsunamiPrefab;
    public int TsunamiColInterval => _tsunamiColInterval;
    public float TsunamiScale => _tsunamiScale;
    public AudioClip ResidualFireSfx => _residualFireSfx;
    public int BreathDamage => _breathDamage;
    public int TsunamiDamage => _tsunamiDamage;
    public float TsunamiDuration => _tsunamiDuration;
    public float TsunamiTickInterval => _tsunamiTickInterval;
    public float TsunamiLagCells    => _tsunamiLagCells;
    public LayerMask BreathBlockMask => _breathBlockMask;
    public GameObject ScreenFireEffectPrefab  => _screenFireEffectPrefab;
    public float      ScreenFireGraceDuration => _screenFireGraceDuration;
    public Texture2D ScorchTexture      => _scorchTexture;
    public int   ScorchClusterCount => _scorchClusterCount;
    public float ScorchDuration     => _scorchDuration;
    public float ScorchScaleMin     => _scorchScaleMin;
    public float ScorchScaleMax     => _scorchScaleMax;
    public float ScorchPerpJitter   => _scorchPerpJitter;
    public int   FireVisualCount    => _fireVisualCount;
    public float FireScaleMin       => _fireScaleMin;
    public float FireScaleMax       => _fireScaleMax;
    public float Cooldown => _cooldown;

    private DragonBreathSweepState _runtimeState;

    public override void Initialize(BossPatternContext ctx)
        => _runtimeState = new DragonBreathSweepState(this);

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

internal sealed class DragonBreathSweepState : FullLockState<DragonBreathSweepPatternSO>
{
    private enum Phase { FlyToStart, Warning, Preview, Sweep, Tsunami, Done }

    private struct TsunamiEntry
    {
        public GameObject Go;
        public Vector3 Center;
        public Vector3 Direction;
        public Vector3 Right;
        public float HalfWidth;
        public float Timer;
        public float TickTimer;
        public int SweepIndex;
    }

    private struct ScorchEntry
    {
        public GameObject Go;
        public float Timer;
        public float MaxTimer;
    }

    private const float FlyThroughPadding   = 3f;
    private const float FlyToStartTolerance = 1.5f;

    private Phase _phase;
    private float _timer;
    private int   _sweepIndex;
    private int   _nextColIndex;
    private int   _totalCols;

    private float _beamLength;
    private float _horizontalReach;
    private float _halfLaneWidth;
    private float _sweepStartProj;
    private float _sweepEndProj;
    private float _flyThroughEndProj;

    private Vector3 _sweepDir   = Vector3.right;
    private Vector3 _sweepRight = Vector3.forward;
    private Vector3 _laneCenter;

    private bool     _animSlowActive;
    private Animator _savedAnimator;
    private int      _revealedTileCount;
    private string   _currentFlyAnim;
    private float    _slowUntilProj;

    private static Texture2D s_ScorchTex;
    private static Material  s_ScorchMat;

    private readonly List<Vector2Int>   _warnCells = new();
    private readonly List<GameObject>   _warnTiles = new();
    private readonly List<Material>     _warnMats  = new();
    private readonly List<TsunamiEntry> _tsunamis  = new();
    private readonly List<ScorchEntry>  _scorches  = new();

    private GameObject  _flameBreathGo;
    private Light       _followLight;
    private AudioSource _flameBreathAudioSource;

    // sweep(브레스 라인) 단위로 잔불 사운드 1개씩만 루프 재생 — 개별 화염 패치마다 재생하면 소리가 겹쳐 터진다.
    private readonly Dictionary<int, int>         _lineFireRemaining = new();
    private readonly Dictionary<int, AudioSource> _lineFireAudio     = new();

    internal DragonBreathSweepState(DragonBreathSweepPatternSO data) : base(data) { }

    internal void Reset()
    {
        RestoreDragonSpeedDirect();
        CleanupWarn();
        CleanupFollowLight();
        CleanupFlameBreath();
        CleanupAllTsunamis();
        CleanupAllScorches();
        _phase          = Phase.Done;
        _timer          = 0f;
        _currentFlyAnim = null;
    }

    public override void Enter(MonsterContext ctx)
    {
        _phase             = Phase.FlyToStart;
        _timer             = 0f;
        _sweepIndex        = 0;
        _nextColIndex      = 0;
        _currentFlyAnim    = null;
        _revealedTileCount = 0;
        _tsunamis.Clear();

        _scorches.Clear();
        if (ctx.Agent != null) ctx.Agent.enabled = false;
        if ((ctx.Monster as IBoss)?.Blackboard is DragonBossBlackboard bb)
            bb.LeapCooldown = Data.Cooldown;

        if (Data.BreathHorizReach > 0f)
        {
            _horizontalReach = Data.BreathHorizReach;
            _beamLength      = Mathf.Sqrt(Data.HideHeight * Data.HideHeight + _horizontalReach * _horizontalReach);
        }
        else
        {
            float sinDown    = Mathf.Max(0.001f, Mathf.Sin(Data.BreathDownAngle * Mathf.Deg2Rad));
            float cosDown    = Mathf.Cos(Data.BreathDownAngle * Mathf.Deg2Rad);
            _beamLength      = Data.HideHeight / sinDown;
            _horizontalReach = _beamLength * cosDown;
        }

        GameCameraController.Instance?.ActivateDragonTopDownView(
            DragonBossRoomContext.WorldCenter,
            new Vector2(DragonBossRoomContext.Width * DragonBossRoomContext.CellSize, DragonBossRoomContext.Height * DragonBossRoomContext.CellSize));
        PickSweepLine(ctx);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        switch (_phase)
        {
            case Phase.FlyToStart: UpdateFlyToStart(ctx); break;
            case Phase.Warning:    UpdateWarning(ctx);    break;
            case Phase.Sweep:      UpdateSweep(ctx);      break;
            case Phase.Tsunami:    UpdateTsunami(ctx);    break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        RestoreDragonSpeed(ctx);
        CleanupWarn();
        CleanupFollowLight();
        CleanupFlameBreath();
        CleanupAllTsunamis();
        CleanupAllScorches();
        RestoreAgent(ctx);
    }

    // ─── Phase: FlyToStart ────────────────────────────────────────────────

    private void UpdateFlyToStart(MonsterContext ctx)
    {
        UpdateLivingTsunamis(ctx);
        UpdateLivingScorches();

        Vector3 targetPos = GetSweepStartPosition(ctx);
        Vector3 toTarget  = targetPos - ctx.Transform.position;
        toTarget.y = 0f;

        string anim = toTarget.sqrMagnitude > 0.01f
            ? (Vector3.SignedAngle(ctx.Transform.forward, toTarget.normalized, Vector3.up) >= 0f
               ? Data.AirChaseRightStateName : Data.AirChaseLeftStateName)
            : Data.AirChaseRightStateName;

        if (_currentFlyAnim != anim)
        {
            _currentFlyAnim = anim;
            PlayAnim(ctx, anim);
        }

        // 비행 중: 목적지 방향을 바라봄
        if (toTarget.sqrMagnitude > 0.01f)
            ctx.Transform.rotation = Quaternion.Slerp(
                ctx.Transform.rotation,
                Quaternion.LookRotation(toTarget.normalized, Vector3.up),
                Time.deltaTime * 5f);

        ctx.Transform.position = Vector3.MoveTowards(
            ctx.Transform.position, targetPos, Data.FlySpeed * Time.deltaTime);

        if (Vector3.Distance(ctx.Transform.position, targetPos) >= FlyToStartTolerance) return;

        // 도착 시점의 플레이어 위치로 레인 중심 재계산 → 비행 중 플레이어 이동 반영
        UpdateLaneCenterToPlayer(ctx);

        ctx.Transform.SetPositionAndRotation(GetSweepStartPosition(ctx),
            Quaternion.LookRotation(_sweepDir, Vector3.up));
        SpawnWarnTilesSorted(ctx);
        _revealedTileCount = 0;
        _phase = Phase.Warning;
        _timer = 0f;
    }

    private Vector3 GetSweepStartPosition(MonsterContext ctx)
    {
        Vector3 pos = _laneCenter + _sweepDir * (_sweepStartProj - _horizontalReach);
        pos.y       = ctx.Runtime.SpawnPosition.y + Data.HideHeight;
        return pos;
    }

    // ─── Phase: Warning ───────────────────────────────────────────────────

    private void BeginNextSweep(MonsterContext ctx)
    {
        CleanupWarn();
        CleanupFollowLight();
        CleanupFlameBreath();
        PickSweepLine(ctx);
        // 2번째/3번째 sweep: 텔레포트 후 짧은 경고장판만 표시 (탑뷰·슬로우 없음)
        ctx.Transform.SetPositionAndRotation(
            GetSweepStartPosition(ctx), Quaternion.LookRotation(_sweepDir, Vector3.up));
        SpawnWarnTilesSorted(ctx);
        _revealedTileCount = 0;
        _phase = Phase.Warning;
        _timer = 0f;
    }

    private void UpdateWarning(MonsterContext ctx)
    {
        UpdateLivingTsunamis(ctx);
        UpdateLivingScorches();

        float warnDur = Mathf.Max(0.01f, Data.WarningDuration);

        float t           = Mathf.Clamp01(_timer / warnDur);
        int   targetCount = Mathf.CeilToInt(t * _warnTiles.Count);
        while (_revealedTileCount < targetCount && _revealedTileCount < _warnMats.Count)
        {
            if (_warnMats[_revealedTileCount] != null)
            {
                Color fireBase = DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Fire);
                _warnMats[_revealedTileCount].color = new Color(fireBase.r, fireBase.g, fireBase.b, Data.WarningColor.a);
            }
            _revealedTileCount++;
        }

        if (_timer < warnDur) return;

        CleanupWarn();
        ctx.Transform.SetPositionAndRotation(
            GetSweepStartPosition(ctx), Quaternion.LookRotation(_sweepDir, Vector3.up));
        PlayAnim(ctx, ResolveAirChaseAnim(ctx));
        CreateFollowLight(ctx);
        SpawnFlameBreath(ctx);
        // 첫 번째 sweep만 슬로우 + 탑뷰 유지
        if (_sweepIndex == 0)
        {
            ApplyDragonSlow(ctx);
            _slowUntilProj = _sweepStartProj + (_sweepEndProj - _sweepStartProj)
                             * Mathf.Clamp01(Data.SlowReleaseRatio);
        }
        _nextColIndex  = 0;
        _phase = Phase.Sweep;
        _timer = 0f;
    }

    private void UpdateSweep(MonsterContext ctx)
    {
        MoveDragonContinuous(ctx);
        UpdateFollowLight(ctx);
        UpdateFlameBreath(ctx);
        UpdateLivingTsunamis(ctx);
        UpdateLivingScorches();

        float tipProj = Vector3.Dot(ctx.Transform.position - _laneCenter, _sweepDir) + _horizontalReach;

        // 첫 sweep 슬로우: 경고장판 1/5 지점 도달 시 속도 복원 + 카메라 하강 시작
        if (_sweepIndex == 0 && _animSlowActive && tipProj >= _slowUntilProj)
        {
            RestoreDragonSpeed(ctx);
            GameCameraController.Instance?.DeactivateDragonTopDownView(Data.CameraReturnDuration);
        }

        while (_nextColIndex < _totalCols)
        {
            float colProj = GetColumnProjection(_nextColIndex);
            if (tipProj < colProj + Data.TsunamiLagCells * DragonBossRoomContext.CellSize) break;

            ApplyBreathDamage(ctx, colProj);

            int interval = Mathf.Max(1, Data.TsunamiColInterval);
            if (_nextColIndex % interval == 0)
            {
                SpawnTsunamiAtProjection(colProj, ctx);
                SpawnFireVisuals(colProj, ctx);
                SpawnScorchCluster(colProj, ctx);
            }

            _nextColIndex++;
        }

        if (_nextColIndex < _totalCols) return;

        CleanupFlameBreath();
        CleanupFollowLight();
        _sweepIndex++;

        if (_sweepIndex < Mathf.Max(1, Data.SweepCount))
            BeginNextSweep(ctx);
        else
        {
            _phase = Phase.Tsunami;
            _timer = 0f;
        }
    }

    private void UpdateTsunami(MonsterContext ctx)
    {
        UpdateLivingTsunamis(ctx);
        UpdateLivingScorches();
        if (_tsunamis.Count != 0) return;

        _phase = Phase.Done;

        // Summon 패턴 공중 대기 루프에서 핸드오프된 경우 — 원래 상태로 복귀
        if ((ctx.Monster as IBoss)?.Blackboard is DragonBossBlackboard bb && bb.AirLoopReturnState != null)
        {
            var returnState = bb.AirLoopReturnState;
            bb.AirLoopReturnState = null;
            ctx.Monster.ChangeState(returnState);
            return;
        }

        RestoreAgent(ctx);
        ctx.Monster.ChangeState<AttackReadyState>();
    }

    // 비행 도착 후 플레이어 현재 위치로 레인 중심만 갱신 (방향은 유지)
    private void UpdateLaneCenterToPlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;

        Vector3 center = ctx.Runtime.PlayerTarget.position;
        center.y    = ctx.Runtime.SpawnPosition.y;
        _laneCenter = center;

        _warnCells.Clear();
        float minProj         = float.PositiveInfinity;
        float maxProj         = float.NegativeInfinity;
        float paddedHalfWidth = _halfLaneWidth + DragonBossRoomContext.CellSize * 0.5f;

        foreach (var cell in DragonBossRoomContext.GetInteriorCells())
        {
            Vector3 delta = DragonBossRoomContext.CellToWorld(cell.x, cell.y, 0f) - _laneCenter;
            float   perp  = Mathf.Abs(Vector3.Dot(delta, _sweepRight));
            if (perp > paddedHalfWidth) continue;

            float proj = Vector3.Dot(delta, _sweepDir);
            minProj    = Mathf.Min(minProj, proj);
            maxProj    = Mathf.Max(maxProj, proj);
            _warnCells.Add(cell);
        }

        if (_warnCells.Count == 0)
        {
            var fallbackCell = DragonBossRoomContext.WorldToCell(center);
            _warnCells.Add(fallbackCell);
            Vector3 world = DragonBossRoomContext.CellToWorld(fallbackCell.x, fallbackCell.y, 0f);
            float   proj  = Vector3.Dot(world - _laneCenter, _sweepDir);
            minProj = maxProj = proj;
        }

        float halfCell     = DragonBossRoomContext.CellSize * 0.5f;
        _sweepStartProj    = minProj - halfCell;
        _sweepEndProj      = maxProj + halfCell;
        _flyThroughEndProj = _sweepEndProj + FlyThroughPadding;
        _totalCols         = Mathf.Max(1, Mathf.CeilToInt(
            (_sweepEndProj - _sweepStartProj) / DragonBossRoomContext.CellSize) + 1);
        _nextColIndex      = 0;
    }

    private void PickSweepLine(MonsterContext ctx)
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        _sweepDir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)).normalized;
        _sweepRight = new Vector3(-_sweepDir.z, 0f, _sweepDir.x);

        Vector3 center = ctx.Runtime.PlayerTarget != null
            ? ctx.Runtime.PlayerTarget.position
            : DragonBossRoomContext.WorldCenter;
        center.y = ctx.Runtime.SpawnPosition.y;
        _laneCenter = center;
        _halfLaneWidth = Mathf.Max(
            DragonBossRoomContext.CellSize * 0.5f,
            Mathf.Max(1, Data.WarningRowCount) * DragonBossRoomContext.CellSize * 0.5f);

        _warnCells.Clear();
        float minProj = float.PositiveInfinity;
        float maxProj = float.NegativeInfinity;
        float paddedHalfWidth = _halfLaneWidth + DragonBossRoomContext.CellSize * 0.5f;

        foreach (var cell in DragonBossRoomContext.GetInteriorCells())
        {
            Vector3 world = DragonBossRoomContext.CellToWorld(cell.x, cell.y, 0f);
            Vector3 delta = world - _laneCenter;
            float perp = Mathf.Abs(Vector3.Dot(delta, _sweepRight));
            if (perp > paddedHalfWidth) continue;

            float proj = Vector3.Dot(delta, _sweepDir);
            minProj = Mathf.Min(minProj, proj);
            maxProj = Mathf.Max(maxProj, proj);
            _warnCells.Add(cell);
        }

        if (_warnCells.Count == 0)
        {
            var cell = DragonBossRoomContext.WorldToCell(center);
            _warnCells.Add(cell);
            Vector3 world = DragonBossRoomContext.CellToWorld(cell.x, cell.y, 0f);
            float proj = Vector3.Dot(world - _laneCenter, _sweepDir);
            minProj = proj;
            maxProj = proj;
        }

        float halfCell = DragonBossRoomContext.CellSize * 0.5f;
        _sweepStartProj = minProj - halfCell;
        _sweepEndProj = maxProj + halfCell;
        _flyThroughEndProj = _sweepEndProj + FlyThroughPadding;
        _totalCols = Mathf.Max(1, Mathf.CeilToInt((_sweepEndProj - _sweepStartProj) / DragonBossRoomContext.CellSize) + 1);
    }

    private void SpawnWarnTilesSorted(MonsterContext ctx)
    {
        CleanupWarn();
        _warnCells.Sort((a, b) =>
        {
            float pa = Vector3.Dot(DragonBossRoomContext.CellToWorld(a.x, a.y, 0f) - _laneCenter, _sweepDir);
            float pb = Vector3.Dot(DragonBossRoomContext.CellToWorld(b.x, b.y, 0f) - _laneCenter, _sweepDir);
            return pa.CompareTo(pb);
        });

        var shader = Shader.Find("Sprites/Default")
                  ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");

        float fallbackY = ctx.Runtime.SpawnPosition.y;
        foreach (var cell in _warnCells)
        {
            Vector3 pos = DragonBossRoomContext.CellToWorld(cell.x, cell.y, 0f);
            pos.y = GetFloorY(pos, ctx) + 0.05f;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "BreathSweepWarn";
            Object.Destroy(go.GetComponent<MeshCollider>());
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(90f, 0f, 0f));
            go.transform.localScale = Vector3.one;
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows    = false;
            Color fireBase = DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Fire);
            var mat = new Material(shader);
            mat.color = new Color(fireBase.r, fireBase.g, fireBase.b, 0f);
            mr.material = mat;
            _warnTiles.Add(go);
            _warnMats.Add(mat);
        }
    }

    private void MoveDragonContinuous(MonsterContext ctx)
    {
        Vector3 target = _laneCenter + _sweepDir * _flyThroughEndProj;
        target.y = ctx.Runtime.SpawnPosition.y + Data.HideHeight;
        ctx.Transform.position = Vector3.MoveTowards(
            ctx.Transform.position, target, Data.FlySpeed * Time.deltaTime);
    }

    private float GetColumnProjection(int index)
        => _sweepStartProj + index * DragonBossRoomContext.CellSize;

    private void SpawnFlameBreath(MonsterContext ctx)
    {
        _flameBreathAudioSource = Managers.Sound?.PlayEffectAt(Data.BreathSfx, ctx.Transform.position);

        if (Data.FlameBreathPrefab == null) return;
        _flameBreathGo = BossEffectPool.Spawn(
            Data.FlameBreathPrefab,
            ctx.Transform.position,
            GetBreathRotation());
        _flameBreathGo.transform.localScale = Vector3.one * Data.FlameBreathScale;
    }

    private void UpdateFlameBreath(MonsterContext ctx)
    {
        if (_flameBreathGo == null) return;
        _flameBreathGo.transform.SetPositionAndRotation(
            ctx.Transform.position, GetBreathRotation());
    }

    private void CleanupFlameBreath()
    {
        Managers.Sound?.StopEffect(_flameBreathAudioSource, Data.BreathSfx);

        if (_flameBreathGo == null) return;
        BossEffectPool.Release(_flameBreathGo);
        _flameBreathGo = null;
    }

    private Quaternion GetBreathRotation()
    {
        Vector3 dir;
        if (Data.BreathHorizReach > 0f)
            dir = (_sweepDir * _horizontalReach + Vector3.down * Data.HideHeight).normalized;
        else
        {
            float downRad = Data.BreathDownAngle * Mathf.Deg2Rad;
            dir = (_sweepDir * Mathf.Cos(downRad) + Vector3.down * Mathf.Sin(downRad)).normalized;
        }
        return Quaternion.LookRotation(dir, Vector3.up);
    }

    private void CreateFollowLight(MonsterContext ctx)
    {
        var go = new GameObject("DragonSweepSpotlight");
        _followLight = go.AddComponent<Light>();
        _followLight.type = LightType.Spot;
        _followLight.color = Data.LightColor;
        _followLight.intensity = Data.LightIntensity;
        _followLight.range = Data.LightRange;
        _followLight.spotAngle = Data.LightSpotAngle;
        _followLight.shadows = LightShadows.Hard;
        UpdateFollowLight(ctx);
    }

    private void UpdateFollowLight(MonsterContext ctx)
    {
        if (_followLight == null) return;
        _followLight.transform.position = ctx.Transform.position + Vector3.up * Data.LightHeightOffset;
        _followLight.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    private void CleanupFollowLight()
    {
        if (_followLight == null) return;
        Object.Destroy(_followLight.gameObject);
        _followLight = null;
    }

    private static int s_groundLayerMask = -1;

    // Ground 레이어만 검사 — 그렇지 않으면 경고장판/장식 효과가 그 위치에 선 플레이어 콜라이더 위에 생성됨
    private static float GetFloorY(Vector3 xzPos, MonsterContext ctx)
    {
        if (s_groundLayerMask < 0)
            s_groundLayerMask = 1 << LayerMask.NameToLayer("Ground");

        Vector3 origin = new Vector3(xzPos.x, ctx.Runtime.SpawnPosition.y + 50f, xzPos.z);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 100f, s_groundLayerMask))
            return hit.point.y;
        return ctx.Runtime.SpawnPosition.y;
    }

    private void SpawnTsunamiAtProjection(float projection, MonsterContext ctx)
    {
        if (Data.FlameTsunamiPrefab == null) return;

        Vector3 pos = _laneCenter + _sweepDir * projection;
        pos.y = GetFloorY(pos, ctx) + 0.05f;
        Quaternion rot = Quaternion.LookRotation(_sweepDir, Vector3.up);
        var go = BossEffectPool.Spawn(Data.FlameTsunamiPrefab, pos, rot);
        go.transform.localScale = Vector3.one * Data.TsunamiScale;

        foreach (var mb in go.GetComponentsInChildren<VariousTranslateMove>(true))
            mb.enabled = false;

        _tsunamis.Add(new TsunamiEntry
        {
            Go = go,
            Center = pos,
            Direction = _sweepDir,
            Right = _sweepRight,
            HalfWidth = _halfLaneWidth,
            SweepIndex = _sweepIndex,
        });

        // 이 라인(sweep)의 첫 화염 패치일 때만 루프 사운드 1개 시작 — 패치마다 개별 재생하지 않음
        _lineFireRemaining.TryGetValue(_sweepIndex, out int remaining);
        _lineFireRemaining[_sweepIndex] = remaining + 1;
        if (remaining == 0)
            _lineFireAudio[_sweepIndex] = Managers.Sound?.PlayLoopingEffectAt(Data.ResidualFireSfx, pos);
    }

    private void ReleaseLineFireSlot(int sweepIndex)
    {
        if (!_lineFireRemaining.TryGetValue(sweepIndex, out int remaining)) return;

        remaining--;
        if (remaining > 0)
        {
            _lineFireRemaining[sweepIndex] = remaining;
            return;
        }

        _lineFireRemaining.Remove(sweepIndex);
        if (_lineFireAudio.TryGetValue(sweepIndex, out var source))
        {
            Managers.Sound?.StopLoopingEffect(source);
            _lineFireAudio.Remove(sweepIndex);
        }
    }

    private void StopAllLineFireAudio()
    {
        foreach (var source in _lineFireAudio.Values)
            Managers.Sound?.StopLoopingEffect(source);
        _lineFireAudio.Clear();
        _lineFireRemaining.Clear();
    }

    private void UpdateLivingTsunamis(MonsterContext ctx)
    {
        for (int i = _tsunamis.Count - 1; i >= 0; i--)
        {
            var entry = _tsunamis[i];
            entry.Timer += Time.deltaTime;
            entry.TickTimer += Time.deltaTime;

            if (entry.TickTimer >= Data.TsunamiTickInterval)
            {
                entry.TickTimer = 0f;
                ApplyTsunamiDamage(ctx, entry);
            }

            if (entry.Timer >= Data.TsunamiDuration)
            {
                if (entry.Go != null) BossEffectPool.Release(entry.Go);
                ReleaseLineFireSlot(entry.SweepIndex);
                _tsunamis.RemoveAt(i);
                continue;
            }

            _tsunamis[i] = entry;
        }
    }

    private void ApplyBreathDamage(MonsterContext ctx, float projection)
    {
        if (ctx?.Runtime?.PlayerTarget == null) return;

        Vector3 playerPos = ctx.Runtime.PlayerTarget.position;
        Vector3 delta = playerPos - _laneCenter;
        float playerProj = Vector3.Dot(delta, _sweepDir);
        float playerPerp = Mathf.Abs(Vector3.Dot(delta, _sweepRight));
        if (Mathf.Abs(playerProj - projection) > DragonBossRoomContext.CellSize * 0.5f) return;
        if (playerPerp > _halfLaneWidth) return;

        Vector3 origin = ctx.Transform.position;
        if (Data.BreathBlockMask != 0)
        {
            Vector3 rayDir = (playerPos - origin).normalized;
            float dist = Vector3.Distance(origin, playerPos);
            if (Physics.Raycast(origin, rayDir, dist, Data.BreathBlockMask))
                return;
        }

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        player.TakeDamage(Data.BreathDamage);
        PlayerStatusEffectVisuals.ApplyTimed(player, Data.ScreenFireEffectPrefab, 1f, Data.ScreenFireGraceDuration, "StatusEffectScreen_" + StatusEffectType.Slow);
    }

    private void ApplyTsunamiDamage(MonsterContext ctx, TsunamiEntry entry)
    {
        if (ctx?.Runtime?.PlayerTarget == null) return;

        Vector3 delta = ctx.Runtime.PlayerTarget.position - entry.Center;
        float along = Mathf.Abs(Vector3.Dot(delta, entry.Direction));
        float perp = Mathf.Abs(Vector3.Dot(delta, entry.Right));
        float halfLength = Mathf.Max(1, Data.TsunamiColInterval / 2) * DragonBossRoomContext.CellSize;
        if (along > halfLength || perp > entry.HalfWidth) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        player.TakeDamage(Data.TsunamiDamage);
        PlayerStatusEffectVisuals.ApplyTimed(player, Data.ScreenFireEffectPrefab, 1f, Data.ScreenFireGraceDuration, "StatusEffectScreen_" + StatusEffectType.Slow);
    }

    private void ApplyDragonSlow(MonsterContext ctx)
    {
        if (_animSlowActive) return;
        float slow      = Mathf.Clamp(Data.PreviewTimeScale, 0.05f, 1f);
        _savedAnimator  = ctx.Animator;
        if (ctx.Animator != null) ctx.Animator.speed = slow;
        // 브레스 파티클은 슬로우 적용 안 함 — 정상 속도로 보여야 함
        _animSlowActive = true;
    }

    private void RestoreDragonSpeed(MonsterContext ctx)
    {
        if (!_animSlowActive) return;
        if (ctx.Animator != null) ctx.Animator.speed = 1f;
        _animSlowActive = false;
        _savedAnimator  = null;
    }

    private void RestoreDragonSpeedDirect()
    {
        if (!_animSlowActive) return;
        if (_savedAnimator != null) _savedAnimator.speed = 1f;
        _animSlowActive = false;
        _savedAnimator  = null;
    }

    private void SetFlameBreathSimSpeed(float speed)
    {
        if (_flameBreathGo == null) return;
        foreach (var ps in _flameBreathGo.GetComponentsInChildren<ParticleSystem>())
        {
            var m = ps.main;
            m.simulationSpeed = speed;
        }
    }

    private void CleanupWarn()
    {
        foreach (var go in _warnTiles) if (go != null) Object.Destroy(go);
        foreach (var mat in _warnMats) if (mat != null) Object.Destroy(mat);
        _warnTiles.Clear();
        _warnMats.Clear();
    }

    // ─── Scorch marks ────────────────────────────────────────────────────────

    private void SpawnScorchCluster(float projection, MonsterContext ctx)
    {
        float cell  = DragonBossRoomContext.CellSize;
        var   mat   = GetOrCreateScorchMaterial();
        int   count = Mathf.Max(1, Data.ScorchClusterCount);

        // 경고장판 너비를 count개 구역으로 분할 — 구역당 1개 배치로 겹침 최소화
        float laneHalf  = _halfLaneWidth;
        float zoneWidth = 2f * laneHalf / count;

        // sweepAngle은 구역 반복마다 동일하므로 한 번만 계산
        float sweepAngle = Vector3.SignedAngle(Vector3.forward, _sweepDir, Vector3.up);

        for (int i = 0; i < count; i++)
        {
            // 구역 안쪽(양 끝 5% 여백)에서 랜덤 배치
            float zoneCenter = -laneHalf + (i + 0.5f) * zoneWidth;
            float margin     = zoneWidth * 0.05f;
            float perp       = Random.Range(zoneCenter - zoneWidth * 0.5f + margin,
                                            zoneCenter + zoneWidth * 0.5f - margin);

            float fwd = Random.Range(-0.5f, 0.5f) * cell;

            Vector3 pos = _laneCenter
                + _sweepDir   * (projection + fwd)
                + _sweepRight * perp;
            pos.y = GetFloorY(pos, ctx) + 0.02f;

            // 진행방향 기준: 왼쪽 구역은 오른쪽(양수)으로만, 오른쪽 구역은 왼쪽(음수)으로만 휘어짐
            float jitter;
            if (zoneCenter < -0.01f)
                jitter = Random.Range(5f, 30f);
            else if (zoneCenter > 0.01f)
                jitter = Random.Range(-30f, -5f);
            else
                jitter = Random.Range(-15f, 15f);
            Quaternion rot = Quaternion.Euler(90f, sweepAngle - 90f + jitter, 0f);

            // 세로(sweep 방향)은 ScorchScaleMin/Max, 가로(수직)는 1.5~2배로 역전
            float length = Random.Range(Data.ScorchScaleMin, Data.ScorchScaleMax) * cell;
            float width  = Mathf.Min(
                length * Random.Range(1.5f, 2.0f),
                2f * laneHalf * 0.92f);  // 경고장판 전체 너비를 넘지 않도록 클램프

            var go  = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "ScorchMark";
            Object.Destroy(go.GetComponent<MeshCollider>());
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = new Vector3(length, width, 1f);

            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows    = false;
            mr.material          = mat;

            _scorches.Add(new ScorchEntry { Go = go, Timer = 0f, MaxTimer = Data.ScorchDuration });
        }
    }

    private void SpawnFireVisuals(float projection, MonsterContext ctx)
    {
        if (Data.FlameTsunamiPrefab == null || Data.FireVisualCount <= 0) return;

        float cell = DragonBossRoomContext.CellSize;

        for (int i = 0; i < Data.FireVisualCount; i++)
        {
            // 경고장판 전체 폭(_halfLaneWidth)에 걸쳐 분산 — 경고장판이 넓어지면 함께 넓어진다
            float perp = Random.Range(-_halfLaneWidth, _halfLaneWidth);
            float fwd  = Random.Range(-0.35f, 0.35f) * cell;

            Vector3 pos = _laneCenter
                + _sweepDir   * (projection + fwd)
                + _sweepRight * perp;
            pos.y = GetFloorY(pos, ctx) + 0.05f;

            float  yRot = Random.Range(-25f, 25f);
            var    rot  = Quaternion.LookRotation(_sweepDir, Vector3.up) * Quaternion.Euler(0f, yRot, 0f);
            float  scl  = Random.Range(Data.FireScaleMin, Data.FireScaleMax);

            var go = BossEffectPool.Spawn(Data.FlameTsunamiPrefab, pos, rot);
            go.transform.localScale = Vector3.one * scl;
            foreach (var mb in go.GetComponentsInChildren<VariousTranslateMove>(true))
                mb.enabled = false;

            _scorches.Add(new ScorchEntry { Go = go, Timer = 0f, MaxTimer = Data.TsunamiDuration });
        }
    }

    private void UpdateLivingScorches()
    {
        for (int i = _scorches.Count - 1; i >= 0; i--)
        {
            var e = _scorches[i];
            e.Timer += Time.deltaTime;
            if (e.Timer >= e.MaxTimer)
            {
                if (e.Go != null) BossEffectPool.Release(e.Go);
                _scorches.RemoveAt(i);
                continue;
            }
            _scorches[i] = e;
        }
    }

    private void CleanupAllScorches()
    {
        foreach (var e in _scorches) if (e.Go != null) BossEffectPool.Release(e.Go);
        _scorches.Clear();
    }

    private Material GetOrCreateScorchMaterial()
    {
        var tex = Data.ScorchTexture != null ? Data.ScorchTexture : GetOrCreateScorchTexture();

        if (s_ScorchMat != null && s_ScorchMat.GetTexture("_MainTex") == tex)
            return s_ScorchMat;

        // Sprites/Default: 투명도 내장, ZWrite=Off, SrcAlpha OneMinusSrcAlpha
        var shader = Shader.Find("Sprites/Default")
                  ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");

        s_ScorchMat = new Material(shader);
        s_ScorchMat.SetTexture("_MainTex", tex);
        s_ScorchMat.SetTexture("_BaseMap", tex);
        s_ScorchMat.SetColor("_Color",     Color.white);
        s_ScorchMat.SetColor("_BaseColor", Color.white);

        // URP Particles/Unlit 폴백일 경우 투명 모드 강제 설정
        if (shader != null && shader.name.Contains("Particles"))
        {
            s_ScorchMat.SetFloat("_Surface", 1f);   // Transparent
            s_ScorchMat.SetFloat("_Blend",   0f);   // Alpha
            s_ScorchMat.SetInt("_SrcBlend",  5);    // SrcAlpha
            s_ScorchMat.SetInt("_DstBlend",  10);   // OneMinusSrcAlpha
            s_ScorchMat.SetInt("_ZWrite",    0);
            s_ScorchMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            s_ScorchMat.renderQueue = 3000;
        }

        return s_ScorchMat;
    }

    // Perlin noise 기반 절차적 그을림 텍스처 — 불규칙한 가장자리의 어두운 얼룩
    private static Texture2D GetOrCreateScorchTexture()
    {
        if (s_ScorchTex != null) return s_ScorchTex;

        const int size = 256;
        s_ScorchTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        s_ScorchTex.wrapMode = TextureWrapMode.Clamp;

        var center = new Vector2(0.5f, 0.5f);

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (float)x / (size - 1);
            float v = (float)y / (size - 1);

            float dist = Vector2.Distance(new Vector2(u, v), center);

            // 3겹 노이즈로 불규칙한 가장자리 구현
            float n0 = Mathf.PerlinNoise(u * 3.7f + 0.10f, v * 3.7f + 0.30f);   // 저주파 형태
            float n1 = Mathf.PerlinNoise(u * 7.3f + 1.40f, v * 7.3f + 2.10f);   // 중주파 디테일
            float n2 = Mathf.PerlinNoise(u * 14f  + 3.20f, v * 14f  + 0.70f);   // 고주파 균열

            float noise = n0 * 0.55f + n1 * 0.30f + n2 * 0.15f;

            // 노이즈로 변형된 거리 → 불규칙 외곽선
            float pertDist = dist + (noise - 0.5f) * 0.28f;

            // 외곽 페이드 (0.30 → 0.48 사이에서 0→1)
            float outerMask = 1f - Mathf.Clamp01((pertDist - 0.30f) / 0.18f);

            // 내부 중심부 (더 진하게)
            float innerMask = Mathf.Clamp01(1f - pertDist * 3.2f);

            float alpha = outerMask;

            // 탄 색상: 진한 갈흑색, 가장자리는 약간 밝게
            float brightness = Mathf.Lerp(0.09f, 0.02f, innerMask);
            s_ScorchTex.SetPixel(x, y, new Color(brightness * 1.3f, brightness * 0.75f, brightness * 0.4f, alpha));
        }

        s_ScorchTex.Apply();
        return s_ScorchTex;
    }

    private void CleanupAllTsunamis()
    {
        foreach (var entry in _tsunamis) if (entry.Go != null) BossEffectPool.Release(entry.Go);
        _tsunamis.Clear();
        StopAllLineFireAudio();
    }

    private string ResolveAirChaseAnim(MonsterContext ctx)
    {
        float angle = Vector3.SignedAngle(ctx.Transform.forward, _sweepDir, Vector3.up);
        return angle >= 0f ? Data.AirChaseRightStateName : Data.AirChaseLeftStateName;
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        if (ctx.Animator.HasState(0, Animator.StringToHash(stateName)))
            ctx.Animator.CrossFade(stateName, 0.12f, 0, 0f);
    }

    private static void RestoreAgent(MonsterContext ctx)
        => DragonPatternFloorUtils.SnapToFloorAndRestoreAgent(ctx);
}
}
