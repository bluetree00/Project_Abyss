using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// DeathKnight 영혼 창 투척 (SoulSpear / Attack2) 패턴.
///
/// Phase 1 콤보용 FullLockState 흐름:
///  Windup (0.5s) → Attack2 애니메이션
///  3회 반복:
///    플레이어 현재 행 캡처 → 해당 행 전체 경고 타일 생성
///    warningDuration(0.6s) 후 타일 제거 → 행 전체 폭 빔 VFX 출현
///    beamHitDelay(0.45s) 후 피격 판정 (플레이어 회피 창)
///    nextRoundDelay(0.3s) 대기
///  Recovery (0.4s)
///
/// 빔 VFX localScale.z = (Width-2)*CellSize (방 실제 가로 폭) 로 고정.
/// Phase 2 패시브 발동 공유 데이터: passiveWarningDuration, passiveBeamHitDelay, passiveCooldownMin/Max
/// </summary>
[CreateAssetMenu(menuName = "Abyss/Boss/DeathKnight/DK_SoulSpearPattern",
                 fileName = "DK_SoulSpearPattern")]
public class DKSoulSpearPatternSO : BossPatternSO
{
    [Header("그리드 타일")]
    public GameObject whiteTilePrefab;
    public GameObject blackTilePrefab;

    [Header("VFX")]
    [Tooltip("행 전체를 가로지르는 빔 VFX (Laser beam 8 soul)")]
    public GameObject impactVfxPrefab;

    [Header("사운드")]
    public AudioClip beamSfx;

    [Header("타이밍 — Phase 1 콤보")]
    public float windupDuration   = 0.5f;
    [Tooltip("경고 장판 지속 시간")]
    public float warningDuration  = 0.6f;
    [Tooltip("빔 출현 → 피격 판정까지 회피 창 (초)")]
    public float beamHitDelay     = 0.45f;
    [Tooltip("피격 후 다음 타격까지 인터벌")]
    public float nextRoundDelay   = 0.3f;
    public int   strikeCount      = 3;
    public float recoveryTime     = 0.4f;

    [Header("데미지")]
    public float damageMultiplier    = 1f;
    public float knockbackMultiplier = 1f;

    [Header("패시브 (Phase 2 DKP2PassiveAttackRunner)")]
    [Tooltip("패시브 경고 장판 지속 시간")]
    public float passiveWarningDuration   = 0.6f;
    [Tooltip("패시브 빔 출현 → 피격까지 회피 창")]
    public float passiveBeamHitDelay      = 0.4f;
    [Tooltip("버스트 내 공격과 공격 사이 대기 시간 최소 (s)")]
    public float passiveBurstIntervalMin  = 1.0f;
    [Tooltip("버스트 내 공격과 공격 사이 대기 시간 최대 (s)")]
    public float passiveBurstIntervalMax  = 2.0f;
    public float passiveCooldownMin       = 5f;
    public float passiveCooldownMax       = 8f;

    private DKSoulSpearState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new DKSoulSpearState(this);
    public override void OnRecycled()                       => _state = new DKSoulSpearState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var dk = ctx.Ctx.Monster as DeathKnightBossMonster;
        return dk == null || !dk.DKBlackboard.IsPhase2;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

public class DKSoulSpearState : FullLockState<DKSoulSpearPatternSO>
{
    private const string AnimName = "Attack2";

    private enum Phase { Windup, Striking, Recovery }

    private Phase            _phase;
    private float            _timer;
    private int              _strikeIndex;
    private bool             _beamVisible;       // 타일 제거, 빔 출현 완료
    private bool             _strikeApplied;     // 피격 판정 완료
    private List<GameObject> _tiles;
    private int              _currentRowZ;
    private float            _currentSpawnWorldX; // 빔 스폰 월드 X (플레이어 기준 왼쪽)
    private DKSwordColor     _swordColor;

