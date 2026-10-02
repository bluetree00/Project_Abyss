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
    // Constants — 09-27 2차(사용자: 「깔끔하고 보기 좋게」): 이름판을 없애고 가는 바 + 그림자 이름만 남긴다.
    //   1차의 둥근 이름판(청동 테두리)은 몬스터가 모이면 판끼리 겹쳐 상자 더미로 읽혔다.
    // ─────────────────────────────────────────────────────────────

    private const float FadeInSec    = 0.18f;
    private const float FlashSec     = 0.16f;
    private const float BarRadius    = 2f;     // 바 높이 7 — 둥글기를 크게 주면 채움(사각) 끝과 어긋난다

    private static readonly Color OutlineColor = new(0f, 0f, 0f, 0.80f);          // 1px 윤곽 — 밝은 바닥에서도 바가 떠 보이게
    private static readonly Color TrackColor   = new(0.10f, 0.09f, 0.12f, 0.72f);  // 빈 홈 — 살짝 비친다
    private static readonly Color GlossColor   = new(1f, 1f, 1f, 0.08f);           // 채움 윗면 광택(채운 만큼만) — 선형 색공간이라 0.18은 분홍으로 떴다
    private static readonly Color NameInk      = new(0.93f, 0.91f, 0.87f, 0.95f);
    private static readonly Color FlashColor   = new(1f, 0.90f, 0.82f, 1f);

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

    private bool          _styled;
    private Image         _gloss;
    private CanvasGroup   _group;
    private float         _fadeT = 1f;
    private float         _flash;

    // 디버프 아이콘 행 — 프리팹에 앵커가 없어 _subLabel/_nameLabel과 같은 절차 생성 패턴으로 만든다.
    private sealed class StatusCell
    {
        public GameObject      go;
        public Image           icon;
        public Image           ring;     // 계열색 테 = 남은 시간(시계 방향으로 줄어든다) — 칸이 「무슨 계열」인지 먼저 읽히게
        public TMPro.TMP_Text  stack;    // ×N (2중첩 이상일 때만)
    }

    // 상태 칸 — 어두운 원판 위 아이콘 + 지난 시간만큼 시계 방향으로 어두워지는 쓸림(10-02 사용자 「상태이상 표시가 그냥 원 모양」).
    // 16px 토큰은 머리 위에서 점으로 보였고, 2px 잔여 바는 읽히지 않았다.
    private const float MinStatusCellSize = 20f;
    private static readonly Color StatusPlateColor   = new(0.05f, 0.05f, 0.07f, 0.72f);
    private float StatusCellSize => Mathf.Max(_statusIconSize, MinStatusCellSize);

    private RectTransform _statusRow;

    // 시기 특성 배지(10-02 레벨디자인 §4) — 바 왼쪽에 한 줄. 테 = 분류색(공격 붉음 · 방어 청록 · 지원 금 · 방해 보라).
    private const float TraitBadgeSize = 18f;
    private const float TraitBadgeGap  = 2f;
    private RectTransform _traitRow;
    private readonly System.Collections.Generic.List<StatusCell> _traitCells = new();
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
        // 이어진 몸이 꺼졌다(풀 반환 · 파괴). 정상 바는 몸이 꺼질 때 먼저 돌려받으므로 여기 오는 건 참조를 잃은 바뿐이다 —
        // 두면 꺼진 몸 자리에 이름표가 떠서 같은 자리에 지어진 다음 방들에서도 보였다(10-01 f5 전주기 시뮬 「Spider」).
        if (_monster == null || !_monster.gameObject.activeInHierarchy)
        {
            Debug.LogWarning($"[MonsterHPBar] 몸 없는 바를 끈다 — {(_monster != null ? _monster.name : "파괴된 몬스터")}");
            gameObject.SetActive(false);
            return;
        }

        if (_fadeT < 1f && _group != null)
        {
            _fadeT = Mathf.Min(1f, _fadeT + Time.unscaledDeltaTime / FadeInSec);
            _group.alpha = _fadeT;
        }
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

        EnsureStyle();
        EnsureNameLabel();
        SetTraitBadges(monster.TryGetComponent<MonsterTraits>(out var traits) && traits.enabled ? traits.Badges : null);
        // 등장 — 머리 위에 툭 나타나지 않고 짧게 번진다
        _fadeT = 0f;
        _flash = 0f;
        if (_group != null) _group.alpha = 0f;
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

        // 풀로 돌아가는 바에 이전 몬스터의 디버프 · 특성이 남지 않게 한다.
        SetStatuses(null);
        SetTraitBadges(null);

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

    /// <summary>몬스터 이름을 체력바 위 라벨에 표시.</summary>
    public void SetMonsterName(string monsterName) => SetMonsterInfo(monsterName);

    /// <summary>몬스터 이름을 체력바 위 라벨에 표시. 빈 이름이면 바만 남는다.</summary>
    public void SetMonsterInfo(string monsterName)
    {
        EnsureNameLabel();
        if (_nameLabel == null) return;
        _nameLabel.text = monsterName ?? string.Empty;
        TMPOutlineHelper.ApplySoftShadow(_nameLabel);
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
            cell.icon.color  = EffectIconRegistry.TintFor(item.IconKey);   // 흰 글리프에 계열색
            var ringCol = cell.icon.color; ringCol.a = 0.75f;
            cell.ring.color  = ringCol;
            // 지난 시간 쓸림 — 남은 비율이 없으면(무기한) 쓸지 않는다
            // 남은 시간 = 테의 길이(무기한이면 가득). 예전엔 칸 위를 어둡게 덮어 글리프가 반쯤 가려졌다(10-02 실측).
            cell.ring.fillAmount = item.Remaining01 < 0f ? 1f : Mathf.Clamp01(item.Remaining01);

            bool multi = item.Stacks > 1;
            if (cell.stack.gameObject.activeSelf != multi) cell.stack.gameObject.SetActive(multi);
            if (multi) cell.stack.text = "×" + item.Stacks;
        }

        // 개수가 바뀔 때만 가로 배치를 다시 계산한다(매 갱신마다 레이아웃을 흔들지 않는다).
        if (_statusShown != count)
        {
            _statusShown = count;
            float step = StatusCellSize + _statusSpacing;
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
            _flash       = 1f;   // 맞은 순간 채움이 잠깐 밝아진다
        }
        _targetRatio = newRatio;
    }

    // ─────────────────────────────────────────────────────────────
    // Private Methods
    // ─────────────────────────────────────────────────────────────

    private void UpdateHpAnimation()
    {
        float dt = Time.deltaTime;
        if (_flash > 0f) _flash = Mathf.Max(0f, _flash - Time.unscaledDeltaTime / FlashSec);
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
        _hpFill.color = _flash > 0f ? Color.Lerp(_hpColor, FlashColor, _flash * 0.65f) : _hpColor;
        if (_gloss != null) _gloss.fillAmount = ratio;
    }

    private void ApplyGhostFill(float ratio)
    {
        if (_ghostFill == null) return;
        _ghostFill.fillAmount = ratio;
        _ghostFill.color = _ghostColor;
    }

    /// <summary>
    /// 시기 특성 배지 — 바 왼쪽 끝에서 왼쪽으로 늘어선다(이름 · 상태 행과 자리가 겹치지 않는다). 비면 숨긴다.
    /// 칸은 상태 행과 같은 모양(어두운 원판 + 테 + 아이콘)이고, 테가 시간 대신 분류색이다.
    /// </summary>
    public void SetTraitBadges(System.Collections.Generic.IReadOnlyList<MonsterTraitBadge> badges)
    {
        int count = badges != null ? badges.Count : 0;
        if (count == 0)
        {
            if (_traitRow != null && _traitRow.gameObject.activeSelf) _traitRow.gameObject.SetActive(false);
            return;
        }

        if (_traitRow == null)
        {
            var parentRT = _barRoot != null ? _barRoot : (RectTransform)transform;
            if (parentRT == null) return;
            var go = new GameObject("TraitRow", typeof(RectTransform));
            go.transform.SetParent(parentRT, false);
            _traitRow = (RectTransform)go.transform;
            _traitRow.anchorMin = _traitRow.anchorMax = new Vector2(0f, 0.5f);
            _traitRow.pivot     = new Vector2(1f, 0.5f);
            _traitRow.anchoredPosition = new Vector2(-4f, 0f);
            _traitRow.sizeDelta = new Vector2(TraitBadgeSize * 2f + TraitBadgeGap, TraitBadgeSize);
        }
        if (!_traitRow.gameObject.activeSelf) _traitRow.gameObject.SetActive(true);

        while (_traitCells.Count < count) _traitCells.Add(CreateBadgeCell(_traitRow, TraitBadgeSize));
        for (int i = 0; i < _traitCells.Count; i++)
        {
            var cell = _traitCells[i];
            bool on = i < count;
            if (cell.go.activeSelf != on) cell.go.SetActive(on);
            if (!on) continue;
            var b = badges[i];
            cell.icon.sprite = EffectIconRegistry.GetSprite(b.IconKey);
            cell.icon.color  = EffectIconRegistry.TintFor(b.IconKey);
            cell.ring.color  = b.Ring;
            cell.ring.fillAmount = 1f;
            // 오른쪽(바 쪽)부터 왼쪽으로
            ((RectTransform)cell.go.transform).anchoredPosition = new Vector2(-(TraitBadgeSize * 0.5f + i * (TraitBadgeSize + TraitBadgeGap)), 0f);
        }
    }

    /// <summary>배지 1칸 — 원판 + 분류색 테 + 아이콘(오른쪽 끝 기준 배치).</summary>
    private static StatusCell CreateBadgeCell(RectTransform row, float size)
    {
        var cell = new StatusCell();
        cell.go = new GameObject("Trait", typeof(RectTransform));
        cell.go.transform.SetParent(row, false);
        var rt = (RectTransform)cell.go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        var plate = NewStatusImage("Plate", rt, 0f);
        plate.sprite = UIProceduralSprites.Circle();
        plate.color  = StatusPlateColor;
        cell.ring = NewStatusImage("Ring", rt, 0f);
        cell.ring.sprite = UIProceduralSprites.Ring(0.13f);
        cell.ring.type   = Image.Type.Filled;
        cell.ring.fillMethod = Image.FillMethod.Radial360;
        cell.icon = NewStatusImage("Icon", rt, 3.5f);
        cell.icon.preserveAspect = true;
        return cell;
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
        _statusRow.sizeDelta = new Vector2(200f, StatusCellSize);
    }

    /// <summary>상태 1칸 — 어두운 원판 + 아이콘 + 지난 시간 쓸림(원형) + 중첩 배지.</summary>
    private StatusCell CreateStatusCell()
    {
        var cell = new StatusCell();
        float size = StatusCellSize;

        cell.go = new GameObject("Status", typeof(RectTransform));
        cell.go.transform.SetParent(_statusRow, false);
        var rt = (RectTransform)cell.go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);

        // 원판 — 밝은 바닥 위에서도 아이콘 테두리가 서게
        var plate = NewStatusImage("Plate", rt, 0f);
        plate.sprite = UIProceduralSprites.Circle();
        plate.color  = StatusPlateColor;

        // 계열색 테 — 원판 가장자리
        cell.ring = NewStatusImage("Ring", rt, 0f);
        cell.ring.sprite        = UIProceduralSprites.Ring(0.11f);
        cell.ring.type          = Image.Type.Filled;
        cell.ring.fillMethod    = Image.FillMethod.Radial360;
        cell.ring.fillOrigin    = (int)Image.Origin360.Top;
        cell.ring.fillClockwise = false;   // 12시에서 시계 반대로 비워진다 — 남은 쪽이 시계 방향으로 남는다

        // 아이콘 — 원판 안으로 들여(테와 겹치지 않게)
        cell.icon = NewStatusImage("Icon", rt, 3f);
        cell.icon.preserveAspect = true;

        // 중첩 배지 — 우하단
        var stGo = new GameObject("Stack", typeof(RectTransform), typeof(CanvasRenderer));
        stGo.transform.SetParent(rt, false);
        var srt = (RectTransform)stGo.transform;
        srt.anchorMin = new Vector2(1f, 0f);
        srt.anchorMax = new Vector2(1f, 0f);
        srt.pivot     = new Vector2(1f, 0f);
        srt.anchoredPosition = new Vector2(1f, 0f);
        srt.sizeDelta = new Vector2(size, size * 0.6f);
        cell.stack = stGo.AddComponent<TMPro.TextMeshProUGUI>();
        cell.stack.alignment      = TMPro.TextAlignmentOptions.BottomRight;
        cell.stack.fontSize       = size * 0.55f;
        cell.stack.color          = Color.white;
        cell.stack.raycastTarget  = false;
        cell.stack.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        cell.stack.overflowMode   = TMPro.TextOverflowModes.Overflow;
        TMPOutlineHelper.ApplySoftShadow(cell.stack);   // 글자 정본 — 두꺼운 테두리 금지
        stGo.SetActive(false);

        return cell;
    }

    private static Image NewStatusImage(string name, RectTransform parent, float inset)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var r = (RectTransform)go.transform;
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(inset, inset); r.offsetMax = new Vector2(-inset, -inset);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        return img;
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
        rt.anchoredPosition = new Vector2(0f, -(StatusCellSize + 8f));
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
        _nameLabel.color              = NameInk;
        _nameLabel.fontStyle          = TMPro.FontStyles.Normal;   // 기본 폰트(DNFForgedBlade Bold)가 이미 굵다 — 가짜 굵게를 더하면 뭉갠다
        _nameLabel.characterSpacing   = 0.5f;
        _nameLabel.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        _nameLabel.overflowMode       = TMPro.TextOverflowModes.Overflow;
        _nameLabel.raycastTarget      = false;
        _nameLabel.fontSize           = _nameLabelFontSize;
    }

    /// <summary>
    /// 바 윤곽·광택·페이드 그룹 — 프리팹에 없는 것을 한 번만 붙인다(풀 재사용 시 다시 안 짓는다).
    /// 바는 검은 1px 윤곽 안의 반투명 홈, 채움 위쪽 절반에 옅은 광택. 이름 뒤에는 판을 깔지 않는다(그림자만).
    /// </summary>
    private void EnsureStyle()
    {
        if (_styled) return;
        _styled = true;

        if (!TryGetComponent(out _group)) _group = gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable   = false;

        if (_barRoot == null) return;

        var bar  = UIProceduralSprites.RoundedRect(radius: BarRadius, feather: 1f, size: 32);
        // 채움·잔상·광택은 곧은 막대 — 프리팹 채움 그림은 양끝이 가늘어져 가득 차도 홈 끝에 닿지 않았다(09-27 실측).
        // Filled는 스프라이트가 있어야 채운 만큼만 그린다(없으면 전체를 덮는다).
        var flat = UIProceduralSprites.RoundedRect(radius: 1f, feather: 0.5f, size: 8);
        if (_hpFill != null)    _hpFill.sprite    = flat;
        if (_ghostFill != null) _ghostFill.sprite = flat;
        var bgT = _barRoot.Find("BG");
        if (bgT != null && bgT.TryGetComponent<Image>(out var bgImg))
        {
            bgImg.sprite = bar; bgImg.type = Image.Type.Sliced; bgImg.pixelsPerUnitMultiplier = 2.2f;
            bgImg.color  = TrackColor;
        }

        var outlineGo = new GameObject("Outline", typeof(RectTransform), typeof(CanvasRenderer));
        outlineGo.transform.SetParent(_barRoot, false);
        outlineGo.transform.SetAsFirstSibling();   // 홈 뒤 — 한 치 크게 깔려 테두리로 보인다
        var ort = (RectTransform)outlineGo.transform;
        ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one;
        ort.offsetMin = new Vector2(-1f, -1f); ort.offsetMax = new Vector2(1f, 1f);
        var outline = outlineGo.AddComponent<Image>();
        outline.sprite = bar; outline.type = Image.Type.Sliced; outline.pixelsPerUnitMultiplier = 2.2f;
        outline.color  = OutlineColor;
        outline.raycastTarget = false;

        // 광택 — 채움과 같은 양만큼만(가로 채우기), 위쪽 절반. 빈 홈까지 밝히면 바가 회색으로 뜬다.
        var glossGo = new GameObject("Gloss", typeof(RectTransform), typeof(CanvasRenderer));
        glossGo.transform.SetParent(_barRoot, false);
        var grt = (RectTransform)glossGo.transform;
        grt.anchorMin = new Vector2(0f, 0.5f); grt.anchorMax = Vector2.one;
        grt.offsetMin = Vector2.zero; grt.offsetMax = new Vector2(0f, -1f);
        _gloss = glossGo.AddComponent<Image>();
        _gloss.sprite = flat;
        _gloss.color = GlossColor;
        _gloss.raycastTarget = false;
        _gloss.type = Image.Type.Filled;
        _gloss.fillMethod = Image.FillMethod.Horizontal;
        _gloss.fillOrigin = (int)Image.OriginHorizontal.Left;
        _gloss.fillAmount = _displayRatio;
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
