using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// 재화 숫자에 <b>변화 연출</b>을 붙이는 공용 컴포넌트. 골드·강화재료·원석·심연의 정수가 모두 이걸 쓴다.
///
/// <para>예전엔 여섯 화면이 각자 <c>text = value.ToString()</c>으로 숫자를 <b>즉시 갈아끼웠다</b>.
/// 값이 언제 변했는지, 얼마나 변했는지가 화면에 남지 않아 — 특히 상점에서 사고 나면
/// "빠져나간 느낌" 없이 숫자만 달라져 있었다.</para>
///
/// <para><b>연출 셋</b>이 한 벌로 움직인다:
/// ① 숫자가 이전 값에서 새 값으로 <b>굴러간다</b>(텀블러) ·
/// ② 늘면 재화색, 줄면 붉은색으로 <b>한 번 물든다</b> ·
/// ③ <c>+120</c> / <c>−400</c> 델타가 <b>떠올랐다 사라진다</b>.</para>
///
/// <para>화면을 <b>처음 열 때는 연출하지 않는다</b> — 0에서 현재값까지 굴러가는 카운트업은
/// 아무 일도 안 일어났는데 일어난 것처럼 보여 소음이 된다. 첫 값은 즉시 앉힌다.</para>
///
/// <para>모든 시간은 <c>unscaledDeltaTime</c>이다 — 재화 UI는 대부분 시간이 멈춘 팝업 위에 있다.</para>
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public sealed class CurrencyCounter : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────
    private const float RollTime  = 0.34f;   // 텀블러 길이
    private const float PulseTime = 0.30f;   // 색·크기 펄스
    private const float DeltaTime_ = 0.85f;  // 델타 라벨 수명
    private const float PopScale   = 1.16f;
    private const float DeltaRise  = 26f;    // 델타가 떠오르는 높이(px)

    private static readonly Color Gain = new(0.53f, 0.86f, 0.45f, 1f);
    private static readonly Color Loss = new(0.92f, 0.38f, 0.34f, 1f);

    // ── [SerializeField] ─────────────────────────────────────
    [Header("표기")]
    [Tooltip("숫자 앞뒤에 붙는 글자. 예: 접두 \"원석 \"")]
    [SerializeField] private string prefix = "";
    [SerializeField] private string suffix = "";

    [Tooltip("천 단위 쉼표. 정수처럼 큰 값에 켠다.")]
    [SerializeField] private bool thousands = true;

    [Header("연출")]
    [Tooltip("끄면 숫자만 즉시 바뀐다(연출 없음).")]
    [SerializeField] private bool animate = true;

    [Tooltip("델타(+120 / −400)를 띄울지. 자리가 좁은 칸에서는 끈다.")]
    [SerializeField] private bool showDelta = true;

    // ── Private ──────────────────────────────────────────────
    private TMP_Text _text;
    private Color    _baseColor;
    private Vector3  _baseScale;
    private int      _shown;          // 화면에 지금 떠 있는 값
    private bool     _seeded;         // 첫 값을 받았는가
    private TMP_Text _deltaLabel;

    private CancellationTokenSource _rollCts, _pulseCts, _deltaCts;

    // ── Properties ───────────────────────────────────────────
    /// <summary>화면에 떠 있는 값(연출 중이면 굴러가는 중간값이 아니라 <b>목표값</b>).</summary>
    public int Value => _shown;

    // ── Lifecycle ────────────────────────────────────────────
    private void Awake()
    {
        _text      = GetComponent<TMP_Text>();
        _baseColor = _text.color;
        _baseScale = _text.rectTransform.localScale;
    }

    private void OnDisable()
    {
        // 꺼질 때 연출을 끊고 목표값·기본 모습으로 되돌린다 —
        // 중간값이나 펄스색이 남은 채 다시 켜지면 그게 그 화면의 첫인상이 된다.
        CancelAll();
        if (_text == null) return;
        _text.text  = Format(_shown);
        _text.color = _baseColor;
        _text.rectTransform.localScale = _baseScale;
    }

    private void OnDestroy() => CancelAll();

    // ── Public Methods ───────────────────────────────────────

    /// <summary>값을 표시한다. 같은 값이면 아무 일도 하지 않는다(매 프레임 불러도 안전).</summary>
    public void Set(int value)
    {
        if (_text == null) _text = GetComponent<TMP_Text>();

        if (!_seeded)
        {
            _seeded = true;
            _shown  = value;
            _text.text = Format(value);
            return;
        }

        if (value == _shown) return;

        int delta = value - _shown;
        int from  = _shown;
        _shown    = value;

        if (!animate || !isActiveAndEnabled)
        {
            _text.text = Format(value);
            return;
        }

        RollAsync(from, value).Forget();
        PulseAsync(delta > 0).Forget();
        if (showDelta) DeltaAsync(delta).Forget();
    }

    /// <summary>연출 없이 즉시 맞춘다. 화면 전환·재바인딩처럼 "변한 게 아닌" 경우에 쓴다.</summary>
    public void SetInstant(int value)
    {
        if (_text == null) _text = GetComponent<TMP_Text>();
        CancelAll();
        _seeded = true;
        _shown  = value;
        _text.text  = Format(value);
        _text.color = _baseColor;
        _text.rectTransform.localScale = _baseScale;
    }

    // ── Private Methods ──────────────────────────────────────

    private string Format(int v) => prefix + (thousands ? v.ToString("N0") : v.ToString()) + suffix;

    private async UniTaskVoid RollAsync(int from, int to)
    {
        _rollCts?.Cancel();
        _rollCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        var ct = _rollCts.Token;

        try
        {
            float t = 0f;
            while (t < RollTime)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / RollTime);
                k = 1f - (1f - k) * (1f - k);                       // ease-out — 끝에서 천천히 멎는다
                _text.text = Format(Mathf.RoundToInt(Mathf.Lerp(from, to, k)));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            _text.text = Format(to);
        }
        catch (System.OperationCanceledException) { }
    }

    private async UniTaskVoid PulseAsync(bool gain)
    {
        _pulseCts?.Cancel();
        _pulseCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        var ct = _pulseCts.Token;

        var tint = gain ? Gain : Loss;
        try
        {
            float t = 0f;
            while (t < PulseTime)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / PulseTime);
                _text.color = Color.Lerp(tint, _baseColor, k);
                _text.rectTransform.localScale = Vector3.Lerp(_baseScale * PopScale, _baseScale, k);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            _text.color = _baseColor;
            _text.rectTransform.localScale = _baseScale;
        }
        catch (System.OperationCanceledException) { }
    }

    private async UniTaskVoid DeltaAsync(int delta)
    {
        _deltaCts?.Cancel();
        _deltaCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        var ct = _deltaCts.Token;

        var label = EnsureDeltaLabel();
        if (label == null) return;

        label.text  = (delta > 0 ? "+" : "−") + Mathf.Abs(delta).ToString("N0");
        label.color = delta > 0 ? Gain : Loss;
        label.gameObject.SetActive(true);

        var rt = label.rectTransform;
        try
        {
            float t = 0f;
            while (t < DeltaTime_)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / DeltaTime_);
                rt.anchoredPosition = new Vector2(0f, Mathf.Lerp(0f, DeltaRise, 1f - (1f - k) * (1f - k)));
                var c = label.color; c.a = 1f - k * k;              // 뒤로 갈수록 빨리 사라진다
                label.color = c;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (System.OperationCanceledException) { }
        finally
        {
            if (label != null) label.gameObject.SetActive(false);
        }
    }

    /// <summary>델타 라벨을 1회 만든다. 본체 글자 <b>위쪽</b>에 뜬다.</summary>
    private TMP_Text EnsureDeltaLabel()
    {
        if (_deltaLabel != null) return _deltaLabel;
        if (_text == null) return null;

        var go = new GameObject("Delta", typeof(RectTransform), typeof(CanvasRenderer));
        var rt = (RectTransform)go.transform;
        rt.SetParent(_text.rectTransform, false);
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(140f, 22f);
        rt.anchoredPosition = Vector2.zero;

        _deltaLabel = go.AddComponent<TextMeshProUGUI>();
        _deltaLabel.font          = _text.font;
        _deltaLabel.fontSize      = _text.fontSize * 0.72f;
        _deltaLabel.fontStyle     = FontStyles.Bold;
        _deltaLabel.alignment     = TextAlignmentOptions.Center;
        _deltaLabel.raycastTarget = false;
        _deltaLabel.textWrappingMode = TextWrappingModes.NoWrap;
        go.SetActive(false);
        return _deltaLabel;
    }

    private void CancelAll()
    {
        _rollCts?.Cancel();  _rollCts = null;
        _pulseCts?.Cancel(); _pulseCts = null;
        _deltaCts?.Cancel(); _deltaCts = null;
    }

    // ── Static ───────────────────────────────────────────────

    /// <summary>
    /// 텍스트에 카운터를 붙이고 값을 넣는다. 호출부가 <c>null</c> 검사와 컴포넌트 부착을
    /// 매번 쓰지 않도록 이 한 줄로 끝낸다. 이미 붙어 있으면 그대로 쓴다.
    /// </summary>
    public static void Apply(TMP_Text text, int value, string prefix = null, bool thousands = true)
    {
        if (text == null) return;

        if (!text.TryGetComponent<CurrencyCounter>(out var c))
        {
            c = text.gameObject.AddComponent<CurrencyCounter>();
            c._text      = text;
            c._baseColor = text.color;
            c._baseScale = text.rectTransform.localScale;
            c.thousands  = thousands;
            if (prefix != null) c.prefix = prefix;
        }
        c.Set(value);
    }
}
