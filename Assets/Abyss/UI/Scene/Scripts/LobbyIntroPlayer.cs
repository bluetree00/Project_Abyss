using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using System;
using System.Threading;

public class LobbyIntroPlayer : MonoBehaviour
{
    [Header("Video Clips")]
    [SerializeField] private VideoClip _introClip;
    [SerializeField] private VideoClip _loopClip;

    [Header("HUD Fade")]
    [SerializeField] private float _hudFadeInStartTime = 10f;
    [SerializeField] private float _hudFadeDuration = 1.5f;

    private VideoPlayer _videoPlayer;
    private RawImage _bgRawImage;
    private AudioSource _audioSource;
    private CanvasGroup[] _fadeTargets;
    private CanvasGroup _hudCanvasGroup;
    private RenderTexture _renderTexture;
    private CancellationTokenSource _cts;

    private void Awake()
    {
        var bgTr = transform.Find("BG");
        _bgRawImage = bgTr?.GetComponent<RawImage>();
        _videoPlayer = bgTr?.GetComponent<VideoPlayer>();
        _audioSource = GetComponent<AudioSource>();

        _renderTexture = new RenderTexture(1920, 1080, 0);
        if (_videoPlayer != null)
            _videoPlayer.targetTexture = _renderTexture;
        if (_bgRawImage != null)
            _bgRawImage.texture = _renderTexture;
    }

    private void Start()
    {
        var hud = FindAnyObjectByType<HudPresenter>(FindObjectsInactive.Include);
        _hudCanvasGroup = hud?.MainCanvasGroup;

        _fadeTargets = new CanvasGroup[]
        {
            transform.Find("DarkOverlay")?.GetComponent<CanvasGroup>(),
            transform.Find("LeftPanel")?.GetComponent<CanvasGroup>(),
            transform.Find("MenuPanel")?.GetComponent<CanvasGroup>()
        };

        HideAll();

        _cts = new CancellationTokenSource();
        DelayPlayBgmAsync(_cts.Token).Forget();
        PlaySequenceAsync(_cts.Token).Forget();
    }

    private void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
            _renderTexture = null;
        }
    }

    private void HideAll()
    {
        foreach (var cg in _fadeTargets)
            SetGroup(cg, 0f, false);
        SetGroup(_hudCanvasGroup, 0f, false);
    }

    private static void SetGroup(CanvasGroup cg, float alpha, bool interactive)
    {
        if (cg == null) return;
        cg.alpha = alpha;
        cg.interactable = interactive;
        cg.blocksRaycasts = interactive;
    }

    private async UniTaskVoid DelayPlayBgmAsync(CancellationToken token)
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(0.5f), cancellationToken: token);
            _audioSource?.Play();
        }
        catch (OperationCanceledException) { }
    }

    private async UniTaskVoid PlaySequenceAsync(CancellationToken token)
    {
        try
        {
            if (_videoPlayer == null || _introClip == null) return;

            _videoPlayer.source = VideoSource.VideoClip;
            _videoPlayer.clip = _introClip;
            _videoPlayer.isLooping = false;
            _videoPlayer.Prepare();
            await UniTask.WaitUntil(() => _videoPlayer.isPrepared, cancellationToken: token);
            _videoPlayer.Play();

            await UniTask.Delay(TimeSpan.FromSeconds(_hudFadeInStartTime), cancellationToken: token);
            FadeInAllAsync(token).Forget();

            await WaitForVideoEndAsync(_videoPlayer, token);

            if (_loopClip == null) return;

            _videoPlayer.Stop();
            await UniTask.Yield(token);

            _videoPlayer.source = VideoSource.VideoClip;
            _videoPlayer.clip = _loopClip;
            _videoPlayer.isLooping = true;
            _videoPlayer.Prepare();
            await UniTask.WaitUntil(() => _videoPlayer.isPrepared, cancellationToken: token);
            _videoPlayer.Play();
        }
        catch (OperationCanceledException) { }
    }

    private static async UniTask WaitForVideoEndAsync(VideoPlayer player, CancellationToken token)
    {
        var tcs = new UniTaskCompletionSource();
        void OnEnd(VideoPlayer _)
        {
            player.loopPointReached -= OnEnd;
            tcs.TrySetResult();
        }
        player.loopPointReached += OnEnd;
        try
        {
            await tcs.Task.AttachExternalCancellation(token);
        }
        finally
        {
            player.loopPointReached -= OnEnd;
        }
    }

    private async UniTaskVoid FadeInAllAsync(CancellationToken token)
    {
        try
        {
            float elapsed = 0f;
            while (elapsed < _hudFadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float alpha = Mathf.Clamp01(elapsed / _hudFadeDuration);
                foreach (var cg in _fadeTargets)
                    if (cg != null) cg.alpha = alpha;
                if (_hudCanvasGroup != null)
                    _hudCanvasGroup.alpha = alpha;
                await UniTask.Yield(token);
            }

            foreach (var cg in _fadeTargets)
                SetGroup(cg, 1f, true);
            SetGroup(_hudCanvasGroup, 1f, true);
        }
        catch (OperationCanceledException) { }
    }
}
