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

    // ── 로딩바(09-27 개편) ── 옛 흰 띠·기본 슬라이더·영문 문구 대신 코드로 짓는 바
    private const float BarW = 760f, BarH = 6f, BarY = 118f;
    private const float ShineSec = 1.6f;
    private static readonly Color BarTrack = new(0.07f, 0.06f, 0.09f, 0.92f);
    private static readonly Color BarEdge  = new(0.62f, 0.50f, 0.31f, 0.85f);
    private static readonly Color BarFill  = new(0.94f, 0.77f, 0.43f, 1f);
    private static readonly Color BarInk   = new(0.93f, 0.90f, 0.83f, 0.95f);
    private static Sprite s_fade;
    private RectTransform _bar;
    private Image    _fill, _head, _shine;
    private RawImage _sigil;
    private TMP_Text _label, _pct;
    private float    _progress;

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

        BuildStyledBar();
        gameObject.SetActive(false);
    }

    /// <summary>열려 있는 동안 머리 맥박 · 광택 · 마법진 회전(할당 없음, 실시간).</summary>
    private void Update()
    {
        if (!_shown || _bar == null) return;
        float now = Time.unscaledTime;
        if (_head != null)
        {
            var c = _head.color; c.a = 0.55f + 0.35f * (0.5f + 0.5f * Mathf.Sin(now * 5f)); _head.color = c;
        }
        if (_shine != null)
        {
            float filled = BarW * _progress;
            float k = (now % ShineSec) / ShineSec;
            _shine.enabled = filled > 40f;
            _shine.rectTransform.anchoredPosition = new Vector2(-BarW * 0.5f + filled * k, 0f);
        }
        if (_sigil != null) _sigil.rectTransform.localEulerAngles = new Vector3(0f, 0f, -now * 25f);
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
        if (_bar != null) _bar.gameObject.SetActive(true);   // 걷힐 때 먼저 껐다(HideAsync) — 다시 켠다

        // 레이아웃 리빌드가 끝난 뒤 Prepare 시작해 같은 프레임 작업 분산
        await UniTask.Yield(PlayerLoopTiming.Update);

        SetProgress(0f);
        // 마법진 문양은 방 전환 스킨이 갖는다 — 첫 로딩(부트 직후)엔 아직 안 왔을 수 있어 열 때마다 확인한다.
        if (_sigil != null && _sigil.texture == null)
        {
            var sig = UISkin.ScreenWipe != null ? UISkin.ScreenWipe.Sigil : null;
            _sigil.texture = sig;
            _sigil.enabled = sig != null;
        }

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

        // 막대·글자는 걷히기 전에 먼저 끈다 — 어둠이 걷히는 0.5초 동안 「100%」 막대가 드러난 HUD 체력바에 겹쳤다(09-28 실측).
        if (_bar != null) _bar.gameObject.SetActive(false);
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

        _progress = clamped;
        if (_fill != null) _fill.fillAmount = clamped;
        if (_head != null) _head.rectTransform.anchoredPosition = new Vector2(-BarW * 0.5f + BarW * clamped, 0f);
        if (_pct  != null) _pct.text = $"<mspace=0.62em>{Mathf.RoundToInt(clamped * 100)}</mspace>%";
    }

    // -------------------------------------------------------------------------
    // Internal
    // -------------------------------------------------------------------------

    /// <summary>
    /// 새 로딩바를 짓는다(한 번). 옛 흰 띠(Progress)·기본 슬라이더·영문 문구는 감춘다 — 프리팹은 그대로 두고 코드가 덮는다.
    /// </summary>
    private void BuildStyledBar()
    {
        if (_bar != null) return;
        var band = transform.Find("Progress");
        if (band != null && band.TryGetComponent<Image>(out var bandImg)) bandImg.enabled = false;
        if (progressBar != null) progressBar.gameObject.SetActive(false);
        if (percentText != null) percentText.gameObject.SetActive(false);

        // 바닥 음영 — 영상 위에 글자·바가 뜨게. 흰 띠 대신 아래에서 위로 옅어지는 어둠.
        var shade = NewImage("LoadingShade", transform, new Color(0f, 0f, 0f, 0.78f));
        var srt = shade.rectTransform;
        srt.anchorMin = new Vector2(0f, 0f); srt.anchorMax = new Vector2(1f, 0f); srt.pivot = new Vector2(0.5f, 0f);
        srt.anchoredPosition = Vector2.zero; srt.sizeDelta = new Vector2(0f, 320f);
        shade.sprite = VerticalFade();

        _bar = (RectTransform)new GameObject("LoadingBar", typeof(RectTransform)).transform;
        _bar.SetParent(transform, false);
        _bar.anchorMin = _bar.anchorMax = new Vector2(0.5f, 0f);
        _bar.pivot = new Vector2(0.5f, 0.5f);
        _bar.anchoredPosition = new Vector2(0f, BarY);
        _bar.sizeDelta = new Vector2(BarW, BarH);

        var rounded = UIProceduralSprites.RoundedRect(radius: 4f, feather: 1.5f, size: 24);
        var edge = NewImage("Edge", _bar, BarEdge);
        Stretch(edge.rectTransform, -1.5f);
        edge.sprite = rounded; edge.type = Image.Type.Sliced;
        var track = NewImage("Track", _bar, BarTrack);
        Stretch(track.rectTransform, 0f);
        track.sprite = rounded; track.type = Image.Type.Sliced;

        _fill = NewImage("Fill", _bar, BarFill);
        Stretch(_fill.rectTransform, 0.5f);
        _fill.type = Image.Type.Filled;
        _fill.fillMethod = Image.FillMethod.Horizontal;
        _fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        _fill.fillAmount = 0f;

        var dot = UI_RuneSelectPopup.SoftDot;
        _shine = NewImage("Shine", _bar, new Color(1f, 0.97f, 0.88f, 0.45f));
        _shine.sprite = dot;
        var shrt = _shine.rectTransform;
        shrt.anchorMin = shrt.anchorMax = new Vector2(0.5f, 0.5f);
        shrt.sizeDelta = new Vector2(90f, 12f);

        _head = NewImage("Head", _bar, new Color(BarFill.r, BarFill.g, BarFill.b, 0.8f));
        _head.sprite = dot;
        var hrt = _head.rectTransform;
        hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.5f);
        hrt.sizeDelta = new Vector2(44f, 44f);
        hrt.anchoredPosition = new Vector2(-BarW * 0.5f, 0f);

        var sigGo = new GameObject("Sigil", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        sigGo.transform.SetParent(_bar, false);
        _sigil = sigGo.GetComponent<RawImage>();
        _sigil.color = new Color(BarFill.r, BarFill.g, BarFill.b, 0.85f);
        _sigil.raycastTarget = false;
        var sirt = _sigil.rectTransform;
        sirt.anchorMin = sirt.anchorMax = new Vector2(0f, 0.5f);
        sirt.sizeDelta = new Vector2(46f, 46f);
        sirt.anchoredPosition = new Vector2(-34f, 0f);
        _sigil.enabled = false;

        _label = NewText("Label", _bar, 20f, TextAlignmentOptions.BottomLeft, BarInk);
        var lrt = _label.rectTransform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 1f); lrt.pivot = new Vector2(0f, 0f);
        lrt.anchoredPosition = new Vector2(0f, 10f); lrt.sizeDelta = new Vector2(400f, 28f);
        _label.characterSpacing = 3f;
        _label.text = "불러오는 중";

        _pct = NewText("Percent", _bar, 20f, TextAlignmentOptions.BottomRight, BarFill);
        var prt = _pct.rectTransform;
        prt.anchorMin = prt.anchorMax = new Vector2(1f, 1f); prt.pivot = new Vector2(1f, 0f);
        prt.anchoredPosition = new Vector2(0f, 10f); prt.sizeDelta = new Vector2(160f, 28f);
        _pct.text = "<mspace=0.62em>0</mspace>%";
    }

    private static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static TMP_Text NewText(string name, Transform parent, float size, TextAlignmentOptions align, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.fontStyle = FontStyles.Normal;
        t.alignment = align;
        t.color = color;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        TMPOutlineHelper.ApplySoftShadow(t);
        return t;
    }

    private static void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
    }

    /// <summary>아래가 짙고 위로 투명해지는 세로 음영(1×64, 한 번 구워 공유).</summary>
    private static Sprite VerticalFade()
    {
        if (s_fade != null) return s_fade;
        var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < 64; y++)
        {
            float a = 1f - y / 63f;
            tex.SetPixel(0, y, new Color(1f, 1f, 1f, a * a));
        }
        tex.Apply();
        s_fade = Sprite.Create(tex, new Rect(0, 0, 1, 64), new Vector2(0.5f, 0.5f), 100f);
        s_fade.hideFlags = HideFlags.HideAndDontSave;
        return s_fade;
    }

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
