using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 브레스 휩쓸기 패턴.
/// ① 5행 경고장판 페이드인 → ② 장판 소멸 + 드래곤 측면 위에서 AirChase로 통과
///    (위쪽 Spotlight가 따라다니며 바닥에 드래곤 그림자 연출)
/// ③ 드래곤 통과 방향 대각선 아래로 FlameBreath 발사 + 해당 열 중간칸 FlameTsunami 즉시 스폰
/// ④ sweep 종료 후 모든 FlameTsunami 5초 잔류 + 틱 피해 → 전부 소멸 시 종료
/// </summary>
[CreateAssetMenu(fileName = "DragonBreathSweepPattern",
    menuName = "RelicFairy/Boss/Dragon/BreathSweepPattern")]
public class DragonBreathSweepPatternSO : BossPatternSO
{
    [Header("비행 — 애니메이션")]
    [SerializeField] private string _airChaseLeftStateName  = "AirChaseLeft";
    [SerializeField] private string _airChaseRightStateName = "AirChaseRight";

    [Header("비행 — 위치")]
    [SerializeField] private float _hideHeight   = 38f;
    [SerializeField] private float _flySpeed     = 18f;
    [SerializeField] private float _flySideOffset= 20f;   // 맵 중앙에서 시작/끝 여백

    [Header("경고 타일")]
    [SerializeField] private int   _warningRowCount = 5;
    [SerializeField] private float _warningDuration = 2f;
    [SerializeField] private Color _warningColor    = new Color(1f, 0.35f, 0f, 0.45f);

    [Header("따라다니는 광원 (그림자 연출)")]
    [SerializeField] private float _lightHeightOffset = 4f;   // 드래곤 위 오프셋
    [SerializeField] private float _lightIntensity    = 4f;
    [SerializeField] private float _lightRange        = 60f;
    [SerializeField] private float _lightSpotAngle    = 55f;
    [SerializeField] private Color _lightColor        = new Color(1f, 0.85f, 0.6f);

    [Header("이펙트")]
    [SerializeField] private GameObject _flameBreathPrefab;
    [SerializeField] private GameObject _flameTsunamiPrefab;

    [Header("브레스 대각선 각도 (도, 0=수평 90=수직)")]
    [SerializeField] private float _breathDownAngle = 30f;   // 아래로 꺾이는 각도

    [Header("피해")]
    [SerializeField] private int   _breathDamage        = 20;
    [SerializeField] private int   _tsunamiDamage       = 10;
    [SerializeField] private float _tsunamiDuration     = 5f;
    [SerializeField] private float _tsunamiTickInterval = 1f;
    [SerializeField] private float _damageRadius        = 1.5f;

    [Header("쿨다운")]
    [SerializeField] private float _cooldown = 22f;

