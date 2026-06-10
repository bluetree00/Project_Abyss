using System;
using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DeathKnight 피라미드 슬래시 패턴.
///
/// 흐름:
///  MovingToCenter  → Walk1 재생, NavMesh로 맵 중앙 이동
///  TileExpansion   → Idle2 루프, DK 무적 + FireShield + 부유검 소환
///                    경고 타일 링 단위로 맵 전체 확장
///                    같은 색 검 파괴 → GuardianShield 생성 (피격 면제 존)
///                    다른 색 검 파괴 → 타일 확장 가속
///  SlashAttack     → 전 가로줄 Sword Slash 15 + slashHitDelay 후 피격
///                    GuardianShield 내 플레이어 면제
///  Recovery        → recoveryTime 후 AttackReadyState
/// </summary>
[CreateAssetMenu(menuName = "Abyss/Boss/DeathKnight/DK_PyramidSlashPattern",
                 fileName = "DK_PyramidSlashPattern")]
public class DKPyramidSlashPatternSO : BossPatternSO
{
    [Header("Grid Tiles")]
    [Tooltip("흰색 경고 타일 프리팹")]
    public GameObject whiteTilePrefab;
    [Tooltip("검은색 경고 타일 프리팹")]
    public GameObject blackTilePrefab;

    [Header("VFX")]
    [Tooltip("Sword Slash 15 — 가로줄 슬래시 이펙트")]
    public GameObject impactVfxPrefab;
    [Tooltip("FireShield — DK를 감싸는 이펙트 (데스나이트 이펙트 폴더)")]
    public GameObject fireShieldPrefab;
    [Tooltip("Effect_09_GuardianShield — 보호막 이펙트 (데스나이트 이펙트 폴더)")]
    public GameObject guardianShieldPrefab;
    [Tooltip("GuardianShield 보호 반경")]
    public float guardianShieldRadius = 2f;

    [Header("Floating Swords")]
    [Tooltip("SM_DarkKnight2_Sword 메시 프리팹")]
    public GameObject floatingSwordPrefab;
    [Tooltip("흰색 검 머티리얼")]
    public Material whiteSwordMaterial;
    [Tooltip("검은색 검 머티리얼")]
    public Material blackSwordMaterial;
    [Tooltip("DK 좌우 수평 거리")]
    public float swordSideOffset   = 2.5f;
    [Tooltip("소환 높이")]
    public float swordHeight        = 2.2f;
    [Tooltip("상하 부동 진폭")]
    public float swordBobAmplitude  = 0.25f;
    [Tooltip("상하 부동 속도")]
    public float swordBobSpeed      = 1.5f;
    [Tooltip("검 최대 체력")]
    public float swordHp            = 30f;

    [Header("Timing")]
    [Tooltip("맵 중앙 이동 최대 허용 시간")]
    public float moveToCenterTime   = 2.5f;
    [Tooltip("타일 링 1층 확장 간격 (초)")]
    public float tileLayerDelay     = 0.35f;
    [Tooltip("전체 맵 덮인 후 슬래시 발동까지 대기")]
    public float slashHitDelay      = 1.2f;
    [Tooltip("슬래시 후 AttackReady 까지 대기")]
    public float recoveryTime       = 0.4f;
    [Tooltip("다른 색 검 파괴 시 타일 확장 가속 배수")]
    public float wrongSwordSpeedMult = 2.5f;

    [Header("Damage")]
    public float damageMultiplier    = 1.5f;
    public float knockbackMultiplier = 1f;

    private DKPyramidSlashState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new DKPyramidSlashState(this);
    public override void OnRecycled()                       => _state = new DKPyramidSlashState(this);

    public override bool CanExecute(BossPatternContext ctx)
        => ctx.Ctx.Runtime.PlayerTarget != null;

    public override SpecialStateBase GetRuntimeState() => _state;
}

public class DKPyramidSlashState : FullLockState<DKPyramidSlashPatternSO>
{
    private const string AnimWalk  = "Walk1";
    private const string AnimIdle2 = "Idle2";

    private enum Phase { MovingToCenter, TileExpansion, SlashAttack, Recovery }

    // ── 페이즈 ────────────────────────────────────────────
    private Phase _phase;
    private float _timer;

    // ── 타일 ─────────────────────────────────────────────
    private List<DKTileInfo> _tiles;
    private int              _currentLayer;
    private int              _maxLayer;
    private float            _tileLayerTimer;
    private float            _activeTileDelay;
    private Vector2Int       _centerCell;
    private DKSwordColor     _swordColor;

    // Effect_09_GuardianShield 프리팹 스케일(1,1,1) 기준 구체 방출기 반경
    private const float ShieldVfxSphereUnit = 15f;

