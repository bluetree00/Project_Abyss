using UnityEngine;

/// <summary>
/// 출구 게이트 위 월드 라벨(「■ 상점 / 물건 구매」)의 화면 크기 상한 + 게이트 앞 옅어지기.
/// 라벨은 월드 공간 글이라 카메라가 게이트에 다가갈수록 커져, 출구 앞에서는 글자 높이가 화면의 1/6까지 커져
/// 캐릭터와 화면 가운데를 덮었다(10-01 전주기 시뮬 피드백). 같은 정보는 출구 배지(<see cref="ExitCompassHud"/>)가
/// 화면에 따로 띄우므로, 가까이서는 라벨을 줄이고 옅게 한다.
/// 알파는 CanvasGroup으로 곱한다 — 공개 연출(RunFlowController.RevealGateAsync)이 글자 색 알파를 따로 올리기 때문.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public sealed class GateLabelScreenCap : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────
    private const float MaxLinePx   = 72f;    // 1행 글자 높이 상한(1080 기준 px)
    private const float FadeStartM  = 7f;     // 플레이어~게이트 거리가 이보다 가까우면 옅어지기 시작(m)
    private const float FadeFullM   = 3.5f;   // 이 거리에서 가장 옅다
    private const float NearAlpha   = 0.2f;

    // ── Private ───────────────────────────────────────────────
    private float       _baseScale;
    private float       _lineWorld;   // 1행 글자 높이(월드 m, 기본 배율 기준)
    private Camera      _cam;
    private CanvasGroup _group;

    // ── Lifecycle ─────────────────────────────────────────────
    private void Awake() => _group = GetComponent<CanvasGroup>();

    private void LateUpdate()
    {
        if (_lineWorld <= 0f) return;
        if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }

        // 화면 크기 상한 — 원근 카메라에서 깊이 d의 1 m가 화면 몇 px인지로 1행 높이를 재고, 넘치면 그만큼 줄인다.
        Transform camT = _cam.transform;
        float depth = Vector3.Dot(transform.position - camT.position, camT.forward);
        float k = 1f;
        if (!_cam.orthographic && depth > 0.1f)
        {
            float refPxPerM = 1080f / (2f * depth * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            float linePx = _lineWorld * refPxPerM;
            if (linePx > MaxLinePx) k = MaxLinePx / linePx;
        }
        float s = _baseScale * k;
        if (!Mathf.Approximately(transform.localScale.x, s)) transform.localScale = new Vector3(s, s, s);

        // 게이트 앞에 서면 옅게 — 캐릭터를 덮지 않게(정보는 출구 배지가 화면에 띄운다).
        var player = Managers.Player?.PlayerTransform;
        if (player == null) return;
        Vector3 d = transform.position - player.position; d.y = 0f;
        _group.alpha = Mathf.Lerp(NearAlpha, 1f, Mathf.InverseLerp(FadeFullM, FadeStartM, d.magnitude));
    }

    // ── Public Methods ────────────────────────────────────────
    /// <summary>라벨을 만든 직후 1회 — 지금 배율을 기본값으로 삼고, 1행 글자 높이(월드 m)를 넘긴다.</summary>
    public void Init(float lineWorldHeight)
    {
        _baseScale = transform.localScale.x;
        _lineWorld = lineWorldHeight;
    }
}
