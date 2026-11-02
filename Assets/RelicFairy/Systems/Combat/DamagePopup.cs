using System.Globalization;
using TMPro;
using UnityEngine;

/// <summary>
/// 월드 공간에 떠오르는 데미지 숫자.
///
/// 연출(10-23 정리 — 사용자 「지저분하다 · 깔끔하고 강조되게」):
///   • 세 층 — 일반(상아 흰) · 치명(금 · 크게 · 찍히듯 내려앉음 + 흰 번쩍) · 추가 피해(룬 · 지속, 속성 단색 · 작게 · 옆 줄).
///     예전엔 속성마다 세로 그라데이션 · 치명 백열→적열 + 발광 · 자릿수 아치 · 「!」가 겹쳐 무지개처럼 읽혔다.
///   • 곧게 떠오른다 — 좌우로 튀고 중력에 떨어지던 포물선을 걷었다. 처음에 빨리 오르고 곧 멈춘다.
///   • 영수증 줄 — 같은 대상의 새 숫자는 맨 아래에 찍히고 앞의 숫자들이 한 칸씩 밀려 올라간다(<see cref="Bump"/>).
///     위로 쌓던 방식은 늦게 찍힌 숫자가 앞 숫자를 뚫고 지나갔다. 줄은 <see cref="MaxRows"/>칸까지 — 넘친 맨 위는 바로 흐려진다.
///   • 글꼴 — 톤 v2 Noto Sans KR Medium(UI 「얇게 · 모던」). 굵은 DNF 디스플레이체 + 두꺼운 테두리 + 그림자가 획을 뭉갰다.
///
/// 10-31 사용자 「굵은 표기는 아니고 깔끔한 텍스트에 종류 · 치명에 맞는 연출, 누적 데미지가 계속 올라오는 느낌」:
///   • 크기는 <b>최근 평균 타격 대비</b>로 — 시기 · 챕터가 올라 피해 자릿수가 커져도 「평소보다 큰 한 방」이 크게 뜬다(고정 15~400 매핑은 후반에 전부 최대였다).
///   • 치명 = 찍히며 짧게 떨린다 · 줄은 6칸, 밀려 올라간 숫자일수록 작고 옅어진다(흘러 올라가는 영수증).
///   • 누적 — 정예 · 보스에 이어 맞힌 피해를 줄 옆 하나의 숫자로 세어 올린다(<see cref="ShowTotal"/> · 스포너가 관리).
///   • 깊이 검사 끔 — 근접 거리에서 숫자가 큰 보스 몸에 묻혔다(10-30 실측 · 숲 수호자 3 m 네 숫자 중 셋).
///
/// 풀링되며 라이프타임 종료 시 비활성화 (재사용 대비).
/// </summary>
public class DamagePopup : MonoBehaviour
{
    // ── Constants ───────────────────────────────────────────────────
    /// <summary>한 줄(같은 대상 · 같은 층)에 동시에 남는 숫자 수. 넘친 맨 위 숫자는 바로 흐려진다.</summary>
    private const int MaxRows = 6;
    /// <summary>최근 평균 타격이 새 타격을 따라가는 몫(지수 이동 평균).</summary>
    private const float AvgFollow = 0.08f;
    /// <summary>누적 숫자 머리말(작고 옅게) — 글자 배열로 쓴다(세어 올리는 동안 문자열을 만들지 않게).</summary>
    private const string TotalPrefix = "<size=52%><alpha=#B8>누적<alpha=#FF></size>\n";

    // ── [SerializeField] ────────────────────────────────────────────
    [SerializeField] private TMP_Text label;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Life")]
    [SerializeField] private float normalLife = 0.8f;
    [Tooltip("치명은 조금 더 오래 남는다 — 강조는 크기 · 색 · 머무는 시간으로.")]
    [SerializeField] private float critLife = 1.2f;
    [SerializeField] private float extraLife = 0.65f;
    [Tooltip("수명의 이 비율부터 흐려진다.")]
    [SerializeField] private float fadeFrom = 0.62f;

