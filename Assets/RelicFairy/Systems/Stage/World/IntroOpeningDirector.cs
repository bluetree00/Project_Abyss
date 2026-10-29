using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 인트로 오프닝 시네마틱 — 세계관 프롤로그(암전 위 내레이션) → 성당 카메라 연출 → 플레이어 조작 인계.
///
/// 조우 컷신(IntroMordredDirector)과는 트리거·책임이 달라 분리한다(오프닝=씬 시작, 조우=구간 진입).
/// 카메라는 정지 포즈가 아니라 <b>from→to로 계속 움직인다</b> — 장소(성당 · 붉은 길 · 제단)를 먼저 보여 주고
/// 그 길 끝에서 기다리는 모르드레드에게 다가가는 한 호흡의 공개(10-16 사용자 「모드레드의 위치와 모르드레드가 있는 장소를 보여주는 카메라 무브」).
/// 구도는 모르드레드의 <b>몸 범위</b>(스킨드 메시) 기준이다 — 액터 피벗은 바닥 아래라 피벗 오프셋은 머리 위 하늘만 잡았다.
/// </summary>
public sealed class IntroOpeningDirector : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────────────
    private const float WideFov  = 55f;   // 성당 전경
    private const float CloseFov = 40f;   // 통로 끝 — 기사 가슴
    private const float LowFov   = 36f;   // 낮은 3/4 — 올려다봄

    // ── [SerializeField] ─────────────────────────────────────────────
    [Header("프롤로그 (DIALOGUE_DATA.csv)")]
    [SerializeField] private string prologueSequenceId = "Intro_Prologue";

    [Header("연출 앵커")]
    [Tooltip("제단/천사상 — 전경 구도에서 기사 뒤로 보이는 끝점.")]
    [SerializeField] private Transform altarAnchor;
    [Tooltip("모르드레드 — 공개 대상(몸 범위로 구도를 잡는다).")]
    [SerializeField] private Transform mordredAnchor;

    [Header("타이밍")]
    [SerializeField, Min(0f)]   private float bootDelay    = 0.4f;
    [SerializeField, Min(0.1f)] private float fadeDuration = 1.2f;
    [Tooltip("성당 입구 높은 곳 → 통로를 따라 내려앉아 기사 가슴 높이까지(초).")]
    [SerializeField, Min(0.1f)] private float shotReveal   = 7.5f;
    [Tooltip("통로 끝 → 낮은 3/4로 살짝 돌며 올려다봄(초).")]
    [SerializeField, Min(0.1f)] private float shotArc      = 3.5f;
    [Tooltip("마지막 구도에 머무는 시간(초).")]
    [SerializeField, Min(0f)]   private float shotHold     = 0.8f;
    [Tooltip("마지막 페이지가 확대되며 사라져 3D 성당으로 녹아드는 시간(초).")]
    [SerializeField, Min(0.1f)] private float pageIntoScene = 1.8f;

    // ── Private ──────────────────────────────────────────────────────
    private bool _played;

    // ── Lifecycle ────────────────────────────────────────────────────
    private void Start() => PlayAsync(this.GetCancellationTokenOnDestroy()).Forget();

    // ── Public Methods ───────────────────────────────────────────────
    /// <summary>아레나가 런타임 생성이라 제단 앵커는 IntroArenaLoader가 로드 후 주입한다.</summary>
    public void SetAltarAnchor(Transform anchor) => altarAnchor = anchor;

    // ── 시퀀스 ───────────────────────────────────────────────────────
    private async UniTaskVoid PlayAsync(CancellationToken ct)
    {
        if (_played) return;
        _played = true;

        var view = Camera.main;
        float baseFov = view != null ? view.fieldOfView : 60f;
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

            // 성당은 런타임 Addressable 생성이다 — 앵커를 읽기 전에 배치가 끝나 있어야 구도가 맞는다.
            await IntroArenaLoader.ReadyAsync();

            // 구도 축: 플레이어가 들어오는 쪽 → 모르드레드가 앞(+fwd). 높이는 몸 범위로 잰다.
            Vector3 mPos  = mordredAnchor != null ? mordredAnchor.position : new Vector3(0.25f, 0f, 2.26f);
            var body      = IntroMordredDirector.BodyBounds(mordredAnchor != null ? mordredAnchor.gameObject : null, mPos, 2.3f);
            Vector3 foot  = new Vector3(mPos.x, body.min.y, mPos.z);
            Vector3 chest = foot + Vector3.up * (body.size.y * 0.68f);
            Vector3 head  = foot + Vector3.up * (body.size.y - 0.2f);
            Vector3 from  = player != null ? player.transform.position : mPos - Vector3.forward * 20f;
            Vector3 fwd   = Flat(mPos - from, Vector3.forward);
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 altar = altarAnchor != null ? altarAnchor.position : mPos + fwd * 18f;

            // ② 페이지 안으로 — 성당 입구 높은 곳에서 붉은 길 · 그 끝에 선 기사 · 뒤의 제단을 한 화면에.
            //    페이지가 녹는 동안 카메라는 이미 통로를 따라 내려앉기 시작한다("그림 속으로 들어간" 이음).
            Vector3 wideAt   = foot - fwd * 18f + Vector3.up * (body.size.y + 5.5f);
            Vector3 wideLook = Vector3.Lerp(chest, altar + Vector3.up * 2f, 0.45f);
            SetCamera(view, wideAt, wideLook, WideFov);
            IntroPageBook.DissolveIntoSceneAsync(pageIntoScene, ct).Forget();

            // ③ 통로를 따라 — 크레인 다운 + 돌리 인으로 기사의 가슴 높이까지(장소 → 인물)
            Vector3 aisleAt = foot - fwd * 5.5f + right * 1.4f;
            aisleAt.y = chest.y + 0.2f;
            await MoveCameraAsync(view, aisleAt, chest, CloseFov, shotReveal, ct);

            // ④ 낮은 3/4로 살짝 돌며 올려다본다 — 검을 건넬 자(제단이 그의 뒤에 선다)
            Vector3 lowAt = foot - fwd * 3.4f + right * 2.2f + Vector3.up * 1.0f;
            await MoveCameraAsync(view, lowAt, head, LowFov, shotArc, ct);
            await DelaySafe(shotHold, ct);

            // ⑤ 암전 → 플레이어 뒤로 → 밝힘 → 조작 인계
            await ScreenFade.Out(fadeDuration, ct);
            if (view != null) view.fieldOfView = baseFov;
            var handOff = UniTask.CompletedTask;
            if (player != null)
            {
                Vector3 p = player.transform.position;
                SetCamera(view, p + new Vector3(0f, 2.6f, -5.5f), p + new Vector3(0f, 1.4f, 6f), baseFov);
                if (cam != null) handOff = cam.HandToGameplayCameraAsync(player.transform, ct: ct);
            }
            await ScreenFade.In(fadeDuration, ct);
            // 카메라 보간이 끝난 뒤에 조작을 넘긴다 — 블렌드 중에 조작이 들어가면 시점이 튄다.
            await handOff;

            // 통로 이동 구간에서도 HUD는 계속 숨긴다(전투 진입 시 FadeInHudAsync가 띄운다).
            UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(true);
            player?.SetInputEnabled(true);
            ShowControlsCard(player);
        }
        catch (OperationCanceledException)
        {
            IntroPageBook.Close(); // 연출 취소 시 페이지가 화면에 남지 않도록
            if (view != null) view.fieldOfView = baseFov;
        }
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────
    /// <summary>
    /// 처음 조작을 넘겨받는 순간 잠시 — 이동 · 공격 · 회피 · 무기 전환(10-24 사용자 「점프는 없으니 빼고 · 인트로 초반에 잠시 ·
    /// 이동 · 회피 · 공격 정도와 무기 전환」). 대사 넘기기는 대사창 오른쪽 아래가 알려 준다. 10초 또는 10 m 걸으면 걷힌다.
    /// </summary>
    private static void ShowControlsCard(PlayerController player)
    {
        if (player == null) return;
        var tr = player.transform;
        Vector3 from = tr.position;
        KeyGuideCard.Show("조작", new[]
        {
            ("W A S D", "이동"),
            ("좌클릭", "공격 · 꾹 누르면 연속"),   // 10-25 ④ 평타 꾹 누르기
            ("Shift · 우클릭", "회피"),
            ("1 · 2", "무기 전환"),
        }, 10f, () => tr == null || (tr.position - from).sqrMagnitude > 100f);
    }

    /// <summary>카메라를 즉시 해당 포즈로 세운다(샷 시작 = 컷). 벽 · 기둥 속이면 대상 쪽으로 당긴다.</summary>
    private static void SetCamera(Camera view, Vector3 pos, Vector3 lookAt, float fov)
    {
        if (view == null) return;
        pos = IntroMordredDirector.Unblock(pos, lookAt);
        view.transform.SetPositionAndRotation(pos, LookRot(pos, lookAt, view.transform.rotation));
        view.fieldOfView = fov;
    }

    /// <summary>현재 포즈에서 목표 포즈로 부드럽게 이동(샷 내내 움직임 유지). 비스케일 시간 — 시간 배율에 흔들리지 않는다.</summary>
    private static async UniTask MoveCameraAsync(Camera view, Vector3 pos, Vector3 lookAt, float fov, float dur, CancellationToken ct)
    {
        if (view == null) return;
        pos = IntroMordredDirector.Unblock(pos, lookAt);
        var t = view.transform;
        Vector3    startPos = t.position;
        Quaternion startRot = t.rotation;
        Quaternion endRot   = LookRot(pos, lookAt, startRot);
        float      startFov = view.fieldOfView;

        if (dur <= 0f) { t.SetPositionAndRotation(pos, endRot); view.fieldOfView = fov; return; }

        float e = 0f;
        while (e < dur)
        {
            e += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / dur));
            t.SetPositionAndRotation(Vector3.Lerp(startPos, pos, k), Quaternion.Slerp(startRot, endRot, k));
            view.fieldOfView = Mathf.Lerp(startFov, fov, k);
            try { await UniTask.Yield(PlayerLoopTiming.Update, ct); }
            catch (OperationCanceledException) { return; }
        }
        t.SetPositionAndRotation(pos, endRot);
        view.fieldOfView = fov;
    }

    private static Quaternion LookRot(Vector3 pos, Vector3 lookAt, Quaternion fallback)
    {
        Vector3 dir = lookAt - pos;
        return dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir.normalized, Vector3.up) : fallback;
    }

    private static Vector3 Flat(Vector3 v, Vector3 fallback)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : fallback;
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
