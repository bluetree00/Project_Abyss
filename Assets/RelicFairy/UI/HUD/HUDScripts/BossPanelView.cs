//============================================================
// BossPanelView.cs
// - 보스 HP 슬라이더/텍스트
// - 보스 이름 텍스트
// - 프리팹 연결이 비어 있으면 런타임에 최소 UI를 자동 구성
//============================================================
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class BossPanelView : MonoBehaviour
{
    [Header("Boss HP")]
    [SerializeField] private Slider hpSlider;
    [SerializeField] private Image hpFillImage;
    [SerializeField] private TMP_Text hpText;

    [Header("Boss Name")]
    [SerializeField] private TMP_Text nameText;

    [Header("HP Color Gradient")]
    [SerializeField] private Color colorHigh = new Color(0.25f, 0.90f, 0.35f);
    [SerializeField] private Color colorMid = new Color(1.00f, 0.80f, 0.10f);
    [SerializeField] private Color colorLow = new Color(0.95f, 0.18f, 0.10f);

    [Header("보스바 스킨 (선택 — 지정 시 플랫색 대신 아트 사용)")]
    [SerializeField] private Sprite bossFrameSprite;   // 보스바 테두리
    [SerializeField] private Sprite bossTrackSprite;   // 보스바 내부 검정
    [SerializeField] private Sprite bossFillSprite;    // 보스바 내부 빨강

    private int _maxHp;
    private bool _skinApplied;

    private bool HasSkin => bossTrackSprite != null || bossFillSprite != null || bossFrameSprite != null;

    private void Awake()
    {
        EnsureWired();
    }

    public void Init(int maxHp, string bossName)
    {
        EnsureWired();

        _maxHp = Mathf.Max(1, maxHp);

        if (hpSlider != null)
        {
            hpSlider.maxValue = _maxHp;
            hpSlider.value = _maxHp;
        }

        if (hpText != null)
            hpText.text = $"{_maxHp} / {_maxHp}";
        if (nameText != null)
            nameText.text = bossName ?? string.Empty;
        EnsureTextVisible();

        ApplyFillColor(1f);
    }

    public void SetHP(int hp, int maxHp)
    {
        EnsureWired();

        _maxHp = Mathf.Max(1, maxHp);
        int clamped = Mathf.Clamp(hp, 0, _maxHp);

        if (hpSlider != null)
        {
            hpSlider.maxValue = _maxHp;
            hpSlider.value = clamped;
        }

        if (hpText != null)
            hpText.text = $"{Mathf.Max(0, hp)} / {_maxHp}";
        EnsureTextVisible();

        ApplyFillColor((float)clamped / _maxHp);
    }

    private void EnsureWired()
    {
        hpSlider ??= GetComponentInChildren<Slider>(true);
        if (hpFillImage == null && hpSlider != null)
            hpFillImage = hpSlider.fillRect != null ? hpSlider.fillRect.GetComponent<Image>() : null;

        if (hpText == null || nameText == null)
        {
            var texts = GetComponentsInChildren<TMP_Text>(true);
            foreach (var text in texts)
            {
                if (text == null) continue;

                string lower = text.name.ToLowerInvariant();
                if (nameText == null && lower.Contains("name"))
                {
                    nameText = text;
                    continue;
                }

                if (hpText == null && (lower.Contains("hp") || lower.Contains("value")))
                    hpText = text;
            }

            if (nameText == null && texts.Length > 0)
                nameText = texts[0];
            if (hpText == null && texts.Length > 1)
                hpText = texts[texts.Length - 1];
        }

        if (hpText == nameText)
            hpText = null;

        if (hpSlider == null || hpText == null || nameText == null)
            BuildRuntimeFallback();

        ApplySkin();
    }

    /// <summary>
    /// 디자이너 아트로 보스바 스킨 적용: 트랙(검정) + fill(빨강) + 테두리 오버레이.
    /// fill을 Filled/Horizontal로 두면 Slider가 폭(anchor) 대신 fillAmount를 구동하므로 아트가 찌그러지지 않는다.
    /// 스프라이트 미지정 시 아무 것도 하지 않아 기존 플랫색 동작이 유지된다.
    /// </summary>
    private void ApplySkin()
    {
        if (_skinApplied || !HasSkin) return;
        _skinApplied = true;

        if (hpSlider != null && bossTrackSprite != null)
        {
            var bg = hpSlider.transform.Find("Background");
            if (bg != null && bg.TryGetComponent<Image>(out var bgImg))
            {
                bgImg.sprite = bossTrackSprite;
                bgImg.type   = Image.Type.Sliced;
                bgImg.color  = Color.white;
            }
        }

        if (hpFillImage != null && bossFillSprite != null)
        {
            hpFillImage.sprite     = bossFillSprite;
            hpFillImage.type       = Image.Type.Filled;
            hpFillImage.fillMethod = Image.FillMethod.Horizontal;
            hpFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            hpFillImage.color      = Color.white;   // 색 틴트 대신 아트 그대로
        }

        // 어두운 사각 배경판은 테두리 아트와 겹쳐 박스처럼 보이므로 감춘다(스킨 시에만).
        var panelBg = transform.Find("BossPanelBG");
        if (panelBg != null && panelBg.TryGetComponent<Image>(out var panelImg))
            panelImg.color = new Color(0f, 0f, 0f, 0f);

        if (bossFrameSprite != null)
        {
            var frame = EnsureImage("BossBarFrame", transform, Color.white);
            var frt = (RectTransform)frame.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;
            frame.sprite = bossFrameSprite;
            frame.type   = Image.Type.Sliced;
            frt.SetAsLastSibling();   // 슬라이더 위
        }
    }

    private void BuildRuntimeFallback()
    {
        if (hpSlider != null && hpText != null && nameText != null)
            return;

        var root = transform as RectTransform;
        if (root == null)
            root = gameObject.AddComponent<RectTransform>();

        root.anchorMin = new Vector2(0.5f, 1f);
        root.anchorMax = new Vector2(0.5f, 1f);
        root.pivot = new Vector2(0.5f, 1f);
        root.anchoredPosition = new Vector2(0f, -36f);
        root.sizeDelta = new Vector2(760f, 90f);

        var background = EnsureImage("BossPanelBG", transform, new Color(0f, 0f, 0f, 0.6f));
        var bgRect = (RectTransform)background.transform;
        bgRect.anchorMin = new Vector2(0f, 0f);
        bgRect.anchorMax = new Vector2(1f, 1f);
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        nameText ??= EnsureText(
            "BossNameText",
            transform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, -12f),
            new Vector2(640f, 28f),
            28,
            TextAlignmentOptions.Center,
            FontStyles.Bold);

        hpText ??= EnsureText(
            "BossHpText",
            transform,
            new Vector2(1f, 0f),
            new Vector2(1f, 0f),
            new Vector2(-24f, 18f),
            new Vector2(180f, 24f),
            20,
            TextAlignmentOptions.Right,
            FontStyles.Normal);

        if (hpSlider == null)
        {
            var sliderObj = new GameObject("BossHpSlider", typeof(RectTransform), typeof(Slider));
            sliderObj.transform.SetParent(transform, false);

            var sliderRect = (RectTransform)sliderObj.transform;
            sliderRect.anchorMin = new Vector2(0f, 0f);
            sliderRect.anchorMax = new Vector2(1f, 0f);
            sliderRect.pivot = new Vector2(0.5f, 0f);
            sliderRect.anchoredPosition = new Vector2(0f, 14f);
            sliderRect.sizeDelta = new Vector2(-48f, 24f);

            var bg = EnsureImage("Background", sliderObj.transform, new Color(0.15f, 0.15f, 0.18f, 0.95f));
            var bgSliderRect = (RectTransform)bg.transform;
            bgSliderRect.anchorMin = new Vector2(0f, 0f);
            bgSliderRect.anchorMax = new Vector2(1f, 1f);
            bgSliderRect.offsetMin = Vector2.zero;
            bgSliderRect.offsetMax = Vector2.zero;

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(sliderObj.transform, false);
            var fillAreaRect = (RectTransform)fillArea.transform;
            fillAreaRect.anchorMin = new Vector2(0f, 0f);
            fillAreaRect.anchorMax = new Vector2(1f, 1f);
            fillAreaRect.offsetMin = new Vector2(4f, 4f);
            fillAreaRect.offsetMax = new Vector2(-4f, -4f);

            var fill = EnsureImage("Fill", fillArea.transform, colorHigh);
            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            hpSlider = sliderObj.GetComponent<Slider>();
            hpSlider.fillRect = fillRect;
            hpSlider.targetGraphic = fill;
            hpSlider.direction = Slider.Direction.LeftToRight;
            hpSlider.minValue = 0f;
            hpSlider.maxValue = 1f;
            hpSlider.value = 1f;
            hpFillImage = fill;
        }
    }

    private static Image EnsureImage(string name, Transform parent, Color color)
    {
        var existing = parent.Find(name);
        var obj = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.transform.SetParent(parent, false);

        var image = obj.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text EnsureText(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 size,
        float fontSize,
        TextAlignmentOptions alignment,
        FontStyles style)
    {
        var existing = parent.Find(name);
        var obj = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);

        var rect = (RectTransform)obj.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        var text = obj.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.fontStyle = style;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private void ApplyFillColor(float pct)
    {
        if (hpFillImage == null) return;
        if (HasSkin) return;   // 스킨: fill 아트를 그대로 쓰므로 색 틴트하지 않음

        Color c;
        if (pct >= 0.75f)
            c = colorHigh;
        else if (pct >= 0.40f)
            c = Color.Lerp(colorMid, colorHigh, (pct - 0.40f) / 0.35f);
        else
            c = Color.Lerp(colorLow, colorMid, pct / 0.40f);

        hpFillImage.color = c;
    }

    private void EnsureTextVisible()
    {
        ForceTextVisible(nameText);
        ForceTextVisible(hpText);
    }

    private static void ForceTextVisible(TMP_Text text)
    {
        if (text == null) return;
        if (!text.gameObject.activeSelf)
            text.gameObject.SetActive(true);

        var color = text.color;
        if (color.a < 0.95f)
        {
            color.a = 1f;
            text.color = color;
        }
    }
}
