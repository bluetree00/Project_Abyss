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
    // ── 레일 표식 (와이어프레임 09-09: 행 바깥 왼쪽에 원형 표식, 연결선 없음) ──
    // 열의 VerticalLayoutGroup이 왼쪽 padding 57(표식 34 + 간격 22 + 1)을 비워 두고, 표식은 행 rect 밖 왼쪽에 선다.
    private const float RailX     = -39f;  // 행 왼쪽 가장자리에서 표식 중심까지(간격 22 + 반지름 17)
    private const float MarkSize  = 34f;
    private const float MarkGlyph = 16f;

    // ── 연출 시계 ─────────────────────────────────────────────────────────
    /// <summary>연출 한 프레임의 시간 걸음(초). 0이면 실시간(<see cref="Time.unscaledDeltaTime"/>).
    /// 레이아웃 프로브가 연출 <b>중간 프레임</b>을 재현 가능하게 찍으려고 1/60로 고정한다 — 에디터 프레임 시간은 들쭉날쭉해서
    /// 실시간으로는 0.35초짜리 흐름이 스크린샷 전에 끝나 버린다.</summary>
    public static float FxStepOverride;
    private static float FxStep => FxStepOverride > 0f ? FxStepOverride : Time.unscaledDeltaTime;

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
        fontSizes = new Vector3(17f, 15f, 14f),
        reachBottom = 6f,
    };

    [SerializeField] private SeatLayout passedSeat = new()
    {
        height = 40f,
        name      = new Rect(26f,  4f, 250f, 18f),
        change    = new Rect(26f, 23f, 290f, 14f),
        fontSizes = new Vector3(16f, 14f, 14f),
    };

    [SerializeField] private SeatLayout aheadSeat = new()
    {
        height = 28f,
        name      = new Rect(26f, 5f, 290f, 18f),
        fontSizes = new Vector3(16f, 14f, 14f),
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

    // 레일 표식 — 행 왼쪽 바깥. 런타임 1회 생성. bg=원/고리, fg=체크(지난 칸)·안쪽 고리(잠긴 칸), glyph=◆·×
    private Image    _markBg, _markFg;
    private TMP_Text _markGlyph;
    // 차례 행의 주황 테두리(와이어프레임) — 점멸·승격 연출의 대상.
    private Image    _turnBorder;

    // 「다음 칸」 점멸(기획 §5-1) — 차례일 때만 돈다. 자리가 바뀌면 끈다.
    private CancellationTokenSource _pulseCts;
    private Seat _seat = Seat.Ahead;

    /// <summary>지금 행 높이(px). 해금 연출이 '옛 높이 → 새 높이' 트윈의 시작값으로 읽는다.</summary>
    public float CurrentHeight => ((RectTransform)transform).sizeDelta.y;

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
        if (background == null) return;

        var target = background.color;
        var flash  = Color.Lerp(target, Color.white, 0.55f);
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
                background.color = Color.Lerp(flash, target, k);
                // 초반에만 살짝 부풀었다 제자리로 — 목록 전체가 들썩이지 않게 폭은 작게 둔다.
                float pop = 1f + 0.04f * (1f - k) * Mathf.Sin(k * Mathf.PI * 2f);
                rt.localScale = baseScale * pop;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (background != null) background.color = target;
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

        _seat = seat;
        SetPulse(seat == Seat.Turn && !state.BlockedByRequirement);
    }

    /// <summary>행 높이를 즉시 잡는다(트윈 시작값용). 열의 VLG가 rect 높이를 간격으로 쓴다.</summary>
    public void SetHeightImmediate(float h)
    {
        var rt = (RectTransform)transform;
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, h);
        if (layoutElement != null) { layoutElement.preferredHeight = h; layoutElement.minHeight = h; }
        if (_markBg != null) _markBg.rectTransform.anchoredPosition = new Vector2(RailX, -h * 0.5f);
    }

    /// <summary>
    /// 기획 §7-1 0.55~0.90 — 이 칸의 표식에서 <b>아래로 빛이 흘러</b> 다음 칸 표식에 닿는다.
    /// 와이어프레임엔 연결선이 없으므로 빛줄기는 연출 중에만 나타났다 사라진다. <paramref name="distance"/> = 두 표식 중심 사이(px).
    /// </summary>
    public async UniTask PlayFlowDownAsync(float distance, CancellationToken ct)
    {
        EnsureRail();
        if (_markBg == null || distance <= 0f) return;

        var mark = _markBg.rectTransform;
        var beam = new GameObject("Beam", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
        var brt  = beam.rectTransform;
        brt.SetParent(mark, false);
        brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
        brt.pivot     = new Vector2(0.5f, 1f);
        brt.anchoredPosition = Vector2.zero;
        brt.sizeDelta = new Vector2(3f, 0f);
        beam.color = AltarPalette.Gold;
        beam.raycastTarget = false;

        var spark = new GameObject("Spark", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
        var srt   = spark.rectTransform;
        srt.SetParent(mark, false);
        srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
        srt.pivot     = new Vector2(0.5f, 0.5f);
        srt.sizeDelta = new Vector2(9f, 16f);
        spark.sprite  = UIProceduralSprites.Circle();
        spark.color   = Color.white;
        spark.raycastTarget = false;

        const float Dur = 0.35f;
        float t = 0f;
        try
        {
            while (t < Dur)
            {
                t += FxStep;
                float k = Mathf.Clamp01(t / Dur);
                float e = 1f - (1f - k) * (1f - k);                     // ease-out — 닿을 때 느려진다
                brt.sizeDelta = new Vector2(3f, distance * e);
                srt.anchoredPosition = new Vector2(0f, -distance * e);
                beam.color  = new Color(1f, 0.83f, 0.45f, 0.9f * (1f - k * 0.5f));
                spark.color = new Color(1f, 1f, 1f, 1f - k * 0.3f);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (spark != null) Destroy(spark.gameObject);
            if (beam  != null) Destroy(beam.gameObject);
        }
    }

    /// <summary>
    /// 기획 §7-1 0.90 — 다음 칸이 <b>차례로 승격</b>한다: 옛 높이(축약 행)에서 새 높이(차례 행)로 자라며
    /// 테두리가 점등하고 ◆ 표식이 튄다. <paramref name="fromHeight"/>는 Refresh 전에 읽어 둔 값.
    /// </summary>
    public async UniTask PlayBecomeTurnAsync(float fromHeight, CancellationToken ct)
    {
        var rt   = (RectTransform)transform;
        float to = rt.sizeDelta.y;
        if (fromHeight <= 0f || Mathf.Approximately(fromHeight, to)) fromHeight = to;

        var group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();

        const float Dur = 0.30f;
        float t = 0f;
        try
        {
            while (t < Dur)
            {
                t += FxStep;
                float k = Mathf.Clamp01(t / Dur);
                float e = 1f - Mathf.Pow(1f - k, 3f);                   // ease-out cubic
                SetHeightImmediate(Mathf.Lerp(fromHeight, to, e));
                if (_turnBorder) _turnBorder.color = Color.Lerp(Color.white, TurnOrange, k);
                if (_markBg)     _markBg.color = Color.Lerp(Color.white, TurnOrange, k);
                if (_markBg)     _markBg.rectTransform.localScale = Vector3.one * (1f + 0.35f * Mathf.Sin(k * Mathf.PI));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            SetHeightImmediate(to);
            if (_turnBorder) _turnBorder.color = TurnOrange;
            if (_markBg)     { _markBg.color = TurnOrange; _markBg.rectTransform.localScale = Vector3.one; }
        }
    }

    private void SetPulse(bool on)
    {
        if (!on)
        {
            if (_pulseCts != null) { _pulseCts.Cancel(); _pulseCts.Dispose(); _pulseCts = null; }
            return;
        }
        if (_pulseCts != null) return;   // 이미 돌고 있다
        _pulseCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        PulseAsync(_pulseCts.Token).Forget();
    }

    /// <summary>기획 §5-1 「강조 테두리 + 은은한 점멸」 — 띠와 표식이 1.6초 주기로 숨 쉬듯 밝아졌다 가라앉는다.</summary>
    private async UniTaskVoid PulseAsync(CancellationToken ct)
    {
        var baseCol = TurnOrange;
        var lit     = Color.Lerp(baseCol, Color.white, 0.45f);
        try
        {
            float t = 0f;
            while (!ct.IsCancellationRequested)
            {
                t += Time.unscaledDeltaTime;
                float k = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f / 1.6f);
                if (_turnBorder) _turnBorder.color = Color.Lerp(baseCol, lit, k);
                if (_markBg)     _markBg.color     = Color.Lerp(baseCol, lit, k);
                if (_markGlyph)  _markGlyph.color  = Color.Lerp(baseCol, lit, k);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_seat == Seat.Turn)
            {
                if (_turnBorder) _turnBorder.color = baseCol;
                if (_markBg)     _markBg.color     = baseCol;
                if (_markGlyph)  _markGlyph.color  = baseCol;
            }
        }
    }

    /// <summary>와이어프레임의 차례 표식·테두리 주황.</summary>
    private static readonly Color TurnOrange = new(1f, 0.53f, 0.13f, 1f);
    private static readonly Color MarkOff    = new(0.42f, 0.42f, 0.46f, 1f);
    private static readonly Color MarkOffDim = new(0.30f, 0.30f, 0.34f, 1f);

    private void OnDisable() => SetPulse(false);

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
            fontSizes = new Vector3(16f, 14f, 14f),
        },
        Seat.Ahead => new SeatLayout
        {
            height = 28f,
            name   = new Rect(16f, 5f, 300f, 18f),
            fontSizes = new Vector3(16f, 14f, 14f),
        },
        _ => new SeatLayout
        {
            height    = 74f,
            name      = new Rect( 26f,  8f, 142f, 22f),
            cost      = new Rect(-14f,  8f, 120f, 22f),
            change    = new Rect( 26f, 32f, 290f, 17f),
            condition = new Rect( 26f, 51f, 290f, 15f),
            fontSizes = new Vector3(17f, 15f, 14f),
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
        bool locked = seat == Seat.Ahead && state.BlockedByRequirement;

        // 와이어프레임 표식 4종: 지난 칸 = 청록 원 + 체크 / 차례 = 주황 고리 + ◆ / 잠긴 칸 = 회색 이중 고리 / 선행 조건 = 회색 원 + ×
        if (passed)
        {
            _markBg.sprite = UIProceduralSprites.Circle();     _markBg.color = AltarPalette.Essence;
            _markFg.sprite = UIProceduralSprites.Check();      _markFg.color = Color.white;
            _markFg.gameObject.SetActive(true); _markFg.rectTransform.localScale = Vector3.one;
            _markGlyph.text = "";
        }
        else if (turn)
        {
            _markBg.sprite = UIProceduralSprites.Ring(0.11f);  _markBg.color = TurnOrange;
            _markFg.gameObject.SetActive(false);
            _markGlyph.text = "◆"; _markGlyph.color = TurnOrange;
        }
        else if (locked)
        {
            _markBg.sprite = UIProceduralSprites.Circle();     _markBg.color = MarkOffDim;
            _markFg.gameObject.SetActive(false);
            _markGlyph.text = "×"; _markGlyph.color = MarkOff;
        }
        else
        {
            _markBg.sprite = UIProceduralSprites.Ring(0.11f);  _markBg.color = MarkOff;
            _markFg.sprite = UIProceduralSprites.Ring(0.16f);  _markFg.color = MarkOff;
            _markFg.gameObject.SetActive(true); _markFg.rectTransform.localScale = Vector3.one * 0.55f;
            _markGlyph.text = "";
        }

        // 표식을 행 세로 가운데에 맞춘다 — 행 높이가 자리마다 달라 고정값이면 어긋난다.
        _markBg.rectTransform.anchoredPosition = new Vector2(RailX, -rowHeight * 0.5f);
    }

    private void EnsureTurnBorder()
    {
        if (_turnBorder != null || background == null) return;
        var go = new GameObject("TurnBorder", typeof(RectTransform), typeof(CanvasRenderer));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        _turnBorder = go.AddComponent<Image>();
        _turnBorder.sprite = UIProceduralSprites.RoundedOutline(radius: 6f, stroke: 2f);
        _turnBorder.type   = Image.Type.Sliced;
        _turnBorder.color  = TurnOrange;
        _turnBorder.raycastTarget = false;
        rt.SetAsLastSibling();
    }

    private void EnsureRail()
    {
        if (_markBg != null) return;

        var parent = (RectTransform)transform;

        var bg = new GameObject("Mark", typeof(RectTransform), typeof(CanvasRenderer));
        var rt = (RectTransform)bg.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(MarkSize, MarkSize);
        _markBg = bg.AddComponent<Image>();
        _markBg.raycastTarget = false;

        var fg = new GameObject("MarkFg", typeof(RectTransform), typeof(CanvasRenderer));
        var frt = (RectTransform)fg.transform;
        frt.SetParent(rt, false);
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
        _markFg = fg.AddComponent<Image>();
        _markFg.raycastTarget = false;

        _markGlyph = new GameObject("Glyph", typeof(RectTransform), typeof(CanvasRenderer))
            .AddComponent<TextMeshProUGUI>();
        var grt = _markGlyph.rectTransform;
        grt.SetParent(rt, false);
        grt.anchorMin = Vector2.zero; grt.anchorMax = Vector2.one;
        grt.offsetMin = Vector2.zero; grt.offsetMax = Vector2.zero;
        if (nameText != null && nameText.font != null) _markGlyph.font = nameText.font;
        _markGlyph.fontSize = MarkGlyph;
        _markGlyph.enableAutoSizing = false;
        _markGlyph.alignment = TextAlignmentOptions.Center;
        _markGlyph.raycastTarget = false;
    }

    /// <summary>Rect의 y는 <b>아래로 재는 값</b>이라 anchoredPosition에는 부호를 뒤집어 넣는다.</summary>
    private static void Put(TMP_Text t, Rect r, float size)
    {
        if (t == null) return;
        var rt = t.rectTransform;
        rt.anchoredPosition = new Vector2(r.x, -r.y);
        rt.sizeDelta        = new Vector2(r.width, r.height);
        t.fontSize          = size;
        // 프리팹에 구워진 자동 크기 상한(17.26/12.69)이 자리 글자 크기를 덮어쓰지 않게 상한도 같이 맞춘다.
        if (t.enableAutoSizing) t.fontSizeMax = size;
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

        // 와이어프레임(09-09): 왼쪽 띠 대신 <b>차례 행에만 주황 테두리</b>. 상태는 바깥 레일 표식이 말한다.
        if (accentBar) accentBar.gameObject.SetActive(false);
        EnsureTurnBorder();
        if (_turnBorder)
        {
            _turnBorder.gameObject.SetActive(seat == Seat.Turn);
            _turnBorder.color = TurnOrange;
        }

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
            nameText.text     = node.DisplayName;   // 지난 칸 표시는 레일의 체크 표식이 맡는다(■ 접두 제거)
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
