using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 기억의 제단 노드 <b>행</b> 1개. 갈래 열 안에 세로로 쌓인다.
///
/// <para><b>버튼이 없다.</b> 행은 고르는 것이고, 사는 것은 화면 하단 행동 바 한 곳에서만 일어난다.
/// 카드마다 버튼을 달았던 초안은 화면에 버튼이 18개 깔려 정작 행동 지점이 어디인지 흐려졌다.</para>
///
/// <para><b>사슬의 세 자리</b> — 지난 것 / 지금 차례 / 아직 먼 것.
/// 갈래는 위에서 아래로 이어지는 사슬이라, 행의 <b>높이 자체가 어디까지 왔는지를 말한다</b>:
/// 지금 차례만 크게 펴지고 나머지는 접힌다. 색만 다르고 형태가 같으면 목록으로 읽힌다.</para>
///
/// <para>덕분에 가장 긴 갈래(등장 8칸)도 스크롤 없이 들어간다 —
/// 최악이 74 + 40×7 = 354로 열 높이(660)의 절반이다.</para>
/// </summary>
public class AltarNodeRowView : MonoBehaviour
{
    // ── 사슬 레일 ─────────────────────────────────────────────────────────
    private const float RailX     = 6f;    // 행 왼쪽 안쪽 여백(AccentBar 3px 바로 옆)
    private const float MarkSize  = 15f;
    private const float LinkWidth = 1.5f;
    private const float LinkReach = 40f;   // 행 위·아래로 뻗는 길이 — 간격 6px를 넉넉히 건넌다

    // ── 자리별 배치 ───────────────────────────────────────────────────────
    /// <summary>
    /// 한 자리의 배치. <b>인스펙터에서 조절한다</b> — 행은 상태에 따라 높이가 2.6배까지 달라지는
    /// 상태 위젯이라 프리팹 하나로는 세 모습을 담을 수 없다. 그래서 코드가 앉히되,
    /// <b>숫자는 프리팹이 갖는다</b>: 재컴파일 없이 값을 바꾸며 볼 수 있다.
    ///
    /// <para>Rect는 (x, y, w, h)를 담는 그릇으로만 쓴다 — 좌표는 <b>행 좌상단에서 아래로</b>다
    /// (이름·변화·조건 피벗이 (0,1)이라 그대로 anchoredPosition에 들어간다. 값은 부호를 뒤집어 쓴다).</para>
    /// </summary>
    [System.Serializable]
    private struct SeatLayout
    {
        [Tooltip("행 높이")]                  public float height;
        [Tooltip("이름 (x, y, 폭, 높이)")]     public Rect  name;
        [Tooltip("값 — 오른쪽 정렬")]          public Rect  cost;
        [Tooltip("변화 「A → B」")]            public Rect  change;
        [Tooltip("조건·진척")]                public Rect  condition;
        [Tooltip("글자 크기 — 이름/변화/조건")] public Vector3 fontSizes;
        [Tooltip("접근도 막대 바닥 여백")]      public float reachBottom;
    }

    [Header("사슬 자리별 배치 — 값만 바꿔도 즉시 반영된다")]
    [SerializeField] private SeatLayout turnSeat = new()
    {
        height = 74f,
        name      = new Rect( 26f,  8f, 142f, 22f),
        cost      = new Rect(-14f,  8f, 120f, 22f),
        change    = new Rect( 26f, 32f, 290f, 17f),
        condition = new Rect( 26f, 51f, 290f, 15f),
        fontSizes = new Vector3(17f, 13f, 12f),
        reachBottom = 6f,
    };

    [SerializeField] private SeatLayout passedSeat = new()
    {
        height = 40f,
        name      = new Rect(26f,  4f, 250f, 18f),
        change    = new Rect(26f, 23f, 290f, 14f),
        fontSizes = new Vector3(15f, 12f, 12f),
    };

    [SerializeField] private SeatLayout aheadSeat = new()
    {
        height = 28f,
        name      = new Rect(26f, 5f, 290f, 18f),
        fontSizes = new Vector3(14f, 12f, 12f),
    };