    public DKSoulSpearState(DKSoulSpearPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _phase        = Phase.Windup;
        _timer        = 0f;
        _strikeIndex  = 0;
        _beamVisible  = false;
        _strikeApplied = false;
        _tiles        = new List<GameObject>();
        _swordColor   = GetSwordColor(ctx);

        StopAgent(ctx);
        FacePlayer(ctx);
        PlayAnim(ctx, AnimName);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime * AnimSpeed(ctx);

        switch (_phase)
        {
            case Phase.Windup:
                if (_timer >= Data.windupDuration)
                {
                    _phase = Phase.Striking;
                    _timer = 0f;
                    BeginStrike(ctx);
                }
                break;

            case Phase.Striking:
                // Stage 1: 경고 타일 → 빔 출현
                if (!_beamVisible && _timer >= Data.warningDuration)
                {
                    _beamVisible = true;
                    ClearTiles();
                    SpawnRowBeam(Data.impactVfxPrefab, _currentRowZ, _swordColor, _currentSpawnWorldX);
                    Managers.Sound?.PlayEffectAt(Data.beamSfx, DKBossRoomContext.CellToWorld(DKBossRoomContext.Width / 2, _currentRowZ, 0.1f));
                }
                // Stage 2: 빔 출현 → 피격 (회피 창)
                if (_beamVisible && !_strikeApplied && _timer >= Data.warningDuration + Data.beamHitDelay)
                {
                    _strikeApplied = true;
                    DKGridPatternHelper.TriggerSingleRowDamage(
                        ctx, _currentRowZ, Data.damageMultiplier, Data.knockbackMultiplier);
                    BossImpactFeedback.TriggerCameraShake(0.08f, 0.15f);
                }
                // Stage 3: 다음 타격으로
                if (_strikeApplied && _timer >= Data.warningDuration + Data.beamHitDelay + Data.nextRoundDelay)
                {
                    _strikeIndex++;
                    if (_strikeIndex >= Data.strikeCount)
                    {
                        _phase = Phase.Recovery;
                        _timer = 0f;
                    }
                    else
                    {
                        _timer = 0f;
                        BeginStrike(ctx);
                    }
                }
                break;

            case Phase.Recovery:
                if (_timer >= Data.recoveryTime)
                    ctx.Monster.ChangeState<AttackReadyState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        ClearTiles();
        RestoreAgent(ctx);
    }

    private void BeginStrike(MonsterContext ctx)
    {
        _beamVisible   = false;
        _strikeApplied = false;
        _timer         = 0f;

        if (ctx.Runtime.PlayerTarget == null) return;

        var playerCell       = DKBossRoomContext.WorldToCell(ctx.Runtime.PlayerTarget.position);
        _currentRowZ         = playerCell.y;
        _currentSpawnWorldX  = ctx.Runtime.PlayerTarget.position.x - DKBossRoomContext.CellSize;
        _swordColor          = GetSwordColor(ctx);

        ClearTiles();
        GameObject prefab = _swordColor == DKSwordColor.White
            ? Data.whiteTilePrefab : Data.blackTilePrefab;
        if (prefab == null) return;

        for (int x = 1; x <= DKBossRoomContext.Width - 2; x++)
        {
            if (!DKBossRoomContext.IsInterior(x, _currentRowZ)) continue;
            var go = BossEffectPool.Spawn(prefab,
                DKBossRoomContext.CellToWorld(x, _currentRowZ, 0.05f),
                Quaternion.Euler(-90f, 0f, 0f));
            if (go == null) continue;
            go.transform.localScale = Vector3.one * DKBossRoomContext.CellSize;
            _tiles.Add(go);
        }
    }

    private void ClearTiles()
    {
        foreach (var go in _tiles) BossEffectPool.Release(go);
        _tiles.Clear();
    }

    /// <summary>빔 VFX를 플레이어 기준 왼쪽(spawnWorldX)에서 오른쪽으로 날아가도록 스폰한다.</summary>
    public static void SpawnRowBeam(GameObject prefab, int rowZ, DKSwordColor sc, float spawnWorldX)
    {
        if (prefab == null) return;
        Vector3 rowCenter = DKBossRoomContext.CellToWorld(DKBossRoomContext.Width / 2, rowZ, 0.1f);
        Vector3 pos = new Vector3(spawnWorldX, rowCenter.y, rowCenter.z);
        var go = BossEffectPool.SpawnOneShot(prefab, pos, Quaternion.Euler(0f, 90f, 0f), fallbackLifetime: 2f);
        if (go == null) return;
        go.transform.localScale = new Vector3(DKBossRoomContext.CellSize, 1f, 7f);
        DKGridPatternHelper.TintVfx(go, sc == DKSwordColor.White ? Color.white : DKGridPatternHelper.DarkTint);
    }

    // ── 헬퍼 ──────────────────────────────────────────────────

    private static DKSwordColor GetSwordColor(MonsterContext ctx)
        => (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.SwordColor ?? DKSwordColor.White;

    private static float AnimSpeed(MonsterContext ctx)
        => (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.AnimSpeedMult ?? 1f;

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        ctx.Animator.speed = AnimSpeed(ctx);
        ctx.Animator.CrossFade(stateName, 0.05f, 0, 0f);
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
            ctx.Agent.isStopped = false;
    }

    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }
}
}