    [Header("Motion")]
    [Tooltip("떠오르는 높이(m · 화면 위). 처음에 빨리 오르고 곧 멈춘다(감속).")]
    [SerializeField] private float riseDistance = 0.34f;
    [Tooltip("다 떠오르기까지(초). 모든 숫자가 같은 곡선이라 먼저 찍힌 숫자가 늘 위에 있다.")]
    [SerializeField] private float riseTime = 0.55f;
    [Tooltip("줄 한 칸 간격(m · 화면 위). 새 숫자가 찍히면 앞 숫자들이 이만큼 밀려 올라간다.")]
    [SerializeField] private float rowStep = 0.3f;
    [Tooltip("밀려 올라간 한 칸마다 줄어드는 크기 몫 — 위로 갈수록 작아져 새 숫자가 눈에 먼저 든다.")]
    [SerializeField] private float rowShrink = 0.06f;
    [Tooltip("밀려 올라간 한 칸마다 옅어지는 몫.")]
    [SerializeField] private float rowFade = 0.1f;
    [Tooltip("추가 피해 줄을 주 숫자 옆으로 비키는 거리(m · 화면 오른쪽).")]
    [SerializeField] private float extraSideOffset = 0.62f;

    [Header("Pop")]
    [Tooltip("일반 — 이 배율에서 커지며 살짝 넘쳤다 안착한다.")]
    [SerializeField] private float normalPopFrom = 0.55f;
    [Tooltip("치명 — 이 배율에서 <b>내려앉는다</b>(찍히는 느낌).")]
    [SerializeField] private float critPopFrom = 1.45f;
    [SerializeField] private float popTime = 0.12f;
    [Tooltip("합산으로 값이 오를 때 다시 튕기는 배율.")]
    [SerializeField] private float kickScale = 1.12f;
    [Tooltip("치명이 찍힐 때 좌우 떨림(m) — 끝으로 갈수록 잦아든다.")]
    [SerializeField] private float critShake = 0.03f;
    [SerializeField] private float critShakeTime = 0.12f;

    [Header("Size")]
    // 크기는 최근 평균 타격 대비 — 평균의 smallAtAvg배 이하는 최소, bigAtAvg배 이상은 최대(로그 매핑).
    [SerializeField] private float smallAtAvg = 0.6f;
    [SerializeField] private float bigAtAvg = 4f;
    [SerializeField] private float minSize = 0.95f;
    [SerializeField] private float maxSize = 1.6f;
    [SerializeField] private float critSize = 1.45f;
    [SerializeField] private float synergyScale = 0.8f;
    [SerializeField] private float dotScale = 0.68f;
    [Tooltip("추가 피해(룬 · 증강 보너스) 기본 크기 — 주 숫자보다 조금 작게.")]
    [SerializeField] private float bonusScale = 0.85f;
    [Tooltip("최근 평균 타격의 이 비율 이상인 추가 피해는 살짝 크게(11-02 「피해량이 많이 보이게」).")]
    [SerializeField] private float bonusBigAt = 0.5f;
    [SerializeField] private float bonusBigScale = 1.2f;

    [Header("Total (누적)")]
    [Tooltip("마지막 타 뒤 누적 숫자가 남는 시간(끝 무렵 흐려진다). 스포너의 이어 맞힘 창보다 조금 길다.")]
    [SerializeField] private float totalLife = 1.4f;
    [Tooltip("줄 가운데에서 누적 숫자 오른쪽 끝까지(m · 화면 왼쪽). 누적은 오른쪽 맞춤 — 자릿수가 늘면 왼쪽으로 자란다. 추가 피해 줄은 오른쪽.")]
    [SerializeField] private float totalSideOffset = 0.6f;
    [SerializeField] private float totalMinScale = 1.15f;
    [SerializeField] private float totalMaxScale = 1.5f;
    [Tooltip("이 타수에 이르면 누적 숫자가 가장 크고 따뜻해진다(두 번째 타 = 시작).")]
    [SerializeField] private float totalHitsForMax = 12f;
    [SerializeField] private Color totalColor = new(0.98f, 0.96f, 0.92f, 1f);
    [SerializeField] private Color totalHotColor = new(1f, 0.84f, 0.48f, 1f);
    [SerializeField] private float totalKickScale = 1.18f;
    [Tooltip("세어 올리는 빠르기(지수) — 12면 약 0.25초에 거의 닿는다.")]
    [SerializeField] private float totalCountRate = 12f;

    [Header("Colors")]
    [Tooltip("일반 피해 — 조용한 상아 흰.")]
    [SerializeField] private Color normalColor = new(0.97f, 0.95f, 0.91f, 1f);
    [Tooltip("치명 — 톤 v2 금(「누를 것 · 남는 것」의 금과 같은 결).")]
    [SerializeField] private Color critTint = new(1f, 0.8f, 0.32f, 1f);
    [Tooltip("치명이 찍히는 순간 번쩍이는 색 → 금으로 식는다.")]
    [SerializeField] private Color critFlashColor = Color.white;
    [SerializeField] private float critFlashTime = 0.14f;
    [Tooltip("속성 없는 추가 피해(방어 무시 등).")]
    [SerializeField] private Color synergyColor = new(0.78f, 0.68f, 1f, 1f);
    [Tooltip("속성 없는 지속 피해 틱.")]
    [SerializeField] private Color dotColor = new(1f, 0.6f, 0.3f, 1f);

