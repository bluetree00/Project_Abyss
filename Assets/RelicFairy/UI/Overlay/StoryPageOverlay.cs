using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 이야기 장면 그림 한 장을 대사 뒤에 까는 전체화면 오버레이(10-13 힉스필드 · 사용자 「보스나 중요 스토리 때는 그림이 필요」).
/// 프롤로그 책(<see cref="IntroPageBook"/>)과 같은 결 — 검은 바탕 위 양피지 그림이 천천히 밀려 들어온다.
/// 다만 줄마다 그림을 바꾸지 않고 대사창 이벤트도 구독하지 않는다 — 대사창의 초상(멀린 · 그림자)은 그대로 위에 뜬다.
/// 정렬은 장면 밴드(300): HUD보다 위 · 대사창(팝업 400)보다 아래.
/// </summary>
public sealed class StoryPageOverlay : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────────────
    private const int   SortingOrder = UISortingOrder.Scene;
    private const float PushInPerSec = 0.010f;   // 프롤로그 책과 같은 밀어 넣기
    private const float FadeInTime   = 0.6f;
    private const float FadeOutTime  = 0.5f;

    // ── Static ───────────────────────────────────────────────────────
    private static StoryPageOverlay _instance;

    // ── Private ──────────────────────────────────────────────────────
    private CanvasGroup   _group;
    private RectTransform _page;
    private Image         _image;
    private Action        _release;
    private float         _scale = 1f;

    // ── Lifecycle ────────────────────────────────────────────────────
    private void Update()
    {
        _scale += PushInPerSec * Time.unscaledDeltaTime;
        if (_page != null) _page.localScale = new Vector3(_scale, _scale, 1f);
    }

    private void OnDestroy()
    {
        _release?.Invoke();
        _release = null;
        if (_instance == this) _instance = null;
    }

    // ── Public Methods ───────────────────────────────────────────────
    /// <summary>그림을 불러 페이드 인한다. 그림이 없으면(주소 없음 · 취소) 아무것도 띄우지 않고 false.</summary>
    public static async UniTask<bool> ShowAsync(string key, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(key)) return false;
        Close();
        var overlay = Create();
        try
        {
            if (!await overlay.LoadAsync(key) || overlay == null)
            {
                if (overlay != null) Destroy(overlay.gameObject);
                return false;
            }
            await overlay.FadeAsync(1f, FadeInTime, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            if (overlay != null) Destroy(overlay.gameObject);
            return false;
        }
    }

    /// <summary>페이드 아웃한 뒤 걷는다. 취소돼도 걷는다.</summary>
    public static async UniTask HideAsync(CancellationToken ct)
    {
        var overlay = _instance;
        if (overlay == null) return;
        try { await overlay.FadeAsync(0f, FadeOutTime, ct); }
        catch (OperationCanceledException) { }
        finally { if (overlay != null) Destroy(overlay.gameObject); }
    }

    /// <summary>즉시 걷는다.</summary>
    public static void Close()
    {
        if (_instance == null) return;
        Destroy(_instance.gameObject);
        _instance = null;
    }

    // ── Private Methods ──────────────────────────────────────────────
    private static StoryPageOverlay Create()
    {
        var go     = new GameObject("@StoryPage");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;

        // 프로젝트 캔버스 기준 — 1920×1080, Scale With Screen Size, Match 0.5
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        var overlay = go.AddComponent<StoryPageOverlay>();
        overlay._group = go.AddComponent<CanvasGroup>();
        overlay._group.alpha          = 0f;
        overlay._group.blocksRaycasts = false;   // 대사창 입력을 막지 않는다

        var black = new GameObject("Black", typeof(RectTransform), typeof(Image));
        black.transform.SetParent(go.transform, false);
        Stretch((RectTransform)black.transform);
        var blackImage = black.GetComponent<Image>();
        blackImage.color         = Color.black;
        blackImage.raycastTarget = false;

        var page = new GameObject("Page", typeof(RectTransform), typeof(Image));
        page.transform.SetParent(go.transform, false);
        overlay._page = (RectTransform)page.transform;
        Stretch(overlay._page);
        overlay._image = page.GetComponent<Image>();
        overlay._image.preserveAspect = true;    // 화면비가 달라도 그림이 찌그러지지 않게(남는 곳은 검은 바탕)
        overlay._image.raycastTarget  = false;
        overlay._image.color          = new Color(1f, 1f, 1f, 0f);

        _instance = overlay;
        return overlay;
    }

    /// <summary>스프라이트로 먼저, 텍스처로 들어온 PNG면 스프라이트로 감싼다(프롤로그 책과 같은 폴백).</summary>
    private async UniTask<bool> LoadAsync(string key)
    {
        var addressables = Managers.AddressableManager;
        if (addressables == null) return false;

        var sprite = await addressables.TryLoadAssetAsync<Sprite>(key);
        if (sprite != null)
        {
            _release = () => addressables.ReleaseAsset<Sprite>(key);
        }
        else
        {
            var tex = await addressables.TryLoadAssetAsync<Texture2D>(key);
            if (tex == null)
            {
                Debug.LogWarning($"[StoryPage] 그림 주소 없음: {key}");
                return false;
            }
            sprite   = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            _release = () => addressables.ReleaseAsset<Texture2D>(key);
        }
        if (this == null) { _release?.Invoke(); _release = null; return false; }   // 불러오는 사이 걷혔다
        _image.sprite = sprite;
        _image.color  = Color.white;
        return true;
    }

    private async UniTask FadeAsync(float target, float duration, CancellationToken ct)
    {
        float from = _group.alpha, t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            if (_group == null) return;
            _group.alpha = Mathf.Lerp(from, target, Mathf.Clamp01(t / duration));
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        if (_group != null) _group.alpha = target;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