    // ── 이펙트 ────────────────────────────────────────────
    private GameObject _fireShieldVfx;
    private GameObject _guardianShieldVfx;
    private bool       _guardianShieldActive;
    private Vector3    _guardianShieldPos;

    // ── 부유 검 ───────────────────────────────────────────
    private readonly List<DKFloatingSword> _swords = new List<DKFloatingSword>();
    private bool                           _patternEnded;

    // ── 슬래시 ───────────────────────────────────────────
    private bool  _damageDone;

    public DKPyramidSlashState(DKPyramidSlashPatternSO data) : base(data) { }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Enter / Exit
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public override void Enter(MonsterContext ctx)
    {
        _phase                = Phase.MovingToCenter;
        _timer                = 0f;
        _tiles                = new List<DKTileInfo>();
        _currentLayer         = 0;
        _tileLayerTimer       = 0f;
        _activeTileDelay      = Data.tileLayerDelay;
        _swordColor           = GetSwordColor(ctx);
        _fireShieldVfx        = null;
        _guardianShieldVfx    = null;
        _guardianShieldActive = false;
        _guardianShieldPos    = Vector3.zero;
        _swords.Clear();
        _patternEnded = false;
        _damageDone   = false;

        _centerCell = new Vector2Int(DKBossRoomContext.Width / 2, DKBossRoomContext.Height / 2);
        _maxLayer   = CalculateMaxLayer();

        MoveToCenter(ctx);
        PlayAnim(ctx, AnimWalk);
    }

    public override void Exit(MonsterContext ctx)
    {
        _patternEnded = true;
        (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.SetInvincible(false);
        CleanupEffects();
        DKGridPatternHelper.DestroyTiles(_tiles);
        RestoreAgent(ctx);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // Update
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.MovingToCenter: UpdateMovingToCenter(ctx); break;
            case Phase.TileExpansion:  UpdateTileExpansion(ctx);  break;
            case Phase.SlashAttack:    UpdateSlashAttack(ctx);    break;
            case Phase.Recovery:       UpdateRecovery(ctx);       break;
        }
    }

    // ── Phase: MovingToCenter ──────────────────────────────