    [Header("Colors — 추가 피해 출처(11-02 Q29)")]
    [Tooltip("룬 — 속성이 있으면 속성 색, 없으면 이 금.")]
    [SerializeField] private Color bonusRuneColor = new(0.91f, 0.78f, 0.42f, 1f);
    [Tooltip("재련 증강 — 은청.")]
    [SerializeField] private Color bonusAugmentColor = new(0.66f, 0.76f, 0.94f, 1f);
    [Tooltip("전설 — 주홍(#FF855C).")]
    [SerializeField] private Color bonusLegendaryColor = new(1f, 0.522f, 0.361f, 1f);
    [Tooltip("아이템(다음 공격 강화 등) — 옅은 보라.")]
    [SerializeField] private Color bonusItemColor = new(0.79f, 0.7f, 0.95f, 1f);

    [Header("Material")]
    // 폰트의 공용 머티리얼을 고치면 그 폰트를 쓰는 모든 UI가 같이 변한다 → 사본 두 벌(일반 · 치명)을 팝업끼리만 공유한다(배칭 유지).
    [SerializeField] private float faceDilate = 0.1f;   // Medium 정적 굵기 — 살짝만 채워 작은 크기에서도 또렷하게
    [SerializeField] private float outlineWidth = 0.14f;
    [SerializeField] private Color outlineColor = new(0.05f, 0.04f, 0.06f, 1f);
    [SerializeField] private Color critOutlineColor = new(0.2f, 0.1f, 0.02f, 1f);

    // ── Static ──────────────────────────────────────────────────────
    // 빌보드용 메인 카메라 — 전 팝업 공유 1회 캐시. 씬 전환 시 파괴되면 Unity-null → 다음 Show에서 재탐색.
    private static Camera s_cam;

    // 글꼴과 재질은 처음 만든 한 벌을 전 팝업이 쓴다 — 팝업마다 글꼴이 갈리면 재질의 아틀라스가 어긋난다.
    private static TMP_FontAsset s_font;
    private static Material s_normalMat;
    private static Material s_critMat;
    private static readonly int GuiZTestId = Shader.PropertyToID("unity_GUIZTestMode");

    // 최근 평균 타격(주 피해) — 크기 기준. 0 = 아직 없음.
    private static float s_avgHit;
    // 누적 숫자 글자 배열(머리말 + 자릿수 · 쉼표) — 팝업끼리 함께 쓴다(한 프레임에 하나씩 써서 바로 넘긴다).
    private static readonly char[] s_totalChars = new char[64];

    // ── Private ─────────────────────────────────────────────────────
    private Vector3 _anchor;          // 찍힌 자리(옆 줄 비킴 포함) — 여기서 떠오르고 밀려 올라간다
    private float _age;               // 찍힌 뒤 흐른 시간 — 떠오름 · 튕김(되감지 않는다)
    private float _elapsed;           // 수명 진행 — 합산이 되감아 흐려짐을 미룬다
    private float _life;
    private float _flashElapsed = -1f; // 치명 번쩍 경과(음수 = 없음)
    private float _baseScale;
    private TextAlignmentOptions _baseAlign;   // 프리팹 맞춤 · 축 — 누적(오른쪽 맞춤)으로 쓰인 팝업이 풀에서 돌아오면 되돌린다
    private Vector2 _basePivot;
    private float _popFrom;
    private float _kickElapsed = -1f; // 합산 튕김 경과(음수 = 없음)
    private float _rowCurrent;        // 지금 밀려 올라간 높이(m) — 목표로 부드럽게 따라간다
    private int _row;                 // 줄에서의 칸(0 = 맨 아래 · 가장 새 숫자)
    private bool _crit;
    private Color _color;
    private bool _active;
    private Camera _cam;
    private System.Action<DamagePopup> _onReleased;
    private float _shakeElapsed = -1f; // 치명 떨림 경과(음수 = 없음)
    private float _kickNow;           // 이번 튕김 배율(합산 = kickScale · 누적 = totalKickScale)
    private bool  _isTotal;           // 누적 숫자인가 — 떠오르지 않고 세어 올린다
    private float _totalTarget;
    private float _totalShown;
    private int   _totalShownInt = -1;
    private int   _hits;
    private BonusSource _source;      // 추가 피해 출처(Bonus일 때만 쓴다) — 색
    private float _delay;             // 나타나기까지 남은 시간 — 추가 피해는 주 숫자 뒤에 이어 찍힌다

