using TMPro;
using UnityEngine;

/// <summary>
/// 월드 공간에 떠오르는 데미지 숫자.
/// 카메라를 향해 빌보드 + 위로 부드럽게 상승 + 페이드아웃.
/// 풀링되며 라이프타임 종료 시 비활성화 (재사용 대비).
/// </summary>
public class DamagePopup : MonoBehaviour
{
    // ── [SerializeField] ────────────────────────────────────────────
    [SerializeField] private TMP_Text label;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Motion")]
    [SerializeField] private float lifetime = 0.85f;
    [SerializeField] private float riseHeight = 1.4f;
    [SerializeField] private AnimationCurve riseCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0.5f, 1, 1, 0);

    [Header("Crit Style")]
    [SerializeField] private float critScale = 1.6f;
    [SerializeField] private Color critColor = new(1f, 0.85f, 0.2f, 1f);

    // ── Private ─────────────────────────────────────────────────────
    private Vector3 _startWorldPos;
    private float _elapsed;
    private bool _active;
    private Camera _cam;

    // ── Lifecycle ───────────────────────────────────────────────────
    private void Awake()
    {
        if (label == null)       label       = GetComponentInChildren<TMP_Text>();
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
    }

    private void LateUpdate()
    {
        if (!_active) return;

        _elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsed / lifetime);

        // 상승
        float yOff = riseCurve.Evaluate(t) * riseHeight;
        transform.position = _startWorldPos + Vector3.up * yOff;

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
        }
    }

    // ── Public Methods ───────────────────────────────────────────────
    /// <summary>스폰 — 풀에서 꺼낸 직후 호출. damage<0 이면 회복으로 표기.</summary>
    public void Show(Vector3 worldPos, float damage, bool isCrit, Color textColor)
    {
        _startWorldPos = worldPos;
        _elapsed = 0f;
        _active = true;
        _cam = Camera.main;
        transform.position = worldPos;

        if (label != null)
        {
            int amount = Mathf.RoundToInt(Mathf.Abs(damage));
            label.text = isCrit ? $"{amount}!" : amount.ToString();
            label.color = isCrit ? critColor : textColor;
            label.transform.localScale = Vector3.one * (isCrit ? critScale : 1f);
        }

        if (canvasGroup != null)
            canvasGroup.alpha = 1f;

        gameObject.SetActive(true);
    }
}
