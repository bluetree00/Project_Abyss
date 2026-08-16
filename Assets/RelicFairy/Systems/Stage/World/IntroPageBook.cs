using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 인트로 프롤로그를 "책 페이지"로 보여주는 전체화면 오버레이(런타임 생성).
///
/// 검은 배경 위에 양피지 삽화를 띄우고 계속 밀고 들어간다(Ken Burns). 대사 라인이 넘어가면
/// <see cref="UI_DialoguePopup.OnLineIllustration"/>을 받아 다음 페이지로 크로스 디졸브한다.
/// 마지막에 <see cref="DissolveIntoSceneAsync"/>로 페이지가 확대되며 사라져 뒤의 3D 성당이 드러난다
/// — "페이지 안으로 들어가는" 전환.
///
/// 캔버스 정렬은 UI 팝업(200~)보다 낮아 대사창이 페이지 위에 표시된다.
/// 페이지 자체가 불투명 검정 배경을 깔므로 ScreenFade 암전과 겹칠 필요가 없다.
/// </summary>
public sealed class IntroPageBook : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────────────
    private const int   SortingOrder       = UISortingOrder.WorldProp;  // 화면 UI(팝업 400+)보다 아래
    private const float PushInPerSec       = 0.010f; // 페이지가 초당 밀려 들어오는 배율
    private const float CrossFade          = 0.7f;   // 페이지 넘김(크로스 디졸브) 시간
    private const float DissolveExtraScale = 0.40f;  // 씬으로 빨려 들어갈 때 추가 확대

    // ── Static ───────────────────────────────────────────────────────
    private static IntroPageBook _instance;

    // ── Private ──────────────────────────────────────────────────────
    private CanvasGroup   _root;
    private RectTransform _pageRoot;
    private readonly Image[] _pages = new Image[2];

    private int   _front;
    private float _scale = 1f;
    private bool  _pushIn;
    private int   _pageSeq;

    private readonly Dictionary<string, Sprite> _cache   = new();
    private readonly List<Action>               _release = new();

    // ── Lifecycle ────────────────────────────────────────────────────
    private void OnEnable()  => UI_DialoguePopup.OnLineIllustration += HandleLineIllustration;
    private void OnDisable() => UI_DialoguePopup.OnLineIllustration -= HandleLineIllustration;

    private void Update()
    {
        if (!_pushIn || _pageRoot == null) return;
        _scale += PushInPerSec * Time.unscaledDeltaTime;
        _pageRoot.localScale = new Vector3(_scale, _scale, 1f);
    }

    private void OnDestroy()
    {
        ReleaseAll();
        if (_instance == this) _instance = null;
    }

    // ── Public Methods ───────────────────────────────────────────────
    /// <summary>오버레이를 연다. 배경은 즉시 검정 — 첫 페이지가 도착할 때까지 3D 씬을 가린다.</summary>
    public static void Open()
    {
        Ensure();
        _instance._root.alpha         = 1f;
        _instance._scale              = 1f;
        _instance._pageRoot.localScale = Vector3.one;
        _instance._pushIn             = true;
    }

    /// <summary>페이지가 확대되며 사라져 뒤의 3D 씬이 드러난다. 끝나면 오버레이를 정리한다.</summary>
    public static async UniTask DissolveIntoSceneAsync(float duration, CancellationToken ct)
    {
        if (_instance == null) return;
        await _instance.DissolveAsync(duration, ct);
    }

    /// <summary>오버레이 즉시 정리(연출 취소 등).</summary>
    public static void Close()
    {
        if (_instance == null) return;
        Destroy(_instance.gameObject);
        _instance = null;
    }

    // ── Private Methods ──────────────────────────────────────────────
    private static void Ensure()
    {
        if (_instance != null) return;

        var go     = new GameObject("@IntroPageBook");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;

        // 프로젝트 캔버스 기준 준수 — 1920×1080, Scale With Screen Size, Match 0.5
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        var cg = go.AddComponent<CanvasGroup>();
        cg.alpha          = 0f;
        cg.blocksRaycasts = false;
        cg.interactable   = false;

        var book = go.AddComponent<IntroPageBook>();
        book._root = cg;

        var backdrop = CreateStretchedImage(go.transform, "Backdrop");
        backdrop.color = Color.black;

        var pageRootGO = new GameObject("Pages", typeof(RectTransform));
        pageRootGO.transform.SetParent(go.transform, false);
        var prt = (RectTransform)pageRootGO.transform;
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
        book._pageRoot = prt;

        for (int i = 0; i < 2; i++)
        {
            var img = CreateStretchedImage(prt, $"Page{i}");
            img.color          = new Color(1f, 1f, 1f, 0f);
            img.preserveAspect = true;
            book._pages[i]     = img;
        }

        _instance = book;
    }

    private static Image CreateStretchedImage(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var img = go.AddComponent<Image>();
        img.raycastTarget = false;

        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return img;
    }

    /// <summary>새 페이지를 앞면으로 크로스 디졸브. 매 페이지마다 푸시인을 처음부터 다시 시작한다.</summary>
    private async UniTaskVoid TurnPageAsync(string key, int seq, CancellationToken ct)
    {
        var sprite = await LoadSpriteAsync(key);
        if (sprite == null || seq != _pageSeq) return;

        int back = 1 - _front;
        _pages[back].sprite = sprite;
        _pages[back].color  = new Color(1f, 1f, 1f, 0f);
        _pages[back].transform.SetAsLastSibling();

        _scale = 1f;
        _pageRoot.localScale = Vector3.one;

        float t = 0f;
        try
        {
            while (t < CrossFade)
            {
                if (seq != _pageSeq) return;
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / CrossFade);
                _pages[back].color   = new Color(1f, 1f, 1f, k);
                _pages[_front].color = new Color(1f, 1f, 1f, 1f - k);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { return; }

        _pages[back].color   = Color.white;
        _pages[_front].color = new Color(1f, 1f, 1f, 0f);
        _front = back;
    }

    private async UniTask DissolveAsync(float duration, CancellationToken ct)
    {
        _pushIn = false;

        float startScale = _scale;
        float t          = 0f;
        try
        {
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
                float s = startScale + DissolveExtraScale * k;
                _pageRoot.localScale = new Vector3(s, s, 1f);
                _root.alpha          = 1f - k;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }

        Close();
    }

    private async UniTask<Sprite> LoadSpriteAsync(string key)
    {
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var addressables = Managers.AddressableManager;
        if (addressables == null) return null;

        var sprite = await addressables.TryLoadAssetAsync<Sprite>(key);
        if (sprite != null)
        {
            _cache[key] = sprite;
            _release.Add(() => addressables.ReleaseAsset<Sprite>(key));
            return sprite;
        }

        // PNG가 Texture2D 주 타입으로 등록된 경우 폴백
        var tex = await addressables.TryLoadAssetAsync<Texture2D>(key);
        if (tex == null)
        {
            Debug.LogWarning($"[IntroPageBook] 페이지 삽화 키 없음: {key}");
            return null;
        }

        sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        _cache[key] = sprite;
        _release.Add(() => addressables.ReleaseAsset<Texture2D>(key));
        return sprite;
    }

    private void ReleaseAll()
    {
        foreach (var release in _release)
            release();
        _release.Clear();
        _cache.Clear();
    }

    // ── Event Handlers ───────────────────────────────────────────────
    private void HandleLineIllustration(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        TurnPageAsync(key, ++_pageSeq, this.GetCancellationTokenOnDestroy()).Forget();
    }
}
