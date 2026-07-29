using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using System.Threading;

public class GameStartVideoPlayer : MonoBehaviour
{
    [SerializeField] private VideoClip _clip;

    [Tooltip("영상이 끝난 뒤 다음 화면이 덮을 때까지 화면을 가리는 불투명 막. 비우면 자식에서 찾는다.")]
    [SerializeField] private Image _coverOverlay;

    private VideoPlayer _videoPlayer;
    private RawImage _rawImage;
    private RenderTexture _rt;

    private void Awake()
    {
        InitComponents();
        HideNow();
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
        if (_coverOverlay == null)
        {
            // 프리팹에 이미 불투명 검은 막(FadeOverlay)이 authoring 돼 있다 — 미배선이면 자동으로 잡는다.
            foreach (var img in GetComponentsInChildren<Image>(true))
            {
                if (img.name != "FadeOverlay") continue;
                _coverOverlay = img;
                break;
            }
        }
        _rt = new RenderTexture(1920, 1080, 0);
        if (_videoPlayer != null)
        {
            _videoPlayer.renderMode    = VideoRenderMode.RenderTexture;
            _videoPlayer.targetTexture = _rt;
        }
        if (_rawImage != null)
            _rawImage.texture = _rt;
    }

    /// <summary>
    /// 영상 레이어를 완전히 내린다. <b>다음 화면이 화면을 덮은 뒤에</b> 호출해야 한다 —
    /// 영상이 끝나자마자 내려버리면 로딩 오버레이가 페이드인하는 동안 아무것도 화면을 가리지 않아
    /// 유니티 기본 배경이 그대로 드러난다(그래서 PlayAsync는 스스로 내리지 않는다).
    /// </summary>
    public void HideNow()
    {
        if (_rawImage != null) _rawImage.enabled = false;
        if (_coverOverlay != null) _coverOverlay.gameObject.SetActive(false);
    }

    public async UniTask PlayAsync(CancellationToken token)
    {
        InitComponents();
        if (_clip == null) return;

        if (_rawImage != null) _rawImage.enabled = true;

        if (_videoPlayer != null)
        {
            _videoPlayer.source    = VideoSource.VideoClip;
            _videoPlayer.clip      = _clip;
            _videoPlayer.isLooping = false;
            _videoPlayer.Prepare();
            await UniTask.WaitUntil(() => _videoPlayer.isPrepared, cancellationToken: token);
            _videoPlayer.Play();
            await WaitForEndAsync(_videoPlayer, token);
        }

        // 영상은 껐지만 레이어는 검은 막으로 남긴다 — 호출자가 다음 화면을 띄운 뒤 HideNow()로 내린다.
        if (_coverOverlay != null) _coverOverlay.gameObject.SetActive(true);
        if (_rawImage != null) _rawImage.enabled = false;
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
