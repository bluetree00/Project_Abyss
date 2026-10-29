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
/// 풀링되며 라이프타임 종료 시 비활성화 (재사용 대비).
/// </summary>
public class DamagePopup : MonoBehaviour
{
    // ── Constants ───────────────────────────────────────────────────
    /// <summary>한 줄(같은 대상 · 같은 층)에 동시에 남는 숫자 수. 넘친 맨 위 숫자는 바로 흐려진다.</summary>
    private const int MaxRows = 4;

    // ── [SerializeField] ────────────────────────────────────────────
    [SerializeField] private TMP_Text label;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Life")]
    [SerializeField] private float normalLife = 0.8f;
    [Tooltip("치명은 조금 더 오래 남는다 — 강조는 크기 · 색 · 머무는 시간으로.")]
    [SerializeField] private float critLife = 1.05f;
    [SerializeField] private float extraLife = 0.65f;
    [Tooltip("수명의 이 비율부터 흐려진다.")]
    [SerializeField] private float fadeFrom = 0.62f;

    [Header("Motion")]
    [Tooltip("떠오르는 높이(m). 처음에 빨리 오르고 곧 멈춘다(감속).")]
    [SerializeField] private float riseDistance = 0.38f;
    [Tooltip("다 떠오르기까지(초). 모든 숫자가 같은 곡선이라 먼저 찍힌 숫자가 늘 위에 있다.")]
    [SerializeField] private float riseTime = 0.55f;
    [Tooltip("줄 한 칸 간격(m). 새 숫자가 찍히면 앞 숫자들이 이만큼 밀려 올라간다.")]
    [SerializeField] private float rowStep = 0.3f;
    [Tooltip("추가 피해 줄을 주 숫자 옆으로 비키는 거리(m · 화면 오른쪽).")]
    [SerializeField] private float extraSideOffset = 0.62f;

    [Header("Pop")]
    [Tooltip("일반 — 이 배율에서 커지며 살짝 넘쳤다 안착한다.")]
    [SerializeField] private float normalPopFrom = 0.55f;
    [Tooltip("치명 — 이 배율에서 <b>내려앉는다</b>(찍히는 느낌).")]
    [SerializeField] private float critPopFrom = 1.65f;
    [SerializeField] private float popTime = 0.12f;
    [Tooltip("합산으로 값이 오를 때 다시 튕기는 배율.")]
    [SerializeField] private float kickScale = 1.12f;

    [Header("Size")]
    // 피해량에 크기를 묶으면 연타는 자잘하게, 마무리는 큼직하게 뜬다. 폭을 좁혀(0.9~1.15) 크기가 들쭉날쭉하지 않게 한다.
    [SerializeField] private float damageAtMinSize = 15f;
    [SerializeField] private float damageAtMaxSize = 400f;
    [SerializeField] private float minSize = 0.9f;
    [SerializeField] private float maxSize = 1.15f;
    [SerializeField] private float critSize = 1.4f;
    [SerializeField] private float synergyScale = 0.72f;
    [SerializeField] private float dotScale = 0.62f;

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

    // ── Private ─────────────────────────────────────────────────────
    private Vector3 _anchor;          // 찍힌 자리(옆 줄 비킴 포함) — 여기서 떠오르고 밀려 올라간다
    private float _age;               // 찍힌 뒤 흐른 시간 — 떠오름 · 튕김(되감지 않는다)
    private float _elapsed;           // 수명 진행 — 합산이 되감아 흐려짐을 미룬다
    private float _life;
    private float _flashElapsed = -1f; // 치명 번쩍 경과(음수 = 없음)
    private float _baseScale;
    private float _popFrom;
    private float _kickElapsed = -1f; // 합산 튕김 경과(음수 = 없음)
    private float _rowCurrent;        // 지금 밀려 올라간 높이(m) — 목표로 부드럽게 따라간다
    private int _row;                 // 줄에서의 칸(0 = 맨 아래 · 가장 새 숫자)
    private bool _crit;
    private Color _color;
    private bool _active;
    private Camera _cam;
    private System.Action<DamagePopup> _onReleased;

    // ── Properties ──────────────────────────────────────────────────
    /// <summary>아직 화면에 떠 있는가. 합산(Accumulate) 대상이 될 수 있는지 판단하는 조건.</summary>
    public bool IsShowing => _active;

    /// <summary>같은 줄 열쇠(대상 × 주/추가). 새 숫자가 찍히면 같은 열쇠의 숫자들이 밀려 올라간다. 0 = 줄 없음.</summary>
    public int Column { get; private set; }

    // ── Lifecycle ───────────────────────────────────────────────────
    private void Awake()
    {
        if (label == null)       label       = GetComponentInChildren<TMP_Text>();
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        EnsureStyle();
    }

