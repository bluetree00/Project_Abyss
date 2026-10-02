using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;
using UnityEngine;
using Random = UnityEngine.Random;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 페이지 전환 패턴 — 강제 발동 (forceExecute=true). 에셋 하나가 전환 하나다.
///   · T1 「사슬의 각성」 (봉인기 1→2) — 낫이 나타나지만 사슬에 묶인다. 버프·붕괴·폭발 없음
///   · T2 「봉인은 없다」 (악몽기 1→2) — 외곽 링 붕괴 + 원형 폭발 + 속도 버프
///   · T3 「최후의 원」   (악몽기 2→3)
///
/// 임계 HP에 닿으면(LichMonster가 그 아래로 못 내려가게 붙잡는다) BossPatternRunner가 강제 인터럽트.
/// 흐름: 무적 · 전환 연출(entryDuration) → (폭발) → 페이지 진입(버프·폼) → 복귀 → ChaseState
///
/// 페이즈 전환 컷신(<see cref="cinematic"/>, 09-19 사용자 지시 — 「한 줄을 다 깎으면 체력이 차오르며 2페이즈」):
///   HP바 한 줄이 비는 순간 → ① 휘청(섬광·슬로모) → 레터박스(HUD 아래 — 보스 바는 보인다) · 입력 잠금 · 플레이어 무적
///   → 샷 ① 로우 앵글(첫 관람만) → 샷 ② 전경: 사슬이 감기고 <b>다음 페이즈 바가 차오른다</b>
///   → 샷 ③ 정면: 폼 드러남(낫) · 포효 충격파 · 페이즈 자막 → 카메라 복귀 → 페이지 진입.
/// 같은 전환을 두 번째 보면 샷 ①을 건너뛴 짧은 판.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich_Phase2EntryPattern", fileName = "Lich_Phase2EntryPattern")]
public class LichPhase2EntryPatternSO : BossPatternSO
{
    [Header("Page Transition")]
    [Tooltip("전환이 끝나면 들어갈 페이지 (2 = 낫, 3 = 최후의 원)")]
    public int targetPage = 2;
    [Tooltip("페이지 진입 시 폼")]
    public LichForm form = LichForm.Phase2;
    [Tooltip("전환 대사 — DIALOGUE_DATA 시퀀스 키. 첫 줄은 페이즈 자막, 나머지는 바크. 비우면 대사 없음")]
    public string dialogueKey = "Lich_Page2_Nightmare";
    [Tooltip("페이지 진입 후 이동 속도 배율 (1 = 기본)")]
    public float speedMultiplier = 1.2f;
    [Tooltip("페이지 진입 후 공격 속도 배율 (1 = 기본)")]
    public float attackSpeedMultiplier = 1.2f;
    [Tooltip("페이지 진입 후 패턴 사이 휴식 (초). 음수면 설정값(BossConfig) 그대로")]
    public float breakDurationMin = 0.3f;
    public float breakDurationMax = 0.8f;

    [Header("Phase2 Entry — Timing")]
    [Tooltip("해방 연출 총 시간 (초)")]
    public float entryDuration = 2.0f;
    [Tooltip("복귀 대기 시간 (초)")]
    public float recoveryDuration = 0.5f;
    [Tooltip("entryDuration 중 AoE 폭발이 발동하는 비율 (0~1)")]
    [Range(0f, 1f)]
    public float blastTiming = 0.6f;

    [Header("Phase2 Entry — AoE")]
    [Tooltip("T1 사슬의 각성 — 금빛 사슬이 리치를 감고, 다음 페이지 내내 제단 바깥에 묶어 둔다")]
    public bool  bindChains  = false;
    [Tooltip("사슬이 감기는 시점 (entryDuration 대비 비율)")]
    [Range(0f, 1f)]
    public float chainTiming = 0.45f;

    [Tooltip("원형 폭발을 쓰는가 (T1 사슬의 각성은 쓰지 않는다)")]
    public bool blast = true;
    [Tooltip("폭발 판정 반경 (m)")]
    public float blastRadius = 10f;
    [Tooltip("기본 attackPower에 곱할 배율")]
    public float damageMultiplier = 2.0f;
    [Tooltip("넉백 힘 배율")]
    public float knockbackMultiplier = 3.0f;

