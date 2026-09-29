using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>노드 이름표를 어디에 붙이나(겹침을 피해 뷰가 고른다).</summary>
public enum AltarLabelSide { Below, Above, Left, Right }

/// <summary>
/// 기억의 제단 트리의 노드 한 개 — 둥근 문양 + 금 테 + 속 표식 + 이름표 + 값 칩(09-29 개편).
/// <para>상태는 색과 모양으로만 말한다: 산 = 갈래 색으로 차고 금 테 · 살 수 있음 = 금 테 + 숨 쉬는 불 ·
/// 열림 = 잉크 테 · 한 칸 앞 = 흐린 테 · 가려짐 = 흐린 점. 연출은 각인(글자 고리가 스스로 그려짐) → 점화 → 개화.</para>
/// </summary>
[DisallowMultipleComponent]
public sealed class AltarTreeNodeView : MonoBehaviour, ISelectHandler, IPointerEnterHandler, IPointerExitHandler
{
    // ── Constants ────────────────────────────────────────
    private const float BreathPeriod = 1.8f;
    private const float LabelGap     = 6f;
    private const float ChipH        = 22f;
    private static readonly Color Dark      = new(0.11f, 0.10f, 0.15f, 1f);
    private static readonly Color Darker    = new(0.08f, 0.075f, 0.11f, 1f);
    private static readonly Color NextRim   = new(0.34f, 0.32f, 0.40f, 1f);
    private static readonly Color NextInk   = new(0.56f, 0.54f, 0.61f, 1f);
    private static readonly Color HiddenDot = new(0.40f, 0.38f, 0.48f, 0.55f);

    // ── Static ───────────────────────────────────────────
    private static Sprite s_glyphRing;

    // ── Private ──────────────────────────────────────────
    private RectTransform _rt;
    private Button   _button;
    private Image    _glow, _focus, _glyph, _base, _rim, _mark, _chipBg;
    private TMP_Text _label, _chip;
    private CanvasGroup _group;
    private Color    _branch;
    private float    _diameter;
    private bool     _focused, _hover;
    private AltarNodeVisual _visual = AltarNodeVisual.Hidden;
    private bool     _animating;   // 각인·개화 중엔 Apply가 모양을 덮지 않는다

    // ── Properties ───────────────────────────────────────
    public MemoryAltarNode Node     { get; private set; }
    public RectTransform   Rect     => _rt;
    public AltarNodeVisual Visual   => _visual;
    public float           Diameter => _diameter;
    public Button          Button   => _button;
    public TMP_Text        Label    => _label;

    public event Action<AltarTreeNodeView>       Focused;
    public event Action<AltarTreeNodeView>       Clicked;
    public event Action<AltarTreeNodeView, bool> HoverChanged;

