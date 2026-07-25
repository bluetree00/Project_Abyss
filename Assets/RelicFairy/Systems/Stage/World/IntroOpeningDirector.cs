using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 인트로 오프닝 시네마틱 — 세계관 프롤로그(암전 위 내레이션) → 성당 카메라 연출 → 플레이어 조작 인계.
///
/// 조우 컷신(IntroMordredDirector)과는 트리거·책임이 달라 분리한다(오프닝=씬 시작, 조우=구간 진입).
/// 모든 샷은 정지 포즈가 아니라 <b>from→to로 계속 움직인다</b>(푸시인·궤도) — 시네마틱의 핵심.
/// 앵커만 씬에서 연결하면 샷 구도는 앵커 기준 오프셋으로 자동 계산된다.
/// </summary>
public sealed class IntroOpeningDirector : MonoBehaviour
{
    // ── [SerializeField] ─────────────────────────────────────────────
    [Header("프롤로그 (DIALOGUE_DATA.csv)")]
    [SerializeField] private string prologueSequenceId = "Intro_Prologue";

    [Header("연출 앵커")]
    [Tooltip("제단/천사상 — 스테인드글라스를 올려다보는 기준점.")]
    [SerializeField] private Transform altarAnchor;
    [Tooltip("무형검 — 클로즈업 대상.")]
    [SerializeField] private Transform swordAnchor;
    [Tooltip("모르드레드 — 실루엣 샷 대상.")]
    [SerializeField] private Transform mordredAnchor;

    [Header("타이밍")]
    [SerializeField, Min(0f)]   private float bootDelay    = 0.4f;
    [SerializeField, Min(0.1f)] private float fadeDuration = 1.2f;
    [SerializeField, Min(0.1f)] private float shotGlass    = 6.0f;
    [SerializeField, Min(0.1f)] private float shotAltar    = 5.0f;
    [SerializeField, Min(0.1f)] private float shotSword    = 5.0f;
    [SerializeField, Min(0.1f)] private float shotMordred  = 4.5f;
    [Tooltip("마지막 페이지가 확대되며 사라져 3D 성당으로 녹아드는 시간(초).")]
    [SerializeField, Min(0.1f)] private float pageIntoScene = 1.8f;

    // ── Private ──────────────────────────────────────────────────────
    private bool _played;

    // ── Lifecycle ────────────────────────────────────────────────────
    private void Start() => PlayAsync(this.GetCancellationTokenOnDestroy()).Forget();

