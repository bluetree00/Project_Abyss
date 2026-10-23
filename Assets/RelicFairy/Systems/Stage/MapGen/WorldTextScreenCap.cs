using TMPro;
using UnityEngine;

/// <summary>
/// 월드 글자(이름표 · 「[F] …」 상호작용 안내)의 화면 크기 상한 — 글자 크기가 미터로 정해져 카메라가 다가가면 화면을 덮었다
/// (10-22 사용자 「서약 근처에 가면 서약 조립 UI가 겹쳐 보인다」 — 「[F] 서약 조립」이 화면 아래 HUD를 덮음).
/// 출구 라벨의 <see cref="GateLabelScreenCap"/>와 같은 셈(깊이 d의 1 m가 화면 몇 px인가)이고, 알파는 건드리지 않는다.
/// 상호작용 안내로 붙이면 떠 있는 동안 <see cref="ActivePrompts"/>를 센다 — 출구 배지가 그동안 옅어진다(정제소 앞에서 배지가 「[F] 정제소」를 덮었다).
/// </summary>
public sealed class WorldTextScreenCap : MonoBehaviour
{
    // ── Static ────────────────────────────────────────────────
    /// <summary>지금 화면에 떠 있는 상호작용 안내 수.</summary>
    public static int ActivePrompts { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ActivePrompts = 0;

    // ── Private ───────────────────────────────────────────────
    private TMP_Text _text;
    private float  _baseScale;
    private float  _lineWorld;   // 1행 글자 높이(월드 m, 기본 배율 기준) — 처음엔 글자 크기로 어림, 글이 그려지면 실제 경계로 바꾼다
    private bool   _measured;
    private float  _maxLinePx;   // 1행 높이 상한(1080 기준 px)
    private bool   _prompt;
    private bool   _counted;
    private Camera _cam;

    // ── Lifecycle ─────────────────────────────────────────────
    private void OnEnable()
    {
        if (_prompt && !_counted) { ActivePrompts++; _counted = true; }
    }

    private void OnDisable()
    {
        if (_counted) { ActivePrompts = Mathf.Max(0, ActivePrompts - 1); _counted = false; }
    }

    private void LateUpdate()
    {
        if (!_measured) Measure();
        if (_lineWorld <= 0f) return;
        if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }
        Transform camT = _cam.transform;
        float depth = Vector3.Dot(transform.position - camT.position, camT.forward);
        float k = 1f;
        if (!_cam.orthographic && depth > 0.1f)
        {
            float refPxPerM = 1080f / (2f * depth * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            float linePx = _lineWorld * refPxPerM;
            if (linePx > _maxLinePx) k = _maxLinePx / linePx;
        }
        float s = _baseScale * k;
        if (!Mathf.Approximately(transform.localScale.x, s)) transform.localScale = new Vector3(s, s, s);
    }

    /// <summary>
    /// 실제 글 경계(로컬) ÷ 줄 수 → 1행 월드 높이(기본 배율 기준). 글자 크기 × 0.1 어림은 괄호 · 한글이 든 「[F] 서약 조립」에서
    /// 실제보다 1.4배 작아 상한이 안 걸렸다(10-22 실측 49 px). 글이 아직 안 그려졌으면 다음 프레임에 다시.
    /// </summary>
    private void Measure()
    {
        if (_text == null) { _measured = true; return; }
        int lines = _text.textInfo != null ? _text.textInfo.lineCount : 0;
        float h = _text.textBounds.size.y;
        if (lines <= 0 || h <= 0f) return;
        Transform t = transform;
        float parentScale = Mathf.Abs(t.localScale.y) > 1e-5f ? t.lossyScale.y / t.localScale.y : 1f;
        _lineWorld = h / lines * parentScale * _baseScale;
        _measured  = true;
    }

    // ── Public Methods ────────────────────────────────────────
    /// <summary>
    /// 글자를 만든 직후 1회 — 지금 배율을 기본값으로 삼는다. 1행 높이는 TMP 글자 크기에서 어림한다(크기 10 ≈ 1 m).
    /// <paramref name="prompt"/>면 켜져 있는 동안 <see cref="ActivePrompts"/>에 센다.
    /// </summary>
    public static WorldTextScreenCap Attach(TMP_Text text, float maxLinePx, bool prompt)
    {
        if (text == null) return null;
        if (!text.TryGetComponent(out WorldTextScreenCap cap)) cap = text.gameObject.AddComponent<WorldTextScreenCap>();
        cap._text      = text;
        cap._baseScale = text.transform.localScale.x;
        cap._lineWorld = text.fontSize * 0.1f;
        cap._measured  = false;
        cap._maxLinePx = maxLinePx;
        cap._prompt    = prompt;
        if (prompt && cap.isActiveAndEnabled && !cap._counted) { ActivePrompts++; cap._counted = true; }
        return cap;
    }
}
