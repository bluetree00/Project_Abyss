using UnityEngine;

/// <summary>
/// 창(팝업 판)을 <b>화면에 맞춰 최대로</b> 키운다. 프리팹에서 재던 <b>비율은 그대로</b> 지킨다.
///
/// <para>화면들은 목업 px를 그대로 박은 고정 크기 창이었다 — 유물 파츠는 화면 세로의 56%,
/// 원거리는 72%만 쓰고 나머지를 암막으로 버렸다. 그래서 같은 내용이 좁은 판에 눌려
/// 목업 비율대로 앉히기가 어려웠다.</para>
///
/// <para><b>이게 동작하는 전제</b> — 창 안의 요소가 전부 <b>비율(스트레치) 앵커</b>여야 한다.
/// 점 앵커면 창만 커지고 내용은 좌상단에 원래 크기로 남는다. 프리팹들은 그 변환을 마쳤다.
/// 자세한 배경은 <see cref="UIProportional"/>.</para>
///
/// <para>크기·비율의 기준은 <b>프리팹에 저장된 창 rect</b>다. 인스펙터에서 창을 끌어 다시 재면
/// 그 비율이 새 기준이 된다 — 코드를 고칠 일이 없다.</para>
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class UIWindowFitter : MonoBehaviour
{
    // ── [SerializeField] ─────────────────────────────────────
    [Header("맞춤")]
    [Tooltip("화면 대비 창이 차지할 비율. 1이면 가장자리에 여백이 없다.")]
    [Range(0.5f, 1f)]
    [SerializeField] private float margin = 0.94f;

    [Tooltip("창이 커진 배율만큼 글꼴도 키운다. 끄면 상자만 커지고 글자는 그대로다.")]
    [SerializeField] private bool scaleFonts = true;

    [Tooltip("프리팹 크기보다 작아지지 않게 한다. 세로가 아주 짧은 화면에서 판이 뭉개지는 것을 막는다.")]
    [SerializeField] private bool neverShrink = false;

    [Tooltip("프리팹 크기 대비 최대 확대 배율. 작게 설계된 모달이 화면을 통째로 점거하는 것을 막는다.")]
    [Range(1f, 3f)]
    [SerializeField] private float maxScale = 1.3f;

    /// <summary>
    /// <b>컨텐츠 화면</b>(플레이어가 머무는 주 화면)이 쓰는 확대 상한.
    ///
    /// <para>기본 1.3은 작게 설계된 모달이 화면을 점거하는 것을 막는 값이다. 그런데 그 값이
    /// 컨텐츠 화면에도 걸려, 같은 게임인데 화면마다 차지하는 크기가 제각각이 됐다 —
    /// 유물 선택은 세로 78%, 무기 교체는 75%인데 재련소·상점은 94%였다.</para>
    ///
    /// <para>이 값을 쓰면 상한이 아니라 <c>margin</c>이 기준이 되어, 모든 컨텐츠 화면이
    /// <b>같은 세로 비율</b>로 열린다. 가로는 각 화면 아트의 가로세로비를 따른다.</para>
    /// </summary>
    public const float ContentScreen = 3f;

    // ── Private ──────────────────────────────────────────────
    private RectTransform _rt;
    private float   _baseW, _baseH;      // 프리팹에 저장된 기준 크기
    private Vector2 _lastArea;           // 마지막으로 맞춘 화면 크기

    // ── Lifecycle ────────────────────────────────────────────
    private void Awake()
    {
        _rt = (RectTransform)transform;
        // 프리팹 값을 기준으로 잡는다 — Awake는 인스턴스당 한 번이라 아직 손대기 전이다.
        _baseW = _rt.sizeDelta.x;
        _baseH = _rt.sizeDelta.y;
    }

    private void OnEnable() => Fit();

    /// <summary>
    /// 창 모드에서 해상도가 바뀌면 다시 맞춘다. 부모 크기가 그대로면 즉시 빠지므로
    /// 사실상 <see cref="Vector2"/> 비교 한 번이다.
    /// </summary>
    private void LateUpdate()
    {
        if (_rt != null && _rt.parent is RectTransform area && area.rect.size != _lastArea) Fit();
    }

    // ── Public Methods ───────────────────────────────────────

    /// <summary>
    /// 코드로 붙일 때 설정을 함께 준다. 프리팹에 손으로 붙이면 인스펙터 값이 쓰이지만,
    /// <b>재굽기가 계층을 다시 만들면 손으로 붙인 컴포넌트는 사라진다</b> — 그래서 빌더가 붙인다.
    /// </summary>
    public UIWindowFitter Configure(float margin = 0.94f, bool scaleFonts = true, float maxScale = 1.3f)
    {
        this.margin     = margin;
        this.scaleFonts = scaleFonts;
        this.maxScale   = maxScale;
        return this;
    }


    /// <summary>지금 화면 크기에 맞춰 창을 다시 잰다.</summary>
    public void Fit()
    {
        if (_rt == null) _rt = (RectTransform)transform;
        if (_baseH <= 0f || _baseW <= 0f) return;

        if (_rt.parent is not RectTransform area) return;
        var size = area.rect.size;
        if (size.x <= 1f || size.y <= 1f) return;
        _lastArea = size;

        float aspect = _baseW / _baseH;
        float h = Mathf.Min(size.y * margin, size.x * margin / aspect);

        // 작게 설계된 모달까지 화면 가득 채우면 <b>디자인 비례가 무너진다</b> —
        // 화면의 56%를 쓰던 카드 선택 창을 94%로 부풀리면 모달이 아니라 전체화면이 된다.
        // 여백을 남기는 것도 설계의 일부라, 원래 크기 대비 확대 폭을 묶는다.
        if (maxScale > 1f) h = Mathf.Min(h, _baseH * maxScale);
        if (neverShrink)   h = Mathf.Max(h, _baseH);

        _rt.sizeDelta = new Vector2(h * aspect, h);

        if (scaleFonts) UIProportional.ScaleFonts(_rt, h / _baseH);
    }
}
