using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 2페이지 전환 — 숲의 수호자 · 화룡 · 죽음의 기사 공용(09-28 설계 확정 §2).
/// 1페이지 체력이 다 깎이면(<see cref="BossPages.TransitionDue"/>) 강제 발동:
///   휘청 → (첫 관람만) 로우 앵글 → 전경 — 두 번째 체력바가 차오르고 무대가 바뀐다(<see cref="IPagedBoss.OnPageStageChange"/>)
///   → 포효 · 대사 → 플레이어 카메라로 복귀 → 2페이지(<see cref="IPagedBoss.OnPage2Entered"/>).
/// 전환 내내 보스는 무적 · 제자리, 플레이어는 입력 잠금 + 잠깐 무적. 리치 전환 연출(LichPhase2EntryPatternSO)을 일반화했다.
/// 보스마다 다른 것(모션 이름 · 대사 · 이펙트 · 카메라 거리)은 이 SO의 값으로 준다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Common/BossPageTransitionPattern", fileName = "BossPageTransitionPattern")]
public class BossPageTransitionPatternSO : BossPatternSO
{
    [Header("모션 (애니메이터 상태 이름 — 없으면 건너뜀)")]
    public string staggerState = "GetHit";
    public string chargeState  = "Idle";
    public string roarState    = "";
    [Tooltip("포효 모션 속도 배율")]
    public float  roarSpeed    = 1f;

    [Header("타이밍 (실시간 초)")]
    public float staggerSeconds = 0.5f;
    public float lowShotSeconds = 0.8f;
    public float lowShotHold    = 0.5f;
    public float refillSeconds  = 2.4f;
    public float roarHoldSeconds = 1.4f;

    [Header("카메라 (보스 기준 m)")]
    [Tooltip("전경 샷 — 보스 뒤로 이 거리 · 이 높이에서 아레나를 내려다본다")]
    public float wideBack   = 16f;
    public float wideHeight = 12f;
    [Tooltip("포효 샷 — 보스 앞 이 거리")]
    public float roarDistance = 9f;
    public float roarHeight   = 2.6f;
    [Tooltip("보스 몸 높이(시선을 둘 곳)")]
    public float bodyHeight   = 1.6f;

    [Header("이펙트")]
    public GameObject roarVfxPrefab;
    public float      roarVfxScale = 1f;
    public Color      flashColor   = new Color(1f, 0.9f, 0.8f);
    public AudioClip  roarSfx;

    [Header("대사")]
    [Tooltip("대사 CSV 시퀀스 키 — 없으면 아래 한 줄")]
    public string dialogueKey = "";
    [TextArea] public string fallbackLine = "";

    private BossPageTransitionState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new BossPageTransitionState(this);
    public override void OnRecycled() => _state = new BossPageTransitionState(this);

    public override bool CanExecute(BossPatternContext ctx)
        => ctx?.Ctx?.Monster is IPagedBoss paged && paged.Pages != null && paged.Pages.TransitionDue(ctx.Ctx.Monster.CurrentHp);

    public override bool CanForceInterrupt(BossPatternContext ctx) => CanExecute(ctx);

    public override SpecialStateBase GetRuntimeState() => _state ??= new BossPageTransitionState(this);
}

/// <summary>전환 상태 — 중단 불가 · 이동 잠금 · 무적.</summary>
public sealed class BossPageTransitionState : SpecialStateBase
{
    private const float PlayerSafety = 1.5f;

    private static readonly HashSet<string> s_seen = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_seen.Clear();

    private readonly BossPageTransitionPatternSO _data;
    private CancellationTokenSource _cts;
    private bool _done;

    public BossPageTransitionState(BossPageTransitionPatternSO data) { _data = data; }

    public override SpecialStateConstraint Constraints =>
        SpecialStateConstraint.UnInterruptible | SpecialStateConstraint.MovementLocked | SpecialStateConstraint.Invincible;

