using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// StagePointUI 노드 사이에 자동으로 연결선을 생성한다.
/// 현재 노드에서 이동 가능한 경로는 Glow + Flow 연출,
/// 그 외 경로는 흐림(dim) 처리하여 시각적으로 구분한다.
/// </summary>
public class StageLineConnector : MonoBehaviour
{
    // ── Constants ──
    private static readonly int PropDashCount    = Shader.PropertyToID("_DashCount");
    private static readonly int PropDashRatio    = Shader.PropertyToID("_DashRatio");
    private static readonly int PropScrollSpeed  = Shader.PropertyToID("_ScrollSpeed");
    private static readonly int PropGlowColor    = Shader.PropertyToID("_GlowColor");
    private static readonly int PropGlowWidth    = Shader.PropertyToID("_GlowWidth");
    private static readonly int PropGlowIntensity= Shader.PropertyToID("_GlowIntensity");
    private static readonly int PropGlowPulseSpeed = Shader.PropertyToID("_GlowPulseSpeed");
    private static readonly int PropColor        = Shader.PropertyToID("_Color");

    // ── SerializeField ──
    [Header("공통")]
    [SerializeField] private float lineThickness = 6f;
    [SerializeField] private Sprite lineSprite;
    [SerializeField] private bool renderBehindNodes = true;

    [Header("활성 경로 (갈 수 있는 노드)")]
    [SerializeField] private Color activeColor = new Color(0.9f, 0.75f, 0.4f, 1f);
    [SerializeField] private float activeDashCount = 8f;
    [SerializeField] private float activeDashRatio = 0.55f;
    [SerializeField] private float activeScrollSpeed = 1.5f;
    [SerializeField] private Color activeGlowColor = new Color(1f, 0.85f, 0.5f, 0.8f);
    [SerializeField] private float activeGlowWidth = 0.15f;
    [SerializeField] private float activeGlowIntensity = 1.5f;
    [SerializeField] private float activeGlowPulseSpeed = 2f;

    [Header("비활성 경로 (갈 수 없는 노드)")]
    [SerializeField] private Color inactiveColor = new Color(0.25f, 0.2f, 0.15f, 0.4f);
    [SerializeField] private float inactiveDashCount = 6f;
    [SerializeField] private float inactiveDashRatio = 0.4f;
    [SerializeField] private float inactiveScrollSpeed = 0f;

    // ── Private ──
    private readonly List<LineEntry> _lines = new();
    private Material _activeMat;
    private Material _inactiveMat;

    private struct LineEntry
    {
        public GameObject Go;
        public int FromId;
        public int ToId;
        public Image Img;
    }

    // ── Lifecycle ──

    private bool _built;

    private void Start()
    {
        // StageMapScroller.Awake에서 Rebuild()를 먼저 호출하면 중복 방지
        if (_built) return;
        CreateMaterials();
        BuildLines();
        RefreshLineStates();
        _built = true;
    }

    /// <summary>모든 선을 다시 생성한다. 외부에서 호출 가능.</summary>
    public void Rebuild()
    {
        ClearLines();
        CreateMaterials();
        BuildLines();
        RefreshLineStates();
        _built = true;
    }

    /// <summary>노드 상태가 변경될 때 호출하여 활성/비활성을 갱신한다.</summary>
    public void RefreshLineStates()
    {
        var run = AppBootstrapper.Instance?.CurrentRun;
        var mgr = run?.StagePointManager;

        int currentId = mgr?.CurrentPointId ?? -1;
        var availableNext = new HashSet<int>();

        if (mgr != null && currentId >= 0)
        {
            var nextIds = mgr.GetAvailableNextPoints();
            if (nextIds != null)
            {
                foreach (var id in nextIds)
                    availableNext.Add(id);
            }
        }

        foreach (var entry in _lines)
        {
            bool isActive = IsActiveConnection(entry.FromId, entry.ToId, currentId, availableNext);
            entry.Img.material = isActive ? _activeMat : _inactiveMat;
            entry.Img.color = isActive ? activeColor : inactiveColor;
        }
    }

    // ── Private Methods ──

    private bool IsActiveConnection(int fromId, int toId, int currentId, HashSet<int> availableNext)
    {
        if (currentId < 0) return false;

        // 현재 노드 → 갈 수 있는 다음 노드
        if (fromId == currentId && availableNext.Contains(toId)) return true;
        if (toId == currentId && availableNext.Contains(fromId)) return true;

        return false;
    }

