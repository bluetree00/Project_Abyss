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
    // SerializeField
    // ─────────────────────────────────────────────────────────────

    [Header("UI References")]
    [SerializeField] private Image _hpFill;
    [SerializeField] private Image _ghostFill;
    [SerializeField] private RectTransform _barRoot;
    [SerializeField] private TMPro.TMP_Text _nameLabel;

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

    [Header("Status Row (디버프 아이콘)")]
    [Tooltip("아이콘 한 칸의 크기(px).")]
    [SerializeField] private float _statusIconSize = 16f;
    [Tooltip("아이콘 사이 간격(px).")]
    [SerializeField] private float _statusSpacing = 2f;
    [Tooltip("동시에 표시할 최대 아이콘 수. 넘치면 오래된 것부터 잘린다.")]
    [SerializeField] private int _statusMaxIcons = 6;

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

    private TMPro.TMP_Text _subLabel;

    // 디버프 아이콘 행 — 프리팹에 앵커가 없어 _subLabel/_nameLabel과 같은 절차 생성 패턴으로 만든다.
    private sealed class StatusCell
    {
        public GameObject      go;
        public Image           icon;
        public Image           remain;   // 하단 잔여 게이지
        public TMPro.TMP_Text  stack;    // ×N (2중첩 이상일 때만)
    }

    private RectTransform _statusRow;
    private readonly System.Collections.Generic.List<StatusCell> _statusCells = new();
    private int _statusShown = -1;   // 마지막으로 배치한 개수(개수가 바뀔 때만 재배치)

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

        if (_subLabel != null) _subLabel.text = string.Empty;

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
        if (_subLabel != null) _subLabel.text = string.Empty;

        // 풀로 돌아가는 바에 이전 몬스터의 디버프가 남지 않게 한다.
        SetStatuses(null);

        gameObject.SetActive(false);
    }

    /// <summary>HP바 아래 보조 텍스트 (DPS 등 더미 전용). 빈 문자열이면 숨긴다.</summary>
    public void SetSubLabel(string text)
    {
        EnsureSubLabel();
        if (_subLabel == null) return;
        _subLabel.text = text ?? string.Empty;
        _subLabel.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    /// <summary>몬스터 이름을 체력바 위 라벨에 표시. 검정 테두리로 가독성 확보.</summary>
    public void SetMonsterName(string monsterName)
    {
        EnsureNameLabel();
        if (_nameLabel == null) return;
        _nameLabel.text = monsterName ?? string.Empty;
        TMPOutlineHelper.ApplyDefault(_nameLabel);
    }

    /// <summary>몬스터 이름을 체력바 라벨에 표시.</summary>
    public void SetMonsterInfo(string monsterName)
    {
        EnsureNameLabel();
        if (_nameLabel == null) return;
        _nameLabel.text = monsterName ?? string.Empty;
        TMPOutlineHelper.ApplyDefault(_nameLabel);
    }

    /// <summary>
    /// HP바 아래에 걸린 상태이상(디버프) 아이콘 행을 갱신한다.
    /// 아이콘 + 중첩 수 + 잔여 게이지. 빈 리스트면 행 전체를 숨긴다.
    ///
    /// 지금까지 몬스터 상태는 머리 위 디버그 마커(GuidelineVisual)로만 보였고, 여러 개가 걸리면
    /// 같은 지점에 포개져 읽히지 않았다. 이 행이 그 역할을 대체한다.
    /// </summary>
    public void SetStatuses(System.Collections.Generic.IReadOnlyList<BuffViewItem> items)
    {
        int count = items != null ? Mathf.Min(items.Count, Mathf.Max(1, _statusMaxIcons)) : 0;

        if (count == 0)
        {
            if (_statusRow != null && _statusRow.gameObject.activeSelf) _statusRow.gameObject.SetActive(false);
            _statusShown = 0;
            return;
        }

        EnsureStatusRow();
        if (_statusRow == null) return;
        if (!_statusRow.gameObject.activeSelf) _statusRow.gameObject.SetActive(true);

        while (_statusCells.Count < count) _statusCells.Add(CreateStatusCell());

        for (int i = 0; i < _statusCells.Count; i++)
        {
            var cell = _statusCells[i];
            if (i >= count) { if (cell.go.activeSelf) cell.go.SetActive(false); continue; }
            if (!cell.go.activeSelf) cell.go.SetActive(true);

            var item = items[i];
            cell.icon.sprite = EffectIconRegistry.GetSprite(item.IconKey);
            cell.remain.fillAmount = Mathf.Clamp01(item.Remaining01);

            bool multi = item.Stacks > 1;
            if (cell.stack.gameObject.activeSelf != multi) cell.stack.gameObject.SetActive(multi);
            if (multi) cell.stack.text = "×" + item.Stacks;
        }

        // 개수가 바뀔 때만 가로 배치를 다시 계산한다(매 갱신마다 레이아웃을 흔들지 않는다).
        if (_statusShown != count)
        {
            _statusShown = count;
            float step = _statusIconSize + _statusSpacing;
            float startX = -(count - 1) * 0.5f * step;
            for (int i = 0; i < count; i++)
            {
                var rt = (RectTransform)_statusCells[i].go.transform;
                rt.anchoredPosition = new Vector2(startX + i * step, 0f);
            }
        }
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

    /// <summary>디버프 아이콘 행 컨테이너 — HP바 바로 아래.</summary>
    private void EnsureStatusRow()
    {
        if (_statusRow != null) return;

        var parentRT = _barRoot != null ? _barRoot : (RectTransform)transform;
        if (parentRT == null) return;

        var go = new GameObject("StatusRow", typeof(RectTransform));
        go.transform.SetParent(parentRT, false);

        _statusRow = go.GetComponent<RectTransform>();
        _statusRow.anchorMin = new Vector2(0.5f, 0f);
        _statusRow.anchorMax = new Vector2(0.5f, 0f);
        _statusRow.pivot     = new Vector2(0.5f, 1f);
        _statusRow.anchoredPosition = new Vector2(0f, -2f);
        _statusRow.sizeDelta = new Vector2(200f, _statusIconSize);
    }

    /// <summary>아이콘 1칸 — 아이콘 + 하단 잔여 게이지 + 중첩 배지.</summary>
    private StatusCell CreateStatusCell()
    {
        var cell = new StatusCell();

        cell.go = new GameObject("Status", typeof(RectTransform));
        cell.go.transform.SetParent(_statusRow, false);
        var rt = (RectTransform)cell.go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(_statusIconSize, _statusIconSize);

        // 아이콘
        var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer));
        iconGo.transform.SetParent(rt, false);
        var irt = (RectTransform)iconGo.transform;
        irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
        irt.offsetMin = Vector2.zero; irt.offsetMax = Vector2.zero;
        cell.icon = iconGo.AddComponent<Image>();
        cell.icon.raycastTarget = false;
        cell.icon.preserveAspect = true;

        // 잔여 게이지 — 아이콘 하단의 얇은 바(가로 채우기)
        var remGo = new GameObject("Remain", typeof(RectTransform), typeof(CanvasRenderer));
        remGo.transform.SetParent(rt, false);
        var rrt = (RectTransform)remGo.transform;
        rrt.anchorMin = new Vector2(0f, 0f);
        rrt.anchorMax = new Vector2(1f, 0f);
        rrt.pivot     = new Vector2(0.5f, 0f);
        rrt.offsetMin = Vector2.zero;
        rrt.offsetMax = Vector2.zero;
        rrt.sizeDelta = new Vector2(0f, 2f);
        cell.remain = remGo.AddComponent<Image>();
        cell.remain.raycastTarget = false;
        cell.remain.color = new Color(1f, 1f, 1f, 0.85f);
        cell.remain.type = Image.Type.Filled;
        cell.remain.fillMethod = Image.FillMethod.Horizontal;
        cell.remain.fillOrigin = 0;

        // 중첩 배지 — 우하단
        var stGo = new GameObject("Stack", typeof(RectTransform), typeof(CanvasRenderer));
        stGo.transform.SetParent(rt, false);
        var srt = (RectTransform)stGo.transform;
        srt.anchorMin = new Vector2(1f, 0f);
        srt.anchorMax = new Vector2(1f, 0f);
        srt.pivot     = new Vector2(1f, 0f);
        srt.anchoredPosition = new Vector2(1f, 0f);
        srt.sizeDelta = new Vector2(_statusIconSize, _statusIconSize * 0.6f);
        cell.stack = stGo.AddComponent<TMPro.TextMeshProUGUI>();
        cell.stack.alignment      = TMPro.TextAlignmentOptions.BottomRight;
        cell.stack.fontSize       = _statusIconSize * 0.55f;
        cell.stack.color          = Color.white;
        cell.stack.raycastTarget  = false;
        cell.stack.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        cell.stack.overflowMode   = TMPro.TextOverflowModes.Overflow;
        TMPOutlineHelper.ApplyDefault(cell.stack);
        stGo.SetActive(false);

        return cell;
    }

    private void EnsureSubLabel()
    {
        if (_subLabel != null) return;

        var parentRT = _barRoot != null ? _barRoot : (RectTransform)transform;
        if (parentRT == null) return;

        var go = new GameObject("SubLabel", typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parentRT, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot     = new Vector2(0.5f, 1f);
        // 상태 아이콘 행이 바로 아래(y -2)에 오므로 그만큼 내려 겹치지 않게 한다.
        rt.anchoredPosition = new Vector2(0f, -(_statusIconSize + 8f));
        rt.sizeDelta = new Vector2(200f, 20f);

        _subLabel = go.AddComponent<TMPro.TextMeshProUGUI>();
        _subLabel.alignment          = TMPro.TextAlignmentOptions.Center;
        _subLabel.color              = new Color(1f, 0.85f, 0.4f, 1f);
        _subLabel.fontSize           = 11f;
        _subLabel.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        _subLabel.overflowMode       = TMPro.TextOverflowModes.Overflow;
        _subLabel.raycastTarget      = false;
        TMPOutlineHelper.ApplyDefault(_subLabel);
        go.SetActive(false);
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
        _nameLabel.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
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
