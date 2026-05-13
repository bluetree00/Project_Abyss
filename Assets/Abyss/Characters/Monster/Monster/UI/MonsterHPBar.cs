using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// World-space HP bar for monsters.
/// Position priority:
/// 1) Explicit HP anchor / head transform passed from MonsterBase
/// 2) Combined renderer bounds top
/// 3) Capsule collider top fallback
/// </summary>
public class MonsterHPBar : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────

    private static readonly Color[] ElementColors =
    {
        new(1.00f, 0.92f, 0.23f, 1f), // Lightning — yellow
        new(0.13f, 0.59f, 0.95f, 1f), // Water     — blue
        new(0.96f, 0.26f, 0.21f, 1f), // Fire      — red
        new(0.30f, 0.69f, 0.31f, 1f), // Grass     — green
        new(0.55f, 0.43f, 0.39f, 1f), // Earth     — brown
    };

    private static readonly string[] ElementIcons = { "⚡", "💧", "🔥", "🌿", "🪨" };

    // ─────────────────────────────────────────────────────────────
    // SerializeField
    // ─────────────────────────────────────────────────────────────

    [Header("UI References")]
    [SerializeField] private Image _hpFill;
    [SerializeField] private Image _ghostFill;
    [SerializeField] private RectTransform _barRoot;
    [SerializeField] private TMPro.TMP_Text _nameLabel;

    [Header("Element Gauge")]
    [SerializeField] private GameObject _elementGaugeRoot;
    [SerializeField] private Image _elementGaugeFill;
    [SerializeField] private TMPro.TMP_Text _elementGaugeLabel;

    [Header("HP Animation")]
    [SerializeField] private float _hpLerpSpeed = 1.8f;
    [SerializeField] private float _ghostDelay = 0.35f;
    [SerializeField] private float _ghostLerpSpeed = 0.35f;

    [Header("Colors")]
    [SerializeField] private Color _hpColor    = new(0.88f, 0.18f, 0.18f, 1f);
    [SerializeField] private Color _ghostColor = new(1.00f, 0.75f, 0.20f, 0.65f);

    [Header("Name Label")]
    [Tooltip("비워두면 Link 시점에 자동 생성된다.")]
    [SerializeField] private float _nameLabelYOffset = 4f;
    [SerializeField] private Vector2 _nameLabelSize = new(200f, 26f);
    [SerializeField] private float _nameLabelFontSize = 14f;

    [Header("Position")]
    [SerializeField] private float _headOffset = 0.1f;
    [SerializeField] private float _minAutoOffset = 0.12f;
    [SerializeField] private float _maxAutoOffset = 0.65f;
    [SerializeField] private float _heightToOffsetRatio = 0.04f;
    [SerializeField] private float _anchorLowTolerance = 0.15f;
    [SerializeField] private float _tallMonsterHeightThreshold = 3.2f;
    [SerializeField] private float _tallMonsterDropRatio = 0.28f;
    [SerializeField] private float _tallMonsterMaxDrop = 1.5f;
    [SerializeField] private bool _smoothFollow = true;
    [SerializeField] private float _followLerpSpeed = 20f;

    // ─────────────────────────────────────────────────────────────
    // Private fields
    // ─────────────────────────────────────────────────────────────

    private float _targetRatio;
    private float _displayRatio;
    private float _ghostRatio;
    private float _ghostTimer;
    private bool _ghostActive;

    private MonoBehaviour _monster;
    private Transform _anchor;
    private Renderer[] _renderers;
    private Collider[] _colliders;
    private float _colliderTopY;
    private Transform _camTransform;
    private bool _hasLastPosition;
    private Vector3 _lastPosition;

    // ─────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        gameObject.SetActive(false);
    }

    private void Update()
    {
        if (_monster == null) return;

        UpdateHpAnimation();
        UpdatePosition();
    }

    // ─────────────────────────────────────────────────────────────
    // Public Methods
    // ─────────────────────────────────────────────────────────────

    public void Link(MonoBehaviour monster, int currentHp, int maxHp, Transform anchor, float headOffset = 0.1f)
    {
        _monster = monster;
        _anchor = anchor;
        _camTransform = Camera.main != null ? Camera.main.transform : null;
        _headOffset = headOffset;
        _renderers = monster.GetComponentsInChildren<Renderer>(true);
        _colliders = monster.GetComponentsInChildren<Collider>(true);
        _hasLastPosition = false;

        var cap = monster.GetComponentInChildren<CapsuleCollider>();
        _colliderTopY = cap != null ? cap.center.y + cap.height * 0.5f : 1.5f;

        // 즉시 초기화 (애니메이션 없이 현재 HP 즉시 표시)
        float initialRatio = maxHp > 0 ? Mathf.Clamp01((float)currentHp / maxHp) : 0f;
        _targetRatio  = initialRatio;
        _displayRatio = initialRatio;
        _ghostRatio   = initialRatio;
        _ghostActive  = false;
        _ghostTimer   = 0f;

        ApplyHpFill(_displayRatio);
        ApplyGhostFill(_displayRatio);

        EnsureNameLabel();
        gameObject.SetActive(true);
    }

    public void Unlink()
    {
        _monster = null;
        _anchor = null;
        _renderers = null;
        _colliders = null;
        _hasLastPosition = false;
        gameObject.SetActive(false);
    }

    /// <summary>몬스터 이름을 체력바 위 라벨에 표시. 검정 테두리로 가독성 확보.</summary>
    public void SetMonsterName(string monsterName)
    {
        EnsureNameLabel();
        if (_nameLabel == null) return;
        _nameLabel.text = monsterName ?? string.Empty;
        TMPOutlineHelper.ApplyDefault(_nameLabel);
    }

    /// <summary>몬스터 이름 및 고유 속성 표시. 속성 아이콘을 이름 앞에 붙인다.</summary>
    public void SetMonsterInfo(string monsterName, ElementType element)
    {
        EnsureNameLabel();
        if (_nameLabel == null) return;
        string icon = element.IsValid() ? $"{ElementIcons[(int)element]} " : "";
        _nameLabel.text = icon + (monsterName ?? string.Empty);
        TMPOutlineHelper.ApplyDefault(_nameLabel);
    }

    public void UpdateHP(int currentHp, int maxHp)
    {
        float newRatio = maxHp > 0 ? Mathf.Clamp01((float)currentHp / maxHp) : 0f;
        if (newRatio < _targetRatio - 0.001f)
        {
            _ghostRatio  = _displayRatio;
            _ghostTimer  = _ghostDelay;
            _ghostActive = true;
        }
        _targetRatio = newRatio;
    }

    /// <summary>원소 누적치 게이지 갱신. 누적이 없으면 게이지를 숨긴다.</summary>
    public void UpdateElement(float ratio, float accum, float threshold, ElementType element, int poisonStacks = 0)
    {
        bool hasBuildup = element.IsValid() && ratio > 0.001f;

        if (_elementGaugeRoot != null)
            _elementGaugeRoot.SetActive(hasBuildup);

        if (!hasBuildup) return;

        if (_elementGaugeFill != null)
        {
            _elementGaugeFill.fillAmount = ratio;
            _elementGaugeFill.color = ElementColorOf(element);
        }

        if (_elementGaugeLabel != null)
        {
            string label = $"{ElementIcons[(int)element]} {accum:F0} / {threshold:F0}";
            if (element == ElementType.Grass && poisonStacks > 0)
                label += $"  ×{poisonStacks}";
            _elementGaugeLabel.text = label;
        }
    }

    private static Color ElementColorOf(ElementType element)
    {
        if (!element.IsValid()) return Color.gray;
        int idx = (int)element;
        return idx >= 0 && idx < ElementColors.Length ? ElementColors[idx] : Color.gray;
    }

    // ─────────────────────────────────────────────────────────────
    // Private Methods
    // ─────────────────────────────────────────────────────────────

    private void UpdateHpAnimation()
    {
        float dt = Time.deltaTime;
        _displayRatio = Mathf.MoveTowards(_displayRatio, _targetRatio, _hpLerpSpeed * dt);

        if (_ghostActive)
        {
            if (_ghostTimer > 0f)
            {
                _ghostTimer -= dt;
            }
            else
            {
                _ghostRatio = Mathf.MoveTowards(_ghostRatio, _targetRatio, _ghostLerpSpeed * dt);
                if (Mathf.Abs(_ghostRatio - _targetRatio) < 0.002f)
                {
                    _ghostRatio  = _targetRatio;
                    _ghostActive = false;
                }
            }
        }

        float ghostDisplay = Mathf.Max(_ghostRatio, _displayRatio);
        ApplyHpFill(_displayRatio);
        ApplyGhostFill(ghostDisplay);
    }

    private void ApplyHpFill(float ratio)
    {
        if (_hpFill == null) return;
        _hpFill.fillAmount = ratio;
        _hpFill.color = _hpColor;
    }

    private void ApplyGhostFill(float ratio)
    {
        if (_ghostFill == null) return;
        _ghostFill.fillAmount = ratio;
        _ghostFill.color = _ghostColor;
    }

    private void EnsureNameLabel()
    {
        if (_nameLabel != null) return;

        var parentRT = _barRoot != null ? _barRoot : (RectTransform)transform;
        if (parentRT == null) return;

        var go = new GameObject("NameLabel", typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parentRT, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, _nameLabelYOffset);
        rt.sizeDelta = _nameLabelSize;

        _nameLabel = go.AddComponent<TMPro.TextMeshProUGUI>();
        _nameLabel.alignment          = TMPro.TextAlignmentOptions.Center;
        _nameLabel.color              = Color.white;
        _nameLabel.fontStyle          = TMPro.FontStyles.Bold;
        _nameLabel.enableWordWrapping = false;
        _nameLabel.overflowMode       = TMPro.TextOverflowModes.Overflow;
        _nameLabel.raycastTarget      = false;
        _nameLabel.fontSize           = _nameLabelFontSize;
    }

    private void UpdatePosition()
    {
        if (_camTransform == null && Camera.main != null)
            _camTransform = Camera.main.transform;

        bool hasBodyMetrics = TryGetBodyTopAndHeight(out Vector3 bodyTop, out float bodyHeight);

        Vector3 basePos;
        if (_anchor != null && _anchor.gameObject.activeInHierarchy)
        {
            basePos = _anchor.position;

            if (hasBodyMetrics && basePos.y < bodyTop.y - _anchorLowTolerance)
                basePos = bodyTop;
        }
        else if (hasBodyMetrics)
        {
            basePos = bodyTop;
        }
        else
        {
            basePos = _monster.transform.position + Vector3.up * _colliderTopY;
        }

        float autoOffset = hasBodyMetrics
            ? Mathf.Clamp(bodyHeight * _heightToOffsetRatio, _minAutoOffset, _maxAutoOffset)
            : _minAutoOffset;
        float finalOffset = Mathf.Max(_headOffset, autoOffset);
        Vector3 targetPos = basePos + Vector3.up * finalOffset;

        if (_smoothFollow)
        {
            if (!_hasLastPosition)
            {
                _lastPosition    = targetPos;
                _hasLastPosition = true;
            }
            else
            {
                float t = 1f - Mathf.Exp(-Mathf.Max(1f, _followLerpSpeed) * Time.deltaTime);
                _lastPosition = Vector3.Lerp(_lastPosition, targetPos, t);
            }

            transform.position = _lastPosition;
        }
        else
        {
            transform.position = targetPos;
        }

        if (_camTransform != null)
            transform.rotation = _camTransform.rotation;
    }

    private bool TryGetBodyTopAndHeight(out Vector3 topPosition, out float height)
    {
        topPosition = default;
        height = 0f;

        bool hasBounds = false;
        Bounds combined = default;

        if (_renderers != null)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                var renderer = _renderers[i];
                if (!IsRenderableBody(renderer)) continue;

                if (!hasBounds)
                {
                    combined  = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(renderer.bounds);
                }
            }
        }

        if (_colliders != null)
        {
            for (int i = 0; i < _colliders.Length; i++)
            {
                var col = _colliders[i];
                if (col == null || !col.enabled || col.isTrigger) continue;

                if (!hasBounds)
                {
                    combined  = col.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(col.bounds);
                }
            }
        }

        if (!hasBounds)
            return false;

        height = Mathf.Max(0.1f, combined.size.y);

        float extraHeight  = Mathf.Max(0f, height - _tallMonsterHeightThreshold);
        float verticalDrop = Mathf.Clamp(extraHeight * _tallMonsterDropRatio, 0f, _tallMonsterMaxDrop);
        topPosition = new Vector3(combined.center.x, combined.max.y - verticalDrop, combined.center.z);
        return true;
    }

    private static bool IsRenderableBody(Renderer renderer)
    {
        if (renderer == null || !renderer.enabled) return false;
        if (renderer is ParticleSystemRenderer) return false;
        if (renderer is TrailRenderer) return false;
        if (renderer is LineRenderer) return false;
        return true;
    }
}