    private void UpdateMovingToCenter(MonsterContext ctx)
    {
        bool arrived = ctx.Agent != null && ctx.Agent.isOnNavMesh &&
                       !ctx.Agent.pathPending && ctx.Agent.remainingDistance < 0.8f;
        if (!arrived && _timer < Data.moveToCenterTime) return;

        StopAgent(ctx);
        PlayAnim(ctx, AnimIdle2);

        // 무적 + FireShield + 부유검 소환
        (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.SetInvincible(true);
        SpawnFireShield(ctx);
        SpawnFloatingSwords(ctx);

        _phase         = Phase.TileExpansion;
        _timer         = 0f;
        _tileLayerTimer = 0f;
    }

    // ── Phase: TileExpansion ───────────────────────────────

    private void UpdateTileExpansion(MonsterContext ctx)
    {
        _tileLayerTimer += Time.deltaTime;

        if (_currentLayer <= _maxLayer && _tileLayerTimer >= _activeTileDelay)
        {
            _tileLayerTimer = 0f;
            SpawnTileLayer(_currentLayer);
            _currentLayer++;
        }

        // 전체 맵 덮임 → 양방향 슬래시 VFX 즉시 스폰 후 피격 대기
        if (_currentLayer > _maxLayer)
        {
            FireAllRowSlashVfx();
            _phase = Phase.SlashAttack;
            _timer = 0f;
        }
    }

    // ── Phase: SlashAttack ─────────────────────────────────

    private void UpdateSlashAttack(MonsterContext ctx)
    {
        if (!_damageDone && _timer >= Data.slashHitDelay)
        {
            _damageDone = true;
            ApplySlashDamage(ctx);
            // §3 타격감 — 전멸기급 강공격 (0.2s 히트스톱)
            BossImpactFeedback.TriggerHitStop(0.2f);
            BossImpactFeedback.TriggerCameraShake(0.2f, 0.4f);
            BossImpactFeedback.TriggerScreenFlash(new Color(1f, 0.9f, 0.7f, 0.5f), 0.1f);
            _phase = Phase.Recovery;
            _timer = 0f;
        }
    }

    // ── Phase: Recovery ────────────────────────────────────

    private void UpdateRecovery(MonsterContext ctx)
    {
        if (_timer >= Data.recoveryTime)
            ctx.Monster.ChangeState<AttackReadyState>();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 타일 확장
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private int CalculateMaxLayer()
    {
        int cx = _centerCell.x, cz = _centerCell.y;
        int maxDist = 0;
        foreach (var cell in DKBossRoomContext.GetInteriorCells())
        {
            int ring = Mathf.Max(Mathf.Abs(cell.x - cx), Mathf.Abs(cell.y - cz));
            if (ring > maxDist) maxDist = ring;
        }
        return maxDist;
    }

    private void SpawnTileLayer(int ring)
    {
        int cx = _centerCell.x, cz = _centerCell.y;
        GameObject prefab = _swordColor == DKSwordColor.White
            ? Data.whiteTilePrefab : Data.blackTilePrefab;
        if (prefab == null) return;

        if (ring == 0)
        {
            SpawnOneTile(cx, cz, prefab);
            return;
        }

        // 상단/하단 행 (코너 포함)
        for (int x = cx - ring; x <= cx + ring; x++)
        {
            SpawnOneTile(x, cz + ring, prefab);
            SpawnOneTile(x, cz - ring, prefab);
        }
        // 좌측/우측 열 (코너 제외)
        for (int z = cz - ring + 1; z < cz + ring; z++)
        {
            SpawnOneTile(cx - ring, z, prefab);
            SpawnOneTile(cx + ring, z, prefab);
        }
    }

    private void SpawnOneTile(int x, int z, GameObject prefab)
    {
        if (!DKBossRoomContext.IsInterior(x, z)) return;
        Vector3    pos = DKBossRoomContext.CellToWorld(x, z, 0.05f);
        GameObject go  = BossEffectPool.Spawn(prefab, pos, Quaternion.Euler(-90f, 0f, 0f));
        if (go == null) return;
        _tiles.Add(new DKTileInfo { Cell = new Vector2Int(x, z), Color = _swordColor, GO = go });
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 슬래시 & 피격
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void FireAllRowSlashVfx()
    {
        if (Data.impactVfxPrefab == null) return;
        Color tint = _swordColor == DKSwordColor.White ? Color.white : Color.black;

        for (int z = 1; z <= DKBossRoomContext.Height - 2; z++)
        {
            Vector3 pos = DKBossRoomContext.CellToWorld(DKBossRoomContext.Width / 2, z, 0.1f);
            DKGridPatternHelper.SpawnStretchedVfx(
                Data.impactVfxPrefab, pos, Quaternion.Euler(0f,  90f, 0f), 3f, tint);
            DKGridPatternHelper.SpawnStretchedVfx(
                Data.impactVfxPrefab, pos, Quaternion.Euler(0f, -90f, 0f), 3f, tint);
        }
    }

    private void ApplySlashDamage(MonsterContext ctx)
    {
        if (ctx?.Runtime?.PlayerTarget == null) return;

        // GuardianShield 안에 있으면 면제 (XZ 평면 거리만 비교)
        if (_guardianShieldActive)
        {
            Vector3 playerXZ = new Vector3(ctx.Runtime.PlayerTarget.position.x, 0f, ctx.Runtime.PlayerTarget.position.z);
            Vector3 shieldXZ = new Vector3(_guardianShieldPos.x, 0f, _guardianShieldPos.z);
            if (Vector3.Distance(playerXZ, shieldXZ) <= Data.guardianShieldRadius) return;
        }

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier));
        player.TakeDamage(dmg);

        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        toPlayer.y = 0.2f;
        Vector3 knockDir = toPlayer.sqrMagnitude > 0.001f ? toPlayer.normalized : ctx.Transform.forward;
        player.ApplyKnockback(knockDir * ctx.Config.stat.knockbackForce * Data.knockbackMultiplier);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // FireShield & 부유검
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void SpawnFireShield(MonsterContext ctx)
    {
        if (Data.fireShieldPrefab == null) return;
        _fireShieldVfx = BossEffectPool.Spawn(
            Data.fireShieldPrefab, ctx.Transform.position, Quaternion.identity);
        if (_fireShieldVfx == null) return;
        _fireShieldVfx.transform.SetParent(ctx.Transform, worldPositionStays: true);
        Color tint = _swordColor == DKSwordColor.White ? Color.white : Color.black;
        DKGridPatternHelper.TintVfx(_fireShieldVfx, tint);
    }

    private void SpawnFloatingSwords(MonsterContext ctx)
    {
        if (Data.floatingSwordPrefab == null) return;

        Vector3 dkPos   = ctx.Transform.position;
        Vector3 dkRight = ctx.Transform.right;

        bool leftIsWhite   = UnityEngine.Random.value > 0.5f;
        bool leftIsSame    = leftIsWhite == (_swordColor == DKSwordColor.White);
        bool rightIsSame   = !leftIsSame;

        SpawnOneSword(
            dkPos - dkRight * Data.swordSideOffset + Vector3.up * Data.swordHeight,
            leftIsWhite  ? DKSwordColor.White : DKSwordColor.Black,
            leftIsSame);

        SpawnOneSword(
            dkPos + dkRight * Data.swordSideOffset + Vector3.up * Data.swordHeight,
            !leftIsWhite ? DKSwordColor.White : DKSwordColor.Black,
            rightIsSame);
    }

    private void SpawnOneSword(Vector3 position, DKSwordColor color, bool isSameColorAsDK)
    {
        // 칼끝이 바닥을 향하도록 X축 180° 회전
        var go = UnityEngine.Object.Instantiate(
            Data.floatingSwordPrefab, position, Quaternion.Euler(180f, 0f, 0f));
        if (go == null) return;

        // 색상 머티리얼 적용
        var rend = go.GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            Material mat = color == DKSwordColor.White
                ? Data.whiteSwordMaterial : Data.blackSwordMaterial;
            if (mat != null) rend.material = mat;
        }

        // 콜라이더 없으면 추가
        if (go.GetComponentInChildren<Collider>() == null)
        {
            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.6f;
        }

        // DKFloatingSword 컴포넌트
        var sword = go.GetComponent<DKFloatingSword>();
        if (sword == null) sword = go.AddComponent<DKFloatingSword>();

        sword.Initialize(
            isSameColorAsDK,
            Data.swordHp,
            Data.swordBobAmplitude,
            Data.swordBobSpeed,
            OnSwordDestroyed);

        _swords.Add(sword);
    }

    private void OnSwordDestroyed(bool isSameColorAsDK, Vector3 position)
    {
        if (_patternEnded) return;

        if (isSameColorAsDK)
        {
            // 같은 색 검 파괴 → GuardianShield 생성 (지면 기준 위치)
            Vector3 groundPos = new Vector3(position.x, DKBossRoomContext.WorldCenter.y, position.z);
            _guardianShieldActive = true;
            _guardianShieldPos    = groundPos;

            if (Data.guardianShieldPrefab != null)
            {
                _guardianShieldVfx = BossEffectPool.Spawn(
                    Data.guardianShieldPrefab, groundPos, Quaternion.identity);
                if (_guardianShieldVfx != null)
                {
                    _guardianShieldVfx.transform.localScale =
                        Vector3.one * (Data.guardianShieldRadius / ShieldVfxSphereUnit);
                    ApplyGuardianShieldTint(_guardianShieldVfx, _swordColor);
                }
            }
        }
        else
        {
            // 다른 색 검 파괴 → 타일 확장 가속
            _activeTileDelay = Mathf.Max(0.05f, _activeTileDelay / Data.wrongSwordSpeedMult);
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 정리
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void CleanupEffects()
    {
        if (_fireShieldVfx != null)
        {
            _fireShieldVfx.transform.SetParent(null);
            BossEffectPool.Release(_fireShieldVfx);
            _fireShieldVfx = null;
        }

        if (_guardianShieldVfx != null)
        {
            BossEffectPool.Release(_guardianShieldVfx);
            _guardianShieldVfx = null;
        }

        foreach (var sword in _swords)
        {
            if (sword != null && sword.gameObject != null)
                UnityEngine.Object.Destroy(sword.gameObject);
        }
        _swords.Clear();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void MoveToCenter(MonsterContext ctx)
    {
        Vector3 centerWorld = DKBossRoomContext.CellToWorld(_centerCell.x, _centerCell.y, 0f);
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped        = false;
            ctx.Agent.stoppingDistance = 0.5f;
            ctx.Agent.speed            = ctx.Stat.moveSpeed * ctx.Runtime.SpeedMultiplier;
            ctx.Agent.SetDestination(centerWorld);
        }
    }

    private static void StopAgent(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.velocity  = Vector3.zero;
            ctx.Agent.ResetPath();
        }
    }

    private static void RestoreAgent(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped        = false;
            ctx.Agent.stoppingDistance = 1.0f;
        }
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        ctx.Animator.speed = (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.AnimSpeedMult ?? 1f;
        ctx.Animator.CrossFade(stateName, 0.15f);
    }

    private static DKSwordColor GetSwordColor(MonsterContext ctx)
        => (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.SwordColor ?? DKSwordColor.White;

    // Effect_09 셰이더는 _TintColor(HDR)로 색상 제어 — TintVfx의 _BaseColor/_Color 경로가 무효
    private static void ApplyGuardianShieldTint(GameObject go, DKSwordColor swordColor)
    {
        // White: 밝은 은백색 HDR / Black: 어두운 남보라 HDR (additive에서 가시적)
        Color tint = swordColor == DKSwordColor.White
            ? new Color(5f, 5.5f, 6f, 1f)
            : new Color(0.8f, 0.4f, 3f, 1f);

        foreach (var rend in go.GetComponentsInChildren<Renderer>(true))
            foreach (var mat in rend.materials)
                if (mat.HasProperty("_TintColor"))
                    mat.SetColor("_TintColor", tint);

        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
        }
    }
}
}
