using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;
using Cysharp.Threading.Tasks;

/// <summary>
/// 씬 전환 시 화면을 가려주는 로딩 오버레이.
/// - UIRoot(@UIRoot) 하위에 배치하여 DontDestroyOnLoad
/// - ShowAsync(): 영상 재생 + 페이드인 + 입력 차단
/// - HideAsync(): 영상 즉시 정지 + 페이드아웃
/// - SetProgress(): 로딩바 업데이트 (선택)
/// </summary>
public sealed class UI_SceneLoading : MonoBehaviour
{
    public static UI_SceneLoading Instance { get; private set; }

    [Header("References")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Slider progressBar;
    [SerializeField] private TMP_Text percentText;
    [SerializeField] private VideoPlayer _videoPlayer;
    [SerializeField] private RawImage _videoBg;

    [Header("Timing")]
    [SerializeField] private float fadeInDuration  = 0.4f;
    [SerializeField] private float fadeOutDuration = 0.5f;
    [SerializeField] private float minDisplayTime  = 1.5f;
    [SerializeField] private float progressSpeed   = 0.5f;

    public float ProgressSpeed => progressSpeed;

    /// <summary>
    /// 오버레이가 화면을 <b>완전히</b> 덮고 있는지. 페이드인 중(alpha&lt;1)은 false다 —
    /// 앞 레이어를 내려도 되는 시점을 이 값으로 판단한다.
    /// </summary>
    public bool IsCovering => _shown && (canvasGroup == null || canvasGroup.alpha >= 0.999f);

    private float _showStartTime;
    private RenderTexture _rt;
    private bool _shown;   // 이미 화면을 덮고 있는지 — 중복 Show가 영상·진행도를 되감지 않게 한다

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (_videoPlayer != null && _videoBg != null)
        {
            _rt = new RenderTexture(1920, 1080, 0);
            _videoPlayer.renderMode    = VideoRenderMode.RenderTexture;
            _videoPlayer.targetTexture = _rt;
            _videoPlayer.playOnAwake   = false;
            _videoPlayer.isLooping     = false;
            _videoBg.texture           = _rt;
            _videoPlayer.loopPointReached += OnVideoEnded;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha          = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable   = false;
        }

        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;

        if (_videoPlayer != null)
            _videoPlayer.loopPointReached -= OnVideoEnded;

        if (_rt != null)
        {
            _rt.Release();
            Destroy(_rt);
        }
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// 로딩 오버레이를 띄운다. <b>이미 떠 있으면 아무것도 하지 않는다</b> —
    /// 씬 전환 경로에 따라 호출자가 먼저 덮어 두고 SceneTransitionManager가 다시 부르는 경우가 있는데,
    /// 그때 영상을 되감고 진행도를 0으로 되돌리면 화면이 한 번 튄다.
    /// </summary>
    public async UniTask ShowAsync()
    {
        if (_shown) return;
        _shown = true;

        gameObject.SetActive(true);

        // 레이아웃 리빌드가 끝난 뒤 Prepare 시작해 같은 프레임 작업 분산
        await UniTask.Yield(PlayerLoopTiming.Update);

        SetProgress(0f);

        if (_videoPlayer != null)
            _videoPlayer.Prepare();

        if (canvasGroup != null)
            canvasGroup.blocksRaycasts = true;

        await FadeAsync(1f, fadeInDuration);

        if (_videoPlayer != null)
        {
            // Prepare 미완료 시 최대 2초 대기 — 느린 기기에서 첫 프레임 누락 방지
            float elapsed = 0f;
            while (!_videoPlayer.isPrepared && elapsed < 2f)
            {
                elapsed += Time.unscaledDeltaTime;
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            _videoPlayer.Play();
        }

        _showStartTime = Time.realtimeSinceStartup;
    }

    public async UniTask HideAsync()
    {
        if (!_shown) return;
        _shown = false;

        if (_videoPlayer != null)
        {
            _videoPlayer.Stop();
            _videoPlayer.time = 0;
            ClearRenderTexture();   // 다음 Show 시 이전 프레임 잔상 방지
        }

        // 최소 표시 시간 보장
        float elapsed = Time.realtimeSinceStartup - _showStartTime;
        float remain  = minDisplayTime - elapsed;
        if (remain > 0f)
            await UniTask.Delay(System.TimeSpan.FromSeconds(remain), ignoreTimeScale: true);

        await FadeAsync(0f, fadeOutDuration);

        if (canvasGroup != null)
            canvasGroup.blocksRaycasts = false;

        gameObject.SetActive(false);
    }

    /// <param name="progress">0~1</param>
    public void SetProgress(float progress)
    {
        float clamped = Mathf.Clamp01(progress);
        if (progressBar != null)
            progressBar.value = clamped;
        if (percentText != null)
            percentText.text = $"{Mathf.RoundToInt(clamped * 100)}%";
    }

    // -------------------------------------------------------------------------
    // Internal
    // -------------------------------------------------------------------------

    private void ClearRenderTexture()
    {
        if (_rt == null) return;
        var prev = RenderTexture.active;
        RenderTexture.active = _rt;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = prev;
    }

    // -------------------------------------------------------------------------
    // Event Handlers
    // -------------------------------------------------------------------------

    private void OnVideoEnded(VideoPlayer vp)
    {
        // 로딩이 아직 끝나지 않았을 때 영상이 먼저 끝나면 마지막 프레임에 정지
        vp.Pause();
    }

    // -------------------------------------------------------------------------

    private async UniTask FadeAsync(float targetAlpha, float duration)
    {
        if (canvasGroup == null)
            return;

        if (duration <= 0f)
        {
            canvasGroup.alpha = targetAlpha;
            return;
        }

        float start = canvasGroup.alpha;
        float t     = 0f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(start, targetAlpha, Mathf.Clamp01(t / duration));
            await UniTask.Yield(PlayerLoopTiming.Update);
        }

        canvasGroup.alpha = targetAlpha;
    }
}