    // ── Properties ───────────────────────────────────────────────────────────
    public string AirChaseLeftStateName  => _airChaseLeftStateName;
    public string AirChaseRightStateName => _airChaseRightStateName;
    public float  HideHeight             => _hideHeight;
    public float  FlySpeed               => _flySpeed;
    public float  FlySideOffset          => _flySideOffset;
    public int    WarningRowCount        => _warningRowCount;
    public float  WarningDuration        => _warningDuration;
    public Color  WarningColor           => _warningColor;
    public float  LightHeightOffset      => _lightHeightOffset;
    public float  LightIntensity         => _lightIntensity;
    public float  LightRange             => _lightRange;
    public float  LightSpotAngle         => _lightSpotAngle;
    public Color  LightColor             => _lightColor;
    public GameObject FlameBreathPrefab   => _flameBreathPrefab;
    public GameObject FlameTsunamiPrefab  => _flameTsunamiPrefab;
    public float  BreathDownAngle        => _breathDownAngle;
    public int    BreathDamage           => _breathDamage;
    public int    TsunamiDamage          => _tsunamiDamage;
    public float  TsunamiDuration        => _tsunamiDuration;
    public float  TsunamiTickInterval    => _tsunamiTickInterval;
    public float  DamageRadius           => _damageRadius;
    public float  Cooldown               => _cooldown;

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

// ─────────────────────────────────────────────────────────────────────────────
// Runtime state
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class DragonBreathSweepState : FullLockState<DragonBreathSweepPatternSO>
{
    private enum Phase { Warning, Sweep, Tsunami, Done }

    private struct TsunamiEntry
    {
        public GameObject Go;
        public Vector3    Position;
        public float      Timer;
        public float      TickTimer;
    }

    private Phase _phase;
    private float _timer;

    // 경고 타일
    private readonly List<GameObject> _warnTiles = new();
    private readonly List<Material>   _warnMats  = new();

    // 행 범위 (플레이어 중심 N행)
    private int _rowMin;
    private int _rowMax;
    private int _centerRow;

    // sweep 방향 / 열 추적
    private int _fromSide;            // -1: 왼→오, +1: 오→왼
    private int _nextColIndex;        // 다음에 발사할 열 순번 (0 ~ Width-3)
    private int _totalCols;

    // 따라다니는 광원
    private Light _followLight;

    // 열별 FlameTsunami (타이머는 Tsunami 페이즈 진입 시 일괄 시작)
    private readonly List<TsunamiEntry> _tsunamis = new();

    internal DragonBreathSweepState(DragonBreathSweepPatternSO data) : base(data) { }

    internal void Reset()
    {
        CleanupWarn();
        CleanupFollowLight();
        CleanupAllTsunamis();
        _phase = Phase.Done;
        _timer = 0f;
    }

    // ── FSM ──────────────────────────────────────────────────────────────────

    public override void Enter(MonsterContext ctx)
    {
        _phase         = Phase.Warning;
        _timer         = 0f;
        _nextColIndex  = 0;
        _fromSide      = Random.value < 0.5f ? -1 : 1;
        _tsunamis.Clear();

        if (ctx.Agent != null) ctx.Agent.enabled = false;

        var bb = (ctx.Monster as IBoss)?.Blackboard;
        if (bb != null) bb.LeapCooldown = Data.Cooldown;

        ComputeRows(ctx);
        SpawnWarnTiles();

        // 경고 기간 중 드래곤은 맵 위 공중 호버 (현 위치 유지)
        float hoverY = ctx.Runtime.SpawnPosition.y + Data.HideHeight;
        ctx.Transform.position = new Vector3(
            DKBossRoomContext.WorldCenter.x,
            hoverY,
            WorldZOfRow(_centerRow));
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        switch (_phase)
        {
            case Phase.Warning: UpdateWarning(ctx); break;
            case Phase.Sweep:   UpdateSweep(ctx);   break;
            case Phase.Tsunami: UpdateTsunami(ctx); break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        CleanupWarn();
        CleanupFollowLight();
        CleanupAllTsunamis();
    }

    // ── Warning ───────────────────────────────────────────────────────────────

    private void UpdateWarning(MonsterContext ctx)
    {
        // 장판 페이드인
        float t = Mathf.Clamp01(_timer / Data.WarningDuration);
        foreach (var mat in _warnMats)
        {
            if (mat == null) continue;
            Color c = mat.color;
            c.a = Mathf.Lerp(0f, Data.WarningColor.a, t);
            mat.color = c;
        }
        if (_timer < Data.WarningDuration) return;

        // 경고장판 소멸
        CleanupWarn();

        // 드래곤을 시작 측면으로 순간이동
        _totalCols    = DKBossRoomContext.Width - 2;   // interior 열 수
        _nextColIndex = 0;
        PlaceDragonAtStartSide(ctx);

        // AirChase 애니메이션 적용 (진행 방향에 따라 Left/Right)
        string animName = _fromSide < 0
            ? Data.AirChaseRightStateName   // 왼→오 = 오른쪽으로 날기
            : Data.AirChaseLeftStateName;   // 오→왼 = 왼쪽으로 날기
        PlayAnim(ctx, animName);

        // 광원 생성
        CreateFollowLight(ctx);

        _phase = Phase.Sweep;
        _timer = 0f;
    }

    // ── Sweep ─────────────────────────────────────────────────────────────────

    private void UpdateSweep(MonsterContext ctx)
    {
        // 드래곤 수평 이동
        bool reachedEnd = MoveDragonAcross(ctx);

        // 광원 위치 갱신
        UpdateFollowLight(ctx);

        // 드래곤 X 기준으로 통과한 열에 브레스 발사
        while (_nextColIndex < _totalCols)
        {
            int colX = GetColX(_nextColIndex);
            float colWorldX = DKBossRoomContext.CellToWorld(colX, 0, 0f).x;
            float dragonX   = ctx.Transform.position.x;

            // 해당 열 중심 X를 아직 지나지 않았으면 대기
            bool passed = _fromSide < 0 ? dragonX >= colWorldX : dragonX <= colWorldX;
            if (!passed) break;

            FireBreathAtColumn(ctx, colX);
            _nextColIndex++;
        }

        if (reachedEnd || _nextColIndex >= _totalCols)
        {
            // 남은 열 처리 (빠른 종료 대비)
            while (_nextColIndex < _totalCols)
            {
                FireBreathAtColumn(ctx, GetColX(_nextColIndex));
                _nextColIndex++;
            }

            CleanupFollowLight();
            _phase = Phase.Tsunami;
            _timer = 0f;
        }
    }

    // ── Tsunami ───────────────────────────────────────────────────────────────

    private void UpdateTsunami(MonsterContext ctx)
    {
        // 모든 FlameTsunami 타이머 틱 (sweep 종료 후 일괄 시작)
        for (int i = _tsunamis.Count - 1; i >= 0; i--)
        {
            var e = _tsunamis[i];
            e.Timer     += Time.deltaTime;
            e.TickTimer += Time.deltaTime;

            if (e.TickTimer >= Data.TsunamiTickInterval)
            {
                e.TickTimer = 0f;
                ApplyTsunamiDamageAt(ctx, e.Position);
            }

            if (e.Timer >= Data.TsunamiDuration)
            {
                if (e.Go != null) Object.Destroy(e.Go);
                _tsunamis.RemoveAt(i);
                continue;
            }

            _tsunamis[i] = e;
        }

        if (_tsunamis.Count == 0)
        {
            _phase = Phase.Done;
            RestoreAgent(ctx);
            ctx.Monster.ChangeState<AttackReadyState>();
        }
    }

    // ── 행/열 계산 ────────────────────────────────────────────────────────────

    private void ComputeRows(MonsterContext ctx)
    {
        int playerZ = DKBossRoomContext.Height / 2;
        if (ctx.Runtime.PlayerTarget != null)
        {
            var cell = DKBossRoomContext.WorldToCell(ctx.Runtime.PlayerTarget.position);
            playerZ = Mathf.Clamp(cell.y, 1, DKBossRoomContext.Height - 2);
        }
        int half   = Data.WarningRowCount / 2;
        _rowMin    = Mathf.Max(1, playerZ - half);
        _rowMax    = Mathf.Min(DKBossRoomContext.Height - 2, playerZ + half);
        _centerRow = (_rowMin + _rowMax) / 2;
    }

    // fromSide 방향의 n번째 열 X 그리드 좌표
    private int GetColX(int index)
        => _fromSide < 0
            ? 1 + index
            : DKBossRoomContext.Width - 2 - index;

    // 그리드 행 z 인덱스 → 월드 Z
    private static float WorldZOfRow(int rowZ)
        => DKBossRoomContext.CellToWorld(0, rowZ, 0f).z;

    // ── 경고 타일 ─────────────────────────────────────────────────────────────

    private void SpawnWarnTiles()
    {
        CleanupWarn();
        var shader = Shader.Find("Sprites/Default")
                  ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");

        for (int z = _rowMin; z <= _rowMax; z++)
        {
            for (int x = 1; x <= DKBossRoomContext.Width - 2; x++)
            {
                Vector3 pos = DKBossRoomContext.CellToWorld(x, z, 0.1f);
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "BreathSweepWarn";
                Object.Destroy(go.GetComponent<MeshCollider>());
                go.transform.SetPositionAndRotation(pos, Quaternion.Euler(90f, 0f, 0f));
                go.transform.localScale = Vector3.one;

                var mr = go.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows    = false;
                var mat = new Material(shader);
                mat.color = new Color(
                    Data.WarningColor.r, Data.WarningColor.g, Data.WarningColor.b, 0f);
                mr.material = mat;

                _warnTiles.Add(go);
                _warnMats.Add(mat);
            }
        }
    }

    // ── 드래곤 이동 ───────────────────────────────────────────────────────────

    private void PlaceDragonAtStartSide(MonsterContext ctx)
    {
        float startX = _fromSide < 0
            ? DKBossRoomContext.WorldCenter.x - Data.FlySideOffset
            : DKBossRoomContext.WorldCenter.x + Data.FlySideOffset;

        ctx.Transform.position = new Vector3(
            startX,
            ctx.Runtime.SpawnPosition.y + Data.HideHeight,
            WorldZOfRow(_centerRow));

        // 진행 방향을 미리 바라봄
        ctx.Transform.rotation = Quaternion.LookRotation(
            new Vector3(_fromSide < 0 ? 1f : -1f, 0f, 0f));
    }

    // 반환값: 목적지 측면에 도달했으면 true
    private bool MoveDragonAcross(MonsterContext ctx)
    {
        float endX = _fromSide < 0
            ? DKBossRoomContext.WorldCenter.x + Data.FlySideOffset
            : DKBossRoomContext.WorldCenter.x - Data.FlySideOffset;

        Vector3 target = new Vector3(
            endX,
            ctx.Runtime.SpawnPosition.y + Data.HideHeight,
            WorldZOfRow(_centerRow));

        ctx.Transform.position = Vector3.MoveTowards(
            ctx.Transform.position, target, Data.FlySpeed * Time.deltaTime);

        return Vector3.Distance(ctx.Transform.position, target) < 0.3f;
    }

    // ── 따라다니는 광원 (그림자 연출) ────────────────────────────────────────

    private void CreateFollowLight(MonsterContext ctx)
    {
        var go = new GameObject("DragonSweepSpotlight");
        _followLight = go.AddComponent<Light>();
        _followLight.type      = LightType.Spot;
        _followLight.color     = Data.LightColor;
        _followLight.intensity = Data.LightIntensity;
        _followLight.range     = Data.LightRange;
        _followLight.spotAngle = Data.LightSpotAngle;
        _followLight.shadows   = LightShadows.Hard;
        UpdateFollowLight(ctx);
    }

    private void UpdateFollowLight(MonsterContext ctx)
    {
        if (_followLight == null) return;
        _followLight.transform.position =
            ctx.Transform.position + Vector3.up * Data.LightHeightOffset;
        // 수직 아래를 향하게
        _followLight.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    private void CleanupFollowLight()
    {
        if (_followLight == null) return;
        Object.Destroy(_followLight.gameObject);
        _followLight = null;
    }

    // ── 브레스 열 발동 ────────────────────────────────────────────────────────

    private void FireBreathAtColumn(MonsterContext ctx, int colX)
    {
        // FlameBreath: 드래곤 현재 위치에서 진행방향 + 아래 대각선으로
        if (Data.FlameBreathPrefab != null)
        {
            float forwardX  = _fromSide < 0 ? 1f : -1f;
            float downRad   = Data.BreathDownAngle * Mathf.Deg2Rad;
            Vector3 breathDir = new Vector3(
                forwardX * Mathf.Cos(downRad),
                -Mathf.Sin(downRad),
                0f).normalized;

            BossEffectPool.SpawnOneShot(
                Data.FlameBreathPrefab,
                ctx.Transform.position,          // 드래곤 현재 위치에서 발사
                Quaternion.LookRotation(breathDir, Vector3.up),
                fallbackLifetime: 1.5f);
        }

        // FlameTsunami: 해당 열의 경고장판 중간칸(centerRow) 지면에 즉시 생성
        SpawnTsunamiAtColumn(colX);

        // 피격 판정: 해당 열 경고 행 범위 내 플레이어 1회
        if (ctx?.Runtime?.PlayerTarget != null)
        {
            for (int z = _rowMin; z <= _rowMax; z++)
            {
                Vector3 cellPos    = DKBossRoomContext.CellToWorld(colX, z, 0.05f);
                Vector3 playerFlat = ctx.Runtime.PlayerTarget.position;
                playerFlat.y = cellPos.y;
                if (Vector3.Distance(playerFlat, cellPos) <= Data.DamageRadius)
                {
                    ctx.Runtime.PlayerTarget
                        .GetComponent<PlayerController>()
                        ?.TakeDamage(Data.BreathDamage);
                    break;
                }
            }
        }
    }

    // ── FlameTsunami ─────────────────────────────────────────────────────────

    private void SpawnTsunamiAtColumn(int colX)
    {
        if (Data.FlameTsunamiPrefab == null) return;

        Vector3 pos = DKBossRoomContext.CellToWorld(colX, _centerRow, 0.05f);
        var go = Object.Instantiate(Data.FlameTsunamiPrefab, pos, Quaternion.identity);

        // Timer = 0: Tsunami 페이즈 진입 후 틱 시작
        _tsunamis.Add(new TsunamiEntry
        {
            Go        = go,
            Position  = pos,
            Timer     = 0f,
            TickTimer = 0f,
        });
    }

    private void ApplyTsunamiDamageAt(MonsterContext ctx, Vector3 tsunamiPos)
    {
        if (ctx?.Runtime?.PlayerTarget == null) return;
        Vector3 playerPos = ctx.Runtime.PlayerTarget.position;
        playerPos.y = tsunamiPos.y;
        if (Vector3.Distance(playerPos, tsunamiPos) <= Data.DamageRadius * 3f)
            ctx.Runtime.PlayerTarget.GetComponent<PlayerController>()
                ?.TakeDamage(Data.TsunamiDamage);
    }

    // ── 정리 ─────────────────────────────────────────────────────────────────

    private void CleanupWarn()
    {
        foreach (var go  in _warnTiles) if (go  != null) Object.Destroy(go);
        foreach (var mat in _warnMats)  if (mat != null) Object.Destroy(mat);
        _warnTiles.Clear();
        _warnMats.Clear();
    }

    private void CleanupAllTsunamis()
    {
        foreach (var e in _tsunamis) if (e.Go != null) Object.Destroy(e.Go);
        _tsunamis.Clear();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        int hash = Animator.StringToHash(stateName);
        if (ctx.Animator.HasState(0, hash))
            ctx.Animator.CrossFade(stateName, 0.12f, 0, 0f);
    }

    private static void RestoreAgent(MonsterContext ctx)
    {
        if (ctx.Agent == null || ctx.Agent.enabled) return;
        ctx.Agent.enabled = true;
        if (UnityEngine.AI.NavMesh.SamplePosition(
            ctx.Transform.position, out var hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
            ctx.Agent.Warp(hit.position);
    }
}
}
