using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 유물 진화 HUD(10-14 빌드 시너지 C · 83 UI 요청 2) — Q(유물) 칸의 진화 표시 + 진화 순간의 짧은 화면 연출.
/// <list type="bullet">
/// <item>Q 칸: 유물 아이콘 둘레 안쪽에 진화 계열색 고리(1.5px) · 칸 위에 [유물 문장] 「진화 · 이름」(16 — 「진화」만 계열색).</item>
/// <item>진화 순간(HudPresenter가 런 중 진화가 새로 생긴 것을 볼 때만): 화면 위쪽 가운데에 금 고리가 그려지며 계열색 유물 문장이 떠오르고(0.35초)
/// 「유물 진화」 · 이름이 머문 뒤(0.85초) 문장이 Q 칸 덧말 자리로 날아 들어가며(0.4초) Q 고리가 그려지고 덧말이 켜진다(0.25초). 합 1.85초 · 실시간.</item>
/// </list>
/// 흔들림 · 화면 전체 막 없음(플레이어 연출 「살짝」 상한 · 이펙트 톤 원칙). 글자 흐림은 CanvasGroup으로만(HUD 하드 가드가 글자 알파를 1로 되돌린다).
/// </summary>
public sealed partial class CombatPanelView
{
    // ── Constants ──
    private const float EvoRingInset  = 3f;     // Q 원 안쪽 — 아이콘 둘레
    private const float EvoRingStroke = 1.5f;
    private const float EvoLabelGap   = 16f;    // Q 칸 위끝에서 — 게이지 고리(간격 7 + 굵기 3) 바깥
    private const float EvoLabelW     = 200f;
    private const float EvoLabelH     = 26f;
    private const float EvoLabelCrest = 20f;
    private const float EvoLabelSize  = 16f;
    private const float EvoMomentY    = 170f;   // 화면 가운데에서 위로(캐릭터 머리 위 · 보스 바 아래)
    private const float EvoEmblem     = 112f;
    private const float EvoCrestFrac  = 0.58f;  // 문장 = 원의 58%
    private const float EvoGlow       = 300f;
    private const float EvoCaption    = 16f;
    private const float EvoNameSize   = 32f;
    private const float EvoInSec = 0.35f, EvoHoldSec = 0.85f, EvoFlySec = 0.4f, EvoLandSec = 0.25f;

    private static readonly Color EvoDisc = new(0.04f, 0.035f, 0.06f, 0.78f);

    // ── Private ──
    private Image         _evoRing;
    private RectTransform _evoLabel;
    private CanvasGroup   _evoLabelGroup;
    private Image         _evoLabelCrest;
    private TMP_Text      _evoLabelText;
    private CancellationTokenSource _evoCts;

    // ── Public Methods ──

    /// <summary>
    /// Q 칸 진화 표시. <paramref name="def"/>가 null이면 걷는다(새 런 · 플레이어 해제).
    /// <paramref name="playMoment"/>면 진화 순간 연출 끝에 표시가 켜진다. <paramref name="crest"/> = 유물 문장(흰 선화).
    /// </summary>
    public void SetRelicEvolution(RelicEvolution.Def def, Sprite crest, bool playMoment)
    {
        CancelEvolutionMotion();
        if (def == null)
        {
            if (_evoRing != null) Destroy(_evoRing.gameObject);
            if (_evoLabel != null) Destroy(_evoLabel.gameObject);
            _evoRing = null; _evoLabel = null; _evoLabelGroup = null; _evoLabelCrest = null; _evoLabelText = null;
            return;
        }

        var q = QSlotRect();
        if (q == null) return;
        Color tint = BuildFamilyRules.ColorOf(def.Family);
        EnsureEvolutionMarker(q);
        _evoRing.color = tint;
        _evoLabelCrest.sprite  = crest;
        _evoLabelCrest.color   = tint;
        _evoLabelCrest.gameObject.SetActive(crest != null);   // 문장이 없으면 빈 자리를 남기지 않는다
        _evoLabelText.text = $"<color={BuildFamilyRules.Hex(def.Family)}>진화</color> · {def.Name}";

        if (!playMoment || !isActiveAndEnabled)
        {
            _evoRing.fillAmount  = 1f;
            _evoLabelGroup.alpha = 1f;
            return;
        }
        _evoRing.fillAmount  = 0f;
        _evoLabelGroup.alpha = 0f;
        _evoCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        EvolutionMomentAsync(def, crest, tint, _evoCts.Token).Forget();
    }

    // ── Private Methods ──

    private RectTransform QSlotRect() => _wQSlot != null ? _wQSlot : FindChildRecursive(transform, "HUD_QSkile") as RectTransform;

