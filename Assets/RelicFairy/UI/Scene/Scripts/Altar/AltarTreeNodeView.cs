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
/// <para><b>10-11 위계(v2)</b> — 1차 구분은 <b>채도</b>다: 갈래 색은 산 노드만 그대로, 나머지 표식은 채도 30% 이하.
/// 이름표 · 값 칩은 산 것 · 살 수 있음 · 초점 노드만 또렷하게(나머지 알파 0.45). 테 2px(작은 노드 1.5) · 계속 움직이는 것은
/// 「살 수 있음」 숨쉬기 하나(초점 고리 맥동 · 열쇠 고리 회전은 걷었다). 글자는 Noto 500 15.</para>
/// <para><b>10-13 Artsystack</b> — 고리 = 팩 그림(작음 · 보통 icon_slot_active · 열쇠 skill_slot), 속 표식 ◆ · 점 = 노드 아이콘(56%),
/// 배지 = 조건 잠김 자물쇠(오른쪽 아래) · 다음 목표 별(오른쪽 위). 그림은 업적 스킨의 제단 칸 — 비면 그 칸만 지금 코드 모양.</para>
/// </summary>
[DisallowMultipleComponent]
public sealed class AltarTreeNodeView : MonoBehaviour, ISelectHandler, IPointerEnterHandler, IPointerExitHandler
{
    // ── Constants ────────────────────────────────────────
    private const float BreathPeriod = 1.8f;
    private const float LabelGap     = 6f;
    private const float ChipH        = 22f;
    private const float LabelSize    = 15f;    // 이름표 · 값 칩 — Noto 500 15(v2 라벨 단계)
    private const float DimAlpha     = 0.45f;  // 열림 · 조건 잠김 · 한 칸 앞 이름표(초점이면 1)
    private const float EmphSec      = 0.15f;  // 이름표가 또렷해지거나 흐려지는 시간(실시간)
    private const float FocusPx      = 2f;     // 초점 고리 — 고른 것 = 2px
    private const float GlowInner    = 0.74f;  // 후광 판에서 노드 테가 앉는 반지름 비 → 테 밖 약 8~12px
    private const float IconFrac     = 0.56f;  // 노드 아이콘 = 지름의 56%(25 · 31 · 40)
    private const float KeyInset     = 0.82f;  // 열쇠 고리(skill_slot) 눈금 안쪽 반지름 비 0.83 — 판을 여기까지 줄인다
    private const float BadgeFrac    = 0.30f;  // 배지 = 지름의 30%(16~22)
    private static readonly Color Dark       = new(0.11f, 0.10f, 0.15f, 1f);
    private static readonly Color Darker     = new(0.08f, 0.075f, 0.11f, 1f);
    private static readonly Color NextRim    = new(0.34f, 0.32f, 0.40f, 1f);
    private static readonly Color HiddenDot  = new(0.40f, 0.38f, 0.48f, 0.55f);
    private static readonly Color BoughtMark = new(0.08f, 0.075f, 0.11f, 0.85f);   // 갈래 색으로 찬 판 위 — 어두운 표식

    // ── Static ───────────────────────────────────────────
    private static Sprite s_glyphRing, s_glowDisc;

