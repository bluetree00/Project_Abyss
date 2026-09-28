using System.Collections.Generic;
using RelicFairy.UI;
using UnityEngine;
using UnityEngine.AI;

namespace RelicFairy.Monster
{
/// <summary>
/// M7 「결계」 — 메카닉 패턴, 리치 무적. 리치 설계서 §3-2 · §13.
/// (클래스·필드 이름의 Seal은 옛 이름 — 직렬화 호환 때문에 둔다. 「봉인」은 플레이어의 행위라 화면에는 「결계」로 쓴다.)
///
/// 흐름: 리치가 제단 중앙으로 내려와 결계 시전 — 코어를 덮는 보라 돔 + 결계 해골 sealCount마리(청록 표식)
///             + 방해 해골 additionalSummonCount마리
///       → 결계 중 periodicAttackInterval마다 플레이어 주변 산개 원 + 조준 원
///       → 결계 해골을 모두 쓰러뜨리면 돔이 깨지고 리치가 groggyDuration 동안 휘청(무적 해제) → ChaseState
///       → 시간 제한(sealActiveTime &gt; 0) 초과 시 벌칙 피해 → ChaseState (즉사 없음)
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_SealBreakerPattern", fileName = "Lich_SealBreakerPattern")]
public class LichSealBreakerPatternSO : BossPatternSO
{
    [Header("Seal Breaker — Seals")]
    [Tooltip("결계 해골 수. 모두 처치해야 패턴 해제.")]
    public int sealCount = 3;
    [Tooltip("결계 해골 소환 반경 (m)")]
    public float sealRadius = 5f;
    [Tooltip("결계 유지 시간 (초). 0 이하면 제한 없음 — 해골을 모두 처치할 때까지 무한 유지.")]
    public float sealActiveTime = 0f;
    [Tooltip("시간 초과 시 플레이어에게 주는 벌칙 데미지 (공격력 대비 배율)")]
    public float punishmentMultiplier = 1.2f;
    [Tooltip("결계 해골 프리팹. null이면 Disc 가이드만 표시 (테스트용).")]
    public GameObject sealSkeletonPrefab;

    [Header("Seal Breaker — Support Skeletons")]
    [Tooltip("방해 해골 추가 소환 수")]
    public int additionalSummonCount = 2;
    [Tooltip("방해 해골 소환 반경 (m)")]
    public float additionalSummonRadius = 7f;
    [Tooltip("방해 해골 프리팹. null이면 소환 없음.")]
    public GameObject additionalSkeletonPrefab;

    [Header("Seal Breaker — Visuals")]
    [Tooltip("결계 해골 바닥 마커 Y 오프셋")]
    public float groundYOffset = 0.05f;

    [Header("Seal Breaker — Periodic Attack")]
    [Tooltip("결계 중 주기적 광역 공격 간격 (초). 0이면 미사용.")]
    public float periodicAttackInterval = 2.5f;
    [Tooltip("광역 공격 1회당 소환 존 수")]
    public int periodicZoneCount = 3;
    [Tooltip("플레이어 위치 기준 존 산개 반경 (m)")]
    public float periodicScatterRadius = 4f;
    [Tooltip("광역 공격 존 반경 (m)")]
    public float periodicZoneRadius = 1.5f;
    [Tooltip("광역 공격 예고 시간 (초)")]
    public float periodicTelegraphDuration = 0.8f;
    [Tooltip("광역 공격 판정 유지 시간 (초)")]
    public float periodicActiveDuration = 0.5f;
    [Tooltip("광역 공격 데미지 배율 (공격력 대비)")]
    public float periodicDamageMultiplier = 0.5f;

    [Header("Seal Breaker — Aimed Attack")]
    [Tooltip("결계 중 주기 공격 시 플레이어 위치에 직접 조준하는 소규모 존 수")]
    public int aimedZoneCount = 1;
    [Tooltip("조준 존 반경 (m). 직접 타격감.")]
    public float aimedZoneRadius = 1.2f;
    [Tooltip("조준 존 데미지 배율 (공격력 대비)")]
    public float aimedDamageMultiplier = 0.7f;