    /// <summary>Q 칸 고리 · 덧말을 처음 한 번 짓는다(이후엔 색 · 글만 바꾼다).</summary>
    private void EnsureEvolutionMarker(RectTransform q)
    {
        if (_evoRing == null)
        {
            float d = Mathf.Max(8f, Mathf.Min(q.rect.width, q.rect.height) - EvoRingInset * 2f);
            _evoRing = ShopUIStyle.MakeImage(q, "EvoQRing", Color.white);
            _evoRing.sprite        = WuwaHud ? UIThinShapes.Ring(d, EvoRingStroke) : UIThinShapes.Outline(ThinShape.Diamond, d, d, EvoRingStroke);
            _evoRing.type          = Image.Type.Filled;
            _evoRing.fillMethod    = Image.FillMethod.Radial360;
            _evoRing.fillOrigin    = (int)Image.Origin360.Top;
            _evoRing.fillClockwise = true;
            Center(_evoRing.rectTransform, d);
        }
        if (_evoLabel != null) return;

        _evoLabel = (RectTransform)new GameObject("EvoQLabel", typeof(RectTransform)).transform;
        _evoLabel.SetParent(q, false);
        _evoLabel.anchorMin = _evoLabel.anchorMax = new Vector2(0.5f, 1f);
        _evoLabel.pivot            = new Vector2(0.5f, 0f);
        _evoLabel.anchoredPosition = new Vector2(0f, EvoLabelGap);
        _evoLabel.sizeDelta        = new Vector2(EvoLabelW, EvoLabelH);
        _evoLabelGroup = _evoLabel.gameObject.AddComponent<CanvasGroup>();
        _evoLabelGroup.blocksRaycasts = _evoLabelGroup.interactable = false;
        var row = _evoLabel.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 6f;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = false;

        _evoLabelCrest = ShopUIStyle.MakeImage(_evoLabel, "EvoQCrest", Color.white);
        _evoLabelCrest.preserveAspect = true;
        var le = _evoLabelCrest.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = le.minHeight = le.preferredHeight = EvoLabelCrest;

        var go = new GameObject("EvoQName", typeof(RectTransform));
        go.transform.SetParent(_evoLabel, false);
        _evoLabelText = go.AddComponent<TextMeshProUGUI>();
        UITheme.StyleThinText(_evoLabelText, UIWeight.Medium, EvoLabelSize, UITheme.Thin.Ink, shadow: true);
        _evoLabelText.enableAutoSizing = false;
        _evoLabelText.alignment        = TextAlignmentOptions.MidlineLeft;
        _evoLabelText.textWrappingMode = TextWrappingModes.NoWrap;
        _evoLabelText.raycastTarget    = false;
    }