    public override void Enter(MonsterContext ctx)
    {
        _done = false;
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.ResetPath();
        }
        (ctx.Monster as IPagedBoss)?.Pages?.BeginTransition();

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.Monster.destroyCancellationToken);
        CinematicAsync(ctx, _cts.Token).Forget();
        Debug.Log($"[BossPages] 2페이지 전환 시작 — {ctx.Monster.name}", ctx.Monster);
    }

    public override void Update(MonsterContext ctx)
    {
        if (!_done) return;
        _done = false;
        (ctx.Monster as IPagedBoss)?.ReturnToCombat();
    }

    public override void Exit(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.isStopped = false;
        // 전환이 끝나기 전에 끊겼으면(보스 비활성 등) 다음에 다시 시도할 수 있게.
        var pages = (ctx.Monster as IPagedBoss)?.Pages;
        if (pages != null && pages.Transitioning) pages.AbortTransition();
        _cts?.Cancel();
    }

    private float TotalSeconds(bool full)
        => _data.staggerSeconds + (full ? _data.lowShotSeconds + _data.lowShotHold : 0f)
           + _data.refillSeconds + _data.roarHoldSeconds + 0.8f;

    private async UniTaskVoid CinematicAsync(MonsterContext ctx, CancellationToken ct)
    {
        var  paged  = ctx.Monster as IPagedBoss;
        var  pc     = ctx.Runtime.CachedPlayer;
        var  player = ctx.Runtime.PlayerTarget;
        bool full   = s_seen.Add(_data.name);
        bool camera = false;

        pc?.SetInputEnabled(false);
        pc?.SetInvincible(TotalSeconds(full) + PlayerSafety);
        LichCinematics.BossCutsceneHud(true);

        try
        {
            // ① 휘청 — 한 줄이 다 깎였다.
            Vector3 body = ctx.Transform.position + Vector3.up * _data.bodyHeight;
            Play(ctx, _data.staggerState, 1f);
            LichCinematics.Flash(_data.flashColor, 0.25f, 0.2f);
            LichCinematics.SlowMo(0.25f, 0.45f);
            await UniTask.Delay(TimeSpan.FromSeconds(_data.staggerSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);

            LichCinematics.TakeCamera();
            camera = true;
            LichCinematics.LetterboxUnderHudAsync(ct).Forget();
            Play(ctx, _data.chargeState, 1f);

            Vector3 B   = ctx.Transform.position;
            Vector3 dir = player != null ? player.position - B : ctx.Transform.forward;
            dir.y = 0f;
            dir   = dir.sqrMagnitude > 0.01f ? dir.normalized : ctx.Transform.forward;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Vector3 look = B + Vector3.up * _data.bodyHeight;

            // ② 로우 앵글 — 첫 관람만.
            if (full)
            {
                var (lowPos, lowLook) = ClearShot(look, B + dir * 4.5f + side * 1.2f + Vector3.up * 0.6f, look);
                await LichCinematics.ShotAsync(lowPos, lowLook, _data.lowShotSeconds, ct);
                await UniTask.Delay(TimeSpan.FromSeconds(_data.lowShotHold), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            }

            // ③ 전경 — 두 번째 줄이 차오르고 무대가 바뀐다.
            var (widePos, wideLook) = ClearShot(look, B - dir * _data.wideBack + Vector3.up * _data.wideHeight, B + dir * 6f);
            LichCinematics.ShotAsync(widePos, wideLook, 1.2f, ct).Forget();
            paged?.Pages?.BeginRefill(_data.refillSeconds);
            paged?.OnPageStageChange(_data.refillSeconds);
            await UniTask.Delay(TimeSpan.FromSeconds(_data.refillSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);

            // ④ 포효 · 대사.
            Vector3 R = ctx.Transform.position;
            Vector3 roarLook = R + Vector3.up * _data.bodyHeight;
            var (roarPos, roarAt) = ClearShot(roarLook, R + dir * _data.roarDistance + side * 2f + Vector3.up * _data.roarHeight, roarLook);
            LichCinematics.ShotAsync(roarPos, roarAt, 0.6f, ct).Forget();
            Play(ctx, _data.roarState, _data.roarSpeed);
            if (_data.roarVfxPrefab != null)
            {
                var fx = UnityEngine.Object.Instantiate(_data.roarVfxPrefab, R, Quaternion.identity);
                fx.transform.localScale = Vector3.one * _data.roarVfxScale;
                UnityEngine.Object.Destroy(fx, 5f);
            }
            if (_data.roarSfx != null) Managers.Sound?.PlayEffectAt(_data.roarSfx, R);
            LichCinematics.Chroma(0.55f, 0.5f);
            LichCinematics.SlowMo(0.5f, 0.3f);
            if (string.IsNullOrEmpty(_data.dialogueKey) || !UI_BossBark.ShowDialogue(_data.dialogueKey, BossBarkType.PhaseAnnounce))
                if (!string.IsNullOrEmpty(_data.fallbackLine)) UI_BossBark.Show(_data.fallbackLine, BossBarkType.PhaseAnnounce);
            await UniTask.Delay(TimeSpan.FromSeconds(_data.roarHoldSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);

            // ⑤ 복귀 — 2페이지.
            paged?.Pages?.CompleteTransition();
            paged?.OnPage2Entered();
            LichCinematics.LetterboxOutAsync(ct).Forget();
            if (player != null) await LichCinematics.ReturnToPlayerAsync(player, ct);
            camera = false;
            Debug.Log($"[BossPages] 2페이지 진입 — {ctx.Monster.name} HP={ctx.Monster.CurrentHp}/{ctx.Monster.EffectiveMaxHp}", ctx.Monster);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (camera && player != null) LichCinematics.ReturnToPlayerAsync(player, CancellationToken.None).Forget();
            LichCinematics.LetterboxOutAsync(CancellationToken.None).Forget();
            LichCinematics.BossCutsceneHud(false);
            if (pc != null) pc.SetInputEnabled(true);
            _done = true;
        }
    }

    /// <summary>
    /// 카메라 자리 — 보스 몸(<paramref name="body"/>)에서 원하는 자리까지 벽이 가로막으면 보스 둘레로 45°씩 돌려 트인 자리를 고른다
    /// (시선도 같이 돈다). 어디나 막히면 원래 방향에서 막힌 곳 바로 앞으로 당긴다.
    /// 09-28 실측: 숲이 아레나 가장자리에서 전환하자 전경 카메라(보스 뒤 10 m)가 벽 너머 덤불 속에 들어가 화면 절반을 가렸다.
    /// </summary>
    private static (Vector3 pos, Vector3 look) ClearShot(Vector3 body, Vector3 desired, Vector3 look)
    {
        int mask = ~LayerMask.GetMask("Player", "Monster", "MonsterHit", "UI", "Ignore Raycast");
        Vector3 off = desired - body, lookOff = look - body;
        for (int i = 0; i < 8; i++)
        {
            float yaw = (i % 2 == 1 ? 1f : -1f) * 45f * ((i + 1) / 2);   // 0, +45, −45, +90, −90, …
            var   rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 cand = body + rot * off;
            if (!Physics.Linecast(body, cand, mask, QueryTriggerInteraction.Ignore)) return (cand, body + rot * lookOff);
        }
        return Physics.Linecast(body, desired, out var hit, mask, QueryTriggerInteraction.Ignore)
            ? (hit.point - off.normalized * 0.6f, look)
            : (desired, look);
    }

    private static void Play(MonsterContext ctx, string state, float speed)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(state)) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(state))) return;
        ctx.Animator.speed = speed;
        ctx.Animator.CrossFade(state, 0.1f, 0, 0f);
    }
}
}