    [Header("결계 — 연출")]
    [Tooltip("결계 돔 반경 (m) — 이펙트 크기의 기준")]
    public float domeRadius     = 10f;
    [Tooltip("결계가 깨진 뒤 리치가 휘청이는 시간 (초, 무적 해제)")]
    public float groggyDuration = 1.5f;
    [Tooltip("중앙으로 내려오는 시간 (초)")]
    public float descendSeconds = 1.0f;

    [Header("Seal Breaker — Cooldown")]
    [Tooltip("패턴 완료 후 재사용 대기 시간 (초)")]
    public float patternCooldown = 35f;

    // ── 런타임 ───────────────────────────────────────────
    private LichSealBreakerState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichSealBreakerState(this);
    public override void OnRecycled()                       => _state = new LichSealBreakerState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        var lichBB = (ctx.Boss as LichMonster)?.LichBB;
        return lichBB == null || lichBB.SealBreakerCooldown <= 0f;
    }

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichSealBreakerState — Invincible (모든 데미지 무시)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichSealBreakerState : InvincibleState<LichSealBreakerPatternSO>
{
    private static readonly Color InvincibleRingColor = new Color(0.55f, 0f, 1f, 0.6f);

    private static readonly string[] CastAnimations = { "ArcaneOrb", "ElementalBarrage", "MagicBolt" };

    private const string StartBarkKey  = "Lich_Ward";          // 0 리치 · 1 멀린
    private const string BrokenBarkKey = "Lich_Ward_Broken";
    private const string TimeoutBarkKey = "Lich_Ward_Timeout";

    private float      _elapsed;
    private float      _periodicTimer;
    private int        _sealsRemaining;
    private bool       _finished;
    private float      _groggyTimer;
    private int        _attackCycle;
    private GameObject _invincibilityGuide;
    private GameObject _dome;
    private Vector3    _center;
    private UI_ChallengeHud _hud;
    private readonly List<SealSkeletonMarker> _sealMarkers = new();

    public LichSealBreakerState(LichSealBreakerPatternSO data) : base(data) { }

    /// <summary>결계가 깨진 뒤(휘청) 무적이 풀린다.</summary>
    public override SpecialStateConstraint Constraints =>
        _finished ? SpecialStateConstraint.UnInterruptible : base.Constraints;

    public override void Enter(MonsterContext ctx)
    {
        _elapsed        = 0f;
        _periodicTimer  = 0f;
        _sealsRemaining = Data.sealCount;
        _finished       = false;
        _groggyTimer    = 0f;
        _attackCycle    = 0;

        ctx.Animator?.CrossFade("SkeletonSummon", 0.1f);

        // 제단 중앙으로 내려와 결계를 친다.
        var     mc     = (ctx.Monster as LichMonster)?.MovementController;
        Vector3 center = mc != null ? mc.ArenaCenter : ctx.Transform.position;
        if (mc != null)
        {
            mc.SetLocked(true);
            mc.ScriptMove(center, Data.descendSeconds, 0f, facePlayer: true);
        }
        center  = LichPatternUtil.OnFloor(ctx, center);
        _center = center;

        UI_BossBark.ShowDialogue(StartBarkKey, BossBarkType.PatternAnnounce);
        _hud = UI_ChallengeHud.Create();
        _hud.SetObjective("결계 해골을 쓰러뜨려라 — 청록 표식");
        RefreshHud();

        _invincibilityGuide = PatternGuideHelper.Disc(center, Data.sealRadius + 0.5f, InvincibleRingColor);
        _dome = LichVfx.PlayLoop(LichVfxSlot.WardDome, center, Quaternion.identity, Data.domeRadius / 10f);
        LichSfx.Play(LichSfxSlot.ZoneHum, center);
        (ctx.Monster as LichMonster)?.PulseBook(Data.descendSeconds + 0.5f);

        Debug.Log($"[SealBreaker] 시작 — 결계 해골 {Data.sealCount}마리, 제한 시간 {Data.sealActiveTime}s");

        SpawnSealSkeletons(ctx);
        SpawnAdditionalSkeletons(ctx);

        // 실제 생성된 결계 마커 수로 보정. 프리팹 미할당(테스트)으로 마커가 0개면
        // 다음 Update에서 즉시 종료해 무적·이동 잠금이 영구 지속되는 soft-lock을 방지한다.
        _sealsRemaining = _sealMarkers.Count;
    }

    public override void Update(MonsterContext ctx)
    {
        if (_finished)
        {
            _groggyTimer += Time.deltaTime;
            if (_groggyTimer >= Data.groggyDuration)
                ctx.Monster.ChangeState<ChaseState>();
            return;
        }

        _elapsed += Time.deltaTime;

        // 결계 중 항상 플레이어 방향으로 회전
        if (ctx.Runtime.PlayerTarget != null)
            (ctx.Monster as LichMonster)?.MovementController?.FaceTowards(
                ctx.Runtime.PlayerTarget.position, Time.deltaTime);

        if (_sealsRemaining <= 0)
        {
            FinishPattern(ctx, success: true);
            return;
        }

        if (Data.sealActiveTime > 0f && _elapsed >= Data.sealActiveTime)
        {
            FinishPattern(ctx, success: false);
            return;
        }

        if (Data.periodicAttackInterval > 0f)
        {
            _periodicTimer += Time.deltaTime;
            if (_periodicTimer >= Data.periodicAttackInterval)
            {
                _periodicTimer = 0f;
                SpawnPeriodicAttack(ctx);
            }
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        PatternGuideHelper.SafeDestroy(ref _invincibilityGuide);
        LichVfx.Stop(ref _dome);
        _hud?.Close();
        _hud = null;

        foreach (var m in _sealMarkers)
            if (m != null) m.OnKilled -= OnSealKilled;
        _sealMarkers.Clear();

        (ctx.Monster as LichMonster)?.MovementController?.SetLocked(false);

        var lich = ctx.Monster as LichMonster;
        if (lich?.LichBB != null)
            lich.LichBB.SealBreakerCooldown = Data.patternCooldown;
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private void SpawnSealSkeletons(MonsterContext ctx)
    {
        float angleStep = 360f / Data.sealCount;
        Vector3 origin  = _center;   // 리치가 내려올 제단 중앙 둘레
        float groundY   = (ctx.Monster as LichMonster)?.SpawnGroundY ?? 0f;

        for (int i = 0; i < Data.sealCount; i++)
        {
            float   angle  = i * angleStep;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * Data.sealRadius;
            Vector3 pos    = origin + offset;
            if (NavMesh.SamplePosition(pos, out NavMeshHit navHit, 10f, NavMesh.AllAreas))
                pos = new Vector3(navHit.position.x, navHit.position.y + Data.groundYOffset, navHit.position.z);
            else
                pos.y = groundY + Data.groundYOffset;

            if (Data.sealSkeletonPrefab != null)
            {
                var go     = Object.Instantiate(Data.sealSkeletonPrefab, pos, Quaternion.identity);
                var marker = go.AddComponent<SealSkeletonMarker>();
                marker.OnKilled += OnSealKilled;
                _sealMarkers.Add(marker);
            }
            else
            {
                PatternGuideHelper.Disc(pos, 0.7f, PatternGuideHelper.Breakable, lifetime: Data.sealActiveTime);
            }
        }
    }

    private void SpawnAdditionalSkeletons(MonsterContext ctx)
    {
        if (Data.additionalSkeletonPrefab == null || Data.additionalSummonCount <= 0) return;

        float   angleStep = 360f / Data.additionalSummonCount;
        Vector3 origin    = _center;
        float   groundY   = (ctx.Monster as LichMonster)?.SpawnGroundY ?? 0f;

        for (int i = 0; i < Data.additionalSummonCount; i++)
        {
            float   angle  = i * angleStep + 30f;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * Data.additionalSummonRadius;
            Vector3 pos    = origin + offset;
            if (NavMesh.SamplePosition(pos, out NavMeshHit navHit, 10f, NavMesh.AllAreas))
                pos = navHit.position;
            else
                pos.y = groundY;
            Object.Instantiate(Data.additionalSkeletonPrefab, pos, Quaternion.identity);
        }
    }

    private void SpawnPeriodicAttack(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null || ctx.Config?.stat == null) return;

        // 마법 시전 애니메이션 재생 (순환)
        string animName = CastAnimations[_attackCycle % CastAnimations.Length];
        ctx.Animator?.CrossFade(animName, 0.1f);
        _attackCycle++;

        Vector3 basePos = ctx.Runtime.PlayerTarget.position;
        int scatterDmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.periodicDamageMultiplier));
        int aimedDmg   = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.aimedDamageMultiplier));

        // 플레이어 위치 직접 조준 존 — 짧은 예고로 긴장감 증가
        for (int i = 0; i < Data.aimedZoneCount; i++)
        {
            LichDarkRainZone.Spawn(basePos + new Vector3(0f, 0.05f, 0f), Data.aimedZoneRadius,
                Data.periodicTelegraphDuration * 0.6f, Data.periodicActiveDuration,
                aimedDmg, 0.5f);
        }

        // 산개 존 — 추가 압박
        for (int i = 0; i < Data.periodicZoneCount; i++)
        {
            Vector2 rand2D = Random.insideUnitCircle * Data.periodicScatterRadius;
            Vector3 pos = basePos + new Vector3(rand2D.x, 0.05f, rand2D.y);
            LichDarkRainZone.Spawn(pos, Data.periodicZoneRadius,
                Data.periodicTelegraphDuration, Data.periodicActiveDuration,
                scatterDmg, 0.5f);
        }
    }

    private void OnSealKilled()
    {
        _sealsRemaining--;
        RefreshHud();
        // 결계에 금이 간다 — 돔 위에서 번쩍 + 금 가는 소리
        if (_dome != null && _sealsRemaining > 0)
        {
            Vector3 p = _dome.transform.position + Vector3.up * 3f;
            LichVfx.Play(LichVfxSlot.ArcaneOrbBurst, p, Quaternion.identity, 1.2f);
            LichSfx.Play(LichSfxSlot.WardCrack, p);
        }
        Debug.Log($"[SealBreaker] 결계 해골 처치 — 남은 {_sealsRemaining}/{Data.sealCount}");
    }

    private void RefreshHud() => _hud?.SetStatus($"결계 해골 {Data.sealCount - _sealsRemaining} / {Data.sealCount}");

    private void FinishPattern(MonsterContext ctx, bool success)
    {
        _finished    = true;
        _groggyTimer = 0f;
        PatternGuideHelper.SafeDestroy(ref _invincibilityGuide);
        _hud?.Close();
        _hud = null;

        if (!success)
        {
            Debug.Log($"[SealBreaker] 시간 초과 — 벌칙 데미지 x{Data.punishmentMultiplier}");
            if (ctx.Config?.stat != null && ctx.Runtime.PlayerTarget != null)
            {
                var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
                if (player != null)
                {
                    int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.punishmentMultiplier));
                    player.TakeDamage(dmg, null, false, HitWeight.Light);   // 결계 폭격 — 약
                    Debug.Log($"[SealBreaker] 벌칙 데미지 {dmg} 적용");
                }
            }
            UI_BossBark.ShowDialogue(TimeoutBarkKey, BossBarkType.PatternAnnounce);
            LichVfx.Stop(ref _dome, 0.5f);
            _groggyTimer = Data.groggyDuration;   // 시간 초과는 휘청 없이 바로 복귀
        }
        else
        {
            Debug.Log("[SealBreaker] 결계 해골 전부 처치 — 결계 붕괴, 리치 휘청");
            UI_BossBark.ShowDialogue(BrokenBarkKey, BossBarkType.PatternAnnounce);
            Vector3 center = _dome != null ? _dome.transform.position : ctx.Transform.position;
            LichVfx.Stop(ref _dome);
            LichVfx.Play(LichVfxSlot.WardBreak, center, Quaternion.identity, Data.domeRadius / 10f);
            LichSfx.Play(LichSfxSlot.WardBreak, center);
            LichPatternUtil.Impact(LichImpact.Heavy);
            LichCinematics.SlowMo(0.6f, 0.3f);
            ctx.Animator?.CrossFade("GetHit", 0.05f);
            PatternGuideHelper.Disc(LichPatternUtil.OnFloor(ctx, ctx.Transform.position), Data.sealRadius + 1f,
                                    LichPatternUtil.SafeWhite, lifetime: 0.8f);
            (ctx.Monster as LichMonster)?.NotifyVulnerableWindow(Data.groggyDuration);
        }
    }
}
}
