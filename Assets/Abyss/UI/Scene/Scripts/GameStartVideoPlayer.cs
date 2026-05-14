using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using System.Threading;

public class GameStartVideoPlayer : MonoBehaviour
{
    [SerializeField] private VideoClip _clip;
    [SerializeField] private CanvasGroup _fadeOverlay;
    [SerializeField] private float _fadeDuration = 0.5f;

    private VideoPlayer _videoPlayer;
    private RawImage _rawImage;
    private RenderTexture _rt;

    private void Awake()
    {
        InitComponents();
        Hide();
    }

    private void OnDestroy()
    {
        if (_rt == null) return;
        _rt.Release();
        Destroy(_rt);
        _rt = null;
    }

    private void InitComponents()
    {
        if (_rt != null) return;
        _videoPlayer = GetComponentInChildren<VideoPlayer>(true);
        _rawImage    = GetComponentInChildren<RawImage>(true);
        _rt = new RenderTexture(1920, 1080, 0);
        if (_videoPlayer != null)
        {
            _videoPlayer.renderMode    = VideoRenderMode.RenderTexture;
            _videoPlayer.targetTexture = _rt;
        }
        if (_rawImage != null)
            _rawImage.texture = _rt;
    }

    private void Hide()
    {
        if (_rawImage != null) _rawImage.enabled = false;
        if (_fadeOverlay != null)
        {
            _fadeOverlay.alpha = 0f;
            _fadeOverlay.gameObject.SetActive(false);
        }
    }

    public async UniTask PlayAsync(CancellationToken token)
    {
        InitComponents();
        if (_clip == null) return;

        // 1. 로비 화면 페이드 아웃 (검은 오버레이 페이드 인)
        if (_fadeOverlay != null)
        {
            _fadeOverlay.alpha = 0f;
            _fadeOverlay.gameObject.SetActive(true);

            float elapsed = 0f;
            while (elapsed < _fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                _fadeOverlay.alpha = Mathf.Clamp01(elapsed / _fadeDuration);
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
            _fadeOverlay.alpha = 1f;
        }

        // 2. 영상 재생 — Prepare 완료 후 FadeOverlay를 끄고 Play
        if (_rawImage != null) _rawImage.enabled = true;

        if (_videoPlayer != null)
        {
            _videoPlayer.source    = VideoSource.VideoClip;
            _videoPlayer.clip      = _clip;
            _videoPlayer.isLooping = false;
            _videoPlayer.Prepare();
            await UniTask.WaitUntil(() => _videoPlayer.isPrepared, cancellationToken: token);
            if (_fadeOverlay != null) _fadeOverlay.gameObject.SetActive(false);
            _videoPlayer.Play();
            await WaitForEndAsync(_videoPlayer, token);
        }

        Hide();
    }

    private static async UniTask WaitForEndAsync(VideoPlayer vp, CancellationToken token)
    {
        var tcs = new UniTaskCompletionSource();
        void OnEnd(VideoPlayer _) { vp.loopPointReached -= OnEnd; tcs.TrySetResult(); }
        vp.loopPointReached += OnEnd;
        try   { await tcs.Task.AttachExternalCancellation(token); }
        finally { vp.loopPointReached -= OnEnd; }
    }
}
