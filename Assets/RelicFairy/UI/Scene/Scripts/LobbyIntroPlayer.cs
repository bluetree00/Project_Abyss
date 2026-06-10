using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using System;
using System.Threading;

public class LobbyIntroPlayer : MonoBehaviour
{
    [Header("Video Clips")]
    [SerializeField] private VideoClip _loopClip;

    [Header("HUD Fade")]
    [SerializeField] private float _hudFadeDuration = 1.5f;

    private VideoPlayer _videoPlayer;
    private RawImage _bgRawImage;
    private CanvasGroup[] _fadeTargets;
    private CanvasGroup _hudCanvasGroup;
    private RenderTexture _renderTexture;
    private CancellationTokenSource _cts;

    private void Awake()
    {
        var bgTr = transform.Find("BG");
        _videoPlayer = bgTr?.GetComponent<VideoPlayer>();
        _bgRawImage = bgTr?.GetComponent<RawImage>();

        if (bgTr != null)
        {
            var bgRect = bgTr.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
        }

        _renderTexture = new RenderTexture(1920, 1080, 0);
        if (_videoPlayer != null)
        {
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.targetTexture = _renderTexture;
        }
        if (_bgRawImage != null)
            _bgRawImage.texture = _renderTexture;

        _fadeTargets = new CanvasGroup[]
        {
            GetOrAddCanvasGroup(transform, "DarkOverlay"),
            GetOrAddCanvasGroup(transform, "MenuPanel"),
            GetOrAddCanvasGroup(transform, "TitleImage"),
        };
    }

    private static CanvasGroup GetOrAddCanvasGroup(Transform parent, string childName)
    {
        var tr = parent.Find(childName);
        if (tr == null) return null;
        var cg = tr.GetComponent<CanvasGroup>();
        return cg != null ? cg : tr.gameObject.AddComponent<CanvasGroup>();
    }

    private void OnEnable()
    {
        if (_hudCanvasGroup == null)
        {
            var hud = FindAnyObjectByType<HudPresenter>(FindObjectsInactive.Include);
            _hudCanvasGroup = hud?.MainCanvasGroup;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        if (_videoPlayer != null)
            _videoPlayer.loopPointReached -= OnLoopEnd;

        HideAll();
        PlaySequenceAsync(_cts.Token).Forget();
    }

    private void OnDisable()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        if (_videoPlayer != null)
        {
            _videoPlayer.loopPointReached -= OnLoopEnd;
            _videoPlayer.Stop();
        }
    }

    private void OnDestroy()
    {
        if (_videoPlayer != null)
            _videoPlayer.loopPointReached -= OnLoopEnd;

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

    private async UniTaskVoid PlaySequenceAsync(CancellationToken token)
    {
        try
        {
            FadeInAllAsync(token).Forget();

            if (_videoPlayer == null || _loopClip == null) return;

            _videoPlayer.source = VideoSource.VideoClip;
            _videoPlayer.clip = _loopClip;
            _videoPlayer.isLooping = false;
            _videoPlayer.Prepare();
            await UniTask.WaitUntil(() => _videoPlayer.isPrepared, cancellationToken: token);

            // loopPointReached로 수동 재시작 — isLooping=true는 비기준 H.264에서 중간에 멈추는 Unity 버그가 있음
            _videoPlayer.loopPointReached += OnLoopEnd;
            _videoPlayer.Play();
        }
        catch (OperationCanceledException) { }
    }

    private void OnLoopEnd(VideoPlayer vp)
    {
        vp.time = 0;
        vp.Play();
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