    [Header("Arena")]
    [Tooltip("붕괴형 아레나의 최외곽 링을 무너뜨린다 (T2). 봉인기는 균열만 — 실제 붕괴는 붕괴 컷신에서 처음 본다")]
    public bool collapseOuterRing = true;
    [Tooltip("붕괴 대신 바깥 링에 균열이 번진다 (T1)")]
    public bool crackOuterRing = false;
    [Tooltip("전환이 시작될 때 1페이지에 부서진 바닥을 모두 되살린다 (T1 — 다음 페이지를 온전한 제단에서)")]
    public bool restoreFloor = false;

    [Header("페이즈 전환 컷신 (09-19 — 끄면 옛 흐름)")]
    [Tooltip("켜면 entryDuration 타임라인 대신 컷신(샷 3개 · HP바 차오름)으로 전환한다")]
    public bool  cinematic         = false;
    [Tooltip("전환 컷신에서 무대를 바꾼다 — 네 귀퉁이 붕괴 + 거대 석상 상승 + 핏빛 조명(09-19). 컷신일 때만")]
    public bool  stageShift        = false;
    [Tooltip("T3 「최후의 원」 — 코어 밖 모든 링이 바깥부터 차례로 영구히 무너진다(컷신일 때만). 봉인 조각도 흩어진다")]
    public bool  finalCircle       = false;
    [Tooltip("다음 페이즈 HP바가 차오르는 시간(초)")]
    public float refillSeconds     = 2.4f;
    [Tooltip("포효 뒤 자막을 보는 시간(초)")]
    public float roarHoldSeconds   = 1.1f;

    [Header("연출 (설계서 §3-3 · §4-3 타임라인)")]
    [Tooltip("클로즈업 시점 (entryDuration 대비 비율)")]
    [Range(0f, 1f)] public float closeUpTiming = 0.08f;
    [Tooltip("클로즈업 유지 (초)")]
    public float closeUpHold = 0.9f;
    [Tooltip("슬로모 시점 (entryDuration 대비 비율)")]
    [Range(0f, 1f)] public float slowMoTiming = 0.5f;
    public float slowMoScale   = 0.5f;
    public float slowMoSeconds = 0.6f;

    // ── 런타임 ───────────────────────────────────────────
    private LichPhase2EntryState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new LichPhase2EntryState(this);
    public override void OnRecycled()                       => _state = new LichPhase2EntryState(this);

