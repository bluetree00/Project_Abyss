using System;
using System.Threading;
using Cinemachine;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Q스킬 필살기 카메라 연출 서비스 (원신/명조 스타일).
/// 슬로모 + 클로즈업 카메라 + Letterbox + Dim + 스킬명.
/// 어지러움 방지: 카메라는 회전 없이 actor 기준 오프셋 + Cinemachine smooth blend 만 사용.
///
/// 사용:
///   UltimateCinematicService.Play(config, actorTransform).Forget();
/// </summary>
public static class UltimateCinematicService
{
    private class Host : MonoBehaviour { }

    // ── 캐시 ──────────────────────────────────────────────────────────
    private static Host _host;
    private static CinemachineBrain _brain;

    private static GameObject _vcamGo;
    private static CinemachineVirtualCamera _vcam;
    private static CinemachineTransposer _vcamTransposer;
    private static CinemachineComposer _vcamComposer;

    private static Canvas _canvas;
    private static Image _topBar;
    private static Image _bottomBar;
    private static Image _dim;
    private static TextMeshProUGUI _skillNameText;

    private static CancellationTokenSource _cts;
    private static bool _isPlaying;
    private static readonly object _timeScaleOwner = new object();   // TimeScaleArbiter 요청 키

    // 도메인 리로드 OFF: finally 미실행으로 _isPlaying=true 잔류 시 다음 런 PlayInternal이 영구 탈출 → 리셋.
    // Unity-object 캐시(_host/_vcam/_canvas 등)는 Ensure* 가 자가 치유하므로 bool/cts만 복원.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { _isPlaying = false; _cts = null; }

    // ── Public API ────────────────────────────────────────────────────
    /// <summary>필살기 연출 시작 — 슬로모 + 클로즈업 + UI. 이미 실행 중이면 기존 것 취소 후 새로 시작.</summary>
    public static UniTask Play(UltimateCinematicConfig cfg, Transform actor)
    {
        if (cfg == null || actor == null) return UniTask.CompletedTask;

        EnsureHost();
        EnsureVCam();
        EnsureCanvas();

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        return PlayInternal(cfg, actor, _cts.Token);
    }

    /// <summary>진행 중이면 즉시 종료 (timeScale/UI/카메라 복원).</summary>
    public static void Cancel()
    {
        _cts?.Cancel();
    }

    // ── 내부 시퀀스 ───────────────────────────────────────────────────
    private static async UniTask PlayInternal(UltimateCinematicConfig cfg, Transform actor, CancellationToken token)
    {
        if (_isPlaying) return; // 동시 진입 방지 (Cancel 후 재진입은 다른 _cts)
        _isPlaying = true;

        // 상태 백업 (timeScale 은 TimeScaleArbiter 가 소유 — fixedDeltaTime/Brain 만 직접 백업·복원)
        float originalFixedDt   = Time.fixedDeltaTime;
        bool  brainIgnoreSaved  = _brain != null ? _brain.m_IgnoreTimeScale : false;
        float slowScale         = Mathf.Max(0.01f, cfg.timeScale);

        try
        {
            // 슬로모 + Cinemachine Brain unscaled time 사용
            TimeScaleArbiter.Acquire(_timeScaleOwner, slowScale, TimeScaleArbiter.Priority.SlowMotion);
            Time.fixedDeltaTime = 0.02f * slowScale;
            if (_brain != null) _brain.m_IgnoreTimeScale = true;

            // 카메라 세팅
            ActivateVCam(cfg, actor);

            // UI 세팅
            ConfigureUI(cfg);

            // 페이즈 분할 (real time)
            float duration = Mathf.Max(0.3f, cfg.duration);
            float fadeInT  = duration * 0.2f;
            float holdT    = duration * 0.55f;
            float fadeOutT = duration - fadeInT - holdT;

            // Phase 1: Fade IN
            await FadeUI(cfg, 0f, 1f, fadeInT, token);

            // Phase 2: Hold
            if (holdT > 0f)
                await UniTask.Delay(TimeSpan.FromSeconds(holdT), DelayType.UnscaledDeltaTime, cancellationToken: token);

            // Phase 3: Fade OUT
            await FadeUI(cfg, 1f, 0f, fadeOutT, token);
        }
        catch (OperationCanceledException) { /* 정상 종료 */ }
        finally
        {
            TimeScaleArbiter.Release(_timeScaleOwner);
            Time.fixedDeltaTime = originalFixedDt;
            if (_brain != null) _brain.m_IgnoreTimeScale = brainIgnoreSaved;

            DeactivateVCam();
            HideUI();
            _isPlaying = false;
        }
    }

