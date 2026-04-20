using System.Collections;
using UnityEngine;

/// <summary>
/// 적이 원소 효과를 받는 동안 시각적 피드백 제공.
/// - 효과 지속 동안: 원소 색으로 부드러운 틴트 유지
/// - DoT 틱마다: 더 강한 짧은 펄스
/// - 트리거 순간: 가장 강한 임팩트 펄스
/// 같은 GameObject의 ElementBuildup 이벤트를 구독.
/// </summary>
[RequireComponent(typeof(ElementBuildup))]
public class ElementVisualFeedback : MonoBehaviour
{
    // ── Constants ───────────────────────────────────────────────────
    private static readonly Color[] ElementColors =
    {
        new(1.00f, 0.92f, 0.23f, 1f), // Lightning - yellow
        new(0.13f, 0.59f, 0.95f, 1f), // Water     - blue
        new(0.96f, 0.26f, 0.21f, 1f), // Fire      - red
        new(0.30f, 0.69f, 0.31f, 1f), // Grass     - green
        new(0.55f, 0.43f, 0.39f, 1f), // Earth     - brown
    };

    // ── [SerializeField] ────────────────────────────────────────────
    [Header("Renderers (비워두면 자동 탐색)")]
    [SerializeField] private Renderer[] renderers;

    [Header("Tint")]
    [SerializeField] private string colorPropertyName = "_BaseColor";
    [Tooltip("효과 지속 동안 유지되는 틴트 강도 (0=없음, 1=완전 원소색)")]
    [Range(0f, 1f)] [SerializeField] private float sustainStrength = 0.45f;
    [Tooltip("트리거 순간의 임팩트 펄스 강도")]
    [Range(0f, 1f)] [SerializeField] private float triggerPulseStrength = 0.95f;
    [Tooltip("DoT 틱 시의 펄스 강도")]
    [Range(0f, 1f)] [SerializeField] private float tickPulseStrength = 0.8f;
    [SerializeField] private float triggerPulseDuration = 0.18f;
    [SerializeField] private float tickPulseDuration = 0.12f;

    // ── Private ─────────────────────────────────────────────────────
    private ElementBuildup _buildup;
    private MaterialPropertyBlock _mpb;
    private int _colorPropID;
    private Color _baseColor = Color.white;

    // 가장 최근 트리거된 활성 원소 — 지속 틴트의 색
    private ElementType _dominantElement = ElementType.None;
    private Coroutine _pulseCo;

    // ── Lifecycle ───────────────────────────────────────────────────
    private void Awake()
    {
        _buildup = GetComponent<ElementBuildup>();
        _mpb = new MaterialPropertyBlock();
        _colorPropID = Shader.PropertyToID(colorPropertyName);
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>();
    }

    private void OnEnable()
    {
        if (_buildup == null) return;
        _buildup.OnTriggered += HandleTriggered;
        _buildup.OnExpired   += HandleExpired;
        _buildup.OnTick      += HandleTick;
    }

    private void OnDisable()
    {
        if (_buildup == null) return;
        _buildup.OnTriggered -= HandleTriggered;
        _buildup.OnExpired   -= HandleExpired;
        _buildup.OnTick      -= HandleTick;
        StopAllCoroutines();
        ApplyColor(_baseColor);
    }

    // ── Public Methods ───────────────────────────────────────────────
    /// <summary>외부 호출용 — 일반 히트 플래시 등 임의 색상으로 짧게 깜빡.</summary>
    public void FlashHit(Color color, float duration = 0.08f, float strength = 1f)
    {
        StartPulse(color, strength, duration);
    }

    // ── Event Handlers ───────────────────────────────────────────────
    private void HandleTriggered(ElementType element, ElementEffectEntry entry)
    {
        _dominantElement = element;
        StartPulse(ColorOf(element), triggerPulseStrength, triggerPulseDuration);
    }

    private void HandleTick(ElementType element, ElementEffectEntry entry)
    {
        // 틱 발생 원소를 dominant 로 갱신 (색이 바뀌어 가시성↑)
        _dominantElement = element;
        StartPulse(ColorOf(element), tickPulseStrength, tickPulseDuration);
    }

    private void HandleExpired(ElementType element, ElementEffectEntry entry)
    {
        // 만료된 원소가 dominant 였으면 다른 활성 효과 검사
        if (_dominantElement == element)
            _dominantElement = FindAnyActiveElement();

        // 펄스 중이 아니면 즉시 sustained 색 갱신
        if (_pulseCo == null)
            ApplySustained();
    }

    // ── Private Methods ──────────────────────────────────────────────
    private ElementType FindAnyActiveElement()
    {
        for (int i = 0; i < ElementTypeUtil.Count; i++)
        {
            var e = (ElementType)i;
            if (_buildup.IsEffectActive(e)) return e;
        }
        return ElementType.None;
    }

    private void ApplySustained()
    {
        if (_dominantElement.IsValid())
            ApplyColor(Color.Lerp(_baseColor, ColorOf(_dominantElement), sustainStrength));
        else
            ApplyColor(_baseColor);
    }

    private void StartPulse(Color targetColor, float strength, float duration)
    {
        if (_pulseCo != null) StopCoroutine(_pulseCo);
        _pulseCo = StartCoroutine(PulseRoutine(targetColor, strength, duration));
    }

    private IEnumerator PulseRoutine(Color targetColor, float strength, float duration)
    {
        // 즉시 강한 색으로
        Color peakColor = Color.Lerp(_baseColor, targetColor, strength);
        ApplyColor(peakColor);

        // duration 동안 sustained 색으로 페이드
        float t = 0f;
        Color startColor = peakColor;
        Color endColor   = _dominantElement.IsValid()
            ? Color.Lerp(_baseColor, ColorOf(_dominantElement), sustainStrength)
            : _baseColor;

        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            ApplyColor(Color.Lerp(startColor, endColor, k));
            yield return null;
        }

        ApplySustained();
        _pulseCo = null;
    }

    private void ApplyColor(Color color)
    {
        if (renderers == null) return;
        _mpb.SetColor(_colorPropID, color);
        foreach (var r in renderers)
            if (r != null) r.SetPropertyBlock(_mpb);
    }

    private static Color ColorOf(ElementType element)
    {
        if (!element.IsValid()) return Color.white;
        int idx = (int)element;
        if (idx < 0 || idx >= ElementColors.Length) return Color.white;
        return ElementColors[idx];
    }
}