    // ── Lifecycle ────────────────────────────────────────
    private void Update()
    {
        float t = Time.unscaledTime;
        if (_glow != null && _glow.enabled && !_animating)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * (Mathf.PI * 2f / BreathPeriod));
            var c = UITheme.Gold; c.a = Mathf.Lerp(0.14f, 0.36f, pulse);
            _glow.color = c;
        }
        if (_focus != null && _focus.enabled)
        {
            float k = 1f + 0.035f * Mathf.Sin(t * 5f);
            _focus.rectTransform.localScale = new Vector3(k, k, 1f);
        }
        if (_glyph != null && _glyph.enabled && !_animating && _visual == AltarNodeVisual.Bought)
            _glyph.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -t * 6f);   // 산 열쇠 노드 — 글자 고리가 천천히 돈다
    }

    // ── Public Methods ───────────────────────────────────

    public static AltarTreeNodeView Create(Transform parent, MemoryAltarNode node, float diameter, Color branch)
    {
        var go = new GameObject($"Node_{node.Id}", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var v = go.AddComponent<AltarTreeNodeView>();
        v.Build(node, diameter, branch);
        return v;
    }

    /// <summary>화면 값을 입힌다. <paramref name="instant"/>가 아니면 색만 바꾸고 등장은 연출이 맡는다.</summary>
    public void Apply(in AltarNodeViewModel m, bool instant)
    {
        _visual = m.Visual;
        if (_animating) return;
        Paint(m.Visual);
        _chip.text = AltarTreePresenter.CostChip(m);
        bool chip = _chip.text.Length > 0;
        _chip.gameObject.SetActive(chip);
        _chipBg.enabled = chip;
        if (chip) _chipBg.rectTransform.sizeDelta = new Vector2(_chip.GetPreferredValues(_chip.text).x + 16f, ChipH);
        if (instant) SetAlpha(1f);
    }

    public void SetFocused(bool on)
    {
        _focused = on;
        if (_focus != null) _focus.enabled = on && _visual != AltarNodeVisual.Hidden;
        RefreshScale();
    }

    /// <summary>이름표 자리 — 뷰가 겹침을 피해 고른다.</summary>
    public void PlaceLabel(AltarLabelSide side)
    {
        var lrt = _label.rectTransform;
        float r = _diameter * 0.5f;
        float below = r + (_chip.gameObject.activeSelf ? ChipH * 0.5f + 2f : LabelGap);
        switch (side)
        {
            case AltarLabelSide.Below:
                lrt.pivot = new Vector2(0.5f, 1f); lrt.anchoredPosition = new Vector2(0f, -below);
                _label.alignment = TextAlignmentOptions.Top; break;
            case AltarLabelSide.Above:
                lrt.pivot = new Vector2(0.5f, 0f); lrt.anchoredPosition = new Vector2(0f, r + LabelGap);
                _label.alignment = TextAlignmentOptions.Bottom; break;
            case AltarLabelSide.Left:
                lrt.pivot = new Vector2(1f, 0.5f); lrt.anchoredPosition = new Vector2(-r - LabelGap, 0f);
                _label.alignment = TextAlignmentOptions.MidlineRight; break;
            default:
                lrt.pivot = new Vector2(0f, 0.5f); lrt.anchoredPosition = new Vector2(r + LabelGap, 0f);
                _label.alignment = TextAlignmentOptions.MidlineLeft; break;
        }
    }

    /// <summary>이름표가 차지할 사각형(노드 중심 기준) — 뷰의 겹침 검사용.</summary>
    public Rect LabelRectFor(AltarLabelSide side)
    {
        float w = _label.GetPreferredValues(_label.text, 400f, 40f).x;
        float h = 22f;
        float r = _diameter * 0.5f;
        float below = r + (_chip.gameObject.activeSelf ? ChipH * 0.5f + 2f : LabelGap);
        return side switch
        {
            AltarLabelSide.Below => new Rect(-w * 0.5f, -below - h, w, h),
            AltarLabelSide.Above => new Rect(-w * 0.5f, r + LabelGap, w, h),
            AltarLabelSide.Left  => new Rect(-r - LabelGap - w, -h * 0.5f, w, h),
            _                    => new Rect(r + LabelGap, -h * 0.5f, w, h),
        };
    }

    /// <summary>노드 · 이름표 알파(이름표는 이름표 층에 따로 있어 같이 맞춘다).</summary>
    public void SetAlpha(float a) { _group.alpha = a; if (_label != null) _label.alpha = a; }

    /// <summary>각인 → 점화. 글자 고리가 조여들며 스스로 그려지고, 문양이 갈래 색으로 차오르며 파문이 한 번 번진다.</summary>
    public async UniTask PlayEngraveIgniteAsync(AltarNodeVisual from, bool keystone, RectTransform fxRoot, CancellationToken ct)
    {
        _animating = true;
        try
        {
            Paint(from);   // 해금 전 모습에서 시작한다 — 화면 값은 이미 「산 노드」로 바뀌어 있다
            _glow.enabled = false;
            SetAlpha(1f);
            _glyph.enabled = true;
            _glyph.fillAmount = 0f;
            _glyph.color = UITheme.Gold;
            var grt = _glyph.rectTransform;

            const float Engrave = 0.22f;
            for (float t = 0f; t < Engrave; t += Time.unscaledDeltaTime)
            {
                float k = Ease(t / Engrave);
                _glyph.fillAmount = k;
                grt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-120f, 0f, k));
                float s = Mathf.Lerp(1.3f, 1f, k);
                grt.localScale = new Vector3(s, s, 1f);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            _glyph.fillAmount = 1f; grt.localScale = Vector3.one;

            // 점화 — 채움이 갈래 색으로, 테는 금으로
            Color fromCol = _base.color, toCol = BoughtFill();
            RippleAsync(fxRoot, 1.9f, 0.35f, 0.6f, ct).Forget();
            if (keystone) DelayedRippleAsync(fxRoot, ct).Forget();
            SparksAsync(fxRoot, keystone ? 10 : 6, ct).Forget();
            const float Ignite = 0.18f;
            for (float t = 0f; t < Ignite; t += Time.unscaledDeltaTime)
            {
                float k = Ease(t / Ignite);
                _base.color = Color.Lerp(fromCol, toCol, k);
                _rim.color  = Color.Lerp(_rim.color, UITheme.Gold, k);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        finally
        {
            _animating = false;
            if (this != null) Paint(_visual);
        }
    }

    /// <summary>개화 — 흐린 점이 문양으로 돋아난다(0.7 → 1). 글자 고리가 한 번 그려졌다 걷힌다.</summary>
    public async UniTask PlayBloomAsync(CancellationToken ct)
    {
        _animating = true;
        try
        {
            Paint(_visual);
            _glyph.enabled = true; _glyph.color = new Color(1f, 0.86f, 0.55f, 0.8f); _glyph.fillAmount = 0f;
            const float Dur = 0.3f;
            for (float t = 0f; t < Dur; t += Time.unscaledDeltaTime)
            {
                float k = Ease(t / Dur);
                float s = Mathf.Lerp(0.7f, 1f, k);
                _rt.localScale = new Vector3(s, s, 1f);
                SetAlpha(k);
                _glyph.fillAmount = k;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            SetAlpha(1f);
            RefreshScale();
            for (float t = 0f; t < 0.25f; t += Time.unscaledDeltaTime)
            {
                var c = _glyph.color; c.a = Mathf.Lerp(0.8f, 0f, t / 0.25f); _glyph.color = c;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        finally
        {
            _animating = false;
            if (this != null) Paint(_visual);
        }
    }

    // ── Private Methods ──────────────────────────────────

    private void Build(MemoryAltarNode node, float diameter, Color branch)
    {
        Node = node; _branch = branch; _diameter = diameter;
        _rt = (RectTransform)transform;
        _rt.sizeDelta = new Vector2(diameter, diameter);
        _group = gameObject.AddComponent<CanvasGroup>();

        _glow  = MakeImage("Glow",  UI_RuneSelectPopup.SoftDot, diameter * 2.2f, UITheme.Gold);
        _focus = MakeImage("Focus", UIProceduralSprites.Ring(0.06f, 128), diameter + 18f, new Color(1f, 0.93f, 0.75f, 0.9f));
        _glyph = MakeImage("Glyph", GlyphRing(), diameter * 1.42f, UITheme.Gold);
        _glyph.type = Image.Type.Filled; _glyph.fillMethod = Image.FillMethod.Radial360; _glyph.fillOrigin = 2;
        _base  = MakeImage("Base",  UIProceduralSprites.Circle(128), diameter, Dark);
        _base.raycastTarget = true;
        _rim   = MakeImage("Rim",   UIProceduralSprites.Ring(node.Size == AltarNodeSize.Keystone ? 0.07f : 0.09f, 128), diameter, UITheme.Gold);
        _mark  = MakeImage("Mark",  node.Size == AltarNodeSize.Keystone ? UIProceduralSprites.RoundedRect(3f, 1.5f, 32) : UIProceduralSprites.Circle(64),
                           diameter * (node.Size == AltarNodeSize.Keystone ? 0.30f : node.Size == AltarNodeSize.Small ? 0.2f : 0.24f), branch);
        _mark.type = node.Size == AltarNodeSize.Keystone ? Image.Type.Sliced : Image.Type.Simple;
        if (node.Size == AltarNodeSize.Keystone) _mark.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);   // 마름모

        // 값 칩 — 문양 아래 테에 걸친 짙은 알약 + 글자. 판은 글자보다 먼저 만든다(먼저 그려져 글자 뒤).
        _chipBg = MakeImage("ChipBg", UIProceduralSprites.RoundedRect(9f, 1.5f), 10f, new Color(0.06f, 0.05f, 0.09f, 0.92f));
        _chipBg.type = Image.Type.Sliced;
        _chipBg.rectTransform.anchoredPosition = new Vector2(0f, -diameter * 0.5f);
        _chip = MakeText("Chip", 16f, FontStyles.Bold, TextAlignmentOptions.Center);
        _chip.rectTransform.sizeDelta = new Vector2(160f, ChipH);
        _chip.rectTransform.anchoredPosition = new Vector2(0f, -diameter * 0.5f);
        _chip.textWrappingMode = TextWrappingModes.NoWrap;

        _label = MakeText("Label", 16f, FontStyles.Bold, TextAlignmentOptions.Top);
        _label.text = node.DisplayName;
        _label.rectTransform.sizeDelta = new Vector2(320f, 22f);
        _label.textWrappingMode = TextWrappingModes.NoWrap;

        _button = gameObject.AddComponent<Button>();
        _button.targetGraphic = _base;
        _button.transition = Selectable.Transition.None;
        _button.onClick.AddListener(() => Clicked?.Invoke(this));
        var nav = _button.navigation; nav.mode = Navigation.Mode.Automatic; _button.navigation = nav;

        _glyph.enabled = false; _focus.enabled = false; _glow.enabled = false;
    }

    private void Paint(AltarNodeVisual v)
    {
        bool hidden = v == AltarNodeVisual.Hidden;
        _rim.enabled  = !hidden;
        _mark.enabled = !hidden;
        _label.gameObject.SetActive(!hidden);
        _glow.enabled = v == AltarNodeVisual.Affordable;
        _glyph.enabled = v == AltarNodeVisual.Bought && Node.Size == AltarNodeSize.Keystone;
        if (_glyph.enabled) { _glyph.fillAmount = 1f; _glyph.color = new Color(UITheme.Gold.r, UITheme.Gold.g, UITheme.Gold.b, 0.35f); }
        if (_focus != null) _focus.enabled = _focused && !hidden;
        _button.interactable = !hidden;
        _base.raycastTarget  = !hidden;

        float size = hidden ? 12f : _diameter;
        _base.rectTransform.sizeDelta = new Vector2(size, size);

        switch (v)
        {
            case AltarNodeVisual.Bought:
                _base.color = BoughtFill(); _rim.color = UITheme.Gold; _mark.color = UITheme.Ink; _label.color = UITheme.Ink; break;
            case AltarNodeVisual.Affordable:
                _base.color = Dark; _rim.color = UITheme.Gold; _mark.color = _branch; _label.color = UITheme.Gold; break;
            case AltarNodeVisual.Open:
                _base.color = Dark; _rim.color = new Color(UITheme.Ink.r, UITheme.Ink.g, UITheme.Ink.b, 0.8f); _mark.color = _branch; _label.color = UITheme.Ink; break;
            case AltarNodeVisual.OpenLocked:
                _base.color = Dark; _rim.color = UITheme.Mute; _mark.color = new Color(_branch.r, _branch.g, _branch.b, 0.6f); _label.color = UITheme.Mute; break;
            case AltarNodeVisual.Next:
                _base.color = Darker; _rim.color = NextRim; _mark.color = new Color(_branch.r * 0.6f, _branch.g * 0.6f, _branch.b * 0.6f, 0.8f); _label.color = NextInk; break;
            default:
                _base.color = HiddenDot; break;
        }
        _chip.color = v == AltarNodeVisual.Affordable ? UITheme.Gold : UITheme.Mute;
        _chipBg.enabled = _chip.gameObject.activeSelf && !hidden;
        RefreshScale();
    }

    private void RefreshScale()
    {
        if (_animating) return;
        float s = _hover && _visual != AltarNodeVisual.Hidden ? 1.06f : 1f;
        _rt.localScale = new Vector3(s, s, 1f);
    }

    private Color BoughtFill() => Color.Lerp(Dark, _branch, 0.62f);

    private async UniTaskVoid RippleAsync(RectTransform fxRoot, float toScale, float dur, float alpha, CancellationToken ct)
    {
        var img = MakeFx(fxRoot, UIProceduralSprites.Ring(0.05f, 128), _diameter, new Color(1f, 0.86f, 0.5f, alpha));
        try
        {
            for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
            {
                float k = Ease(t / dur);
                float s = Mathf.Lerp(1f, toScale, k);
                img.rectTransform.localScale = new Vector3(s, s, 1f);
                var c = img.color; c.a = alpha * (1f - k); img.color = c;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally { if (img != null) Destroy(img.gameObject); }
    }

    private async UniTaskVoid DelayedRippleAsync(RectTransform fxRoot, CancellationToken ct)
    {
        try
        {
            await UniTask.Delay(110, DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, ct);
            RippleAsync(fxRoot, 2.6f, 0.5f, 0.45f, ct).Forget();
        }
        catch (OperationCanceledException) { }
    }

    private async UniTaskVoid SparksAsync(RectTransform fxRoot, int count, CancellationToken ct)
    {
        var sparks = new Image[count];
        var dirs   = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            float a = (i / (float)count) * Mathf.PI * 2f + UnityEngine.Random.Range(-0.2f, 0.2f);
            dirs[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            sparks[i] = MakeFx(fxRoot, UI_RuneSelectPopup.SoftDot, 12f, new Color(1f, 0.9f, 0.6f, 0.9f));
        }
        try
        {
            const float Dur = 0.38f;
            float reach = _diameter * 0.5f + 34f;
            for (float t = 0f; t < Dur; t += Time.unscaledDeltaTime)
            {
                float k = Ease(t / Dur);
                for (int i = 0; i < count; i++)
                {
                    sparks[i].rectTransform.anchoredPosition = FxCenter(fxRoot) + dirs[i] * Mathf.Lerp(_diameter * 0.35f, reach, k);
                    var c = sparks[i].color; c.a = 0.9f * (1f - k); sparks[i].color = c;
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally { foreach (var s in sparks) if (s != null) Destroy(s.gameObject); }
    }

    private Vector2 FxCenter(RectTransform fxRoot) => (Vector2)fxRoot.InverseTransformPoint(_rt.position);

    private Image MakeFx(RectTransform fxRoot, Sprite sprite, float size, Color color)
    {
        var img = new GameObject("Fx", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
        img.transform.SetParent(fxRoot, false);
        img.sprite = sprite; img.color = color; img.raycastTarget = false;
        img.rectTransform.sizeDelta = new Vector2(size, size);
        img.rectTransform.anchoredPosition = FxCenter(fxRoot);
        return img;
    }

    private Image MakeImage(string name, Sprite sprite, float size, Color color)
    {
        var img = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
        img.transform.SetParent(transform, false);
        img.sprite = sprite; img.color = color; img.raycastTarget = false;
        img.rectTransform.sizeDelta = new Vector2(size, size);
        return img;
    }

    private TMP_Text MakeText(string name, float size, FontStyles style, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var t = go.AddComponent<TextMeshProUGUI>();   // transform 캐시는 AddComponent 뒤(가짜 null 함정)
        t.fontSize = size; t.fontStyle = style; t.alignment = align; t.raycastTarget = false;
        t.richText = true;
        TMPOutlineHelper.ApplySoftShadow(t);
        return t;
    }

    private static float Ease(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }

    /// <summary>각인 고리 — 가는 원 + 바깥쪽 눈금 12개(마법진의 글자 자리). 한 번 구워 공유.</summary>
    internal static Sprite GlyphRing()
    {
        if (s_glyphRing != null) return s_glyphRing;
        const int N = 128;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color32[N * N];
        float c = N * 0.5f;
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float dx = x + 0.5f - c, dy = y + 0.5f - c;
            float r = Mathf.Sqrt(dx * dx + dy * dy) / c;          // 0~1
            float ang = Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) * 12f;
            float frac = Mathf.Abs(ang - Mathf.Round(ang));        // 눈금에서의 거리(0 = 눈금 위)
            float ring = 1f - Mathf.Clamp01(Mathf.Abs(r - 0.80f) / 0.025f);
            float tick = (r > 0.84f && r < 0.95f) ? 1f - Mathf.Clamp01(frac / 0.05f) : 0f;
            float dotRing = (r > 0.90f && r < 0.93f) ? 0.35f : 0f;
            float a = Mathf.Clamp01(Mathf.Max(ring, Mathf.Max(tick, dotRing)));
            px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        tex.SetPixels32(px); tex.Apply();
        s_glyphRing = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        s_glyphRing.hideFlags = HideFlags.HideAndDontSave;
        return s_glyphRing;
    }

    // ── Event Handlers ───────────────────────────────────
    public void OnSelect(BaseEventData eventData) => Focused?.Invoke(this);

    public void OnPointerEnter(PointerEventData eventData)
    {
        _hover = true; RefreshScale(); HoverChanged?.Invoke(this, true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _hover = false; RefreshScale(); HoverChanged?.Invoke(this, false);
    }
}