    // ── Private ──────────────────────────────────────────
    private RectTransform _rt;
    private Button   _button;
    private Image    _glow, _focus, _glyph, _base, _rim, _mark, _chipBg;
    private Image    _icon, _lockBadge, _goalBadge;   // 팩 그림(10-13) — 그림이 없으면 꺼진 채
    private TMP_Text _label, _chip;
    private CanvasGroup _group;
    private Color    _branch;
    private float    _diameter;
    private bool     _focused, _hover;
    private AltarNodeVisual _visual = AltarNodeVisual.Hidden;
    private AltarNodeVisual _painted = AltarNodeVisual.Hidden;   // 마지막으로 칠한 모습(연출 중엔 _visual과 다를 수 있다)
    private bool     _animating;   // 각인·개화 중엔 Apply가 모양을 덮지 않는다
    private float    _labelEmph = -1f, _labelEmphTarget = 1f;   // 이름표 · 값 칩 또렷함(-1 = 아직 안 칠함 → 첫 칠에 바로 맞춘다)
    private float    _groupAlpha = 1f;                          // 노드 알파(이름표는 이름표 층에 따로 있어 곱해 준다)
    private float    _baseScale = 1f;   // 열쇠 고리 그림이 있으면 판을 눈금 안쪽까지 줄인다
    private bool     _artDone, _goal;

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
        // 화면에서 계속 움직이는 것은 이 숨쉬기 하나다(10-11 v2) — 초점 고리 맥동 · 열쇠 글자 고리 회전은 걷었다.
        if (_glow != null && _glow.enabled && !_animating)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (Mathf.PI * 2f / BreathPeriod));
            var c = UITheme.Gold; c.a = Mathf.Lerp(0.14f, 0.36f, pulse);
            _glow.color = c;
        }
        // 이름표가 또렷해지거나 흐려지는 순간 — 상태가 바뀔 때만 0.15초 돈다
        if (_labelEmph >= 0f && _labelEmph != _labelEmphTarget)
        {
            _labelEmph = Mathf.MoveTowards(_labelEmph, _labelEmphTarget, Time.unscaledDeltaTime / EmphSec);
            ApplyEmphasis();
        }
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
        EnsureArt();
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
        _labelEmphTarget = EmphasisFor(_painted);   // 초점 노드는 이름표가 또렷하다
        RefreshScale();
    }

    /// <summary>다음 목표 노드인가(가운데 제단 문양이 가리키는 것) — 오른쪽 위 별 배지.</summary>
    public void SetGoal(bool on)
    {
        if (_goal == on) return;
        _goal = on;
        RefreshBadges();
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
    public void SetAlpha(float a)
    {
        _group.alpha = a;
        _groupAlpha  = a;
        if (_label != null) ApplyEmphasis();
    }

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

        // 후광은 테 바로 밖 8~12px만(v2 「후광 8px 안팎」) — 예전 부드러운 점(지름 ×2.2)은 이웃 노드까지 번졌다.
        _glow  = MakeImage("Glow",  GlowDisc(), diameter / GlowInner, UITheme.Gold);
        _focus = MakeImage("Focus", UIProceduralSprites.Ring(FocusPx / (diameter + 18f), 128), diameter + 18f, new Color(1f, 0.93f, 0.75f, 0.9f));
        _glyph = MakeImage("Glyph", GlyphRing(), diameter * 1.42f, UITheme.Gold);
        _glyph.type = Image.Type.Filled; _glyph.fillMethod = Image.FillMethod.Radial360; _glyph.fillOrigin = 2;
        _base  = MakeImage("Base",  UIProceduralSprites.Circle(128), diameter, Dark);
        _base.raycastTarget = true;
        // 테 2px(작은 노드 1.5) — 예전 지름 비 0.07~0.09는 4~5px였다(10-11 v2 선 단계). 고리 굵기는 지름 비라 지름으로 나눈다.
        _rim   = MakeImage("Rim",   UIProceduralSprites.Ring(RimPx(node.Size) / diameter, 128), diameter, UITheme.Gold);
        _mark  = MakeImage("Mark",  node.Size == AltarNodeSize.Keystone ? UIProceduralSprites.RoundedRect(3f, 1.5f, 32) : UIProceduralSprites.Circle(64),
                           diameter * (node.Size == AltarNodeSize.Keystone ? 0.30f : node.Size == AltarNodeSize.Small ? 0.2f : 0.24f), branch);
        _mark.type = node.Size == AltarNodeSize.Keystone ? Image.Type.Sliced : Image.Type.Simple;
        if (node.Size == AltarNodeSize.Keystone) _mark.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);   // 마름모
        _icon  = MakeImage("Icon",  null, diameter * IconFrac, Color.white);   // 그림은 EnsureArt — 있으면 ◆ · 점 대신
        _icon.preserveAspect = true;

        // 값 칩 — 문양 아래 테에 걸친 짙은 알약 + 글자. 판은 글자보다 먼저 만든다(먼저 그려져 글자 뒤).
        _chipBg = MakeImage("ChipBg", UIProceduralSprites.RoundedRect(9f, 1.5f), 10f, new Color(0.06f, 0.05f, 0.09f, 0.92f));
        _chipBg.type = Image.Type.Sliced;
        _chipBg.rectTransform.anchoredPosition = new Vector2(0f, -diameter * 0.5f);
        _chip = MakeText("Chip", TextAlignmentOptions.Center);
        _chip.rectTransform.sizeDelta = new Vector2(160f, ChipH);
        _chip.rectTransform.anchoredPosition = new Vector2(0f, -diameter * 0.5f);
        _chip.textWrappingMode = TextWrappingModes.NoWrap;

        // 배지 — 테 위 45°. 자물쇠(오른쪽 아래)는 값 칩이 없는 「조건 잠김」에만 켜져 칩과 겹치지 않는다.
        float badge = Mathf.Clamp(Mathf.Round(diameter * BadgeFrac), 16f, 22f);
        float at    = diameter * 0.5f * 0.72f;
        _lockBadge = MakeImage("LockBadge", null, badge, UITheme.Thin.Ink);
        _lockBadge.rectTransform.anchoredPosition = new Vector2(at, -at);
        _goalBadge = MakeImage("GoalBadge", null, badge, UITheme.Thin.Gold);
        _goalBadge.rectTransform.anchoredPosition = new Vector2(at, at);
        _lockBadge.preserveAspect = _goalBadge.preserveAspect = true;

        _label = MakeText("Label", TextAlignmentOptions.Top);
        _label.text = node.DisplayName;
        _label.rectTransform.sizeDelta = new Vector2(320f, 22f);
        _label.textWrappingMode = TextWrappingModes.NoWrap;

        _button = gameObject.AddComponent<Button>();
        _button.targetGraphic = _base;
        _button.transition = Selectable.Transition.None;
        _button.onClick.AddListener(() => Clicked?.Invoke(this));
        var nav = _button.navigation; nav.mode = Navigation.Mode.Automatic; _button.navigation = nav;

        _glyph.enabled = false; _focus.enabled = false; _glow.enabled = false;
        _icon.enabled = false; _lockBadge.enabled = false; _goalBadge.enabled = false;   // 그림 없는 Image는 흰 네모라 꺼 둔다
    }

    /// <summary>
    /// 팩 그림(10-13 Artsystack)을 처음 한 번 — 고리 · 아이콘 · 배지. 스킨이 아직 없으면 다음 Apply에서 다시 본다.
    /// 고리는 무채로 바꿔 지금 상태색(금 · 잉크)을 그대로 입는다.
    /// </summary>
    private void EnsureArt()
    {
        if (_artDone) return;
        var skin = UISkin.Achievement;
        if (skin == null) return;
        _artDone = true;

        bool key  = Node.Size == AltarNodeSize.Keystone;
        var  ring = key ? skin.AltarKeyRing : skin.AltarNodeRing;
        if (ring != null)
        {
            _rim.sprite = UISpriteMaster.Neutral(ring);
            if (key)
            {
                // skill_slot은 안이 차 있다 — 판 아래에 깔고 판을 눈금 안쪽까지 줄여 갈래 색 채움을 살린다
                _rim.transform.SetSiblingIndex(_base.transform.GetSiblingIndex());
                _baseScale = KeyInset;
            }
        }
        _icon.sprite      = skin.NodeIcon(Node.Id);
        _lockBadge.sprite = skin.AltarLock;
        _goalBadge.sprite = skin.AltarNext;
    }

    private void Paint(AltarNodeVisual v)
    {
        bool hidden = v == AltarNodeVisual.Hidden || v == AltarNodeVisual.EraHidden;
        bool icon   = _icon.sprite != null;
        _rim.enabled  = !hidden;
        _mark.enabled = !hidden && !icon;   // 아이콘이 있으면 ◆ · 점 대신(10-13)
        _icon.enabled = !hidden && icon;
        _label.gameObject.SetActive(!hidden);
        _glow.enabled = v == AltarNodeVisual.Affordable;
        _glyph.enabled = v == AltarNodeVisual.Bought && Node.Size == AltarNodeSize.Keystone;
        if (_glyph.enabled) { _glyph.fillAmount = 1f; _glyph.color = new Color(UITheme.Gold.r, UITheme.Gold.g, UITheme.Gold.b, 0.35f); }
        if (_focus != null) _focus.enabled = _focused && !hidden;
        _button.interactable = !hidden;
        _base.raycastTarget  = !hidden;
        _rim.raycastTarget   = !hidden && _baseScale < 1f;   // 열쇠 고리가 판 아래면 줄어든 판 밖 띠도 누른다

        float size = hidden ? 12f : _diameter * _baseScale;
        _base.rectTransform.sizeDelta = new Vector2(size, size);

        // 1차 구분 = 채도(10-11 v2) — 갈래 색은 산 노드만 그대로, 나머지 표식은 채도 30% 이하. 금 테는 살 수 있음 · 산 것에만.
        Color label = UITheme.Ink;
        switch (v)
        {
            case AltarNodeVisual.Bought:
                _base.color = BoughtFill(); _rim.color = UITheme.Gold; _mark.color = BoughtMark; break;
            case AltarNodeVisual.Affordable:
                _base.color = Dark; _rim.color = UITheme.Gold; _mark.color = Tone(_branch, 0.30f, 0.85f, 1f); label = UITheme.Gold; break;
            case AltarNodeVisual.Open:
                _base.color = Dark; _rim.color = UITheme.Thin.InkA(0.45f); _mark.color = Tone(_branch, 0.30f, 0.70f, 1f); break;
            case AltarNodeVisual.OpenLocked:
                _base.color = Dark; _rim.color = UITheme.Thin.InkA(0.30f); _mark.color = Tone(_branch, 0.20f, 0.55f, 0.8f); break;
            case AltarNodeVisual.Next:
                _base.color = Darker; _rim.color = NextRim; _mark.color = Tone(_branch, 0.15f, 0.45f, 0.8f); break;
            default:
                _base.color = HiddenDot; break;
        }
        _painted = v;
        _icon.color  = IconColor(v);
        RefreshBadges();
        _label.color = label;
        _chip.color  = v == AltarNodeVisual.Affordable ? UITheme.Gold : UITheme.Ink;
        _labelEmphTarget = EmphasisFor(v);
        if (_labelEmph < 0f) _labelEmph = _labelEmphTarget;
        ApplyEmphasis();
        _chipBg.enabled = _chip.gameObject.activeSelf && !hidden;
        RefreshScale();
    }

    /// <summary>노드 아이콘 색(10-13) — 열림 = 잉크 · 열쇠 = 금 · 잠김 = 흐림. 산 노드는 갈래 색 판 위라 어두운 표식 색.</summary>
    private Color IconColor(AltarNodeVisual v)
    {
        switch (v)
        {
            case AltarNodeVisual.Bought:     return BoughtMark;
            case AltarNodeVisual.Affordable:
            case AltarNodeVisual.Open:       return Node.Size == AltarNodeSize.Keystone ? UITheme.Thin.Gold : UITheme.Thin.Ink;
            default:                         return UITheme.Thin.InkA(0.30f);
        }
    }

    /// <summary>배지 — 조건 잠김 = 자물쇠(잉크) · 다음 목표 = 별(금, 산 것 빼고). 그림이 없으면 둘 다 없다.</summary>
    private void RefreshBadges()
    {
        bool shown = _painted != AltarNodeVisual.Hidden && _painted != AltarNodeVisual.EraHidden;
        _lockBadge.enabled = shown && _lockBadge.sprite != null && _painted == AltarNodeVisual.OpenLocked;
        _goalBadge.enabled = shown && _goalBadge.sprite != null && _goal && _painted != AltarNodeVisual.Bought;
    }

    /// <summary>또렷하게 = 산 것 · 살 수 있음 · 초점. 나머지(열림 · 조건 잠김 · 한 칸 앞)는 0.45 — 이름은 상세 패널이 말한다(LE · PoE).</summary>
    private float EmphasisFor(AltarNodeVisual v) =>
        v == AltarNodeVisual.Bought || v == AltarNodeVisual.Affordable || _focused ? 1f : DimAlpha;

    /// <summary>이름표 알파 = 노드 알파 × 또렷함. 값 칩은 노드 안이라 노드 알파가 이미 곱해진다.</summary>
    private void ApplyEmphasis()
    {
        float k = Mathf.Max(0f, _labelEmph);
        _label.alpha = _groupAlpha * k;
        _chip.alpha  = k;
    }

    /// <summary>갈래 색의 채도를 <paramref name="sat"/> 이하 · 밝기 <paramref name="val"/>로 낮춘 표식 색.</summary>
    private static Color Tone(Color c, float sat, float val, float a)
    {
        Color.RGBToHSV(c, out float h, out float s, out _);
        var o = Color.HSVToRGB(h, Mathf.Min(s, sat), val);
        o.a = a;
        return o;
    }

    private static float RimPx(AltarNodeSize size) => size == AltarNodeSize.Small ? 1.5f : 2f;

    private void RefreshScale()
    {
        if (_animating) return;
        float s = _hover && _visual != AltarNodeVisual.Hidden ? 1.06f : 1f;
        _rt.localScale = new Vector3(s, s, 1f);
    }

    /// <summary>산 노드 = 갈래 색 그대로(10-11 v2 — 채도가 상태의 1차 변수라 산 것만 100%).</summary>
    private Color BoughtFill() => _branch;

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

    /// <summary>이름표 · 값 칩 — Noto 500 15, 가짜 굵게 · 그림자 없음(10-11 v2).</summary>
    private TMP_Text MakeText(string name, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var t = go.AddComponent<TextMeshProUGUI>();   // transform 캐시는 AddComponent 뒤(가짜 null 함정)
        UITheme.StyleThinText(t, UIWeight.Medium, LabelSize, UITheme.Ink);
        t.alignment = align; t.raycastTarget = false;
        t.richText = true;
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

    /// <summary>
    /// 살 수 있음 후광 — 안쪽(반지름 비 <see cref="GlowInner"/>까지)은 차 있고 그 밖에서 사라지는 원. 안쪽은 문양 판 뒤라 안 보이고
    /// 테 바로 밖 고리만 보인다(안을 채워 테와 후광 사이에 틈이 없다). 한 번 구워 공유.
    /// </summary>
    private static Sprite GlowDisc()
    {
        if (s_glowDisc != null) return s_glowDisc;
        const int N = 128;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color32[N * N];
        float c = N * 0.5f;
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float dx = x + 0.5f - c, dy = y + 0.5f - c;
            float r = Mathf.Sqrt(dx * dx + dy * dy) / c;          // 0~1
            float k = 1f - Mathf.Clamp01((r - GlowInner) / (1f - GlowInner));
            px[y * N + x] = new Color32(255, 255, 255, (byte)(k * k * 255f));
        }
        tex.SetPixels32(px); tex.Apply();
        s_glowDisc = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        s_glowDisc.hideFlags = HideFlags.HideAndDontSave;
        return s_glowDisc;
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