    /// <summary>진화 순간 — 떠오름(0.35) → 머묾(0.85) → Q 칸으로(0.4) → 고리 · 덧말 켜짐(0.25). 끝나거나 끊기면 연출 판을 걷고 표시를 켠 채로 둔다.</summary>
    private async UniTaskVoid EvolutionMomentAsync(RelicEvolution.Def def, Sprite crest, Color tint, CancellationToken ct)
    {
        var canvas = GetComponentInParent<Canvas>();
        var host   = canvas != null ? canvas.rootCanvas.transform as RectTransform : transform as RectTransform;
        RectTransform root = null;
        try
        {
            root = (RectTransform)new GameObject("EvolutionMoment", typeof(RectTransform), typeof(CanvasGroup)).transform;
            root.SetParent(host, false);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            root.SetAsLastSibling();
            var rootGroup = root.GetComponent<CanvasGroup>();
            rootGroup.blocksRaycasts = rootGroup.interactable = false;
            rootGroup.alpha = 0f;

            var mid = new Vector2(0.5f, 0.5f);
            var emblem = (RectTransform)new GameObject("Emblem", typeof(RectTransform)).transform;
            emblem.SetParent(root, false);
            ShopUIStyle.Anchor(emblem, mid, mid, mid, new Vector2(0f, EvoMomentY), Vector2.one * EvoEmblem);

            var glow = ShopUIStyle.MakeImage(emblem, "Glow", UITheme.Thin.WithA(tint, 0f));
            glow.sprite = UI_RuneSelectPopup.SoftDot;
            ShopUIStyle.Anchor(glow.rectTransform, mid, mid, mid, Vector2.zero, Vector2.one * EvoGlow);
            var disc = ShopUIStyle.MakeImage(emblem, "Disc", EvoDisc);
            disc.sprite = UIThinShapes.Disc(EvoEmblem);
            ShopUIStyle.Stretch(disc.rectTransform);
            var ring = ShopUIStyle.MakeImage(emblem, "Ring", UITheme.Thin.Gold);
            ring.sprite        = UIThinShapes.Ring(EvoEmblem, EvoRingStroke);
            ring.type          = Image.Type.Filled;
            ring.fillMethod    = Image.FillMethod.Radial360;
            ring.fillOrigin    = (int)Image.Origin360.Top;
            ring.fillClockwise = true;
            ring.fillAmount    = 0f;
            ShopUIStyle.Stretch(ring.rectTransform);
            var mark = ShopUIStyle.MakeImage(emblem, "Crest", tint);
            mark.sprite = crest;
            mark.preserveAspect = true;
            mark.enabled = crest != null;
            ShopUIStyle.Anchor(mark.rectTransform, mid, mid, mid, Vector2.zero, Vector2.one * EvoEmblem * EvoCrestFrac);

            var words = (RectTransform)new GameObject("Words", typeof(RectTransform), typeof(CanvasGroup)).transform;
            words.SetParent(root, false);
            ShopUIStyle.Anchor(words, mid, mid, mid, new Vector2(0f, EvoMomentY - EvoEmblem * 0.5f - 52f), new Vector2(640f, 90f));
            var wordsGroup = words.GetComponent<CanvasGroup>();
            var caption = MomentText(words, "Caption", "유물 진화", UIWeight.Medium, EvoCaption, UITheme.Thin.Gold, 30f);
            caption.characterSpacing = 12f;
            MomentText(words, "Name", def.Name, UIWeight.Light, EvoNameSize, UITheme.Thin.Ink, -12f);

            // ① 떠오름 — 금 고리가 시계 방향으로 그려지고 문장이 0.85 → 1
            await UIMotion.Tween(EvoInSec, k =>
            {
                rootGroup.alpha = k;
                ring.fillAmount = k;
                glow.color = UITheme.Thin.WithA(tint, 0.5f * k);
                mark.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, k);
            }, ct);

            // ② 머묾 — 빛만 조금 가라앉는다
            await UIMotion.Tween(EvoHoldSec, k => glow.color = UITheme.Thin.WithA(tint, Mathf.Lerp(0.5f, 0.3f, k)), ct);

            // ③ Q 칸 덧말의 문장 자리로 — 줄며 날아간다(글은 먼저 흐려진다)
            Vector3 from = emblem.position;
            Vector3 to   = _evoLabelCrest != null && _evoLabelCrest.gameObject.activeSelf ? _evoLabelCrest.rectTransform.position
                         : _evoRing != null ? _evoRing.rectTransform.position : from;
            float endScale = EvoLabelCrest / (EvoEmblem * EvoCrestFrac);
            await UIMotion.Tween(EvoFlySec, k =>
            {
                wordsGroup.alpha = 1f - Mathf.Clamp01(k * 2f);
                emblem.position  = Vector3.Lerp(from, to, k);
                emblem.localScale = Vector3.one * Mathf.Lerp(1f, endScale, k);
                ring.color = UITheme.Thin.WithA(UITheme.Thin.Gold, 1f - k);
                disc.color = UITheme.Thin.WithA(EvoDisc, EvoDisc.a * (1f - k));
                glow.color = UITheme.Thin.WithA(tint, 0.3f * (1f - k));
            }, ct, t => t * t);
            if (root != null) { Destroy(root.gameObject); root = null; }

            // ④ 내려앉음 — Q 고리가 그려지고 덧말이 켜진다
            await UIMotion.Tween(EvoLandSec, k =>
            {
                if (_evoRing != null) _evoRing.fillAmount = k;
                if (_evoLabelGroup != null) _evoLabelGroup.alpha = k;
            }, ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (root != null) Destroy(root.gameObject);
            if (_evoRing != null) _evoRing.fillAmount = 1f;
            if (_evoLabelGroup != null) _evoLabelGroup.alpha = 1f;
        }
    }

    private static TMP_Text MomentText(RectTransform parent, string name, string text, UIWeight weight, float size, Color color, float y)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        UITheme.StyleThinText(t, weight, size, color, shadow: true);
        t.enableAutoSizing = false;
        t.alignment        = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget    = false;
        t.text = text;
        var mid = new Vector2(0.5f, 0.5f);
        ShopUIStyle.Anchor(t.rectTransform, mid, mid, mid, new Vector2(0f, y), new Vector2(parent.sizeDelta.x, size * 1.6f));
        return t;
    }

    private void CancelEvolutionMotion()
    {
        if (_evoCts == null) return;
        _evoCts.Cancel();
        _evoCts.Dispose();
        _evoCts = null;
    }
}