    /// <summary>UI 요소(letterbox/dim/text) 의 alpha 를 from→to 로 보간.</summary>
    private static async UniTask FadeUI(UltimateCinematicConfig cfg, float fromAlpha, float toAlpha, float duration, CancellationToken token)
    {
        if (duration <= 0f)
        {
            ApplyUIAlpha(cfg, toAlpha);
            return;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            token.ThrowIfCancellationRequested();
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // smoothstep: 어지러움 방지용 ease in/out
            float k = t * t * (3f - 2f * t);
            ApplyUIAlpha(cfg, Mathf.Lerp(fromAlpha, toAlpha, k));
            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }
        ApplyUIAlpha(cfg, toAlpha);
    }

    private static void ApplyUIAlpha(UltimateCinematicConfig cfg, float alpha01)
    {
        if (cfg.useLetterbox && _topBar != null)
        {
            SetImgAlpha(_topBar, alpha01);
            SetImgAlpha(_bottomBar, alpha01);
        }
        if (cfg.useDim && _dim != null)
            SetImgAlpha(_dim, alpha01 * cfg.dimAlpha);

        if (_skillNameText != null)
        {
            var c = _skillNameText.color;
            _skillNameText.color = new Color(c.r, c.g, c.b, alpha01);
        }
    }

    private static void SetImgAlpha(Image img, float a)
    {
        var c = img.color;
        img.color = new Color(c.r, c.g, c.b, a);
    }

    // ── Camera ────────────────────────────────────────────────────────
    private static void EnsureVCam()
    {
        if (_vcamGo != null) return;

        _vcamGo = new GameObject("~UltimateVCam");
        UnityEngine.Object.DontDestroyOnLoad(_vcamGo);

        _vcam = _vcamGo.AddComponent<CinemachineVirtualCamera>();
        _vcam.Priority = 0;

        _vcamTransposer = _vcam.AddCinemachineComponent<CinemachineTransposer>();
        _vcamTransposer.m_BindingMode = CinemachineTransposer.BindingMode.LockToTargetWithWorldUp;
        _vcamTransposer.m_XDamping = 0.4f;
        _vcamTransposer.m_YDamping = 0.4f;
        _vcamTransposer.m_ZDamping = 0.4f;

        _vcamComposer = _vcam.AddCinemachineComponent<CinemachineComposer>();
        _vcamComposer.m_HorizontalDamping = 0.4f;
        _vcamComposer.m_VerticalDamping = 0.4f;
        _vcamComposer.m_LookaheadTime = 0f;
    }

    private static void ActivateVCam(UltimateCinematicConfig cfg, Transform actor)
    {
        if (_vcam == null) return;

        _vcam.Follow = actor;
        _vcam.LookAt = actor;

        if (_vcamTransposer != null)
            _vcamTransposer.m_FollowOffset = cfg.cameraOffset;

        if (_vcamComposer != null)
            _vcamComposer.m_TrackedObjectOffset = cfg.cameraLookOffset;

        if (cfg.cameraFov > 0f)
            _vcam.m_Lens.FieldOfView = cfg.cameraFov;

        _vcam.Priority = 100;

        // Brain 캐시
        if (_brain == null)
            _brain = UnityEngine.Object.FindObjectOfType<CinemachineBrain>();
    }

    private static void DeactivateVCam()
    {
        if (_vcam == null) return;
        _vcam.Priority = 0;
        _vcam.Follow = null;
        _vcam.LookAt = null;
    }