    // ── 직렬화 필드 ──────────────────────────────────────────────────────
    [Header("바탕 · 상태 띠")]
    [SerializeField] private Image background;
    [SerializeField] private Image accentBar;
    [SerializeField] private Image selectionOutline;

    [Header("본문")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text costText;
    [SerializeField] private TMP_Text conditionText;

    [Header("접근도 막대 — 보유 정수 / 가격")]
    [SerializeField] private RectTransform reachRow;
    [SerializeField] private Image reachFill;

    [Header("클릭")]
    [SerializeField] private Button selectButton;
    [SerializeField] private LayoutElement layoutElement;

    // ── 비공개 필드 ──────────────────────────────────────────────────────
    private Action   _onSelect;
    private TMP_Text _changeText;

    // 사슬 레일 — 행 왼쪽 바깥에 세로로 선다. 런타임 1회 생성.
    private Image    _linkUp, _linkDown;
    private Image    _markBg;
    private TMP_Text _markGlyph;

    // ── Public Methods ───────────────────────────────────────────────────

    /// <summary>
    /// 해금 순간 이 행을 <b>한 번 밝힌다</b>. 되돌릴 수 없는 진행이라 화면에도 자국이 남아야 한다.
    ///
    /// <para>그동안은 효과음과 <b>행동 버튼</b>만 반응했다 — 정작 열린 노드는 조용히 색만 바뀌어,
    /// 14,800짜리 마지막 칸이나 첫 칸이나 화면 반응이 똑같았다.</para>
    ///
    /// <para>띠(왼쪽 이음매)를 흰빛으로 튕겼다가 제 색으로 가라앉힌다 — 사슬의 <b>이음매가 채워지는</b>
    /// 자리라 여기가 밝아지면 "한 칸 나아갔다"로 읽힌다. 팝업은 timeScale=0이므로 unscaled뿐이다.</para>
    /// </summary>
    public async UniTask PlayUnlockAsync(CancellationToken ct)
    {
        if (accentBar == null) return;

        var target = accentBar.color;
        var flash  = Color.white;
        var rt     = (RectTransform)transform;
        var baseScale = rt.localScale;

        try
        {
            const float Dur = 0.45f;
            float t = 0f;
            while (t < Dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / Dur);
                accentBar.color = Color.Lerp(flash, target, k);
                // 초반에만 살짝 부풀었다 제자리로 — 목록 전체가 들썩이지 않게 폭은 작게 둔다.
                float pop = 1f + 0.04f * (1f - k) * Mathf.Sin(k * Mathf.PI * 2f);
                rt.localScale = baseScale * pop;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (accentBar != null) accentBar.color = target;
            rt.localScale = baseScale;
        }
    }

    public void Refresh(AltarNodeState state, int essence, bool selected)
    {
        var node = state.Node;
        if (node == null) return;

        var seat = state.Unlocked        ? Seat.Passed
                 : state.BlockedByChain  ? Seat.Ahead
                                         : Seat.Turn;

        var L = Layout(seat);
        float h = L.height;

        // ⚠️ 열의 VerticalLayoutGroup은 <b>childControlHeight = 0</b>이다 —
        //   LayoutElement를 쓰지 않고 <b>행의 rect 높이</b>를 그대로 간격으로 쓴다.
        //   그래서 preferredHeight만 고치면 행은 템플릿 authoring값(100)에 머물고,
        //   가장 긴 갈래(8칸=800)가 열(660) 아래로 다시 흘러내린다. rect를 직접 잡아야 한다.
        var rt = (RectTransform)transform;
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, h);

        // 그룹이 스스로 크기를 계산하는 경로(다른 화면에서 재사용될 때)도 같은 값을 보게 맞춰 둔다.
        if (layoutElement != null)
        {
            layoutElement.preferredHeight = h;
            layoutElement.minHeight       = h;
        }

        LayoutForSeat(seat, L);
        RefreshRail(state, seat, h);
        RefreshTone(state, seat, selected);
        RefreshText(state, node, seat);
        RefreshReach(state, essence, seat);
    }

    /// <summary>사슬에서 이 칸이 앉은 자리.</summary>
    private enum Seat { Passed, Turn, Ahead }

