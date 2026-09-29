//============================================================
// BossPanelView.cs
// - 보스 HP 슬라이더/텍스트
// - 보스 이름 텍스트
// - 프리팹 연결이 비어 있으면 런타임에 최소 UI를 자동 구성
// - 페이즈 바(IBossHudSource 보스): 총 체력을 페이즈 경계로 나눠 페이즈마다 한 줄로 보여 준다.
//   한 줄을 다 깎으면 바가 깨지고(번쩍 · 흔들림 · 빈 바 붉은 맥동), 전환 연출이 PlayPageRefill을 부르면 다시 차오르고
//   가득 찬 순간 한 번 더 번쩍인다. 줄 수는 미리 보여 주지 않는다 — 전환을 겪어서 안다(09-29 사용자).
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

    [Header("페이즈 바 (IBossHudSource 보스만)")]
    [SerializeField] private Color refillGlowColor   = new Color(1.00f, 0.95f, 0.80f, 0.65f);
    [SerializeField] private Color pageFlashColor    = new Color(1.00f, 1.00f, 1.00f, 0.90f);
    [SerializeField] private Color breakPulseColor   = new Color(0.85f, 0.10f, 0.12f, 0.55f);

    [Header("무적 · 무방비 (IBossHudSource 보스만)")]
    [SerializeField] private Color invulnerableColor = new Color(0.55f, 0.62f, 0.80f, 0.72f);
    [SerializeField] private Color vulnerableColor   = new Color(1.00f, 0.85f, 0.35f, 0.55f);
    [SerializeField] private float vulnerablePulseHz = 3f;

    private int _maxHp;
    private bool _skinApplied;
    private bool _textStyled;
    private RectTransform _nameBand;   // 이름 뒤 어두운 띠(09-27)

    private const float NameFontSize = 26f;
    private const float HpFontSize   = 16f;
    private const float NameLift     = 14f;   // 테두리 가운데 장식 위로 올린다(8은 장식 꼭대기에 닿았다)

    // 페이즈 바 연출(09-29) — 줄이 다 깎인 순간 · 다시 가득 찬 순간
    private const float PageFlashSeconds  = 0.35f;
    private const float BreakShakeSeconds = 0.35f;
    private const float BreakShakePixels  = 7f;
    private const float BreakPulseHz      = 2.5f;
    private const float FullPunchSeconds  = 0.3f;
    private const float FullPunchScale    = 0.05f;
    private static readonly Color NameInk = new(0.96f, 0.92f, 0.84f, 1f);
    private static readonly Color HpInk   = new(0.93f, 0.90f, 0.84f, 0.95f);
    private Image _invulnerableOverlay;
    private Image _vulnerableOverlay;
    private float _vulnerableRemaining;

    // 페이즈 바
    private float[] _pageBounds;          // 경계(내림차순). null이면 한 줄
    private int     _page = 1;
    private int     _lastHp;
    private bool    _refilling;
    private float   _refillT;
    private float   _refillSeconds;
    private Image   _refillGlow;
    private float   _shown = 1f;           // 지금 채움 비율 — 덮개·빛이 이만큼만 덮는다
    private Image   _pageFlash;            // 바 창 전체 번쩍임(깨짐 · 가득)
    private Image   _breakPulseImage;      // 빈 바 붉은 맥동 — 다시 차오를 때까지
    private bool    _breakPulse;
    private int     _brokenPage;           // 이 줄은 이미 깨졌다(같은 순간 Init · SetHP가 겹쳐도 한 번만)
    private bool    _pageArmed;            // 이 줄에서 진짜 체력(가득 미만 · 경계 위)을 봤다 — 연결 순간의 0을 「다 깎임」으로 오인하지 않게
    private float   _flashT = -1f;         // 음수 = 꺼짐
    private float   _shakeT = -1f;
    private float   _punchT = -1f;
    private Vector2 _shakeBase;
    private Vector3 _punchBase = Vector3.one;
    private RectTransform _rect;

    private bool HasSkin => bossTrackSprite != null || bossFillSprite != null || bossFrameSprite != null;

    private void Awake()
    {
        EnsureWired();
        _rect = transform as RectTransform;   // 런타임 폴백이 RectTransform을 붙인 뒤에 잡는다
    }

    private void Update()
    {
        TickRefill();
        TickPageFx();
        if (_vulnerableRemaining <= 0f) return;

        _vulnerableRemaining -= Time.unscaledDeltaTime;
        if (_vulnerableOverlay == null) return;
        if (_vulnerableRemaining <= 0f)
        {
            _vulnerableOverlay.enabled = false;
            return;
        }
        float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * vulnerablePulseHz * Mathf.PI * 2f);
        var c = vulnerableColor;
        c.a *= Mathf.Lerp(0.35f, 1f, k);
        _vulnerableOverlay.color = c;
    }

    /// <summary>
    /// 페이즈 바 설정 — <paramref name="bounds"/>(HP 비율 경계, 내림차순)로 총 체력을 나눠 <paramref name="page"/>번째 줄을 보여 준다.
    /// 경계 눈금선은 그리지 않는다(한 줄 = 한 페이즈). null이면 예전처럼 한 줄 전체.
    /// </summary>
    public void SetPages(float[] bounds, int page)
    {
        EnsureWired();
        _pageBounds = bounds != null && bounds.Length > 0 ? bounds : null;
        int newPage = Mathf.Clamp(page, 1, PageCount);
        if (newPage != _page) _refilling = false;   // 페이즈가 바뀌었으면 진행 중인 차오름은 끝
        _page = newPage;
        if (!_refilling) Render();
    }

    /// <summary>다음 페이즈 바가 <paramref name="seconds"/> 동안 0에서 가득 차오른다(전환 연출과 함께).</summary>
    public void PlayPageRefill(int page, float seconds)
    {
        EnsureWired();
        _page          = Mathf.Clamp(page, 1, PageCount);
        _refilling     = true;
        _refillT       = 0f;
        _refillSeconds = Mathf.Max(0.1f, seconds);
        _breakPulse    = false;
        _pageArmed     = false;
        ShowFill(0f, 0);

        // 빛도 채움 자식 — 차오르는 앞머리를 따라간다.
        var root = hpSlider != null ? hpSlider.fillRect : null;
        if (root == null) return;
        if (_refillGlow == null)
        {
            _refillGlow = EnsureImage("RefillGlow", root, refillGlowColor);
        }
        _refillGlow.enabled = true;
        FitToFill(_refillGlow);
    }

    /// <summary>무적 표시 — 채움 위에 차가운 덮개.</summary>
    public void SetInvulnerable(bool on)
    {
        EnsureWired();
        // 덮개는 채움(fillRect) 자식 — 채워진 만큼만 덮는다(빈 바 전체가 회색으로 차 보이지 않게).
        var root = hpSlider != null ? hpSlider.fillRect : null;
        if (root == null) return;
        if (_invulnerableOverlay == null)
        {
            _invulnerableOverlay = EnsureImage("InvulnerableOverlay", root, invulnerableColor);
        }
        _invulnerableOverlay.enabled = on;
        FitToFill(_invulnerableOverlay);
        if (on && _vulnerableOverlay != null) _vulnerableOverlay.enabled = false;
    }

    /// <summary>무방비(반격 기회) 표시 — <paramref name="seconds"/> 동안 채움이 금빛으로 맥동.</summary>
    public void FlashVulnerable(float seconds)
    {
        EnsureWired();
        var root = hpSlider != null ? hpSlider.fillRect : null;
        if (root == null || seconds <= 0f) return;
        if (_vulnerableOverlay == null)
        {
            _vulnerableOverlay = EnsureImage("VulnerableOverlay", root, vulnerableColor);
        }
        _vulnerableOverlay.enabled = true;
        FitToFill(_vulnerableOverlay);
        _vulnerableRemaining       = seconds;
    }

    public void Init(int maxHp, string bossName)
    {
        EnsureWired();

        _maxHp = Mathf.Max(1, maxHp);

        _lastHp = _maxHp;
        _pageBounds = null;
        _page       = 1;
        _refilling  = false;

        if (nameText != null)
            nameText.text = bossName ?? string.Empty;
        FitNameBand();
        Render();
    }

    /// <summary>
    /// 새 보스 — 지난 전투의 깨짐 · 번쩍임 · 흔들림을 걷는다. 같은 보스를 다시 그리는 <see cref="Init"/>(컷신 모드 전환 등)은
    /// 이걸 부르지 않는다 — 전환 도중 Init이 깨진 줄을 되살려 한 번 더 터뜨리지 않게.
    /// </summary>
    public void ResetPageFx()
    {
        _brokenPage = 0;
        _pageArmed  = false;
        _breakPulse = false;
        _flashT     = -1f;
        if (_pageFlash != null)       _pageFlash.enabled = false;
        if (_breakPulseImage != null) _breakPulseImage.enabled = false;
        if (_rect != null)
        {
            if (_shakeT >= 0f) _rect.anchoredPosition = _shakeBase;
            if (_punchT >= 0f) _rect.localScale       = _punchBase;
        }
        _shakeT = -1f;
        _punchT = -1f;
    }

    public void SetHP(int hp, int maxHp)
    {
        EnsureWired();

        _maxHp  = Mathf.Max(1, maxHp);
        _lastHp = Mathf.Clamp(hp, 0, _maxHp);
        if (_refilling) return;   // 차오르는 동안은 연출이 바를 쥔다(전환 중 HP는 경계에 붙잡혀 있다)
        Render();
    }

    // ── 페이즈 바 ─────────────────────────────────────────────

    private int PageCount => _pageBounds != null ? _pageBounds.Length + 1 : 1;

    /// <summary>현재 페이즈 줄의 (위 경계, 아래 경계) — HP 비율.</summary>
    private void PageSegment(out float hi, out float lo)
    {
        hi = 1f;
        lo = 0f;
        if (_pageBounds == null) return;
        int i = _page - 1;
        if (i > 0)                  hi = Mathf.Clamp01(_pageBounds[i - 1]);
        if (i < _pageBounds.Length) lo = Mathf.Clamp01(_pageBounds[i]);
        if (hi - lo < 0.0001f) hi = lo + 0.0001f;
    }

    /// <summary>현재 HP를 현재 페이즈 줄로 그린다.</summary>
    private void Render()
    {
        PageSegment(out float hi, out float lo);
        float ratio  = (float)_lastHp / _maxHp;
        float shown  = Mathf.Clamp01((ratio - lo) / (hi - lo));
        int   segMax = Mathf.Max(1, Mathf.RoundToInt((hi - lo) * _maxHp));
        ShowFill(shown, segMax);
        // 경계에 붙잡힌 체력(페이지 몫 올림)은 경계와 1 미만 차이 — 표시 반올림과 무관하게 「다 깎였다」로 본다
        NotePageEmpty(_lastHp - lo * _maxHp < 1f);
    }

    /// <summary>
    /// 마지막이 아닌 줄이 다 깎였다 — 페이지 경계에서 체력이 붙잡힌 순간. 바를 깨뜨린다(한 줄에 한 번).
    /// 붉은 맥동은 다시 차오를 때(<see cref="PlayPageRefill"/>) · 새 보스(<see cref="ResetPageFx"/>)에만 끈다 —
    /// 컷신 진입의 다시 그리기(Init → 가득 찬 값으로 한 번 그림)가 맥동을 꺼 버렸다(09-29 캡처).
    /// 그 줄에서 진짜 체력(가득 미만 · 경계 위)을 한 번은 봐야 깨진다 — 화룡은 HUD에 붙는 순간 체력이 아직 0이라
    /// 전투 시작부터 줄이 깨지고 맥동이 켜졌다(09-29 캡처). Init의 자리 채움 값(가득)은 무장시키지 않는다.
    /// </summary>
    private void NotePageEmpty(bool empty)
    {
        if (_page >= PageCount) return;
        if (!empty)
        {
            if (_lastHp < _maxHp) _pageArmed = true;
            return;
        }
        if (_pageArmed && _brokenPage != _page) PlayPageBreak();
    }

    /// <summary>줄이 깨지는 순간 — 바 창 전체 번쩍 · 짧은 흔들림 · 빈 바 붉은 맥동(다시 차오를 때까지).</summary>
    private void PlayPageBreak()
    {
        Debug.Log($"[BossPanel] 줄 {_page} 깨짐 — 다시 차오를 때까지 붉은 맥동");
        _brokenPage = _page;
        _breakPulse = true;
        EnsureBarOverlay(ref _breakPulseImage, "BreakPulse", breakPulseColor);
        StartFlash();
        if (_rect == null) return;
        if (_shakeT < 0f) _shakeBase = _rect.anchoredPosition;
        _shakeT = 0f;
    }

    private void StartFlash()
    {
        if (EnsureBarOverlay(ref _pageFlash, "PageFlash", pageFlashColor) == null) return;
        _pageFlash.transform.SetAsLastSibling();
        _flashT = 0f;
    }

    /// <summary>바 창 전체(Fill Area)를 덮는 판 — 채움 비율과 무관하게 빈 바까지 덮는다.</summary>
    private Image EnsureBarOverlay(ref Image image, string name, Color color)
    {
        if (image != null) return image;
        var area = hpSlider != null && hpSlider.fillRect != null ? hpSlider.fillRect.parent : null;
        if (area == null) return null;
        image = EnsureImage(name, area, color);
        Stretch((RectTransform)image.transform);
        image.enabled = false;
        return image;
    }

    /// <summary>번쩍임 · 붉은 맥동 · 흔들림 · 가득 찬 순간의 커짐 — 실시간(전환 슬로모와 무관).</summary>
    private void TickPageFx()
    {
        float dt = Time.unscaledDeltaTime;

        if (_flashT >= 0f && _pageFlash != null)
        {
            _flashT += dt;
            float k = Mathf.Clamp01(_flashT / PageFlashSeconds);
            var c = pageFlashColor;
            c.a *= (1f - k) * (1f - k);
            _pageFlash.color   = c;
            _pageFlash.enabled = k < 1f;
            if (k >= 1f) _flashT = -1f;
        }

        if (_breakPulseImage != null)
        {
            _breakPulseImage.enabled = _breakPulse;
            if (_breakPulse)
            {
                float s = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * BreakPulseHz * Mathf.PI * 2f);
                var c = breakPulseColor;
                c.a *= Mathf.Lerp(0.3f, 1f, s);
                _breakPulseImage.color = c;
            }
        }

        if (_rect == null) return;
        if (_shakeT >= 0f)
        {
            _shakeT += dt;
            float k   = Mathf.Clamp01(_shakeT / BreakShakeSeconds);
            float amp = BreakShakePixels * (1f - k);
            _rect.anchoredPosition = _shakeBase + new Vector2(Mathf.Sin(_shakeT * 95f) * amp, Mathf.Cos(_shakeT * 70f) * amp * 0.4f);
            if (k >= 1f)
            {
                _rect.anchoredPosition = _shakeBase;
                _shakeT = -1f;
            }
        }
        if (_punchT >= 0f)
        {
            _punchT += dt;
            float k = Mathf.Clamp01(_punchT / FullPunchSeconds);
            _rect.localScale = _punchBase * (1f + FullPunchScale * Mathf.Sin(k * Mathf.PI));
            if (k >= 1f)
            {
                _rect.localScale = _punchBase;
                _punchT = -1f;
            }
        }
    }

    private void ShowFill(float shown, int segMax)
    {
        _shown = shown;
        FitToFill(_invulnerableOverlay);
        FitToFill(_vulnerableOverlay);
        FitToFill(_refillGlow);
        if (hpSlider != null)
        {
            hpSlider.minValue = 0f;
            hpSlider.maxValue = 1f;
            hpSlider.value    = shown;
        }
        if (hpText != null && segMax > 0)
            hpText.text = $"{Mathf.RoundToInt(shown * segMax)} / {segMax}";
        EnsureTextVisible();
        ApplyFillColor(shown);
    }

    private void TickRefill()
    {
        if (!_refilling) return;
        _refillT += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(_refillT / _refillSeconds);
        float e = 1f - (1f - k) * (1f - k) * (1f - k);   // 빠르게 차고 끝에서 천천히

        PageSegment(out float hi, out float lo);
        ShowFill(e, Mathf.Max(1, Mathf.RoundToInt((hi - lo) * _maxHp)));
        if (_refillGlow != null)
        {
            var c = refillGlowColor;
            c.a *= Mathf.Lerp(0.35f, 1f, 0.5f + 0.5f * Mathf.Sin(_refillT * 14f)) * (1f - k * 0.6f);
            _refillGlow.color = c;
        }

        if (k < 1f) return;
        _refilling = false;
        if (_refillGlow != null) _refillGlow.enabled = false;
        Render();

        // 가득 찬 순간 — 바 창 전체 번쩍 + 살짝 커졌다 돌아온다(포효와 같은 박자)
        StartFlash();
        if (_rect == null) return;
        if (_punchT < 0f) _punchBase = _rect.localScale;
        _punchT = 0f;
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
        StyleTexts();
    }

    /// <summary>
    /// 이름·수치 글자 — 09-27 사용자 「너무 굵고 뒷배경이 아쉽다」. 가짜 굵게를 빼고(기본 폰트가 이미 굵다),
    /// 두꺼운 테두리 대신 얇은 테두리 + 부드러운 그림자, 이름은 자간을 두고 장식 위로 조금 올린 뒤 어두운 띠 위에 앉힌다.
    /// 글자 칸 높이가 글자 크기와 같아 넘침으로 잡히던 것도 여기서 넉넉히 준다. 한 번만.
    /// </summary>
    private void StyleTexts()
    {
        if (_textStyled) return;
        _textStyled = true;

        if (nameText != null)
        {
            nameText.fontStyle        = FontStyles.Normal;
            nameText.fontSize         = NameFontSize;
            nameText.characterSpacing = 4f;
            nameText.color            = NameInk;
            nameText.enableAutoSizing = false;
            nameText.textWrappingMode = TextWrappingModes.NoWrap;
            nameText.overflowMode     = TextOverflowModes.Overflow;
            var nrt = nameText.rectTransform;
            nrt.sizeDelta        = new Vector2(Mathf.Max(nrt.sizeDelta.x, 640f), 40f);
            nrt.anchoredPosition = nrt.anchoredPosition + new Vector2(0f, NameLift);
            TMPOutlineHelper.ApplySoftShadow(nameText);

            var bandGo = new GameObject("NameBand", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bandGo.transform.SetParent(nrt.parent, false);
            bandGo.transform.SetSiblingIndex(nrt.GetSiblingIndex());   // 이름 바로 뒤
            _nameBand = (RectTransform)bandGo.transform;
            _nameBand.anchorMin = nrt.anchorMin; _nameBand.anchorMax = nrt.anchorMax; _nameBand.pivot = nrt.pivot;
            var band = bandGo.GetComponent<Image>();
            band.sprite = UIProceduralSprites.RoundedRect(radius: 14f, feather: 10f, size: 64);
            band.type   = Image.Type.Sliced;
            band.color  = new Color(0.02f, 0.02f, 0.035f, 0.55f);
            band.raycastTarget = false;
        }

        if (hpText != null)
        {
            hpText.fontStyle        = FontStyles.Normal;
            hpText.fontSize         = HpFontSize;
            hpText.enableAutoSizing = false;
            hpText.color            = HpInk;
            hpText.characterSpacing = 1f;
            TMPOutlineHelper.ApplySoftShadow(hpText);
        }
        FitNameBand();
    }

    /// <summary>이름 띠 폭을 이름에 맞춘다(이름이 바뀔 때마다).</summary>
    private void FitNameBand()
    {
        if (_nameBand == null || nameText == null) return;
        var nrt = nameText.rectTransform;
        float w = string.IsNullOrEmpty(nameText.text) ? 0f : nameText.GetPreferredValues(nameText.text).x + 72f;
        _nameBand.gameObject.SetActive(w > 0f);
        _nameBand.sizeDelta        = new Vector2(w, 36f);
        _nameBand.anchoredPosition = nrt.anchoredPosition;
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

    /// <summary>
    /// 덮개를 채워진 폭만큼만 — 채움 이미지는 Filled(fillAmount)라 fillRect 자체는 늘 전체 폭이다.
    /// 덮개를 fillRect 자식으로 두고 오른쪽 앵커를 채움 비율에 맞춘다(빈 바가 회색으로 가득 차 보이던 문제).
    /// </summary>
    private void FitToFill(Image overlay)
    {
        if (overlay == null) return;
        var rt = (RectTransform)overlay.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = new Vector2(Mathf.Clamp01(_shown), 1f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
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