    private void CreateMaterials()
    {
        var shader = Shader.Find("UI/FlowLine");
        if (shader == null)
        {
            Debug.LogWarning("[StageLineConnector] UI/FlowLine 셰이더를 찾을 수 없음.");
            shader = Shader.Find("UI/Default");
        }

        // 활성 Material (Glow + Flow)
        _activeMat = new Material(shader);
        _activeMat.SetColor(PropColor, Color.white);
        _activeMat.SetFloat(PropDashCount, activeDashCount);
        _activeMat.SetFloat(PropDashRatio, activeDashRatio);
        _activeMat.SetFloat(PropScrollSpeed, activeScrollSpeed);
        _activeMat.SetColor(PropGlowColor, activeGlowColor);
        _activeMat.SetFloat(PropGlowWidth, activeGlowWidth);
        _activeMat.SetFloat(PropGlowIntensity, activeGlowIntensity);
        _activeMat.SetFloat(PropGlowPulseSpeed, activeGlowPulseSpeed);

        // 비활성 Material (Dim, 정지)
        _inactiveMat = new Material(shader);
        _inactiveMat.SetColor(PropColor, Color.white);
        _inactiveMat.SetFloat(PropDashCount, inactiveDashCount);
        _inactiveMat.SetFloat(PropDashRatio, inactiveDashRatio);
        _inactiveMat.SetFloat(PropScrollSpeed, inactiveScrollSpeed);
        _inactiveMat.SetColor(PropGlowColor, Color.clear);
        _inactiveMat.SetFloat(PropGlowWidth, 0f);
        _inactiveMat.SetFloat(PropGlowIntensity, 0f);
        _inactiveMat.SetFloat(PropGlowPulseSpeed, 0f);
    }

    private Transform _lineParent;

    private void BuildLines()
    {
        var points = GetComponentsInChildren<StagePointUI>(true);
        if (points.Length == 0) return;

        // 노드의 실제 부모를 선의 부모로 사용 (MapContent가 있으면 MapContent)
        _lineParent = points[0].transform.parent != null ? points[0].transform.parent : transform;

        var pointMap = new Dictionary<int, RectTransform>(points.Length);
        foreach (var p in points)
        {
            var rt = p.GetComponent<RectTransform>();
            if (rt != null && !pointMap.ContainsKey(p.PointId))
                pointMap.Add(p.PointId, rt);
        }

        var created = new HashSet<long>();
        int firstNodeSibling = points[0].transform.GetSiblingIndex();

        foreach (var p in points)
        {
            if (p.NextPointIds == null) continue;

            var fromRT = pointMap.GetValueOrDefault(p.PointId);
            if (fromRT == null) continue;

            foreach (var nextId in p.NextPointIds)
            {
                var toRT = pointMap.GetValueOrDefault(nextId);
                if (toRT == null) continue;

                int a = Mathf.Min(p.PointId, nextId);
                int b = Mathf.Max(p.PointId, nextId);
                long key = (long)a * 100000 + b;
                if (created.Contains(key)) continue;
                created.Add(key);

                CreateLine(fromRT, toRT, p.PointId, nextId, firstNodeSibling);
            }
        }
    }

    [Header("선 여백")]
    [SerializeField] private float lineMargin = 20f;

    private void CreateLine(RectTransform from, RectTransform to, int fromId, int toId, int behindIndex)
    {
        var go = new GameObject($"Line_{fromId}_{toId}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(_lineParent, false);

        if (renderBehindNodes)
            go.transform.SetSiblingIndex(behindIndex);

        var rt = go.GetComponent<RectTransform>();
        var img = go.GetComponent<Image>();

        img.sprite = lineSprite;
        img.raycastTarget = false;

        rt.anchorMin = from.anchorMin;
        rt.anchorMax = from.anchorMax;
        rt.pivot = new Vector2(0f, 0.5f);

        Vector2 fromPos = from.anchoredPosition;
        Vector2 toPos = to.anchoredPosition;

        Vector2 dir = toPos - fromPos;
        float fullDistance = dir.magnitude;

        if (fullDistance < lineMargin * 2f)
        {
            // 노드가 너무 가까우면 선 생략
            Destroy(go);
            return;
        }

        // 양쪽 노드에서 margin만큼 안쪽으로 줄임
        Vector2 dirNorm = dir / fullDistance;
        Vector2 startPos = fromPos + dirNorm * lineMargin;
        float distance = fullDistance - lineMargin * 2f;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        rt.anchoredPosition = startPos;
        rt.sizeDelta = new Vector2(distance, lineThickness);
        rt.localRotation = Quaternion.Euler(0f, 0f, angle);
        rt.localScale = Vector3.one;

        _lines.Add(new LineEntry
        {
            Go = go,
            FromId = fromId,
            ToId = toId,
            Img = img,
        });
    }

    private void ClearLines()
    {
        foreach (var entry in _lines)
        {
            if (entry.Go != null) Destroy(entry.Go);
        }
        _lines.Clear();

        if (_activeMat != null) { Destroy(_activeMat); _activeMat = null; }
        if (_inactiveMat != null) { Destroy(_inactiveMat); _inactiveMat = null; }
    }

    private void OnDestroy()
    {
        ClearLines();
    }
}