    /// <summary>
    /// 자리 배치를 꺼낸다. <b>height가 0이면 안전값으로 받는다</b> — 직렬화 필드를 새로 추가하면
    /// 기존 프리팹에서 통째로 0으로 들어올 수 있고, 그러면 행이 0px가 되어 화면이 사라진다.
    /// 인스펙터에서 실수로 0을 넣었을 때도 같은 그물에 걸린다.
    /// </summary>
    private SeatLayout Layout(Seat seat)
    {
        var L = seat switch
        {
            Seat.Passed => passedSeat,
            Seat.Ahead  => aheadSeat,
            _           => turnSeat,
        };
        return L.height > 1f ? L : Fallback(seat);
    }

    private static SeatLayout Fallback(Seat seat) => seat switch
    {
        Seat.Passed => new SeatLayout
        {
            height = 40f,
            name   = new Rect(16f,  4f, 260f, 18f),
            change = new Rect(16f, 23f, 300f, 14f),
            fontSizes = new Vector3(15f, 12f, 12f),
        },
        Seat.Ahead => new SeatLayout
        {
            height = 28f,
            name   = new Rect(16f, 5f, 300f, 18f),
            fontSizes = new Vector3(14f, 12f, 12f),
        },
        _ => new SeatLayout
        {
            height    = 74f,
            name      = new Rect( 26f,  8f, 142f, 22f),
            cost      = new Rect(-14f,  8f, 120f, 22f),
            change    = new Rect( 26f, 32f, 290f, 17f),
            condition = new Rect( 26f, 51f, 290f, 15f),
            fontSizes = new Vector3(17f, 13f, 12f),
            reachBottom = 6f,
        },
    };

    /// <summary>
    /// 자리마다 행 높이가 달라지므로 <b>안쪽 글 위치도 같이 다시 잡는다</b>.
    /// 프리팹 authoring 좌표(이름 -11 · 조건 -37)는 100px 행 기준이라, 74/40/28에서는
    /// 줄이 서로 올라타거나 행 밖으로 나간다. 숫자는 위 SeatLayout(인스펙터)이 갖는다.
    /// </summary>
    private void LayoutForSeat(Seat seat, SeatLayout L)
    {
        Put(nameText, L.name, L.fontSizes.x);

        if (seat == Seat.Turn)
        {
            Put(costText,      L.cost,      L.fontSizes.x);
            Put(EnsureChangeText(), L.change, L.fontSizes.y);
            Put(conditionText, L.condition, L.fontSizes.z);
            if (reachRow) reachRow.anchoredPosition = new Vector2(0f, L.reachBottom);
        }
        else if (seat == Seat.Passed)
        {
            Put(EnsureChangeText(), L.change, L.fontSizes.y);
        }
    }

    /// <summary>
    /// 사슬 레일 — 행 왼쪽에 <b>표식과 잇는 선</b>을 그린다. 열을 세로로 훑는 것만으로
    /// 어디까지 왔는지가 읽히게 하는 장치다(행 높이만으로는 상태가 안 읽힌다).
    ///
    /// <para>표식 ✔ 지난 것 · ◆ 지금 차례 · ○ 아직 먼 것 · 🔒 선행 조건이 자물쇠인 칸.
    /// 잇는 선은 <b>지난 구간은 정수색 실선</b>, <b>다음으로 가는 구간은 금색</b>,
    /// 그 아래는 흐린 선이다 — 색이 바뀌는 지점이 곧 "지금 여기"다.</para>
    /// </summary>
    private void RefreshRail(AltarNodeState state, Seat seat, float rowHeight)
    {
        EnsureRail();
        if (_markBg == null) return;

        bool passed = seat == Seat.Passed;
        bool turn   = seat == Seat.Turn;
        bool locked = state.BlockedByRequirement;

        var on   = AltarPalette.Essence;
        var next = AltarPalette.Gold;
        var off  = AltarPalette.AccentLocked;

        // 표식은 폰트(DNFForgedBlade)에 <b>실제로 있는</b> 기호만 쓴다.
        // 예전 ✔·🔒·○는 셋 다 TTF에 없어 화면에서 전부 □(두부)로 나왔다.
        _markGlyph.text = passed ? "■" : locked ? "×" : turn ? "◆" : "◇";
        _markGlyph.color = passed ? on : turn ? next : off;
        _markBg.color    = passed ? on : new Color(off.r, off.g, off.b, 0.35f);

        // 위쪽 선: 이 칸이 열렸으면 여기까지 이어진 것이다.
        // 아래쪽 선: 다음 칸이 '차례'가 되므로, 내가 열렸을 때만 금색으로 흐른다.
        _linkUp.color   = passed ? on : off;
        _linkDown.color = passed ? next : off;

        // 표식을 행 세로 가운데에 맞춘다 — 행 높이가 자리마다 달라 고정값이면 어긋난다.
        _markBg.rectTransform.anchoredPosition = new Vector2(RailX, -rowHeight * 0.5f);
    }

