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
    [Header("UI References")]
    [SerializeField] private Slider _slider;

    [Header("Element Gauge (HP 위)")]
    [SerializeField] private Image _elementFill;
    [SerializeField] private TMPro.TMP_Text _elementLabel;

    private static readonly Color[] _elementColors =
    {
        new(1.00f, 0.92f, 0.23f, 1f), // Lightning
        new(0.13f, 0.59f, 0.95f, 1f), // Water
        new(0.96f, 0.26f, 0.21f, 1f), // Fire
        new(0.30f, 0.69f, 0.31f, 1f), // Grass
        new(0.55f, 0.43f, 0.39f, 1f), // Earth
    };

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

    private MonoBehaviour _monster;
    private Transform _anchor;
    private Renderer[] _renderers;
    private Collider[] _colliders;
    private float _colliderTopY;
    private Transform _camTransform;
    private bool _hasLastPosition;
    private Vector3 _lastPosition;

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

        UpdateHP(currentHp, maxHp);
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

    public void UpdateHP(int currentHp, int maxHp)
    {
        if (_slider == null) return;
        _slider.value = maxHp > 0 ? (float)currentHp / maxHp : 0f;
    }

    /// <summary>원소 누적치 게이지 갱신. element=None 이면 흐리게 표시.</summary>
    public void UpdateElement(float ratio, float accum, float threshold, ElementType element)
    {
        if (_elementFill != null)
        {
            _elementFill.fillAmount = Mathf.Clamp01(ratio);
            _elementFill.color = ColorOf(element);
        }

        if (_elementLabel != null)
        {
            _elementLabel.text = element.IsValid()
                ? $"{accum:F0}/{threshold:F0}"
                : $"- {accum:F0}/{threshold:F0}";
        }
    }

    private static Color ColorOf(ElementType element)
    {
        if (!element.IsValid()) return new Color(0.5f, 0.5f, 0.5f, 1f);
        int idx = (int)element;
        if (idx < 0 || idx >= _elementColors.Length) return Color.white;
        return _elementColors[idx];
    }

    private void Update()
    {
        if (_monster == null) return;

        if (_camTransform == null && Camera.main != null)
            _camTransform = Camera.main.transform;

        Vector3 bodyTop = default;
        float bodyHeight = 0f;
        bool hasBodyMetrics = TryGetBodyTopAndHeight(out bodyTop, out bodyHeight);

        Vector3 basePos;
        if (_anchor != null && _anchor.gameObject.activeInHierarchy)
        {
            basePos = _anchor.position;

            // Head/anchor가 몸통 상단보다 아래면 몸통 기준으로 보정.
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
                _lastPosition = targetPos;
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

    private void Awake()
    {
        if (_slider == null)
            _slider = GetComponentInChildren<Slider>();

        gameObject.SetActive(false);
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
                    combined = renderer.bounds;
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
                    combined = col.bounds;
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

        float extraHeight = Mathf.Max(0f, height - _tallMonsterHeightThreshold);
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
