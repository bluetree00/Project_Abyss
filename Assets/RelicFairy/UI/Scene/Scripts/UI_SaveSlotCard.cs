using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 저장 슬롯 카드 한 장 — 10-23 새 세이브 창(사용자 「너무 튀는 레거시 리소스라 깔끔하게 톤에 맞게 · 세이브 데이터를 잘 보여 줄 만한 내용으로 배경이랑 틀」).
/// 패널(<see cref="UI_SaveSlotPanel"/>)이 런타임에 짓는다(<see cref="Create"/>) — 옛 LobbyRoot 카드 계층(보라 연기 틀 · 동판 버튼 그림)은 패널이 걷는다.
///
/// 흑요석 카드 틀(힉스필드 · 9칸) 위에 위에서 아래로:
///  머리 — 기록 n · 시기 칩(봉인된 세계 / 해방된 세계 / 악몽)
///  자리 — 어디서 멈췄나(런 = 챕터 이름 · 거점 = 베이스캠프) + 네 칸 길(런 = 지나온 챕터와 지금 챕터 · 거점 = 봉인/처치한 보스)
///  줄   — 런 = 유물 · 무기 · 체력 · 포션 / 거점 = 최고 챕터 · 완주 · 심연의 정수
///  발   — 플레이 시간 · 시도 · 마지막 저장 (+ 전투 중에 끈 런이면 「이어하면 사망으로 정산」 경고)
///  [이어하기 / 새 게임] · 기록 삭제
/// 금색은 「누를 것 · 남은 것」에만(톤 v2) — 이어하기 버튼 · 지나온 길 · 마우스를 올린 카드의 안쪽 선.
/// </summary>
public class UI_SaveSlotCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    // ─────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────

    public const float Width  = 480f;
    public const float Height = 680f;

    // 카드 안 배치(목업 px · 카드 왼쪽 위 기준). 틀 테는 화면 ≈ 18px · 쐐기돌 ≈ 29px — 내용은 44px 안쪽에서 시작한다.
    private const float Pad         = 44f;
    private const float InnerW      = Width - Pad * 2f;
    private const float FrameCorner = 32f;     // 틀 9칸 경계 64 → 화면 32(상점 창 틀과 같은 ½)
    private const float HeadY       = 34f;
    private const float CaptionY    = 82f;
    private const float TitleY      = 106f;
    private const float TrackY      = 162f;
    private const float PipSize     = 11f;
    private const float PipStep     = 30f;
    private const float RuleY1      = 198f;
    private const float RowY        = 212f, RowStep  = 34f, RowH  = 26f, RowLabelW = 120f;
    private const float RuleY2      = 362f;
    private const float FootY       = 376f, FootStep = 28f, FootH = 22f;
    private const float NoteY       = 470f, NoteH    = 58f;
    private const float CtaY        = 546f, CtaH     = 56f;
    private const float DeleteY     = 612f, DeleteW  = 160f, DeleteH = 28f;
    private const int   RowMax      = 4;
    private const int   FootMax     = 3;
    private const int   ChapterMax  = 4;
    private const float EnterSec    = 0.26f, EnterRise = 18f;
    private const float GlowSec     = 0.16f, GlowAlpha = 0.55f;

    // ─────────────────────────────────────────────────────────
    // Static
    // ─────────────────────────────────────────────────────────

    private static readonly Color Ink       = UITheme.Thin.Ink;
    private static readonly Color Mute      = UITheme.Thin.InkA(0.62f);
    private static readonly Color Faint     = UITheme.Thin.InkA(0.40f);
    private static readonly Color Gold      = UITheme.Thin.Gold;
    private static readonly Color Line      = UITheme.Thin.LineIdle;
    private static readonly Color PipLit    = UITheme.Thin.WithA(UITheme.Thin.Gold, 0.90f);
    private static readonly Color PipOff    = UITheme.Thin.InkA(0.30f);
    private static readonly Color Danger    = UITheme.Thin.Danger;
    private static readonly Color FreeBlue  = new(0.62f, 0.78f, 1f, 1f);                 // 해방된 세계 — 차가운 달빛
    private static readonly Color CtaFill   = new(0.91f, 0.73f, 0.33f, 0.16f);          // 이어하기 — 호박빛 판(상점 [구매]와 같은 결)
    private static readonly Color CtaEdge   = UITheme.Thin.WithA(UITheme.Thin.Gold, 0.85f);
    private static readonly Color NewFill   = new(0.04f, 0.045f, 0.075f, 0.80f);       // 새 게임 — 어두운 판 + 옅은 금선
    private static readonly Color NewEdge   = UITheme.Thin.LineGold;
    private static readonly Color FrameFill = new(0.050f, 0.050f, 0.082f, 0.96f);       // 틀 그림이 없을 때

    // 챕터 데이터(ChapterDataSO.chapterName)는 런 부트스트래퍼가 들고 있어 로비엔 없을 수 있다 — 그땐 이 이름(Chapter1~4.asset과 같다).
    private static readonly string[] FallbackChapterNames = { "황혼의 숲", "용암 지대", "신성한 성역", "천공의 대제단" };

    // ─────────────────────────────────────────────────────────
    // Private
    // ─────────────────────────────────────────────────────────

    private RectTransform _body;
    private CanvasGroup   _group;
    private Image         _glow, _eraChip, _ctaFill, _ctaEdge;
    private TMP_Text      _slotText, _eraText, _caption, _title, _trackText, _note, _ctaText;
    private GameObject    _filledRoot, _emptyRoot;
    private Button        _startButton, _deleteButton;

    private readonly Image[]    _pips      = new Image[ChapterMax];
    private readonly Image[]    _pipRings  = new Image[ChapterMax];
    private readonly TMP_Text[] _rowLabel  = new TMP_Text[RowMax];
    private readonly TMP_Text[] _rowValue  = new TMP_Text[RowMax];
    private readonly TMP_Text[] _footLabel = new TMP_Text[FootMax];
    private readonly TMP_Text[] _footValue = new TMP_Text[FootMax];

    private int  _slotIndex;
    private bool _hasSave;
    private int  _rowCount, _footCount;
    private Action<int, bool> _onStart;
    private Action<int>       _onDelete;
    private float _glowNow, _glowTarget;

    // ─────────────────────────────────────────────────────────
    // Properties
    // ─────────────────────────────────────────────────────────

    public int  SlotIndex => _slotIndex;
    public bool HasSave   => _hasSave;

    // ─────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────

    private void Update()
    {
        if (_glow == null || Mathf.Approximately(_glowNow, _glowTarget)) return;
        _glowNow = Mathf.MoveTowards(_glowNow, _glowTarget, Time.unscaledDeltaTime / GlowSec);
        _glow.color = UITheme.Thin.WithA(Gold, GlowAlpha * _glowNow);
    }

    private void OnDisable()
    {
        _glowTarget = _glowNow = 0f;
        if (_glow != null) _glow.color = UITheme.Thin.WithA(Gold, 0f);
    }

    // ─────────────────────────────────────────────────────────
    // Public Methods
    // ─────────────────────────────────────────────────────────

    /// <summary>카드 한 장을 짓는다 — 부모 왼쪽 위 기준 (x, y)에 <see cref="Width"/>×<see cref="Height"/>. 틀 그림이 없으면 코드 판.</summary>
    public static UI_SaveSlotCard Create(Transform parent, int index, float x, float y, Sprite frame)
    {
        var root = SaveSlotUI.Box(parent, $"SlotCard_{index}", x, y, Width, Height);
        var card = root.gameObject.AddComponent<UI_SaveSlotCard>();
        card.Build(root, frame);
        return card;
    }

    /// <summary>
    /// 카드를 채운다. onStart(slotIndex, hasSave) — 이어하기 · 새 게임. onDelete(slotIndex) — 기록이 있을 때만 보이는 「기록 삭제」.
    /// </summary>
    public void Setup(RunSaveData data, int slotIndex, Action<int, bool> onStart, Action<int> onDelete = null)
    {
        _slotIndex = slotIndex;
        _onStart   = onStart;
        _onDelete  = onDelete;
        _hasSave   = data?.hasActiveRun == true;

        _slotText.text = $"기록 {slotIndex + 1}";
        _filledRoot.SetActive(_hasSave);
        _emptyRoot.SetActive(!_hasSave);
        _deleteButton.gameObject.SetActive(_hasSave && onDelete != null);
        StyleCta(_hasSave);

        if (!_hasSave)
        {
            _eraChip.gameObject.SetActive(false);
            return;
        }

        var meta = RunProgressManager.Instance?.PeekMeta(slotIndex);
        var era  = EraOf(meta);
        SetEra(era);

        _rowCount = 0;
        if (data.isInStartRoom) FillHub(meta, era);
        else                    FillRun(data);
        for (int i = _rowCount; i < RowMax; i++) ShowRow(_rowLabel[i], _rowValue[i], false);

        _footCount = 0;
        AddFoot("플레이 시간", FormatPlayTime(data.playSeconds));
        AddFoot("시도", $"{Mathf.Max(1, data.retryCount)}회");
        string saved = FormatSavedAt(data.savedAt);
        if (saved != null) AddFoot("마지막 저장", saved);
        for (int i = _footCount; i < FootMax; i++) ShowRow(_footLabel[i], _footValue[i], false);

        // Hades식 저장(10-10) — 피해를 받고 다음 저장 전에 끈 런은 이어하면 사망으로 정산된다. 누르기 전에 알린다.
        bool locked = !data.isInStartRoom && data.combatLocked;
        _note.gameObject.SetActive(locked);
        if (locked) _note.text = "전투 중에 끈 기록이다 — 이어하면 이번 런은 사망으로 정산된다";
    }

    /// <summary>열릴 때 · 내용이 바뀔 때 — 아래 18px에서 떠오르며 밝아진다(0.26초 · 감속). 취소되면 제자리로 둔다.</summary>
    public async UniTaskVoid PlayEnterAsync(float delay, CancellationToken ct)
    {
        if (_group == null || _body == null) return;
        _group.alpha = 0f;
        _body.anchoredPosition = new Vector2(0f, -EnterRise);
        try
        {
            if (delay > 0f) await UniTask.Delay(TimeSpan.FromSeconds(delay), DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, ct);
            await UIMotion.Tween(EnterSec, k =>
            {
                _group.alpha = k;
                _body.anchoredPosition = new Vector2(0f, -EnterRise * (1f - k));
            }, ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_group != null) _group.alpha = 1f;
            if (_body != null) _body.anchoredPosition = Vector2.zero;
        }
    }

    public void OnPointerEnter(PointerEventData eventData) => _glowTarget = 1f;
    public void OnPointerExit(PointerEventData eventData)  => _glowTarget = 0f;

    // ─────────────────────────────────────────────────────────
    // Private Methods — 짓기
    // ─────────────────────────────────────────────────────────

    private void Build(RectTransform root, Sprite frame)
    {
        _group = root.gameObject.AddComponent<CanvasGroup>();

        // 몸통 — 들어올 때 몸통만 움직인다(카드 자리 · 마우스 판정은 그대로).
        _body = (RectTransform)ShopUIStyle.MakeRect(root, "Body").transform;
        ShopUIStyle.Stretch(_body);

        var plate = ShopUIStyle.MakeImage(_body, "Frame", Color.white, raycast: true);   // 마우스 올림 판정도 이 판이 받는다
        ShopUIStyle.Stretch(plate.rectTransform);
        if (frame != null)
        {
            plate.sprite = frame;
            plate.type   = Image.Type.Sliced;
            plate.pixelsPerUnitMultiplier = frame.border.x > 0f ? frame.border.x / FrameCorner : 1f;
        }
        else UITheme.StylePanel(plate, FrameFill, UITheme.Thin.LineGold, 6f);

        // 마우스를 올리면 틀 안쪽 금 상감선 바로 안에 가는 금선이 차오른다(0.16초)
        _glow = ShopUIStyle.MakeImage(_body, "Glow", UITheme.Thin.WithA(Gold, 0f));
        _glow.sprite = UIProceduralSprites.RoundedOutline(radius: 3f, stroke: 1f);
        _glow.type   = Image.Type.Sliced;
        ShopUIStyle.Stretch(_glow.rectTransform, 16f);   // 테 그림은 4px 안쪽에 선이 있다 → 선은 카드 가장자리에서 20px

        BuildHead();
        BuildFilled();
        BuildEmpty();
        BuildActions();
    }

    private void BuildHead()
    {
        _slotText = SaveSlotUI.Text(_body, "Slot", Pad, HeadY, 200f, 24f, 15f, UIWeight.Medium, Mute);
        _slotText.characterSpacing = 3f;

        // 시기 칩 — 오른쪽 끝에 붙어 글 길이만큼 늘어난다. 테 그림은 칸 4px 안쪽에 선이 있어 칸을 4px 바깥으로 잡는다(보이는 선 = 오른쪽 값 끝 · 머리 줄 높이 24).
        _eraChip = ShopUIStyle.MakeImage(_body, "Era", Line);
        _eraChip.sprite = UIProceduralSprites.RoundedOutline(radius: 4f, stroke: 1f);
        _eraChip.type   = Image.Type.Sliced;
        var crt = _eraChip.rectTransform;
        crt.anchorMin = crt.anchorMax = new Vector2(0f, 1f);
        crt.pivot = new Vector2(1f, 1f);
        crt.anchoredPosition = new Vector2(Width - Pad + 4f, -(HeadY - 4f));
        crt.sizeDelta = new Vector2(100f, 32f);
        var hlg = _eraChip.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(16, 16, 4, 4);
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;
        var fitter = _eraChip.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        _eraText = ShopUIStyle.MakeText(_eraChip.transform, "Label", 13f, FontStyles.Normal, TextAlignmentOptions.Center, Mute);
        _eraText.enableAutoSizing = false;
        UITheme.StyleThinText(_eraText, UIWeight.Medium, 13f, Mute);
        _eraText.textWrappingMode = TextWrappingModes.NoWrap;
        _eraText.characterSpacing = 2f;
    }

    private void BuildFilled()
    {
        _filledRoot = ShopUIStyle.MakeRect(_body, "FilledRoot");
        var f = (RectTransform)_filledRoot.transform;
        ShopUIStyle.Stretch(f);

        _caption = SaveSlotUI.Text(f, "Caption", Pad, CaptionY, InnerW, 22f, 15f, UIWeight.Regular, Mute);
        _title   = SaveSlotUI.Text(f, "Title",   Pad, TitleY,   InnerW, 42f, 28f, UIWeight.Medium,  Ink);

        // 네 칸 길 — 마름모 넷을 가는 선이 잇는다
        float cy = TrackY + PipSize * 0.5f;
        SaveSlotUI.Img(f, "TrackLine", Pad + PipSize * 0.5f, cy, PipStep * (ChapterMax - 1), 1f, Line);
        for (int i = 0; i < ChapterMax; i++)
        {
            float cx = Pad + PipSize * 0.5f + PipStep * i;
            _pipRings[i] = SaveSlotUI.Diamond(f, $"Ring{i + 1}", cx, cy, PipSize + 8f, Gold, outline: true);
            _pips[i]     = SaveSlotUI.Diamond(f, $"Pip{i + 1}",  cx, cy, PipSize, PipOff, outline: false);
        }
        float tx = Pad + PipStep * (ChapterMax - 1) + PipSize + 18f;
        _trackText = SaveSlotUI.Text(f, "TrackText", tx, TrackY - 6f, Width - Pad - tx, 22f, 15f, UIWeight.Regular, Mute);

        SaveSlotUI.Img(f, "Rule1", Pad, RuleY1, InnerW, 1f, Line);
        for (int i = 0; i < RowMax; i++)
        {
            float y = RowY + RowStep * i;
            _rowLabel[i] = SaveSlotUI.Text(f, $"RowLabel{i}", Pad, y, RowLabelW, RowH, 15f, UIWeight.Regular, Mute);
            _rowValue[i] = SaveSlotUI.Text(f, $"RowValue{i}", Pad + RowLabelW, y, InnerW - RowLabelW, RowH, 17f, UIWeight.Regular, Ink,
                                           TextAlignmentOptions.MidlineRight);
        }

        SaveSlotUI.Img(f, "Rule2", Pad, RuleY2, InnerW, 1f, Line);
        for (int i = 0; i < FootMax; i++)
        {
            float y = FootY + FootStep * i;
            _footLabel[i] = SaveSlotUI.Text(f, $"FootLabel{i}", Pad, y, RowLabelW, FootH, 15f, UIWeight.Regular, Faint);
            _footValue[i] = SaveSlotUI.Text(f, $"FootValue{i}", Pad + RowLabelW, y, InnerW - RowLabelW, FootH, 15f, UIWeight.Regular, Mute,
                                            TextAlignmentOptions.MidlineRight);
        }

        _note = SaveSlotUI.Text(f, "Note", Pad, NoteY, InnerW, NoteH, 15f, UIWeight.Regular, Danger, TextAlignmentOptions.Center);
        _note.textWrappingMode = TextWrappingModes.Normal;
        _note.lineSpacing      = 4f;
        _note.gameObject.SetActive(false);
    }

    private void BuildEmpty()
    {
        _emptyRoot = ShopUIStyle.MakeRect(_body, "EmptyRoot");
        var e = (RectTransform)_emptyRoot.transform;
        ShopUIStyle.Stretch(e);

        SaveSlotUI.Diamond(e, "Mark",    Width * 0.5f, 252f, 34f, Faint, outline: true);
        SaveSlotUI.Diamond(e, "MarkGem", Width * 0.5f, 252f, 8f,  UITheme.Thin.LineActive, outline: false);
        var title = SaveSlotUI.Text(e, "Txt_Empty", Pad, 292f, InnerW, 38f, 26f, UIWeight.Medium, UITheme.Thin.InkA(0.80f), TextAlignmentOptions.Center);
        title.text = "빈 기록";
        var sub = SaveSlotUI.Text(e, "Txt_EmptySub", Pad, 338f, InnerW, 24f, 15f, UIWeight.Light, Faint, TextAlignmentOptions.Center);
        sub.text = "프롤로그부터 새 이야기를 시작한다";
    }

    private void BuildActions()
    {
        _startButton = SaveSlotUI.MakeButton(_body, "Btn_Start", Pad, CtaY, InnerW, CtaH, CtaFill, CtaEdge, "이어하기", 21f, Gold, out _ctaFill, out _ctaEdge, out _ctaText);
        _startButton.onClick.AddListener(OnClickStart);

        // 지우기는 조용히 — 옅은 붉은 글자 한 줄(마우스를 올리면 짙어진다). 누르면 패널이 한 번 더 묻는다.
        var del = ShopUIStyle.MakeImage(_body, "Btn_Delete", new Color(1f, 1f, 1f, 0f), raycast: true);
        SaveSlotUI.Put(del.rectTransform, (Width - DeleteW) * 0.5f, DeleteY, DeleteW, DeleteH);
        var label = SaveSlotUI.Text(del.transform, "Txt_Delete", 0f, 0f, DeleteW, DeleteH, 15f, UIWeight.Regular, Danger, TextAlignmentOptions.Center);
        label.text = "기록 삭제";
        label.characterSpacing = 2f;
        _deleteButton = del.gameObject.AddComponent<Button>();
        _deleteButton.targetGraphic = label;
        _deleteButton.transition    = Selectable.Transition.ColorTint;
        var cb = _deleteButton.colors;
        cb.normalColor      = new Color(0.80f, 0.80f, 0.80f, 0.62f);
        cb.highlightedColor = Color.white;
        cb.pressedColor     = new Color(0.85f, 0.85f, 0.85f, 1f);
        cb.selectedColor    = cb.normalColor;
        cb.fadeDuration     = 0.08f;
        _deleteButton.colors = cb;
        _deleteButton.onClick.AddListener(OnClickDelete);
    }

    // ─────────────────────────────────────────────────────────
    // Private Methods — 채우기
    // ─────────────────────────────────────────────────────────

    /// <summary>진행 중인 런 — 어디서 멈췄나(챕터 · 이름) · 지나온 챕터 · 무엇으로(유물 · 무기) · 상태(체력 · 포션).</summary>
    private void FillRun(RunSaveData d)
    {
        int ch = Mathf.Clamp(d.chapter, 1, ChapterMax);
        // 보스 클리어 뒤 저장(10-10)은 다음 챕터 대기방부터 연다
        _caption.text = d.chapterStartPending ? $"챕터 {ch} · 시작 전" : $"진행 중인 런 · 챕터 {ch}";
        _title.text   = ChapterName(ch);
        for (int i = 0; i < ChapterMax; i++)
        {
            int n = i + 1;
            SetPip(i, n < ch ? PipLit : n == ch ? Ink : PipOff, n == ch);
        }
        _trackText.text = d.roomClearCount > 0 ? $"방 {d.roomClearCount}개 돌파" : "";

        AddRow("유물", RelicName(d.relicKey));
        AddRow("무기", WeaponLine(d));
        // 대기방 세이브는 체력이 아직 잡히지 않아 「0 / 1」(SaveSanitizer 하한 1)로 나왔다 — 그땐 뺀다(10-17).
        if (d.maxHp > 1)          AddRow("체력", $"{Mathf.Max(0, d.currentHp)} / {d.maxHp}");
        if (d.potionCapacity > 0) AddRow("포션", $"{Mathf.Max(0, d.potionCount)} / {d.potionCapacity}");
    }

    /// <summary>
    /// 거점 기록 — 런 장비가 없다(유물 · 무기는 매 런 다시 갖춘다 · 포션은 새 런에서 받아 늘 0/3 — 10-19 사용자 「의미 없는 포션」).
    /// 대신 이 기록이 이야기에서 어디까지 왔나: 봉인(봉인기) · 처치(해방 뒤)한 보스 · 최고 챕터 · 완주 · 심연의 정수.
    /// </summary>
    private void FillHub(UserGameData meta, StoryEra era)
    {
        _caption.text = "거점";
        _title.text   = "베이스캠프";

        bool freed  = era != StoryEra.Sealed;
        string pre  = freed ? StoryProgress.Rec.KillPrefix : StoryProgress.Rec.SealPrefix;
        int done = 0;
        for (int i = 0; i < ChapterMax; i++)
        {
            string boss = StoryProgress.BossIdForChapter((ChapterId)(i + 1));
            bool lit = meta != null && !string.IsNullOrEmpty(boss) && meta.GetRecord(pre + boss) > 0;
            if (lit) done++;
            SetPip(i, lit ? PipLit : PipOff, false);
        }
        _trackText.text = $"{(freed ? "처치" : "봉인")} {done} / {ChapterMax}";

        if (meta == null) return;
        if (meta.highestChapter > 0) AddRow("최고 챕터", $"챕터 {Mathf.Clamp(meta.highestChapter, 1, ChapterMax)}");
        AddRow("완주", $"{Mathf.Max(0, meta.totalClears)}회");
        AddRow("심연의 정수", $"{Mathf.Max(0, meta.abyssEssence):N0}");
    }

    private void AddRow(string label, string value)
    {
        if (string.IsNullOrEmpty(value) || _rowCount >= RowMax) return;
        _rowLabel[_rowCount].text = label;
        _rowValue[_rowCount].text = value;
        ShowRow(_rowLabel[_rowCount], _rowValue[_rowCount], true);
        _rowCount++;
    }

    private void AddFoot(string label, string value)
    {
        if (string.IsNullOrEmpty(value) || _footCount >= FootMax) return;
        _footLabel[_footCount].text = label;
        _footValue[_footCount].text = value;
        ShowRow(_footLabel[_footCount], _footValue[_footCount], true);
        _footCount++;
    }

    private static void ShowRow(TMP_Text label, TMP_Text value, bool on)
    {
        label.gameObject.SetActive(on);
        value.gameObject.SetActive(on);
    }

    private void SetPip(int i, Color fill, bool current)
    {
        _pips[i].color = fill;
        _pipRings[i].gameObject.SetActive(current);
    }

    private void SetEra(StoryEra era)
    {
        (string label, Color ink) = era switch
        {
            StoryEra.NightmareMode => ("악몽", Danger),
            StoryEra.Liberated     => ("해방된 세계", FreeBlue),
            _                      => ("봉인된 세계", UITheme.Thin.InkA(0.72f)),
        };
        _eraChip.gameObject.SetActive(true);
        _eraText.text  = label;
        _eraText.color = ink;
        _eraChip.color = UITheme.Thin.WithA(ink, 0.45f);
    }

    /// <summary>이어하기(호박빛 판 · 금 글) / 새 게임(어두운 판 · 옅은 금선 · 잉크 글) — 기록이 있는 카드가 먼저 눈에 든다.</summary>
    private void StyleCta(bool hasSave)
    {
        _ctaText.text  = hasSave ? "이어하기" : "새 게임";
        _ctaText.color = hasSave ? Gold : Ink;
        _ctaFill.color = hasSave ? CtaFill : NewFill;
        _ctaEdge.color = hasSave ? CtaEdge : NewEdge;
    }

    /// <summary>슬롯 메타로 시기를 읽는다 — <see cref="StoryProgress.Era"/>는 지금 고른 슬롯만 보므로 카드마다 직접 센다(같은 규칙).</summary>
    private static StoryEra EraOf(UserGameData meta)
    {
        if (meta == null || !meta.IsBossSealBroken(StoryProgress.Lich)) return StoryEra.Sealed;
        return meta.GetRecord(StoryProgress.Rec.Ending) > 0 && meta.GetRecord(StoryProgress.Rec.NightmareMode) > 0
            ? StoryEra.NightmareMode : StoryEra.Liberated;
    }

    private static string ChapterName(int ch)
    {
        var reg = GameRunBootstrapper.Instance != null ? GameRunBootstrapper.Instance.ChapterRegistry : null;
        string name = reg != null ? reg.GetData((ChapterId)ch)?.chapterName : null;
        return !string.IsNullOrEmpty(name) ? name : FallbackChapterNames[ch - 1];
    }

    /// <summary>근접 무기(재련 n) · 원거리 무기 — 모르는 키는 뺀다.</summary>
    private static string WeaponLine(RunSaveData d)
    {
        string melee  = WeaponName(d.weapon0PrefabKey);
        string ranged = WeaponName(d.weapon1PrefabKey);
        if (!string.IsNullOrEmpty(melee) && d.weapon0EnhanceLevel > 0)
            melee += $" 재련 {WeaponReforgeService.CountAt(d.weapon0EnhanceLevel)}";
        if (string.IsNullOrEmpty(melee)) return ranged;
        return string.IsNullOrEmpty(ranged) ? melee : melee + "  ·  " + ranged;
    }

    /// <summary>유물 SO 이름(RelicClass_Gawain 등) → 표시 이름. 모르는 키는 비운다.</summary>
    private static string RelicName(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (key.Contains("Gawain"))   return "가웨인";
        if (key.Contains("Lancelot")) return "랜슬롯";
        return "";
    }

    /// <summary>무기 프리팹 키(T1_Katana 등) → 표시 이름. 모르는 키는 비운다.</summary>
    private static string WeaponName(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (key.Contains("Nameless"))   return "무형검";
        if (key.Contains("Katana"))     return "카타나";
        if (key.Contains("Greatsword")) return "대검";
        if (key.Contains("Crossbow"))   return "석궁";
        if (key.Contains("Bow"))        return "활";
        return "";
    }

    /// <summary>저장 시각(ISO8601 UTC) → 지역 시각 「10월 16일 14:22」. 읽을 수 없으면 null.</summary>
    private static string FormatSavedAt(string iso)
    {
        if (string.IsNullOrEmpty(iso)) return null;
        if (!DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                               System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var utc))
            return null;
        var local = utc.ToLocalTime();
        return $"{local.Month}월 {local.Day}일 {local:HH:mm}";
    }

    /// <summary>누적 플레이 시간 — 1시간 미만은 분 · 초, 넘으면 시간 · 분.</summary>
    private static string FormatPlayTime(int seconds)
    {
        if (seconds <= 0) return "--";
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1d ? $"{(int)t.TotalHours}시간 {t.Minutes}분" : $"{t.Minutes}분 {t.Seconds}초";
    }

    // ─────────────────────────────────────────────────────────
    // Event Handlers
    // ─────────────────────────────────────────────────────────

    private void OnClickStart()  => _onStart?.Invoke(_slotIndex, _hasSave);
    private void OnClickDelete() => _onDelete?.Invoke(_slotIndex);
}