    private void EnsureRail()
    {
        if (_markBg != null) return;

        var parent = (RectTransform)transform;

        _linkUp   = MakeLink(parent, "Link_Up",   new Vector2(0f, 1f));
        _linkDown = MakeLink(parent, "Link_Down", new Vector2(0f, 0f));

        var bg = new GameObject("Mark", typeof(RectTransform), typeof(CanvasRenderer));
        var rt = (RectTransform)bg.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(MarkSize, MarkSize);
        _markBg = bg.AddComponent<Image>();
        _markBg.raycastTarget = false;

        _markGlyph = new GameObject("Glyph", typeof(RectTransform), typeof(CanvasRenderer))
            .AddComponent<TextMeshProUGUI>();
        var grt = _markGlyph.rectTransform;
        grt.SetParent(rt, false);
        grt.anchorMin = Vector2.zero; grt.anchorMax = Vector2.one;
        grt.offsetMin = Vector2.zero; grt.offsetMax = Vector2.zero;
        _markGlyph.fontSize = 11f;
        // 글자가 판 밖으로 나가지 않게 — 최대는 설계 크기로 묶으므로 커지지 않고, 안 들어갈 때만 줄어든다.
        _markGlyph.enableAutoSizing = true;
        _markGlyph.fontSizeMax = 11f;
        _markGlyph.fontSizeMin = 9f;
        _markGlyph.alignment = TextAlignmentOptions.Center;
        _markGlyph.raycastTarget = false;
    }