    // ── 시퀀스 ───────────────────────────────────────────────────────
    private async UniTaskVoid PlayAsync(CancellationToken ct)
    {
        if (_played) return;
        _played = true;

        try
        {
            // 암전 상태로 시작 — 프롤로그는 책 페이지 위에서 읽힌다.
            await ScreenFade.Out(0f, ct);

            var player = await WaitForPlayerAsync(ct);
            player?.SetInputEnabled(false);

            // 프롤로그~오프닝 내내 HUD를 숨긴다. 전투 진입 시에만 페이드로 등장해야 하는데,
            // @UIRoot는 DontDestroyOnLoad라 이전 씬의 표시 상태가 그대로 넘어올 수 있다.
            UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(true);

            var cam = GameCameraController.Instance;
            cam?.TakeManualControl();

            await DelaySafe(bootDelay, ct);

            // ① 세계관 프롤로그 — 책 페이지(전체화면 삽화) 위에서 내레이션이 읽힌다.
            //    라인의 illustration_key를 IntroPageBook이 받아 페이지를 넘긴다.
            IntroPageBook.Open();
            await ScreenFade.In(0f, ct); // 페이지북 자체 검은 배경이 화면을 가리므로 암전은 즉시 해제
            await PlaySequenceAsync(prologueSequenceId);

            Vector3 altar   = altarAnchor   != null ? altarAnchor.position   : new Vector3(0f, 1f, 18f);
            Vector3 sword   = swordAnchor   != null ? swordAnchor.position   : new Vector3(0.2f, 1f, -2.4f);
            Vector3 mordred = mordredAnchor != null ? mordredAnchor.position : new Vector3(0.25f, 0f, 2.26f);

            // ② 페이지 안으로 — 마지막 삽화가 확대되며 사라지고, 같은 구도의 3D 성당이 드러난다.
            //    페이지가 녹는 동안 카메라도 이미 밀고 들어가는 중이라 "그림 속으로 들어간" 것처럼 이어진다.
            SetCamera(altar + new Vector3(0f, 2.0f, -12f), altar + new Vector3(0f, 13f, 4f));
            IntroPageBook.DissolveIntoSceneAsync(pageIntoScene, ct).Forget();
            await MoveCameraAsync(altar + new Vector3(0f, 3.4f, -8.5f), altar + new Vector3(0f, 10f, 3f), shotGlass, ct);

            // ③ 하강 — 시선이 빛에서 원탁으로 내려온다
            await MoveCameraAsync(altar + new Vector3(-2.8f, 2.2f, -13f), altar + new Vector3(0f, 1.4f, 0f), shotAltar, ct);

            // ④ 무형검 — 곁을 돌며 다가간다
            SetCamera(sword + new Vector3(2.8f, 1.5f, -2.6f), sword + new Vector3(0f, 0.6f, 0f));
            await MoveCameraAsync(sword + new Vector3(1.0f, 0.9f, -1.6f), sword + new Vector3(0f, 0.5f, 0f), shotSword, ct);

            // ⑤ 모르드레드 — 올려다보며 밀고 들어간다(검을 건넬 자)
            SetCamera(mordred + new Vector3(3.4f, 1.0f, -5.8f), mordred + new Vector3(0f, 1.7f, 0f));
            await MoveCameraAsync(mordred + new Vector3(2.0f, 1.0f, -3.6f), mordred + new Vector3(0f, 1.6f, 0f), shotMordred, ct);

            // ⑥ 암전 → 플레이어 뒤로 → 밝힘 → 조작 인계
            await ScreenFade.Out(fadeDuration, ct);
            var handOff = UniTask.CompletedTask;
            if (player != null)
            {
                Vector3 p = player.transform.position;
                SetCamera(p + new Vector3(0f, 2.6f, -5.5f), p + new Vector3(0f, 1.4f, 6f));
                if (cam != null) handOff = cam.HandToGameplayCameraAsync(player.transform, ct: ct);
            }
            await ScreenFade.In(fadeDuration, ct);
            // 카메라 보간이 끝난 뒤에 조작을 넘긴다 — 블렌드 중에 조작이 들어가면 시점이 튄다.
            await handOff;

            // 통로 이동 구간에서도 HUD는 계속 숨긴다(전투 진입 시 FadeInHudAsync가 띄운다).
            UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(true);
            player?.SetInputEnabled(true);
        }
        catch (OperationCanceledException)
        {
            IntroPageBook.Close(); // 연출 취소 시 페이지가 화면에 남지 않도록
        }
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────
    /// <summary>카메라를 즉시 해당 포즈로 세운다(샷 시작 = 컷).</summary>
    private static void SetCamera(Vector3 pos, Vector3 lookAt)
    {
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 dir = lookAt - pos;
        cam.transform.SetPositionAndRotation(
            pos,
            dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir.normalized, Vector3.up) : cam.transform.rotation);
    }

    /// <summary>현재 포즈에서 목표 포즈로 부드럽게 이동(샷 내내 움직임 유지).</summary>
    private static async UniTask MoveCameraAsync(Vector3 pos, Vector3 lookAt, float dur, CancellationToken ct)
    {
        var cam = Camera.main;
        if (cam == null) return;

        var t = cam.transform;
        Vector3    startPos = t.position;
        Quaternion startRot = t.rotation;
        Vector3    dir      = lookAt - pos;
        Quaternion endRot   = dir.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(dir.normalized, Vector3.up)
            : startRot;

        if (dur <= 0f) { t.SetPositionAndRotation(pos, endRot); return; }

        float e = 0f;
        while (e < dur)
        {
            e += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / dur));
            t.SetPositionAndRotation(Vector3.Lerp(startPos, pos, k), Quaternion.Slerp(startRot, endRot, k));
            try { await UniTask.Yield(PlayerLoopTiming.Update, ct); }
            catch (OperationCanceledException) { return; }
        }
        t.SetPositionAndRotation(pos, endRot);
    }

    private static async UniTask<PlayerController> WaitForPlayerAsync(CancellationToken ct)
    {
        PlayerController p = null;
        try
        {
            await UniTask.WaitUntil(
                () => (p = FindFirstObjectByType<PlayerController>()) != null,
                cancellationToken: ct);
        }
        catch (OperationCanceledException) { }
        return p;
    }

    private static async UniTask PlaySequenceAsync(string seqId)
    {
        if (string.IsNullOrEmpty(seqId)) return;

        var dlg = Managers.DialogueData;
        if (dlg == null) return;
        if (!dlg.IsInitialized) await dlg.InitializeAsync();

        var lines = dlg.GetLines(seqId);
        if (lines == null || lines.Length == 0) return;

        await Managers.UI.WaitUntilNoBlockingPopupAsync();
        var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_DialoguePopup>();
        if (popup != null) await popup.ShowAsync(lines);
    }

    private static async UniTask DelaySafe(float seconds, CancellationToken ct)
    {
        if (seconds <= 0f) return;
        try { await UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: ct); }
        catch (OperationCanceledException) { }
    }
}
