using TMPro;
using UnityEngine;

/// <summary>
/// 월드 공간에 떠오르는 데미지 숫자.
///
/// 연출(리서치 기준):
///   • 스폰 팝 — 0 → popScale 로 튕겨 커졌다 1.0 으로 안착. "숫자가 찍히는" 타격감의 핵심.
///   • 포물선 — 위로만 뜨지 않고 좌우로 튀며 중력에 끌린다(라그나로크식). 여러 개가 떠도 안 뭉갠다.
///   • 후반 축소 — 관련성이 떨어질수록 작아지며 사라진다.
///   • 영수증 캐스케이드 — 같은 대상에 연타가 꽂히면 <b>위로 한 칸씩 쌓여</b> 열을 이룬다.
///     (cascadeIndex 를 스포너가 넘긴다. 흩뿌리면 난잡하고, 쌓으면 "몇 대 때렸는지"가 읽힌다.)
///
/// 풀링되며 라이프타임 종료 시 비활성화 (재사용 대비).
/// </summary>
public class DamagePopup : MonoBehaviour
{
    // ── [SerializeField] ────────────────────────────────────────────
    [SerializeField] private TMP_Text label;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Motion")]
    [SerializeField] private float lifetime = 0.8f;
    [Tooltip("초기 상승 속도(m/s).")]
    [SerializeField] private float riseSpeed = 2.6f;
    [Tooltip("중력(m/s²). 클수록 빨리 꺾여 떨어진다.")]
    [SerializeField] private float gravity = 5.5f;
    [Tooltip("좌우로 튀는 속도(m/s). 캐스케이드 방향으로 교대 적용.")]
    [SerializeField] private float lateralSpeed = 1.1f;
    [SerializeField] private AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0.55f, 1, 1, 0);

    [Header("Pop")]
    [Tooltip("스폰 순간 튕겨 커지는 배율(오버슈트).")]
    [SerializeField] private float popScale = 1.3f;
    [Tooltip("팝이 끝나고 1.0 으로 안착하기까지의 시간(초).")]
    [SerializeField] private float popTime = 0.09f;
    [Tooltip("수명 끝에서의 최종 배율(작아지며 사라진다).")]
    [SerializeField] private float endScale = 0.7f;
    [Tooltip("이 진행도(0~1)부터 축소를 시작한다.")]
    [SerializeField] private float shrinkStart = 0.55f;

    [Header("Receipt Cascade")]
    [Tooltip("연타가 같은 대상에 꽂힐 때 한 칸씩 올려 쌓는 간격(m).")]
    [SerializeField] private float cascadeStep = 0.26f;

    [Header("Crit Style")]
    // 단색 금색은 "노란 글씨"로만 읽힌다. 위는 백열, 아래는 적열로 세로 그라데이션을 주면
    // 숫자 자체가 달아오른 쇳덩이처럼 보여 '치명타'의 뜨거움이 색으로 전달된다.
    [SerializeField] private float critScale = 1.6f;
    [Tooltip("치명타 글자 위쪽 색 — 백열(가장 뜨거운 심지).")]
    [SerializeField] private Color critGradientTop = new(1f, 0.96f, 0.62f, 1f);
    [Tooltip("치명타 글자 아래쪽 색 — 적열(식어가는 가장자리).")]
    [SerializeField] private Color critGradientBottom = new(1f, 0.24f, 0.05f, 1f);
    [Tooltip("치명타 아웃라인 — 진홍. 일반(먹색)과 달라야 한눈에 구분된다.")]
    [SerializeField] private Color critOutlineColor = new(0.32f, 0.02f, 0.0f, 1f);
    [Tooltip("치명타 발광 색 — 글자 주변으로 열이 번지는 느낌.")]
    [SerializeField] private Color critGlowColor = new(1f, 0.35f, 0.06f, 1f);
    [Range(0f, 1f)]
    [SerializeField] private float critGlowPower = 0.45f;

    [Header("Kind Colors")]
    [Tooltip("평타·스킬 등 일반 피해. 기본 타는 조용해야 한다 — 흰색 유지 권장.")]
    [SerializeField] private Color normalColor = Color.white;
    [Tooltip("룬 시너지·방어무시 등 2차 즉발 피해.")]
    [SerializeField] private Color synergyColor = new(0.75f, 0.55f, 1f, 1f);
    [Tooltip("화상·독 등 지속 피해(DoT) 틱.")]
    [SerializeField] private Color dotColor = new(1f, 0.55f, 0.2f, 1f);

    [Header("Size by Damage")]
    // 피해량에 크기를 묶으면 "연타는 자잘하게, 마무리 강타는 큼직하게" 떠서 공격의 리듬이 숫자로 읽힌다.
    // 피해량은 배수로 벌어지므로(수십 ~ 수백) 선형이 아니라 로그로 매핑한다.
    [Tooltip("이 피해량 이하는 최소 크기.")]
    [SerializeField] private float damageAtMinSize = 15f;
    [Tooltip("이 피해량 이상은 최대 크기.")]
    [SerializeField] private float damageAtMaxSize = 400f;
    [SerializeField] private float minSize = 0.75f;
    [SerializeField] private float maxSize = 1.5f;

    [Header("Outline")]
    // 아웃라인은 가독성의 필수 요소(리서치 공통). 폰트의 공용 머티리얼에 넣으면 그 폰트를 쓰는
    // 모든 텍스트가 오염되므로, 런타임 사본을 하나 만들어 팝업끼리만 공유한다(배칭 유지).
    [SerializeField] private float outlineWidth = 0.2f;
    [SerializeField] private Color outlineColor = new(0.05f, 0.03f, 0.06f, 1f);
    [Tooltip("글자 두께 보정. 기본 폰트가 얇게 구워져 있어 숫자가 흐려 보이는 것을 메운다.")]
    [SerializeField] private float faceDilate = 0.12f;

    // ── Static ──────────────────────────────────────────────────────
    // 빌보드용 메인 카메라 — 전 팝업 공유 1회 캐시. Camera.main(FindWithTag)을 Show마다 부르지 않는다.
    // 씬 전환 시 기존 카메라가 파괴되면 Unity-null → 다음 Show에서 자동 재탐색.
    private static Camera s_cam;

    // 아웃라인 머티리얼 — 일반/치명타 각 1개를 전 팝업이 공유한다(Unity-null이면 재생성).
    // 팝업마다 머티리얼을 만들면 배칭이 깨지므로 딱 두 개만 둔다.
    private static Material s_outlineMat;
    private static Material s_critMat;

    // ── Private ─────────────────────────────────────────────────────
    private Vector3 _startWorldPos;
    private Vector3 _velocity;
    private float _elapsed;
    private float _baseScale;      // 크리 등으로 정해지는 기준 배율(팝/축소는 여기에 곱해진다)
    private bool _active;
    private Camera _cam;
    private System.Action<DamagePopup> _onReleased;

    // ── Lifecycle ───────────────────────────────────────────────────
    private void Awake()
    {
        if (label == null)       label       = GetComponentInChildren<TMP_Text>();
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        EnsureOutlineMaterial();
    }

    private void LateUpdate()
    {
        if (!_active) return;

        float dt = Time.deltaTime;
        _elapsed += dt;
        float t = Mathf.Clamp01(_elapsed / lifetime);

        // 포물선 — 좌우로 튀며 위로 솟았다가 중력에 꺾인다.
        _velocity.y -= gravity * dt;
        _startWorldPos += _velocity * dt;
        transform.position = _startWorldPos;

        // 팝 → 안착 → 후반 축소
        if (label != null)
            label.transform.localScale = Vector3.one * (_baseScale * ScaleCurve(t));

        // 페이드
        if (canvasGroup != null)
            canvasGroup.alpha = fadeCurve.Evaluate(t);

        // 빌보드 (카메라 향함)
        if (_cam != null)
            transform.rotation = _cam.transform.rotation;

        if (t >= 1f)
        {
            _active = false;
            gameObject.SetActive(false);
            _onReleased?.Invoke(this);   // 완료 → 풀의 free 큐로 반환(O(1) 재사용)
        }
    }

    // ── Public Methods ───────────────────────────────────────────────
    /// <summary>풀이 완료 시 재사용 큐로 회수하도록 연결하는 콜백. 풀 인스턴스화 직후 1회 설정.</summary>
    public void SetReleaseCallback(System.Action<DamagePopup> onReleased) => _onReleased = onReleased;

    /// <summary>
    /// 스폰 — 풀에서 꺼낸 직후 호출. damage 의 절댓값을 숫자로 표기(스포너가 damage&lt;=0 은 차단).
    /// kind: 피해 출처(색). cascadeIndex: 같은 대상에 연달아 꽂힌 순번(0부터) — 위로 쌓아 영수증처럼 읽힌다.
    /// </summary>
    public void Show(Vector3 worldPos, float damage, bool isCrit, DamageKind kind, int cascadeIndex = 0,
                     RuneElement? element = null)
    {
        _elapsed = 0f;
        _active = true;
        if (s_cam == null) s_cam = Camera.main;   // 파괴 시 Unity-null → 재탐색, 평시엔 캐시 재사용
        _cam = s_cam;

        // 영수증 캐스케이드 — 연타는 위로 쌓이고, 좌우로 교대로 어긋나 숫자가 서로를 가리지 않는다.
        float side = (cascadeIndex & 1) == 0 ? 1f : -1f;
        _startWorldPos = worldPos + Vector3.up * (cascadeStep * cascadeIndex);
        transform.position = _startWorldPos;

        // 카메라 기준 좌우로 튄다(월드 X가 아니라 화면 가로 방향이라야 항상 옆으로 보인다).
        Vector3 right = _cam != null ? _cam.transform.right : Vector3.right;
        _velocity = right * (lateralSpeed * side) + Vector3.up * riseSpeed;

        // 피해량 기반 크기 × 크리 배율 — 큰 한 방은 크게, 자잘한 연타는 작게.
        _baseScale = SizeForDamage(damage) * (isCrit ? critScale : 1f);

        if (label != null)
        {
            int amount = Mathf.RoundToInt(Mathf.Abs(damage));
            label.text = isCrit ? $"{amount}!" : amount.ToString();
            ApplyStyle(isCrit, kind, element);
            label.transform.localScale = Vector3.zero;   // 팝이 0에서 시작
        }

        if (canvasGroup != null)
            canvasGroup.alpha = 1f;

        gameObject.SetActive(true);
    }

    // ── Private Methods ──────────────────────────────────────────────
    /// <summary>
    /// 치명타는 <b>색이 아니라 재질</b>로 구분한다 — 백열→적열 세로 그라데이션 + 진홍 아웃라인 + 발광.
    /// 일반 피해는 그라데이션을 끄고 출처 색 단색으로 조용히 둔다("기본 타는 조용해야 한다").
    /// </summary>
    private void ApplyStyle(bool isCrit, DamageKind kind, RuneElement? element)
    {
        if (isCrit)
        {
            label.fontSharedMaterial = s_critMat != null ? s_critMat : label.fontSharedMaterial;
            label.enableVertexGradient = true;
            // 속성 치명타는 그 속성의 색으로 달아오르게 — 치명타 재질(발광)은 그대로 쓰되 색만 속성으로.
            Color top = element.HasValue ? ElementPalette.Bright(element.Value) : critGradientTop;
            Color bot = element.HasValue ? ElementPalette.Deep(element.Value)   : critGradientBottom;
            label.colorGradient = new VertexGradient(top, top, bot, bot);
            label.color = Color.white;   // 그라데이션은 color에 곱해지므로 흰색으로 둔다
            return;
        }

        label.fontSharedMaterial = s_outlineMat != null ? s_outlineMat : label.fontSharedMaterial;

        // 속성 피해는 단색이 아니라 세로 그라데이션(밝은 심지 → 짙은 가장자리)으로 준다.
        // 화상/독/감전이 "주황 숫자"가 아니라 "타오르는 숫자"로 읽히게 하는 것이 목적.
        if (element.HasValue)
        {
            label.enableVertexGradient = true;
            Color top = ElementPalette.Bright(element.Value);
            Color bot = ElementPalette.Deep(element.Value);
            label.colorGradient = new VertexGradient(top, top, bot, bot);
            label.color = Color.white;
            return;
        }

        label.enableVertexGradient = false;
        label.color = ColorForKind(kind);
    }

    private Color ColorForKind(DamageKind kind) => kind switch
    {
        DamageKind.Synergy => synergyColor,
        DamageKind.Dot     => dotColor,
        _                  => normalColor,
    };

    /// <summary>피해량 → 글자 크기. 피해는 배수로 벌어지므로 로그로 매핑한다.</summary>
    private float SizeForDamage(float damage)
    {
        float d   = Mathf.Max(1f, Mathf.Abs(damage));
        float lo  = Mathf.Log(Mathf.Max(1f, damageAtMinSize));
        float hi  = Mathf.Log(Mathf.Max(lo + 0.01f, damageAtMaxSize));
        float t   = Mathf.InverseLerp(lo, hi, Mathf.Log(d));
        return Mathf.Lerp(minSize, maxSize, t);
    }

    /// <summary>
    /// 팝업 전용 머티리얼 2종(일반/치명타) — 전 팝업이 공유한다.
    /// 폰트의 공용 머티리얼을 직접 고치면 그 폰트를 쓰는 모든 UI가 같이 변한다 → 사본을 쓴다.
    /// 팝업마다 만들면 배칭이 깨지므로 딱 2개만 둔다.
    /// </summary>
    private void EnsureOutlineMaterial()
    {
        if (label == null) return;

        var src = label.fontSharedMaterial;
        if (src == null) return;

        if (s_outlineMat == null)   // Unity-null(파괴됨) 포함 → 재생성
        {
            s_outlineMat = new Material(src) { name = src.name + " (DamagePopup)" };
            s_outlineMat.EnableKeyword("OUTLINE_ON");
            s_outlineMat.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
            s_outlineMat.SetColor(ShaderUtilities.ID_OutlineColor, outlineColor);
            s_outlineMat.SetFloat(ShaderUtilities.ID_FaceDilate,   faceDilate);
        }

        if (s_critMat == null)
        {
            // 치명타는 아웃라인을 진홍으로 바꾸고 발광을 켜, 일반 숫자와 재질부터 다르게 보이게 한다.
            s_critMat = new Material(src) { name = src.name + " (DamagePopup Crit)" };
            s_critMat.EnableKeyword("OUTLINE_ON");
            s_critMat.EnableKeyword("GLOW_ON");
            s_critMat.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth * 1.3f);
            s_critMat.SetColor(ShaderUtilities.ID_OutlineColor, critOutlineColor);
            s_critMat.SetFloat(ShaderUtilities.ID_FaceDilate,   faceDilate + 0.06f);
            s_critMat.SetColor(ShaderUtilities.ID_GlowColor,    critGlowColor);
            s_critMat.SetFloat(ShaderUtilities.ID_GlowPower,    critGlowPower);
            s_critMat.SetFloat(ShaderUtilities.ID_GlowOuter,    0.3f);
        }

        label.fontSharedMaterial = s_outlineMat;
    }
    /// <summary>0 → popScale 오버슈트 → 1.0 안착 → 후반 endScale 로 축소.</summary>
    private float ScaleCurve(float t)
    {
        float popT = Mathf.Max(0.001f, popTime / Mathf.Max(0.001f, lifetime));

        if (t < popT)
        {
            // 0 → popScale → 1 (사인 오버슈트: 앞에서 튀고 뒤에서 안착)
            float k = t / popT;
            return Mathf.Lerp(0f, popScale, Mathf.Sin(k * Mathf.PI * 0.5f)) * (1f - k) + 1f * k;
        }

        if (t < shrinkStart) return 1f;

        float s = (t - shrinkStart) / Mathf.Max(0.001f, 1f - shrinkStart);
        return Mathf.Lerp(1f, endScale, s);
    }
}