    /// <summary>표식 사이를 잇는 세로선. 행 위·아래로 절반씩 뻗어 옆 행의 것과 만난다.</summary>
    private static Image MakeLink(RectTransform parent, string name, Vector2 anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot     = new Vector2(0.5f, anchor.y);
        rt.sizeDelta = new Vector2(LinkWidth, LinkReach);
        rt.anchoredPosition = new Vector2(RailX, 0f);

        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    /// <summary>Rect의 y는 <b>아래로 재는 값</b>이라 anchoredPosition에는 부호를 뒤집어 넣는다.</summary>
    private static void Put(TMP_Text t, Rect r, float size)
    {
        if (t == null) return;
        var rt = t.rectTransform;
        rt.anchoredPosition = new Vector2(r.x, -r.y);
        rt.sizeDelta        = new Vector2(r.width, r.height);
        t.fontSize          = size;
    }

    public void SetOnSelect(Action callback)
    {
        _onSelect = callback;
        if (selectButton == null) return;

        selectButton.onClick.RemoveAllListeners();
        selectButton.onClick.AddListener(() => _onSelect?.Invoke());
    }

    // ── Private Methods ──────────────────────────────────────────────────

    private void RefreshTone(AltarNodeState state, Seat seat, bool selected)
    {
        if (background)
            background.color = seat switch
            {
                Seat.Passed => AltarPalette.CardUnlocked,
                Seat.Ahead  => AltarPalette.CardIdle,
                _           => state.CanBuy ? AltarPalette.CardBuyable : AltarPalette.CardIdle,
            };

        // 왼쪽 띠가 사슬의 이음매다 — 지난 칸은 채워지고, 지금 차례는 금색, 앞은 흐리다.
        if (accentBar)
            accentBar.color = seat switch
            {
                Seat.Passed => AltarPalette.Essence,
                Seat.Ahead  => AltarPalette.AccentLocked,
                _           => AltarPalette.Gold,
            };

        if (selectionOutline) selectionOutline.gameObject.SetActive(selected);
    }

    /// <summary>
    /// 글은 <b>세 층</b>이다 — 이름 / 무엇이 얼마에서 얼마로(변화) / 값·조건.
    /// 자리마다 층이 하나씩 접힌다: 지난 것은 이름+변화, 앞의 것은 이름만.
    /// </summary>
    private void RefreshText(AltarNodeState state, MemoryAltarNode node, Seat seat)
    {
        if (nameText)
        {
            nameText.text     = seat == Seat.Passed ? $"■ {node.DisplayName}" : node.DisplayName;
            nameText.color    = seat switch
            {
                Seat.Passed => AltarPalette.Essence,
                Seat.Ahead  => AltarPalette.TextFaint,
                _           => AltarPalette.TextPrimary,
            };

        }

        // ② 변화 줄 — 「A → B」. 프리팹에 자리가 없어 한 번만 만들어 붙인다(기존 세 줄 사이).
        var change = EnsureChangeText();
        if (change != null)
        {
            change.gameObject.SetActive(seat != Seat.Ahead);
            change.text  = node.Description;
            change.color = seat == Seat.Passed ? AltarPalette.TextDim : AltarPalette.TextPrimary;
        }

        // 값은 앞의 칸에서도 감춘다 — 아직 볼 때가 아니고, 가려야 다음이 궁금해진다.
        if (costText)
        {
            costText.gameObject.SetActive(seat != Seat.Ahead);
            costText.text  = seat == Seat.Passed ? "" : $"{state.Cost:N0} ◆";
            costText.color = state.CanBuy ? AltarPalette.Gold : AltarPalette.TextDim;
        }

        // ③ 조건·진척은 지금 차례에서만.
        if (conditionText)
        {
            bool show = seat == Seat.Turn;
            conditionText.gameObject.SetActive(show);
            if (show) conditionText.text = BuildCondition(state, node);
        }
    }

    /// <summary>변화 줄을 1회 만든다. 이름(위)과 조건(아래) 사이에 앉는다.</summary>
    private TMP_Text EnsureChangeText()
    {
        if (_changeText != null) return _changeText;
        if (nameText == null) return null;

        var go = new GameObject("Txt_Change", typeof(RectTransform), typeof(CanvasRenderer));
        var rt = (RectTransform)go.transform;
        rt.SetParent(nameText.transform.parent, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot     = new Vector2(0f, 1f);
        // 크기·위치는 LayoutForSeat가 매번 잡는다 — 여기서는 피벗만 기존 줄들과 맞춘다.
        _changeText = go.AddComponent<TextMeshProUGUI>();
        _changeText.raycastTarget   = false;
        _changeText.textWrappingMode = TextWrappingModes.NoWrap;
        _changeText.overflowMode    = TextOverflowModes.Ellipsis;
        return _changeText;
    }

    private static string BuildCondition(AltarNodeState state, MemoryAltarNode node)
    {
        if (!node.HasCondition) return "조건 없음";

        if (state.ConditionMet) return $"■ {node.ConditionLabel}";

        string progress = $"{Mathf.Min(state.Progress, state.Target)}/{state.Target}";
        return node.ConditionRequired
            ? $"{node.ConditionLabel} {progress} · 필수"
            : $"{node.ConditionLabel} {progress} · 반값";
    }

    /// <summary>
    /// 보유 정수가 가격의 몇 %인지. <b>비싼 것으로 조금씩 다가가는 게 보여야</b>
    /// "문턱을 못 넘으면 빈손"이라는 인상이 생기지 않는다.
    /// </summary>
    private void RefreshReach(AltarNodeState state, int essence, Seat seat)
    {
        if (reachRow == null) return;

        bool show = seat == Seat.Turn && !state.CanBuy;
        reachRow.gameObject.SetActive(show);

        if (show && reachFill != null)
            reachFill.fillAmount = state.Cost > 0 ? Mathf.Clamp01((float)essence / state.Cost) : 0f;
    }
}