    private void LateUpdate()
    {
        if (!_active) return;

        float dt = Time.deltaTime;
        _age     += dt;
        _elapsed += dt;
        float t = Mathf.Clamp01(_elapsed / _life);

        // 곧게 떠오른다 — 감속 곡선(1 − (1 − u)³). 모든 숫자가 같은 곡선이라 먼저 찍힌 숫자가 늘 위에 있다.
        float u    = Mathf.Clamp01(_age / riseTime);
        float rise = riseDistance * (1f - (1f - u) * (1f - u) * (1f - u));

        // 밀려 올라감 — 목표 칸으로 부드럽게(뚝 끊겨 옮겨 가면 줄이 떨린다).
        _rowCurrent = Mathf.Lerp(_rowCurrent, _row * rowStep, 1f - Mathf.Exp(-18f * dt));
        transform.position = _anchor + Vector3.up * (rise + _rowCurrent);

        if (label != null)
        {
            label.transform.localScale = Vector3.one * (_baseScale * PopCurve() * KickCurve(dt));

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
            canvasGroup.alpha = t <= fadeFrom ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (t - fadeFrom) / (1f - fadeFrom));

        // 빌보드 (카메라 향함)
        if (_cam != null)
            transform.rotation = _cam.transform.rotation;

        if (t >= 1f)
        {
            _active = false;
            Column  = 0;
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

        _kickElapsed = 0f;
        if (canvasGroup != null) canvasGroup.alpha = 1f;
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
    public void Show(Vector3 worldPos, float damage, bool isCrit, DamageKind kind, RuneElement? element, int column)
    {
        _age          = 0f;
        _elapsed      = 0f;
        _kickElapsed  = -1f;
        _flashElapsed = -1f;
        _row          = 0;
        _rowCurrent   = 0f;
        _active       = true;
        Column        = column;
        if (s_cam == null) s_cam = Camera.main;   // 파괴 시 Unity-null → 재탐색, 평시엔 캐시 재사용
        _cam = s_cam;

        // 추가 피해는 주 숫자 옆 줄 — 같은 자리에 겹치면 둘 다 안 읽힌다.
        Vector3 right = _cam != null ? _cam.transform.right : Vector3.right;
        _anchor = kind == DamageKind.Normal ? worldPos : worldPos + right * extraSideOffset;
        transform.position = _anchor;

        _crit = false;   // ApplyLook이 처음 치명을 보고 번쩍을 건다
        _life = kind != DamageKind.Normal ? extraLife : isCrit ? critLife : normalLife;
        if (label != null) ApplyLook(damage, isCrit, kind, element);
        _popFrom = kind == DamageKind.Normal && isCrit ? critPopFrom : normalPopFrom;
        if (label != null) label.transform.localScale = Vector3.one * (_baseScale * _popFrom);

        if (canvasGroup != null)
            canvasGroup.alpha = 1f;

        gameObject.SetActive(true);
    }

    // ── Private Methods ──────────────────────────────────────────────
    /// <summary>숫자 · 크기 · 색 · 재질. 치명은 주 피해에서만 금으로 — 추가 피해의 치명은 크기만 키운다(색은 속성 그대로).</summary>
    private void ApplyLook(float damage, bool isCrit, DamageKind kind, RuneElement? element)
    {
        int amount = Mathf.RoundToInt(Mathf.Abs(damage));
        label.text = amount >= 1000 ? amount.ToString("N0", CultureInfo.InvariantCulture) : amount.ToString(CultureInfo.InvariantCulture);
        label.enableVertexGradient = false;

        float size = SizeForDamage(damage);
        bool mainCrit = kind == DamageKind.Normal && isCrit;
        if (kind == DamageKind.Normal)
        {
            _baseScale = size * (isCrit ? critSize : 1f);
            _color     = isCrit ? critTint : normalColor;
        }
        else
        {
            _baseScale = size * (kind == DamageKind.Dot ? dotScale : synergyScale) * (isCrit ? 1.2f : 1f);
            _color     = element.HasValue ? ElementPalette.Core(element.Value) : kind == DamageKind.Dot ? dotColor : synergyColor;
        }

        if (s_normalMat != null) label.fontSharedMaterial = mainCrit && s_critMat != null ? s_critMat : s_normalMat;

        // 치명으로 처음 바뀌는 순간에만 번쩍인다(합산 중 이미 치명이면 다시 번쩍이지 않는다).
        if (mainCrit && !_crit) { _flashElapsed = 0f; label.color = critFlashColor; }
        else if (_flashElapsed < 0f) label.color = _color;
        _crit = mainCrit;
    }

    /// <summary>피해량 → 글자 크기. 피해는 배수로 벌어지므로 로그로 매핑한다.</summary>
    private float SizeForDamage(float damage)
    {
        float d  = Mathf.Max(1f, Mathf.Abs(damage));
        float lo = Mathf.Log(Mathf.Max(1f, damageAtMinSize));
        float hi = Mathf.Log(Mathf.Max(lo + 0.01f, damageAtMaxSize));
        float t  = Mathf.InverseLerp(lo, hi, Mathf.Log(d));
        return Mathf.Lerp(minSize, maxSize, t);
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

    /// <summary>합산으로 값이 올랐을 때의 짧은 튕김(kickScale → 1, 0.1초).</summary>
    private float KickCurve(float dt)
    {
        if (_kickElapsed < 0f) return 1f;
        _kickElapsed += dt;
        float k = _kickElapsed / 0.1f;
        if (k >= 1f) { _kickElapsed = -1f; return 1f; }
        return Mathf.Lerp(kickScale, 1f, k);
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