    // ── Canvas / UI ───────────────────────────────────────────────────
    private static void EnsureCanvas()
    {
        if (_canvas != null) return;

        var go = new GameObject("~UltimateCinematicCanvas");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 5000;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;
        go.AddComponent<GraphicRaycaster>();

        // 검정 letterbox 바 (상)
        _topBar = CreateBar("TopBar", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.up);
        _bottomBar = CreateBar("BottomBar", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.down);

        // 풀스크린 dim
        var dimGo = new GameObject("Dim", typeof(RectTransform), typeof(Image));
        dimGo.transform.SetParent(_canvas.transform, false);
        var dimRt = dimGo.GetComponent<RectTransform>();
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = Vector2.zero;
        dimRt.offsetMax = Vector2.zero;
        _dim = dimGo.GetComponent<Image>();
        _dim.color = new Color(0f, 0f, 0f, 0f);
        _dim.raycastTarget = false;
        // dim 은 letterbox 보다 뒤(아래) 에 그려져야 함
        dimGo.transform.SetAsFirstSibling();

        // 스킬명 텍스트
        var txtGo = new GameObject("SkillName", typeof(RectTransform));
        txtGo.transform.SetParent(_canvas.transform, false);
        _skillNameText = txtGo.AddComponent<TextMeshProUGUI>();
        var trt = _skillNameText.rectTransform;
        trt.anchorMin = new Vector2(0.5f, 0.18f);
        trt.anchorMax = new Vector2(0.5f, 0.18f);
        trt.sizeDelta = new Vector2(1400f, 120f);
        trt.anchoredPosition = Vector2.zero;
        _skillNameText.alignment = TextAlignmentOptions.Center;
        _skillNameText.fontSize = 64f;
        _skillNameText.color = new Color(1f, 1f, 1f, 0f);
        _skillNameText.raycastTarget = false;

        HideUI();
    }

    private static Image CreateBar(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivotDir)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(_canvas.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = anchorMin; // 1080 기준 위 또는 아래 정렬
        rt.sizeDelta = new Vector2(1920f, 130f); // height 는 ConfigureUI 에서 갱신
        rt.anchoredPosition = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = false;
        return img;
    }

    private static void ConfigureUI(UltimateCinematicConfig cfg)
    {
        // letterbox 높이 — 1080 기준 비율
        if (cfg.useLetterbox && _topBar != null)
        {
            float h = 1080f * Mathf.Clamp01(cfg.letterboxHeightNorm);
            var topRt = _topBar.rectTransform;
            var botRt = _bottomBar.rectTransform;

            topRt.anchorMin = new Vector2(0f, 1f);
            topRt.anchorMax = new Vector2(1f, 1f);
            topRt.pivot     = new Vector2(0.5f, 1f);
            topRt.sizeDelta = new Vector2(0f, h);
            topRt.anchoredPosition = Vector2.zero;

            botRt.anchorMin = new Vector2(0f, 0f);
            botRt.anchorMax = new Vector2(1f, 0f);
            botRt.pivot     = new Vector2(0.5f, 0f);
            botRt.sizeDelta = new Vector2(0f, h);
            botRt.anchoredPosition = Vector2.zero;

            _topBar.gameObject.SetActive(true);
            _bottomBar.gameObject.SetActive(true);
            SetImgAlpha(_topBar, 0f);
            SetImgAlpha(_bottomBar, 0f);
        }
        else if (_topBar != null)
        {
            _topBar.gameObject.SetActive(false);
            _bottomBar.gameObject.SetActive(false);
        }

        if (_dim != null)
        {
            _dim.gameObject.SetActive(cfg.useDim);
            if (cfg.useDim) SetImgAlpha(_dim, 0f);
        }

        if (_skillNameText != null)
        {
            bool hasName = !string.IsNullOrEmpty(cfg.skillName);
            _skillNameText.gameObject.SetActive(hasName);
            if (hasName)
            {
                _skillNameText.text = cfg.skillName;
                _skillNameText.fontSize = cfg.skillNameFontSize;
                var c = cfg.skillNameColor;
                _skillNameText.color = new Color(c.r, c.g, c.b, 0f);
            }
        }
    }

    private static void HideUI()
    {
        if (_topBar != null) { _topBar.gameObject.SetActive(false); _bottomBar.gameObject.SetActive(false); }
        if (_dim   != null) _dim.gameObject.SetActive(false);
        if (_skillNameText != null) _skillNameText.gameObject.SetActive(false);
    }

    // ── Host ──────────────────────────────────────────────────────────
    private static void EnsureHost()
    {
        if (_host != null) return;
        var go = new GameObject("~UltimateCinematicHost");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _host = go.AddComponent<Host>();
    }
}