    // ── Properties ──────────────────────────────────────────────────
    /// <summary>아직 화면에 떠 있는가. 합산(Accumulate) 대상이 될 수 있는지 판단하는 조건.</summary>
    public bool IsShowing => _active;

    /// <summary>같은 줄 열쇠(대상 × 주/추가). 새 숫자가 찍히면 같은 열쇠의 숫자들이 밀려 올라간다. 0 = 줄 없음.</summary>
    public int Column { get; private set; }

    /// <summary>누적 숫자로 떠 있는 대상(스포너의 대상 ID). 0 = 누적 아님 — 풀에서 다른 숫자로 다시 쓰이면 0으로 돌아간다.</summary>
    public int TotalOwner { get; private set; }

    // ── Lifecycle ───────────────────────────────────────────────────
    private void Awake()
    {
        if (label == null)       label       = GetComponentInChildren<TMP_Text>();
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (label != null) { _baseAlign = label.alignment; _basePivot = label.rectTransform.pivot; }
        // 몬스터 체력 바 · 이름표도 월드 캔버스(정렬 0)라 같은 순서면 거리로 갈려 이름표가 숫자를 덮었다(11-02 사용자)
        var canvas = GetComponentInChildren<Canvas>(true);
        if (canvas != null) canvas.sortingOrder = UISortingOrder.WorldDamage;
        EnsureStyle();
    }

    private void LateUpdate()
    {
        if (!_active) return;
        if (_delay > 0f)
        {
            // 이어 찍힘 — 주 숫자가 먼저 뜨고 그 뒤에 나타난다. 떠오름 · 수명은 나타난 뒤부터
            _delay -= Time.deltaTime;
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            return;
        }

        float dt = Time.deltaTime;
        _age     += dt;
        _elapsed += dt;
        float t = Mathf.Clamp01(_elapsed / _life);

        Vector3 pos;
        float   recede = 0f;   // 밀려 올라간 칸 수(연속) — 위로 갈수록 작고 옅게
        if (_isTotal)
        {
            pos = _anchor;     // 누적은 제자리에서 세어 올린다 — 줄이 그 옆으로 흘러 올라간다
            TickCount(dt);
        }
        else
        {
            // 곧게 떠오른다 — 감속 곡선(1 − (1 − u)³). 모든 숫자가 같은 곡선이라 먼저 찍힌 숫자가 늘 위에 있다.
            float u    = Mathf.Clamp01(_age / riseTime);
            float rise = riseDistance * (1f - (1f - u) * (1f - u) * (1f - u));

            // 밀려 올라감 — 목표 칸으로 부드럽게(뚝 끊겨 옮겨 가면 줄이 떨린다).
            _rowCurrent = Mathf.Lerp(_rowCurrent, _row * rowStep, 1f - Mathf.Exp(-18f * dt));
            // 카메라 위로 쌓는다 — 세상 위(Vector3.up)는 내려다보는 카메라에서 줄어 보여 큰 보스방(멀고 가파른 카메라)에서 줄이 겹쳤다(10-31 화룡).
            Vector3 up = _cam != null ? _cam.transform.up : Vector3.up;
            pos    = _anchor + up * (rise + _rowCurrent);
            recede = Mathf.Min(_rowCurrent / rowStep, MaxRows);
        }

        // 치명 — 찍히는 순간 좌우로 짧게 떨린다(잦아들며).
        if (_shakeElapsed >= 0f)
        {
            _shakeElapsed += dt;
            float k = _shakeElapsed / Mathf.Max(0.001f, critShakeTime);
            if (k >= 1f) _shakeElapsed = -1f;
            else if (_cam != null) pos += _cam.transform.right * (Mathf.Sin(_shakeElapsed * 110f) * critShake * (1f - k));
        }
        transform.position = pos;

        if (label != null)
        {
            label.transform.localScale = Vector3.one * (_baseScale * PopCurve() * KickCurve(dt) * (1f - rowShrink * recede));

            // 치명 — 찍히는 순간 흰 번쩍 → 금으로 식는다.
            if (_flashElapsed >= 0f)
            {
                _flashElapsed += dt;
                float f = Mathf.Clamp01(_flashElapsed / critFlashTime);
                label.color = Color.Lerp(critFlashColor, _color, f);
                if (f >= 1f) _flashElapsed = -1f;
            }
        }

        // 흐려짐 — 끝 부분만. 줄을 넘친 숫자는 Bump가 수명을 흐려짐 시작점으로 당겨 둔다.
        if (canvasGroup != null)
            canvasGroup.alpha = (t <= fadeFrom ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (t - fadeFrom) / (1f - fadeFrom)))
                              * (1f - rowFade * recede);

