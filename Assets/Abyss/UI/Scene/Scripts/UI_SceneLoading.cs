using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cysharp.Threading.Tasks;

/// <summary>
/// 씬 전환 시 화면을 가려주는 로딩 오버레이.
/// - UIRoot(@UIRoot) 하위에 배치하여 DontDestroyOnLoad
/// - ShowAsync(): 페이드인 + 입력 차단
/// - HideAsync(): 페이드아웃
/// - SetProgress(): 로딩바 업데이트 (선택)
/// </summary>
public sealed class UI_SceneLoading : MonoBehaviour
{
    public static UI_SceneLoading Instance { get; private set; }

    [Header("References")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Slider progressBar;        // 선택 (없으면 무시)
    [SerializeField] private TMP_Text percentText;      // 선택 (없으면 무시)

    [Header("Timing")]
    [SerializeField] private float fadeInDuration  = 0.4f;
    [SerializeField] private float fadeOutDuration = 0.5f;
    [SerializeField] private float minDisplayTime  = 1.5f;   // 너무 빨리 끝나면 깜빡임 방지
    [SerializeField] private float progressSpeed   = 0.5f;   // 진행바 이동 속도 (1/s)

    public float ProgressSpeed => progressSpeed;

    private float _showStartTime;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // 초기 상태: 완전히 숨김
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
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public async UniTask ShowAsync()
    {
        gameObject.SetActive(true);
        SetProgress(0f);

        if (canvasGroup != null)
            canvasGroup.blocksRaycasts = true;

        await FadeAsync(1f, fadeInDuration);

        _showStartTime = Time.realtimeSinceStartup;
    }

    public async UniTask HideAsync()
    {
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