/// <summary>세이브 창 배치 도움(목업 px · 부모 왼쪽 위 기준) — 패널과 카드가 함께 쓴다. 상점(<see cref="UI_ShopPanel"/>)의 같은 이름 도우미와 같은 규약.</summary>
internal static class SaveSlotUI
{
    /// <summary>둥근 테 그림(<see cref="UIProceduralSprites.RoundedOutline"/>)은 칸 가장자리 4px 안쪽에 선을 긋는다.</summary>
    private const float OutlineInset = 4f;

    public static void Put(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    public static RectTransform Box(Transform parent, string name, float x, float y, float w, float h)
    {
        var rt = (RectTransform)ShopUIStyle.MakeRect(parent, name).transform;
        Put(rt, x, y, w, h);
        return rt;
    }

    public static Image Img(Transform parent, string name, float x, float y, float w, float h, Color color, bool raycast = false)
    {
        var img = ShopUIStyle.MakeImage(parent, name, color, raycast);
        Put(img.rectTransform, x, y, w, h);
        return img;
    }

    /// <summary>얇은 글 한 칸 — 길면 0.75배까지 줄고, 그래도 넘치면 말줄임. 칸이 한 줄 높이(Noto ≈ 1.45em)보다 낮으면 늘린다(TMP가 줄을 통째로 지우는 함정 · 10-21 상점).</summary>
    public static TMP_Text Text(Transform parent, string name, float x, float y, float w, float h, float size, UIWeight weight, Color color,
                                TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
    {
        var t = ShopUIStyle.MakeText(parent, name, size, FontStyles.Normal, align, color);
        UITheme.StyleThinText(t, weight, size, color);
        t.fontSizeMin      = size * 0.75f;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode     = TextOverflowModes.Ellipsis;
        t.richText         = true;
        float minH = size * 1.5f;
        if (h < minH) { y -= (minH - h) * 0.5f; h = minH; }
        Put(t.rectTransform, x, y, w, h);
        return t;
    }

    /// <summary>마름모(45° 돌린 칸) — 중심 (cx, cy) · 보이는 한 변 size. outline이면 1px 테만(테 그림은 칸 4px 안쪽에 선이 있어 칸을 8px 키운다).</summary>
    public static Image Diamond(Transform parent, string name, float cx, float cy, float size, Color color, bool outline)
    {
        var img = ShopUIStyle.MakeImage(parent, name, color);
        if (outline)
        {
            img.sprite = UIProceduralSprites.RoundedOutline(radius: 1f, stroke: 1f);
            img.type   = Image.Type.Sliced;
            size += OutlineInset * 2f;
        }
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(cx, -cy);
        rt.sizeDelta = new Vector2(size, size);
        rt.localRotation = Quaternion.Euler(0f, 0f, 45f);
        return img;
    }

    /// <summary>판 버튼 — 둥근 판(fill) + 1px 테(edge) + 가운데 글. 눌림 · 올림 반응(UIButtonFeedback)은 가운데를 축으로.</summary>
    public static Button MakeButton(Transform parent, string name, float x, float y, float w, float h, Color fill, Color edge,
                                string text, float size, Color ink, out Image fillImg, out Image edgeImg, out TMP_Text label)
    {
        fillImg = Img(parent, name, x, y, w, h, fill, raycast: true);
        fillImg.sprite = UIProceduralSprites.RoundedRect(radius: 4f, feather: 1f);
        fillImg.type   = Image.Type.Sliced;
        var rt = fillImg.rectTransform;
        rt.pivot = new Vector2(0.5f, 0.5f);                          // 커지고 눌릴 때 왼쪽 위가 아니라 가운데에서
        rt.anchoredPosition = new Vector2(x + w * 0.5f, -(y + h * 0.5f));
        edgeImg = ShopUIStyle.MakeImage(fillImg.transform, "Edge", edge);
        edgeImg.sprite = UIProceduralSprites.RoundedOutline(radius: 4f, stroke: 1f);
        edgeImg.type   = Image.Type.Sliced;
        ShopUIStyle.Stretch(edgeImg.rectTransform, 1f - OutlineInset);   // 선을 판 가장자리(1px 안)에 맞춘다
        var btn = fillImg.gameObject.AddComponent<Button>();
        ShopUIStyle.ApplyButtonColors(btn, fillImg);
        fillImg.gameObject.AddComponent<UIButtonFeedback>();   // 런타임에 지어 UI_Popup.Init이 붙이지 못한다
        label = Text(fillImg.transform, "Label", 0f, 0f, w, h, size, UIWeight.Medium, ink, TextAlignmentOptions.Center);
        label.text = text;
        label.characterSpacing = 6f;
        return btn;
    }
}