        // 빌보드 (카메라 향함)
        if (_cam != null)
            transform.rotation = _cam.transform.rotation;

        if (t >= 1f)
        {
            _active = false;
            Column  = 0;
            TotalOwner = 0;
            gameObject.SetActive(false);
            _onReleased?.Invoke(this);   // 완료 → 풀의 free 큐로 반환(O(1) 재사용)
        }
    }

    // ── Public Methods ───────────────────────────────────────────────
    /// <summary>
    /// 이미 떠 있는 숫자에 값을 <b>더해 갱신</b>한다(다단히트 합산).
    /// 연타 21타를 각각 띄우면 자잘한 숫자가 화면을 덮어 "얼마나 넣었나"가 안 읽힌다 — 한 숫자가 커지는 걸 보여준다.
    /// 수명을 되감아 쌓이는 동안 사라지지 않게 하고, 작은 튕김으로 "지금 또 들어갔다"를 알린다.
    /// </summary>
    public void Accumulate(float total, bool isCrit, DamageKind kind, RuneElement? element)
    {
        if (!_active || label == null) return;

        // 처음부터 다시 떠오르면 산만하다 — 떠오름(_age)은 두고 흐려짐만 미룬다.
        if (isCrit && kind == DamageKind.Normal) _life = Mathf.Max(_life, critLife);
        _elapsed = Mathf.Min(_elapsed, _life * 0.35f);
        ApplyLook(total, isCrit, kind, element);

        _kickNow     = kickScale;
        _kickElapsed = 0f;
        if (canvasGroup != null) canvasGroup.alpha = 1f;
    }

    /// <summary>
    /// 누적 숫자로 띄운다 — 줄 왼쪽 제자리에서 <paramref name="from"/>부터 <paramref name="total"/>까지 세어 올린다.
    /// <paramref name="owner"/> = 스포너의 대상 ID(풀에서 다시 쓰인 팝업에 남의 누적이 얹히지 않게).
    /// </summary>
    public void ShowTotal(Vector3 worldPos, float from, float total, int hits, int owner)
    {
        _age           = 0f;
        _elapsed       = 0f;
        _kickElapsed   = -1f;
        _flashElapsed  = -1f;
        _shakeElapsed  = -1f;
        _row           = 0;
        _rowCurrent    = 0f;
        _active        = true;
        _isTotal       = true;
        _crit          = false;
        Column         = 0;
        TotalOwner     = owner;
        if (s_cam == null) s_cam = Camera.main;
        _cam = s_cam;

        Vector3 right = _cam != null ? _cam.transform.right : Vector3.right;
        _anchor = worldPos - right * totalSideOffset;
        transform.position = _anchor;

        _life          = totalLife;
        _popFrom       = normalPopFrom;
        _totalTarget   = total;
        _totalShown    = Mathf.Max(0f, from);
        _totalShownInt = -1;
        _hits          = hits;
        if (label != null)
        {
            if (s_normalMat != null) label.fontSharedMaterial = s_normalMat;
            label.alignment = TextAlignmentOptions.Right;          // 오른쪽 끝을 묶는다 — 자라도 줄 쪽으로 밀고 들어오지 않는다
            label.rectTransform.pivot = new Vector2(1f, _basePivot.y);
            ApplyTotalLook();
            WriteTotal(Mathf.RoundToInt(_totalShown));
            label.transform.localScale = Vector3.one * (_baseScale * _popFrom);
        }
        if (canvasGroup != null) canvasGroup.alpha = 1f;

        gameObject.SetActive(true);
    }

    /// <summary>누적에 이어 맞힌 피해가 더해졌다 — 목표를 올리고(세어 올라간다) 튕기며 흐려짐을 미룬다.</summary>
    public void AddTotal(float total, int hits)
    {
        if (!_active || !_isTotal) return;
        _totalTarget = total;
        _hits        = hits;
        _elapsed     = 0f;
        _kickNow     = totalKickScale;
        _kickElapsed = 0f;
        if (label != null) ApplyTotalLook();
        if (canvasGroup != null) canvasGroup.alpha = 1f;
    }

    /// <summary>이어 맞힘이 끊겼다 — 남은 시간 없이 바로 흐려지기 시작한다(새 누적이 같은 자리에 겹치지 않게).</summary>
    public void Retire()
    {
        if (_active) _elapsed = Mathf.Max(_elapsed, _life * fadeFrom);
    }

    /// <summary>같은 줄에 새 숫자가 찍혔다 — 한 칸 밀려 올라간다. 줄을 넘치면 바로 흐려진다.</summary>
    public void Bump()
    {
        if (!_active) return;
        _row++;
        if (_row >= MaxRows) _elapsed = Mathf.Max(_elapsed, _life * 0.8f);
    }

    /// <summary>풀이 완료 시 재사용 큐로 회수하도록 연결하는 콜백. 풀 인스턴스화 직후 1회 설정.</summary>
    public void SetReleaseCallback(System.Action<DamagePopup> onReleased) => _onReleased = onReleased;

    /// <summary>
    /// 스폰 — 풀에서 꺼낸 직후 호출. damage 의 절댓값을 숫자로 표기(스포너가 damage&lt;=0 은 차단).
    /// kind: 피해 출처(층 · 색). column: 같은 줄 열쇠 — 스포너가 같은 열쇠의 앞 숫자들을 먼저 <see cref="Bump"/>한다.
    /// </summary>
    public void Show(Vector3 worldPos, float damage, bool isCrit, DamageKind kind, RuneElement? element, int column,
                     BonusSource source = default, float delay = 0f)
    {
        _age          = 0f;
        _elapsed      = 0f;
        _kickElapsed  = -1f;
        _flashElapsed = -1f;
        _shakeElapsed = -1f;
        _row          = 0;
        _rowCurrent   = 0f;
        _active       = true;
        _isTotal      = false;
        _source       = source;
        _delay        = delay;
        Column        = column;
        TotalOwner    = 0;
        if (s_cam == null) s_cam = Camera.main;   // 파괴 시 Unity-null → 재탐색, 평시엔 캐시 재사용
        _cam = s_cam;

        // 크기 기준(최근 평균 타격)은 주 피해만으로 — 작은 추가 피해 · 지속 틱이 평균을 끌어내리면 평타가 다 커 보인다.
        if (kind == DamageKind.Normal)
            s_avgHit = s_avgHit <= 0f ? damage : Mathf.Lerp(s_avgHit, damage, AvgFollow);

        // 추가 피해는 주 숫자 옆 줄 — 같은 자리에 겹치면 둘 다 안 읽힌다.
        Vector3 right = _cam != null ? _cam.transform.right : Vector3.right;
        _anchor = kind == DamageKind.Normal ? worldPos : worldPos + right * extraSideOffset;
        transform.position = _anchor;

        if (label != null) { label.alignment = _baseAlign; label.rectTransform.pivot = _basePivot; }
        _crit = false;   // ApplyLook이 처음 치명을 보고 번쩍을 건다
        _life = kind != DamageKind.Normal ? extraLife : isCrit ? critLife : normalLife;
        if (label != null) ApplyLook(damage, isCrit, kind, element);
        _popFrom = kind == DamageKind.Normal && isCrit ? critPopFrom : normalPopFrom;
        if (label != null) label.transform.localScale = Vector3.one * (_baseScale * _popFrom);

        if (canvasGroup != null)
            canvasGroup.alpha = _delay > 0f ? 0f : 1f;

        gameObject.SetActive(true);
    }

    // ── Private Methods ──────────────────────────────────────────────
    // 도메인 리로드를 끈 에디터에서 지난 플레이의 평균 타격이 새 플레이 첫 숫자 크기를 정하지 않게.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_avgHit = 0f;

    /// <summary>숫자 · 크기 · 색 · 재질. 치명은 주 피해에서만 금으로 — 추가 피해의 치명은 크기만 키운다(색은 속성 그대로).</summary>
    private void ApplyLook(float damage, bool isCrit, DamageKind kind, RuneElement? element)
    {
        int amount = Mathf.RoundToInt(Mathf.Abs(damage));
        label.text = amount >= 1000 ? amount.ToString("N0", CultureInfo.InvariantCulture) : amount.ToString(CultureInfo.InvariantCulture);
        if (kind == DamageKind.Bonus) label.text = "+" + label.text;   // 「추가 피해」 — 주 숫자에 더해진 몫(11-02 Q29)
        label.enableVertexGradient = false;

        float size = SizeForDamage(damage);
        bool mainCrit = kind == DamageKind.Normal && isCrit;
        if (kind == DamageKind.Normal)
        {
            _baseScale = size * (isCrit ? critSize : 1f);
            _color     = isCrit ? critTint : normalColor;
        }
        else if (kind == DamageKind.Bonus)
        {
            bool big = s_avgHit > 0f && damage >= s_avgHit * bonusBigAt;
            _baseScale = size * bonusScale * (big ? bonusBigScale : 1f) * (isCrit ? 1.2f : 1f);
            _color     = BonusColor(_source, element);
        }
        else
        {
            _baseScale = size * (kind == DamageKind.Dot ? dotScale : synergyScale) * (isCrit ? 1.2f : 1f);
            _color     = element.HasValue ? ElementPalette.Core(element.Value) : kind == DamageKind.Dot ? dotColor : synergyColor;
        }

        if (s_normalMat != null) label.fontSharedMaterial = mainCrit && s_critMat != null ? s_critMat : s_normalMat;

        // 치명으로 처음 바뀌는 순간에만 번쩍이고 떨린다(합산 중 이미 치명이면 다시 하지 않는다).
        if (mainCrit && !_crit) { _flashElapsed = 0f; _shakeElapsed = 0f; label.color = critFlashColor; }
        else if (_flashElapsed < 0f) label.color = _color;
        _crit = mainCrit;
    }

    /// <summary>추가 피해 출처 색 — 룬은 속성이 있으면 속성 색(무슨 룬인지 색으로 읽힌다).</summary>
    private Color BonusColor(BonusSource source, RuneElement? element) => source switch
    {
        BonusSource.Rune      => element.HasValue ? ElementPalette.Core(element.Value) : bonusRuneColor,
        BonusSource.Augment   => bonusAugmentColor,
        BonusSource.Legendary => bonusLegendaryColor,
        _                     => bonusItemColor,
    };

    /// <summary>
    /// 피해량 → 글자 크기. 최근 평균 타격 대비 배수를 로그로 매핑한다 — 평균 ≈ 아래쪽 1/4 · 평균의 4배 = 최대.
    /// 시기 · 챕터로 피해 자릿수가 커져도 「평소보다 큰 한 방」만 크게 뜬다.
    /// </summary>
    private float SizeForDamage(float damage)
    {
        float d   = Mathf.Max(1f, Mathf.Abs(damage));
        float avg = s_avgHit > 0f ? s_avgHit : d;
        float lo  = Mathf.Log(avg * smallAtAvg);
        float hi  = Mathf.Log(avg * Mathf.Max(smallAtAvg + 0.01f, bigAtAvg));
        float t   = Mathf.InverseLerp(lo, hi, Mathf.Log(d));
        return Mathf.Lerp(minSize, maxSize, t);
    }

    /// <summary>누적 — 타수가 쌓일수록 조금 커지고 따뜻해진다(상아 → 금). 두 번째 타 = 시작.</summary>
    private void ApplyTotalLook()
    {
        float k = Mathf.InverseLerp(2f, Mathf.Max(2.01f, totalHitsForMax), _hits);
        _baseScale  = Mathf.Lerp(totalMinScale, totalMaxScale, k);
        _color      = Color.Lerp(totalColor, totalHotColor, k);
        label.color = _color;
    }

    /// <summary>세어 올림 — 목표로 지수로 다가가고, 반올림 값이 바뀔 때만 글자를 다시 쓴다.</summary>
    private void TickCount(float dt)
    {
        if (_totalShown < _totalTarget)
        {
            _totalShown += (_totalTarget - _totalShown) * (1f - Mathf.Exp(-totalCountRate * dt));
            if (_totalTarget - _totalShown < 0.5f) _totalShown = _totalTarget;
        }
        WriteTotal(Mathf.RoundToInt(_totalShown));
    }

    /// <summary>머리말 + 천 단위 쉼표 숫자를 글자 배열로 써서 넘긴다(문자열을 만들지 않는다).</summary>
    private void WriteTotal(int value)
    {
        if (label == null || value == _totalShownInt) return;
        _totalShownInt = value;
        int n = TotalPrefix.Length;
        TotalPrefix.CopyTo(0, s_totalChars, 0, n);
        n = WriteGrouped(Mathf.Max(0, value), s_totalChars, n);
        label.SetCharArray(s_totalChars, 0, n);
    }

    private static int WriteGrouped(int value, char[] buf, int at)
    {
        int digits = 1;
        for (int v = value; v >= 10; v /= 10) digits++;
        int len = digits + (digits - 1) / 3;
        if (at + len > buf.Length) return at;
        int i = at + len - 1, count = 0;
        do
        {
            buf[i--] = (char)('0' + value % 10);
            value /= 10;
            if (++count % 3 == 0 && value > 0) buf[i--] = ',';
        } while (value > 0);
        return at + len;
    }

    /// <summary>
    /// 일반 = 작게 시작해 살짝 넘쳤다 안착(easeOutBack) · 치명 = 크게 시작해 <b>내려앉는다</b>(easeOutCubic — 찍히는 느낌).
    /// 끝 무렵엔 0.92까지 살짝 줄며 흐려진다.
    /// </summary>
    private float PopCurve()
    {
        float k = Mathf.Clamp01(_age / Mathf.Max(0.001f, popTime));
        float e;
        if (_popFrom > 1f)
        {
            float r = 1f - k;
            e = 1f - r * r * r;
        }
        else
        {
            const float c = 1.70158f;   // easeOutBack — 약 10% 넘침
            float x = k - 1f;
            e = 1f + (c + 1f) * x * x * x + c * x * x;
        }
        float pop = Mathf.LerpUnclamped(_popFrom, 1f, e);

        float t = Mathf.Clamp01(_elapsed / _life);
        float shrink = t <= fadeFrom ? 1f : Mathf.Lerp(1f, 0.92f, (t - fadeFrom) / (1f - fadeFrom));
        return pop * shrink;
    }

    /// <summary>합산 · 누적으로 값이 올랐을 때의 짧은 튕김(_kickNow → 1, 0.1초).</summary>
    private float KickCurve(float dt)
    {
        if (_kickElapsed < 0f) return 1f;
        _kickElapsed += dt;
        float k = _kickElapsed / 0.1f;
        if (k >= 1f) { _kickElapsed = -1f; return 1f; }
        return Mathf.Lerp(_kickNow, 1f, k);
    }

    /// <summary>
    /// 글꼴(톤 v2 Medium) · 재질 2종(일반/치명)을 한 벌만 만든다. 글꼴이 아직 안 올라왔으면 프리팹 글꼴로 만든다 —
    /// 처음 고른 글꼴을 끝까지 쓴다(재질이 그 글꼴 아틀라스를 가리키므로).
    /// </summary>
    private void EnsureStyle()
    {
        if (label == null) return;

        if (s_font == null)
        {
            var theme = UISkin.ThemeFonts != null ? UISkin.ThemeFonts.Medium : null;
            s_font = theme != null ? theme : label.font;
            s_normalMat = null;
            s_critMat   = null;
        }
        if (s_font == null || s_font.material == null) return;
        if (label.font != s_font) label.font = s_font;

        var src = s_font.material;
        if (s_normalMat == null)   // Unity-null(파괴됨) 포함 → 재생성
        {
            s_normalMat = new Material(src) { name = src.name + " (DamagePopup)" };
            SetupMaterial(s_normalMat, outlineColor, faceDilate);
        }
        if (s_critMat == null)
        {
            // 치명 — 테두리를 짙은 호박색으로 바꿔 금 글자가 밝은 바닥 위에서도 번지지 않게 한다. 발광은 쓰지 않는다(뭉갰다).
            s_critMat = new Material(src) { name = src.name + " (DamagePopup Crit)" };
            SetupMaterial(s_critMat, critOutlineColor, faceDilate + 0.04f);
        }

        label.fontSharedMaterial = s_normalMat;
        label.fontStyle = FontStyles.Normal;
        label.characterSpacing = 1f;
        label.UpdateMeshPadding();
    }

    /// <summary>얇은 테두리 + 아래로 짧게 번지는 그림자 — 밝은 바닥 · 이펙트 위 대비는 그림자가 맡는다.</summary>
    private void SetupMaterial(Material m, Color outline, float dilate)
    {
        // 깊이 검사 끔 — 숫자가 늘 맨 위. 월드 캔버스는 LEqual이라 보스 몸 한가운데(발 + 1.2 m)에 뜬 숫자가 근접 거리에서 몸에 묻혔다(10-30 실측).
        // 정렬 순서는 WorldDamage(60) — 이름표 · 체력 바 · 보스 예고(7)보다 뒤에 그린다. 숫자는 머리 높이라 바닥 예고를 거의 덮지 않는다.
        m.SetFloat(GuiZTestId, (float)UnityEngine.Rendering.CompareFunction.Always);
        m.EnableKeyword("OUTLINE_ON");
        m.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
        m.SetColor(ShaderUtilities.ID_OutlineColor, outline);
        m.SetFloat(ShaderUtilities.ID_FaceDilate, dilate);
        if (!m.HasProperty(ShaderUtilities.ID_UnderlayColor)) return;
        m.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        m.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.5f));
        m.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.2f);
        m.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.3f);
        m.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.1f);
        m.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.35f);
    }
}
