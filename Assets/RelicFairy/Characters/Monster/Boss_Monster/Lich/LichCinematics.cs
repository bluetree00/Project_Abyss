using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.UI.Overlay;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 연출 도구 — 슬로모 · 플래시 · 레터박스 · HUD 컷신 모드 · 수동 카메라 샷.
/// 전부 기존 공용 API를 한 줄로 부르는 얇은 층이다(연출·UX 시나리오 §3 B6).
///   슬로모 = <see cref="TimeScaleArbiter"/>(SlowMotion 우선순위), 크로마틱 = <see cref="VolumePulseService"/>,
///   플래시 = <see cref="FXLayer"/>, 레터박스 = <see cref="CinematicFrame"/>, 컷신 HUD = <see cref="GameRunSession"/>.
/// timeScale을 직접 건드리지 않는다(프로젝트 규칙).
/// </summary>
public static class LichCinematics
{
    private const float LetterboxSeconds = 0.35f;

    private static readonly object s_slowMoOwner = new object();
    private static int s_slowMoVersion;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_slowMoVersion = 0;

    // ── 시간 ────────────────────────────────────────────────

    /// <summary>실시간 <paramref name="seconds"/> 동안 <paramref name="scale"/>배 슬로모. 겹치면 마지막 요청이 끝을 정한다.</summary>
    public static void SlowMo(float scale, float seconds)
    {
        int version = ++s_slowMoVersion;
        TimeScaleArbiter.Acquire(s_slowMoOwner, Mathf.Clamp(scale, 0.05f, 1f), TimeScaleArbiter.Priority.SlowMotion);
        ReleaseSlowMoAsync(version, seconds).Forget();
    }

    private static async UniTaskVoid ReleaseSlowMoAsync(int version, float seconds)
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0.01f, seconds)), DelayType.Realtime);
        }
        finally
        {
            if (version == s_slowMoVersion) TimeScaleArbiter.Release(s_slowMoOwner);
        }
    }

    // ── 화면 ────────────────────────────────────────────────

    public static void Chroma(float peak, float seconds) => VolumePulseService.Pulse(peak, seconds);

    public static void Flash(Color color, float seconds, float peakAlpha = 0.6f)
        => Managers.UI?.GetOverlayUI<FXLayer>()?.Flash(color, seconds, peakAlpha);

    public static UniTask LetterboxInAsync(CancellationToken ct)
        => CinematicFrame.ShowAsync(LetterboxSeconds, null, ct, dimAlpha: 0f);

    /// <summary>레터박스를 HUD 아래에 깐다 — 보스 HP바가 검은 띠 위에 떠서 보인다(페이즈 전환 컷신).</summary>
    public static UniTask LetterboxUnderHudAsync(CancellationToken ct)
        => CinematicFrame.ShowAsync(LetterboxSeconds, null, ct, dimAlpha: 0f, sortingOrder: UISortingOrder.Hud - 1);

    public static UniTask LetterboxOutAsync(CancellationToken ct)
        => CinematicFrame.HideAsync(LetterboxSeconds, null, ct);

    /// <summary>컷신 동안 전투 HUD를 걷는다(시스템 알림만 남는다). 끝나면 <paramref name="on"/>=false.</summary>
    public static void CutsceneHud(bool on)
    {
        var run = GameRunBootstrapper.Instance?.Run;
        if (run == null) return;
        if (on) run.NotifyCutsceneStarted();
        else    run.NotifyCutsceneEnded();
    }

    /// <summary>페이즈 전환 컷신 동안 HUD — 보스 HP바만 남긴다(바가 차오르는 걸 보여 준다). 끝나면 보스 전투 HUD로.</summary>
    public static void BossCutsceneHud(bool on)
    {
        var run = GameRunBootstrapper.Instance?.Run;
        if (run == null) return;
        if (on)
            run.RequestHudMode(HUDIds.Mode.BossCutscene);
        else if (run.CurrentHudMode == HUDIds.Mode.BossCutscene)   // 그사이 런이 다른 모드로 바꿨으면 건드리지 않는다
            run.RequestHudMode(HUDIds.Mode.Boss);
    }

    /// <summary>
    /// 보스 끝 장면(봉인 · 처치) 동안 전투 HUD를 모두 걷는다. 끝나면 보스 전투 HUD로 — 그사이 런이 다른 모드로 바꿨으면 건드리지 않는다.
    /// <see cref="CutsceneHud"/>(false)는 런 상태를 지도로 바꾸므로(거점으로 돌아가는 리치 의식 전용) 보스방 안에서는 이쪽을 쓴다.
    /// </summary>
    public static void EndSceneHud(bool on)
    {
        var run = GameRunBootstrapper.Instance?.Run;
        if (run == null) return;
        if (on)
            run.RequestHudMode(HUDIds.Mode.Cutscene);
        else if (run.CurrentHudMode == HUDIds.Mode.Cutscene)
            run.RequestHudMode(HUDIds.Mode.Boss);
    }

    // ── 카메라 ──────────────────────────────────────────────

    /// <summary>카메라 수동 제어를 잡는다(Cinemachine 정지). 끝은 <see cref="ReturnToPlayerAsync"/>.</summary>
    public static void TakeCamera() => GameCameraController.Instance?.TakeManualControl();

    /// <summary>수동 카메라 샷 — <paramref name="position"/>에서 <paramref name="lookAt"/>을 본다.</summary>
    public static UniTask ShotAsync(Vector3 position, Vector3 lookAt, float seconds, CancellationToken ct)
    {
        var cam = GameCameraController.Instance;
        return cam != null ? cam.MoveManualCameraAsync(position, lookAt, seconds, ct) : UniTask.CompletedTask;
    }

    /// <summary>플레이어 추적 카메라로 부드럽게 돌려준다(수평각 유지).</summary>
    public static async UniTask ReturnToPlayerAsync(Transform player, CancellationToken ct)
    {
        var cam = GameCameraController.Instance;
        if (cam == null || player == null) return;
        await cam.HandToGameplayCameraAsync(player, alignHeadingToTarget: false, ct);
    }

    /// <summary>
    /// 짧은 클로즈업 — 대상 앞 <paramref name="offset"/>(대상 기준 월드 오프셋)에서 <paramref name="hold"/>초 본 뒤 플레이어로 돌아온다.
    /// 전투 중 전환 연출용(입력은 막지 않는다 — 전환 중 리치는 무적이고 판정도 없다).
    /// </summary>
    public static async UniTask CloseUpAsync(Transform target, Vector3 offset, Vector3 lookOffset,
                                             float moveSeconds, float hold, CancellationToken ct)
    {
        var cam    = GameCameraController.Instance;
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (cam == null || target == null || player == null) return;
        await cam.PanToZoneAndReturnAsync(target.position, moveSeconds, hold, 0.8f, player.transform, ct,
                                          offset, customLookOffset: lookOffset);
    }
}
}
