using System;
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
        name      = new Rect( 16f,  8f, 150f, 22f),
        cost      = new Rect(-14f,  8f, 120f, 22f),
        change    = new Rect( 16f, 32f, 300f, 17f),
        condition = new Rect( 16f, 51f, 300f, 15f),
        fontSizes = new Vector3(17f, 13f, 12f),
        reachBottom = 6f,
    };

    [SerializeField] private SeatLayout passedSeat = new()
    {
        height = 40f,
        name      = new Rect(16f,  4f, 260f, 18f),
        change    = new Rect(16f, 23f, 300f, 14f),
        fontSizes = new Vector3(15f, 12f, 12f),
    };

    [SerializeField] private SeatLayout aheadSeat = new()
    {
        height = 28f,
        name      = new Rect(16f, 5f, 300f, 18f),
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

    // ── Public Methods ───────────────────────────────────────────────────

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
            name      = new Rect( 16f,  8f, 150f, 22f),
            cost      = new Rect(-14f,  8f, 120f, 22f),
            change    = new Rect( 16f, 32f, 300f, 17f),
            condition = new Rect( 16f, 51f, 300f, 15f),
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
            nameText.text     = seat == Seat.Passed ? $"✔ {node.DisplayName}" : node.DisplayName;
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

        if (state.ConditionMet) return $"✔ {node.ConditionLabel}";

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