    // forceExecute 전용 — 항상 실행·인터럽트 가능
    public override bool CanExecute(BossPatternContext ctx)       => true;
    public override bool CanForceInterrupt(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// LichPhase2EntryState — FullLock + 무적 (전환 연출 중엔 피해를 받지 않는다)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public class LichPhase2EntryState : FullLockState<LichPhase2EntryPatternSO>
{
    private enum Phase { Entry, Recovery }

    private Phase      _phase;
    private float      _timer;
    private bool       _hasBlasted;
    private bool       _pageEntered;
    private bool       _chained;
    private float      _blastThreshold;
    private bool       _closedUp;
    private bool       _slowed;
    private bool       _cracked;
    private bool       _signaled;
    private GameObject _aoeGuide;
    private bool       _cinematicDone;
    private CancellationTokenSource _cinematicCts;

    // 샷 배치(리치 기준, 플레이어 쪽 방향 dir · 옆 side)
    private const float StaggerSeconds  = 0.4f;
    private const float LowShotSeconds  = 0.8f;
    private const float LowShotHold     = 0.5f;
    private const float WideShotSeconds = 0.9f;
    private const float RoarShotSeconds = 0.5f;
    private const float RoarSwingContact = 0.22f;   // 포효 휘두름 — 접촉(멈칫)까지
    private const float PlayerSafety    = 1.5f;   // 컷신이 끝난 뒤에도 잠깐 무적
    private const string BoundHintKey  = "Lich_Bound_Hint";   // 봉인기 T1 뒤 멀린 — 이 페이지에서 할 일(10-03). CSV에 없으면 아래 줄
    private const string BoundHintLine = "사슬이 버티는 동안 쓰러뜨려! 그러면 봉인석이 깨어날 거야.";

    private static readonly HashSet<string> s_seen = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_seen.Clear();

    public LichPhase2EntryState(LichPhase2EntryPatternSO data) : base(data) { }

    public override SpecialStateConstraint Constraints =>
        base.Constraints | SpecialStateConstraint.Invincible;

    public override void Enter(MonsterContext ctx)
    {
        _phase          = Phase.Entry;
        _timer          = 0f;
        _hasBlasted     = !Data.blast;
        _pageEntered    = false;
        _chained        = !Data.bindChains;
        _closedUp       = false;
        _slowed         = false;
        _cracked        = !Data.crackOuterRing;
        _signaled       = false;
        _blastThreshold = Data.entryDuration * Data.blastTiming;
        _cinematicDone  = false;

        if (Data.cinematic)
        {
            EnterCinematic(ctx);
            return;
        }

        ctx.Animator?.CrossFade("Phase2Entry", 0.1f);

        var mc = (ctx.Monster as LichMonster)?.MovementController;
        mc?.RequestMovementState(LichMovementState.IdleHover);
        mc?.SetLocked(true);

        // 전투 중 소환된 해골은 전환과 함께 걷어낸다(설계: 모든 전환 +0.0).
        LichSkeletonMonster.DespawnAll();
        if (Data.restoreFloor) LichPatternUtil.RestoreFloor(ctx.Transform.position);

        UI_BossBark.ShowDialogue(Data.dialogueKey, BossBarkType.PhaseAnnounce);

        // 폭발 범위 disc — 붉게 차오르다 폭발 순간 번쩍(전환 폭발은 낫의 해방 = 빨강 계열)
        if (Data.blast)
            _aoeGuide = LichPatternUtil.PrepareTelegraph(
                PatternGuideHelper.Disc(LichPatternUtil.OnFloor(ctx, ctx.Transform.position), Data.blastRadius, LichPatternUtil.Crimson),
                LichPatternUtil.Crimson);

        Vector3 body = ctx.Transform.position + Vector3.up;
        LichVfx.Play(LichVfxSlot.PageBreak, body, Quaternion.identity);
        LichVfx.PlayScreen(LichVfxSlot.ScreenDebuff, Data.entryDuration + 0.5f);
        LichSfx.Play(LichSfxSlot.PageTransition, body);
        LichSfx.Play(LichSfxSlot.CastCharge, body);
        (ctx.Monster as LichMonster)?.PulseBook(Data.entryDuration);

        // 붕괴형 아레나면 최외곽 링을 무너뜨린다 — 해방 연출이 끝나는 순간 떨어지도록 흔들림을 entryDuration만큼 준다.
        // 리치가 그 아레나 위에 있을 때만(테스트 씬에 아레나가 여럿 놓여 있어도 엉뚱한 곳이 무너지지 않게).
        if (Data.collapseOuterRing)
        {
            var grid = ArenaTileGrid.Active;
            if (grid != null && grid.TryGetCell(ctx.Transform.position, out _))
                grid.CollapseRing(grid.OuterRing, Data.entryDuration);
        }

        Debug.Log($"[Lich] 페이지 전환 시작 → {Data.targetPage}페이지 ({Data.name})", ctx.Monster);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        if (Data.cinematic && _phase == Phase.Entry)
        {
            if (!_cinematicDone) return;
            if (!_pageEntered)
            {
                _pageEntered = true;
                (ctx.Monster as LichMonster)?.EnterPage(Data);
            }
            _phase = Phase.Recovery;
            _timer = 0f;
            return;
        }

        if (_phase == Phase.Entry)
        {
            if (_aoeGuide != null && !_hasBlasted)
                LichPatternUtil.TickTelegraph(_aoeGuide, _timer, _blastThreshold, 0.2f, ref _signaled);

            if (!_closedUp && _timer >= Data.entryDuration * Data.closeUpTiming)
            {
                _closedUp = true;
                var lich = ctx.Monster as LichMonster;
                if (lich != null)
                    LichCinematics.CloseUpAsync(ctx.Transform, CloseUpOffset(ctx), new Vector3(0f, 1.8f, 0f),
                                                0.45f, Data.closeUpHold, lich.ActivationToken).Forget();
            }

            if (!_slowed && _timer >= Data.entryDuration * Data.slowMoTiming)
            {
                _slowed = true;
                LichCinematics.SlowMo(Data.slowMoScale, Data.slowMoSeconds);
                LichCinematics.Chroma(0.55f, 0.5f);
                LichPatternUtil.Impact(LichImpact.Transition);
            }

            if (!_cracked && _timer >= Data.entryDuration * 0.75f)
            {
                _cracked = true;
                CrackOuterRing(ctx);
            }

            if (!_chained && _timer >= Data.entryDuration * Data.chainTiming)
            {
                _chained = true;
                BindChains(ctx);
            }

            if (!_hasBlasted && _timer >= _blastThreshold)
            {
                _hasBlasted = true;
                PatternGuideHelper.SetColor(_aoeGuide, LichPatternUtil.Lethal);
                LichVfx.Play(LichVfxSlot.SlamImpact, LichPatternUtil.OnFloor(ctx, ctx.Transform.position), Quaternion.identity, Data.blastRadius / 4f);
                LichSfx.Play(LichSfxSlot.SlamImpact, ctx.Transform.position);
                BlastAoE(ctx);
            }

            if (_timer >= Data.entryDuration)
            {
                if (!_pageEntered)
                {
                    _pageEntered = true;
                    (ctx.Monster as LichMonster)?.EnterPage(Data);
                }
                _phase = Phase.Recovery;
                _timer = 0f;
            }
        }
        else
        {
            if (_timer >= Data.recoveryDuration)
                ctx.Monster.ChangeState<ChaseState>();
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        PatternGuideHelper.SafeDestroy(ref _aoeGuide);
        (ctx.Monster as LichMonster)?.MovementController?.SetLocked(false);
        // 컷신 도중 끊기면(리치 비활성 등) 마무리는 컷신의 finally가 한다.
        _cinematicCts?.Cancel();
        _cinematicCts?.Dispose();
        _cinematicCts = null;
    }

    // ── 페이즈 전환 컷신 ─────────────────────────────────────

    private void EnterCinematic(MonsterContext ctx)
    {
        var lich = ctx.Monster as LichMonster;
        var mc   = lich?.MovementController;
        mc?.RequestMovementState(LichMovementState.IdleHover);
        mc?.SetLocked(true);

        // 무대를 비운다 — 해골 · 장판 · 추적 구체 · 결박. 1페이지 바닥은 되살린다.
        LichSkeletonMonster.DespawnAll();
        LichHazards.Clear();
        if (Data.restoreFloor) LichPatternUtil.RestoreFloor(ctx.Transform.position);

        if (Data.collapseOuterRing)
        {
            var grid = ArenaTileGrid.Active;
            if (grid != null && grid.TryGetCell(ctx.Transform.position, out _))
            {
                // 입력이 잠긴 컷신 중에 바깥 링이 무너진다 — 그 위 플레이어는 피할 수 없어 무적을 무시하는 낙사를 맞았다(10-01 감사).
                // T3 「최후의 원」처럼 안쪽 링으로 옮겨 둔다(컷신 카메라가 리치를 보는 동안).
                if (grid.TryGetWorldCenter(out Vector3 gridCenter))
                    MoveInside(ctx.Runtime.PlayerTarget, gridCenter, grid.RingCenterDistance(grid.OuterRing) - grid.CellSize * 0.5f);
                grid.CollapseRing(grid.OuterRing, CinematicSeconds(!s_seen.Contains(Data.name)) - Data.roarHoldSeconds);
            }
        }

        _cinematicCts = lich != null
            ? CancellationTokenSource.CreateLinkedTokenSource(lich.ActivationToken)
            : new CancellationTokenSource();
        CinematicAsync(ctx, _cinematicCts.Token).Forget();
        Debug.Log($"[Lich] 페이즈 전환 컷신 시작 → {Data.targetPage}페이즈 ({Data.name})", ctx.Monster);
    }

    /// <summary>
    /// 플레이어가 중심에서 <paramref name="innerEdge"/> − 1.5 m 밖이면 <paramref name="innerEdge"/> − 2.5 m 지점의 발판으로 옮긴다.
    /// 같은 방위부터 좌우 20°씩 — 1페이지에 부서져 아직 안 돌아온 칸(구멍)은 건너뛴다(T2는 바닥을 복구하지 않는다).
    /// </summary>
    private static void MoveInside(Transform player, Vector3 center, float innerEdge)
    {
        if (player == null) return;
        Vector3 flat = player.position - center;
        flat.y = 0f;
        if (flat.magnitude <= innerEdge - 1.5f) return;

        Vector3 baseDir = flat.sqrMagnitude > 0.01f ? flat.normalized : Vector3.back;
        Vector3 dest    = center + baseDir * (innerEdge - 2.5f) + Vector3.up * 0.3f;
        for (int k = 0; k < 18; k++)
        {
            float   yaw = (k % 2 == 0 ? 1f : -1f) * ((k + 1) / 2) * 20f;
            Vector3 at  = center + Quaternion.Euler(0f, yaw, 0f) * baseDir * (innerEdge - 2.5f);
            if (!Physics.Raycast(at + Vector3.up * 3f, Vector3.down, out var floor, 6f, ~0, QueryTriggerInteraction.Ignore)) continue;
            dest = floor.point + Vector3.up * 0.1f;
            break;
        }
        player.position = dest;
        if (player.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.position       = dest;
            rb.linearVelocity = Vector3.zero;
        }
        Debug.Log($"[Lich] 바깥 링 붕괴 전 플레이어를 안쪽으로 — 중심에서 {flat.magnitude:0.0} m → {innerEdge - 2.5f:0.0} m");
    }

    /// <summary>
    /// T3 「최후의 원」 — 코어(영구 코어 링) 밖 모든 링이 바깥부터 0.4초 간격으로 붉게 흔들리다 떨어진다(상한 예외).
    /// 코어엔 흰 원(안전). 플레이어가 코어 밖이면 코어 가장자리 안으로 옮긴다(컷신 중 입력이 잠겨 있다).
    /// </summary>
    private static async UniTaskVoid FinalCircleAsync(ArenaTileGrid grid, Vector3 center, Transform player, CancellationToken ct)
    {
        const float RingInterval = 0.4f;
        const float RingWarn     = 1.2f;
        const float CoreRadius   = 10f;   // 링 0~1 = 4×4칸(5 m) = 반경 약 10 m

        LichSealShard.ClearAll();
        var safe  = PatternGuideHelper.Disc(center + Vector3.up * 0.03f, CoreRadius, LichPatternUtil.SafeWhite, RingWarn + RingInterval * 5f + 1f);
        var light = CreateCircleLight(center);   // 22 m 위 샷이 어두워 무너지는 링이 안 보였다(09-19 실측) — 마지막 땅에 창백한 빛

        if (player != null)
        {
            Vector3 flat = player.position - center;
            flat.y = 0f;
            if (flat.magnitude > CoreRadius - 1.5f)
            {
                Vector3 dest = center + (flat.sqrMagnitude > 0.01f ? flat.normalized : Vector3.back) * (CoreRadius - 2.5f) + Vector3.up * 0.3f;
                player.position = dest;
                if (player.TryGetComponent<Rigidbody>(out var rb))
                {
                    rb.position       = dest;
                    rb.linearVelocity = Vector3.zero;
                }
            }
        }

        try
        {
            for (int ring = grid.OuterRing; ring >= 2; ring--)
            {
                grid.CollapseRing(ring, RingWarn, ignoreCap: true);
                LichSfx.Play(LichSfxSlot.Collapse, center);
                await UniTask.Delay(TimeSpan.FromSeconds(RingInterval), cancellationToken: ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (safe != null) PatternGuideHelper.SafeDestroy(ref safe);
            if (light != null) FadeOutLightAsync(light, RingWarn + 0.6f, 1.0f).Forget();
        }
    }

    /// <summary>T3 — 코어 위 창백한 빛(무너지는 링이 높은 샷에서 보이게).</summary>
    private static Light CreateCircleLight(Vector3 center)
    {
        var go    = new GameObject("~FinalCircleLight");
        go.transform.position = center + Vector3.up * 14f;
        var light = go.AddComponent<Light>();
        light.type      = LightType.Point;
        light.range     = 38f;
        light.intensity = 14f;
        light.color     = new Color(0.82f, 0.84f, 1f);
        light.shadows   = LightShadows.None;
        return light;
    }

    /// <summary>마지막 링이 떨어진 뒤 빛을 거둔다(게임 시간 — 컷신 슬로모를 따른다).</summary>
    private static async UniTaskVoid FadeOutLightAsync(Light light, float delay, float fade)
    {
        var ct = light.GetCancellationTokenOnDestroy();
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(delay), cancellationToken: ct);
            float from = light.intensity;
            for (float t = 0f; t < fade; t += Time.deltaTime)
            {
                light.intensity = Mathf.Lerp(from, 0f, t / fade);
                await UniTask.Yield(ct);
            }
        }
        catch (OperationCanceledException) { return; }
        if (light != null) UnityEngine.Object.Destroy(light.gameObject);
    }

    /// <summary>컷신 길이(초) — 첫 관람은 로우 앵글 샷을 포함한다.</summary>
    private float CinematicSeconds(bool full)
        => StaggerSeconds + (full ? LowShotSeconds + LowShotHold : 0f) + Data.refillSeconds + Data.roarHoldSeconds + 0.8f;

    private async UniTaskVoid CinematicAsync(MonsterContext ctx, CancellationToken ct)
    {
        var  lich   = ctx.Monster as LichMonster;
        var  pc     = ctx.Runtime.CachedPlayer;
        var  player = ctx.Runtime.PlayerTarget;
        bool full   = s_seen.Add(Data.name);
        bool camera = false;

        pc?.SetInputEnabled(false);
        pc?.SetInvincible(CinematicSeconds(full) + PlayerSafety);
        LichCinematics.BossCutsceneHud(true);

        try
        {
            // ① 휘청 — 한 줄이 다 깎였다.
            Vector3 body = ctx.Transform.position + Vector3.up;
            ctx.Animator?.CrossFade("GetHit", 0.05f);
            LichCinematics.Flash(Color.white, 0.25f, 0.2f);    // 한 줄이 깨지는 휘청 — 한 전투에 2~3번이라 약하게(0.45는 화면이 지워졌다, 09-20 실측)
            LichCinematics.SlowMo(0.25f, 0.45f);
            LichPatternUtil.Impact(LichImpact.Transition);
            LichVfx.Play(LichVfxSlot.PageBreak, body, Quaternion.identity, 0.6f);   // 한 줄이 깨진다
            LichSfx.Play(LichSfxSlot.PageTransition, body);
            await UniTask.Delay(TimeSpan.FromSeconds(StaggerSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);

            LichCinematics.TakeCamera();
            camera = true;
            LichCinematics.LetterboxUnderHudAsync(ct).Forget();
            LichVfx.PlayScreen(LichVfxSlot.ScreenDebuff, CinematicSeconds(full));
            // 팔을 치켜든 자세로 힘을 모은 채 버틴다(로우 앵글 · 전경 · 바 채움 내내) — 포효에서 낫 휘두름으로 끊는다.
            if (lich != null) lich.PlayCastBeat(LichCast.Phase2Entry, CinematicSeconds(full) + 10f);
            else              ctx.Animator?.CrossFade("Phase2Entry", 0.1f);

            Vector3 L     = ctx.Transform.position;
            float   floor = LichPatternUtil.FloorY(ctx);
            Vector3 dir   = player != null ? player.position - L : ctx.Transform.forward;
            dir.y = 0f;
            dir   = dir.sqrMagnitude > 0.01f ? dir.normalized : ctx.Transform.forward;
            Vector3 side  = Vector3.Cross(Vector3.up, dir);
            Vector3 look  = L + Vector3.up * 1.6f;
            Vector3 floorL = new Vector3(L.x, floor, L.z);

            // ② 로우 앵글 — 첫 관람만.
            if (full)
            {
                await LichCinematics.ShotAsync(floorL + dir * 4.5f + side * 1.2f + Vector3.up * 0.6f, look, LowShotSeconds, ct);
                await UniTask.Delay(TimeSpan.FromSeconds(LowShotHold), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            }

            // ③ 전경 — 사슬이 감기고 다음 페이즈 바가 차오른다.
            //    무대 변화가 있으면 제단 전체를 높이서 — 귀퉁이가 무너지고 석상이 솟는 게 한 화면에 담기게.
            var grid = ArenaTileGrid.Active;
            if (Data.finalCircle && grid != null && grid.TryGetWorldCenter(out var circleCenter))
            {
                // 높이서 제단 전체 — 바깥 링부터 무너져 코어만 남는 걸 한 화면에.
                LichCinematics.ShotAsync(circleCenter - dir * 16f + Vector3.up * 22f, circleCenter, WideShotSeconds, ct).Forget();
                FinalCircleAsync(grid, circleCenter, player, ct).Forget();
            }
            else if (Data.stageShift && grid != null && grid.TryGetWorldCenter(out var arena))
            {
                // 리치 등 뒤 높이서 플레이어 쪽 제단을 내려다본다 — 앞쪽 두 귀퉁이가 무너지고 석상이 솟는 게 보이게(09-19: 30 m 위는 어두워 안 보였다).
                LichCinematics.ShotAsync(arena - dir * 18f + Vector3.up * 14f, arena + dir * 8f, WideShotSeconds, ct).Forget();
                LichStageShift.Begin(ctx.Transform, player);
            }
            else
            {
                LichCinematics.ShotAsync(floorL + dir * 11f + side * 4f + Vector3.up * 6f, L + Vector3.up * 1.2f, WideShotSeconds, ct).Forget();
            }
            if (Data.bindChains) BindChains(ctx);
            else LichVfx.Play(LichVfxSlot.PhaseBurst, body, Quaternion.identity);
            lich?.BeginPageRefill(Data.targetPage, Data.refillSeconds);
            lich?.PulseBook(Data.refillSeconds);
            LichSfx.Play(LichSfxSlot.CastCharge, body);
            if (Data.crackOuterRing)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(Data.refillSeconds * 0.35f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
                CrackOuterRing(ctx);
                await UniTask.Delay(TimeSpan.FromSeconds(Data.refillSeconds * 0.65f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            }
            else
            {
                await UniTask.Delay(TimeSpan.FromSeconds(Data.refillSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            }

            // ④ 정면 — 폼이 드러나고 포효. 페이즈 자막.
            // 컷신 사이 리치가 떠밀려 있을 수 있으니 지금 위치로 다시 잡는다(9 m — 낫 휘두름이 화면을 덮지 않게).
            Vector3 R      = ctx.Transform.position;
            Vector3 floorR = new Vector3(R.x, floor, R.z);
            float   drift  = Vector3.Distance(new Vector3(L.x, 0f, L.z), new Vector3(R.x, 0f, R.z));
            if (drift > 0.5f) Debug.Log($"[Lich] 전환 컷신 — 리치가 {drift:0.0} m 밀려 있다", ctx.Monster);
            LichCinematics.ShotAsync(floorR + dir * 9f + side * 2f + Vector3.up * 2.6f, R + Vector3.up * 1.6f, RoarShotSeconds, ct).Forget();
            lich?.DissolveToForm(Data.form);
            // 포효 = 모은 힘을 낫으로 한 번에 — 빠르게 휘둘러 폭발 순간 멈칫(접촉 0.22초 뒤).
            if (lich != null) lich.PlaySwing(LichSwing.LeftToRight, RoarSwingContact, 0.18f);
            else              ctx.Animator?.CrossFade("ScytheSweep", 0.1f);
            LichVfx.Play(LichVfxSlot.PageBreak, body, Quaternion.identity, 1.3f);   // 포효 — 모은 힘이 터진다
            LichVfx.Play(LichVfxSlot.SlamImpact, floorR, Quaternion.identity, Data.blastRadius / 4f);
            LichSfx.Play(LichSfxSlot.SlamImpact, R);
            LichCinematics.Chroma(0.6f, 0.5f);
            LichCinematics.SlowMo(0.5f, 0.3f);
            LichPatternUtil.Impact(LichImpact.Transition);
            if (Data.stageShift) lich?.ShiftToPage2Lighting(1.4f);
            if (Data.blast) BlastAoE(ctx);
            UI_BossBark.ShowDialogue(Data.dialogueKey, BossBarkType.PhaseAnnounce);
            await UniTask.Delay(TimeSpan.FromSeconds(Data.roarHoldSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);

            // ⑤ 복귀
            LichCinematics.LetterboxOutAsync(ct).Forget();
            if (player != null) await LichCinematics.ReturnToPlayerAsync(player, ct);
            camera = false;

            // ⑥ 봉인기 T1(사슬) — 이 페이지에서 무엇을 해야 하는지 멀린이 짚는다(10-03 사용자: 2페이지 봉인 행동 안내가 없었다).
            if (Data.bindChains && !UI_BossBark.ShowDialogue(BoundHintKey))
                UI_BossBark.Show(BoundHintLine, BossBarkType.MerlinNarration, DialogueSpeaker.Merlin);
        }
        catch (OperationCanceledException)
        {
            // 리치가 꺼짐 — 아래에서 원상 복구
        }
        finally
        {
            if (camera && player != null) LichCinematics.ReturnToPlayerAsync(player, CancellationToken.None).Forget();
            LichCinematics.LetterboxOutAsync(CancellationToken.None).Forget();
            LichCinematics.BossCutsceneHud(false);
            pc?.SetInputEnabled(true);
            _cinematicDone = true;
        }
    }

    /// <summary>금빛 사슬이 감기는 순간 + 제단 바깥에 묶는 세 줄(봉인 의식·전투 종료까지).</summary>
    private static void BindChains(MonsterContext ctx)
    {
        var mc = LichPatternUtil.Mover(ctx);
        LichVfx.Play(LichVfxSlot.SealBurst, ctx.Transform.position + Vector3.up, Quaternion.identity);
        LichSfx.Play(LichSfxSlot.ChainPulse, ctx.Transform.position);
        LichHazards.BoundChains(ctx.Transform,
                                mc != null ? mc.ArenaCenter : ctx.Transform.position,
                                mc != null ? mc.ArenaRadius : 12f);
        LichCinematics.Chroma(0.45f, 0.4f);
        LichPatternUtil.Impact(LichImpact.Heavy);
    }

    /// <summary>클로즈업 위치 — 리치 앞(플레이어 쪽) 사선 아래에서 올려다본다.</summary>
    private static Vector3 CloseUpOffset(MonsterContext ctx)
    {
        Vector3 toPlayer = ctx.Runtime.PlayerTarget != null
            ? ctx.Runtime.PlayerTarget.position - ctx.Transform.position
            : ctx.Transform.forward;
        toPlayer.y = 0f;
        toPlayer   = toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : Vector3.back;
        Vector3 side = Vector3.Cross(Vector3.up, toPlayer) * 2.5f;
        return toPlayer * 7f + side + Vector3.up * 1.2f;
    }

    /// <summary>T1 — 바깥 링에 균열이 번진다(붕괴 없음). 붕괴형 아레나가 아니면 리치 둘레에 원형으로.</summary>
    private static void CrackOuterRing(MonsterContext ctx)
    {
        var mc     = LichPatternUtil.Mover(ctx);
        Vector3 c  = LichPatternUtil.OnFloor(ctx, mc != null ? mc.ArenaCenter : ctx.Transform.position);
        float   r  = mc != null ? mc.ArenaRadius * 0.95f : 12f;
        const int Count = 18;
        for (int i = 0; i < Count; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 360f / Count + Random.Range(-6f, 6f), 0f) * Vector3.forward;
            LichCrack.Spawn(c + dir * r * Random.Range(0.9f, 1.02f), Random.Range(3f, 4.5f), 30f);
        }
        LichSfx.Play(LichSfxSlot.Collapse, c, 0.7f);
        LichPatternUtil.Impact(LichImpact.Heavy);
    }

    private void BlastAoE(MonsterContext ctx)
    {
        if (ctx.Config?.stat == null || ctx.Runtime.PlayerTarget == null) return;

        float dist = Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position);
        if (dist > Data.blastRadius) return;

        var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
        if (player == null) return;

        // 배율 0 = 밀어내기만(피해 없음). 예전엔 최소 1이 들어가 무피격 판정·포이즈가 깨졌다(09-18 감사).
        if (Data.damageMultiplier > 0f)
            player.TakeDamage(Mathf.Max(1, (int)(ctx.Config.stat.attackPower * Data.damageMultiplier)), ctx.Monster.gameObject,
                              false, HitWeight.Heavy);   // 전환 폭발

        Vector3 dir = (ctx.Runtime.PlayerTarget.position - ctx.Transform.position).normalized;
        dir.y = 0.5f;
        if (dir.sqrMagnitude > 0.001f) dir.Normalize();
        player.ApplyKnockback(dir * ctx.Config.stat.knockbackForce * Data.knockbackMultiplier);
    }
}
}
