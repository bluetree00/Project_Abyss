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

    // 테두리 아트에는 위아래 장식이 포함돼 있어, 실제 채워지는 '창'은 그중 일부다(아트 실측 2819×57 / 2867×319).
    // 픽셀 여백 대신 비율로 잡아야 패널 크기가 바뀌어도 창이 프레임에서 벗어나지 않는다.
    [Header("내부 창 (테두리 아트 대비 비율) — 트랙/필이 앉을 자리")]
    [SerializeField, Tooltip("프레임 대비 내부 창 크기 (가로, 세로). 아트 실측 2819/2867, 57/319.")]
    private Vector2 innerWindowRatio = new Vector2(0.983f, 0.179f);
    // 프레임 아트(2867×319) 알파 프로파일 실측: 바깥 프레임 상/하 테두리가 png-y 140~149 / 255~264,
    // 그 사이 실제 바 채널(주 슬롯 png-y 178~222)의 세로 중심이 png-y ≈ 201.
    // RectTransform 기준(하단=0) centerY = 1 − 201/319 ≈ 0.37. 0.5로 두면 창이 위 테두리 쪽으로
    // 떠서 필이 홈보다 높게 보였다(플레이어 바도 창이 중앙 아님 — hpInnerPadding T32/B18 → 0.40).
    [SerializeField, Range(0f, 1f), Tooltip("내부 창의 세로 중심 (0=하단, 1=상단). 아트 알파 실측 기준 0.37.")]
    private float innerWindowCenterY = 0.37f;

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

        // 슬라이더를 프레임(패널 전체)과 같은 자리로 편다 — 프레임은 패널 전체를 덮는데
        // 슬라이더만 바닥 스트립에 있으면 채움이 테두리 창을 벗어난다.
        if (hpSlider != null)
        {
            var srt = (RectTransform)hpSlider.transform;
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(0f, -2f);
            srt.offsetMax = new Vector2(0f, -2f);
        }

        if (hpSlider != null && bossTrackSprite != null)
        {
            var bg = hpSlider.transform.Find("Background");
            if (bg != null && bg.TryGetComponent<Image>(out var bgImg))
            {
                bgImg.sprite = bossTrackSprite;
                bgImg.type   = Image.Type.Sliced;
                bgImg.color  = Color.white;
                FitInnerWindow((RectTransform)bg);
            }
        }

        // Fill Area(슬라이더 컨테이너)를 창에 맞춘다. fillRect 자체의 앵커는 Slider가 매 프레임 덮어쓰므로
        // 반드시 '부모'를 맞춰야 한다.
        if (hpSlider != null && hpSlider.fillRect != null)
            FitInnerWindow(hpSlider.fillRect.parent as RectTransform);

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
            // 패널 높이를 테두리 아트 비율에 맞춘다 — 안 맞추면 장식이 세로로 눌려 보이고,
            // 비율로 잡은 내부 창도 아트의 실제 창과 어긋난다.
            if (transform is RectTransform root && bossFrameSprite.rect.width > 0f)
            {
                float aspect = bossFrameSprite.rect.height / bossFrameSprite.rect.width;
                root.sizeDelta = new Vector2(root.sizeDelta.x, root.sizeDelta.x * aspect);
            }

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

        // 텍스트는 테두리보다 위 — 프레임을 마지막 형제로 올리면 이름/수치가 장식에 가린다.
        if (nameText != null) nameText.transform.SetAsLastSibling();
        if (hpText   != null) hpText.transform.SetAsLastSibling();

        PlaceHpTextInside();
    }

    /// <summary>
    /// HP 수치를 <b>바 안쪽</b> 오른쪽 끝에 앉힌다.
    ///
    /// 예전엔 패널 하단(anchor y=0)에 매달려 있었는데, 스킨을 씌우면 바가 패널 세로 <b>가운데</b>로
    /// 올라가서(FitInnerWindow) 수치만 바 아래에 남아 테두리를 밟았다. 바와 같은 창(innerWindow)에
    /// 붙이면 프레임 높이가 어떻게 잡히든 항상 바 안에 들어온다.
    /// </summary>
    private void PlaceHpTextInside()
    {
        if (hpText == null) return;

        var rt = hpText.rectTransform;
        rt.pivot = new Vector2(0.5f, 0.5f);

        if (HasSkin)
        {
            FitInnerWindow(rt);                              // 바와 같은 창
            rt.offsetMin = new Vector2(0f, -2f);
            rt.offsetMax = new Vector2(-46f, -2f);           // 우측 장식(화살촉) 피하기
        }
        else
        {
            // 색 폴백: 슬라이더(y 14~38) 위쪽 줄에 둔다.
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot     = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(190f, 26f);
            rt.anchoredPosition = new Vector2(-28f, 52f);
        }

        hpText.alignment        = TextAlignmentOptions.MidlineRight;
        hpText.textWrappingMode = TextWrappingModes.NoWrap;
        hpText.overflowMode     = TextOverflowModes.Overflow;
    }

    /// <summary>테두리 아트의 내부 창 비율로 rect를 앉힌다(픽셀 오프셋 0 → 앵커만으로 크기 결정).</summary>
    private void FitInnerWindow(RectTransform rt)
    {
        if (rt == null) return;

        float w = Mathf.Clamp01(innerWindowRatio.x);
        float h = Mathf.Clamp01(innerWindowRatio.y);
        float cx = 0.5f;
        float cy = Mathf.Clamp(innerWindowCenterY, h * 0.5f, 1f - h * 0.5f);

        rt.anchorMin = new Vector2(cx - w * 0.5f, cy - h * 0.5f);
        rt.anchorMax = new Vector2(cx + w * 0.5f, cy + h * 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
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
