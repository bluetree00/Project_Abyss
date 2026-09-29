using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 기억의 제단 트리 화면(09-29 개편) — 가운데 제단에서 갈래 다섯이 빛실을 타고 자란다.
///
/// <para><b>구성</b> — 깊이 고리(흐린 타원) · 빛실(부모 → 자식 선, 자랄 수 있는 선) · 노드 · 가운데 제단 문양(다음 목표) ·
/// 갈래 이름표. 자리는 <see cref="MemoryAltarLayout"/>가 계산하고 화면 크기에 맞춰 줄인다(끌기 · 휠로 옮기고 키운다).</para>
///
/// <para><b>연출</b> — 열 때 제단 문양이 그려지고 갈래 순서대로 산 길이 자란다 · 해금은 각인 → 점화 → 빛실이 자라며 다음 노드가 돋는다 ·
/// 쉬는 동안 금실을 타고 빛 알갱이가 흐른다. 순간 등장 · 흔들림 · 원색은 쓰지 않는다. 시간은 멈춘 팝업 안에서도 흐른다(unscaled).</para>
/// </summary>
public sealed class AltarTreeView : MonoBehaviour, IBeginDragHandler, IDragHandler, IScrollHandler
{
    // ── Constants ────────────────────────────────────────
    private const float LabelAllowance = 34f;
    private const float MaxFitScale    = 1.05f;
    private const float MinZoom = 0.8f, MaxZoom = 1.8f;
    private const float MoteInterval   = 3f;
    private const float MoteSpeed      = 170f;
    private const int   MotePool       = 8;
    private static readonly Color RingColor  = new(1f, 1f, 1f, 0.06f);
    private static readonly Color RingInk    = new(0.62f, 0.60f, 0.66f, 0.55f);
    private static readonly Color LineBought = new(0.91f, 0.73f, 0.33f, 0.95f);
    private static readonly Color LineOpen   = new(0.91f, 0.73f, 0.33f, 0.50f);
    private static readonly Color LineNext   = new(0.55f, 0.52f, 0.62f, 0.32f);
    private static readonly Color LineHidden = new(0.55f, 0.52f, 0.62f, 0.10f);

    // ── Static ───────────────────────────────────────────
    private static bool s_openedThisSession;

    /// <summary>갈래 색 — 채도를 낮춘 색(원색 금지). 다 연 갈래는 금으로 읽힌다(성소 수정과 같은 말).</summary>
    public static Color BranchColor(AltarBranch b) => b switch
    {
        AltarBranch.Rune     => new Color(0.66f, 0.56f, 0.90f),
        AltarBranch.Covenant => new Color(0.44f, 0.78f, 0.72f),
        AltarBranch.Gear     => new Color(0.90f, 0.65f, 0.38f),
        AltarBranch.Ranged   => new Color(0.52f, 0.71f, 0.92f),
        _                    => new Color(0.86f, 0.52f, 0.54f),
    };

    // ── Private ──────────────────────────────────────────
    private struct Edge
    {
        public UIPolylineGraphic Line;
        public string Parent;   // null = 가운데 제단
        public string Child;
    }

    private struct Mote
    {
        public bool  Active;
        public int   Edge;
        public float T;
        public float Alpha;
    }

    private RectTransform _rt, _content, _ringsRoot, _linesRoot, _nodesRoot, _fxRoot, _labelsRoot;
    private readonly Dictionary<string, AltarTreeNodeView> _nodes = new(40);
    private Dictionary<string, AltarNodePlacement> _place;
    private readonly List<Edge> _edges = new(48);
    private readonly Dictionary<string, List<int>> _edgesFrom = new(40);   // 부모 id("" = 가운데) → 선 번호
    private readonly Dictionary<string, int> _edgeTo = new(40);            // 자식 id → 배치 부모 선 번호
    private readonly List<UIPolylineGraphic> _rings = new(6);
    private readonly List<TMP_Text> _ringLabels = new(6);
    private readonly Dictionary<AltarBranch, TMP_Text> _branchLabels = new(5);
    private readonly Image[] _moteImgs = new Image[MotePool];
    private readonly Mote[]  _motes    = new Mote[MotePool];
    private readonly float[] _moteTimer = new float[5];
    private IReadOnlyDictionary<string, AltarNodeViewModel> _models;
    private Image    _centerDisc, _centerTrack, _centerFill, _centerGlyph;
    private TMP_Text _centerTitle, _centerName, _centerStatus;
    private Rect     _bounds;
    private float    _fit = 1f, _zoom = 1f;
    private Vector2  _pan;
    private AltarTreeNodeView _focused;
    private string   _hoverId;
    private int      _visibleDepth;
    private bool     _playing;

    // ── Properties ───────────────────────────────────────
    public MemoryAltarNode FocusedNode => _focused != null ? _focused.Node : null;

    public event Action<MemoryAltarNode> NodeFocused;
    public event Action<MemoryAltarNode> NodeSubmitted;

    // ── Lifecycle ────────────────────────────────────────
    private void Update()
    {
        if (_centerGlyph != null) _centerGlyph.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -Time.unscaledTime * 4f);
        if (!_playing) TickMotes(Time.unscaledDeltaTime);
    }

    private void OnRectTransformDimensionsChange()
    {
        if (_place != null) FitToArea();
    }

    // ── Public Methods ───────────────────────────────────

    /// <summary>부모(해금 탭 영역) 안에 트리 영역을 만든다. <paramref name="rightInset"/>만큼 오른쪽을 비운다(상세 패널 자리).</summary>
    public static AltarTreeView Create(RectTransform parent, float rightInset)
    {
        var go = new GameObject("AltarTree", typeof(RectTransform), typeof(CanvasRenderer));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = new Vector2(-rightInset, 0f);
        // 끌기 · 휠을 받는 투명 판(노드 버튼은 이 위에 있다)
        var catcher = go.AddComponent<Image>();
        catcher.color = new Color(0f, 0f, 0f, 0f);
        go.AddComponent<RectMask2D>();
        var v = go.AddComponent<AltarTreeView>();
        v.Build();
        return v;
    }

    /// <summary>화면 값을 입힌다 — 노드 색 · 선 굵기 · 가운데 목표 · 갈래 진척 · 고리.</summary>
    public void Apply(IReadOnlyDictionary<string, AltarNodeViewModel> models, in AltarCenterModel center, bool instant)
    {
        _models = models;
        foreach (var kv in _nodes)
            if (models.TryGetValue(kv.Key, out var m)) kv.Value.Apply(m, instant);

        PlaceLabels();
        StyleEdges();
        ApplyCenter(center);
        ApplyBranchLabels();
        _visibleDepth = VisibleDepth();
        ApplyRings(_visibleDepth, animate: false);
    }

    /// <summary>노드에 초점을 둔다. <paramref name="select"/>면 이벤트 시스템 선택도 옮긴다(게임패드).</summary>
    public void Focus(MemoryAltarNode node, bool select)
    {
        if (node == null || !_nodes.TryGetValue(node.Id, out var view)) return;
        if (_focused != null && _focused != view) _focused.SetFocused(false);
        _focused = view;
        view.SetFocused(true);
        if (select && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != view.gameObject)
            EventSystem.current.SetSelectedGameObject(view.gameObject);
        NodeFocused?.Invoke(node);
    }

    /// <summary>
    /// 여는 연출. 처음 열 때(세션당 한 번) 0.7초 — 제단 문양이 그려지고 갈래 순서대로 산 길이 자란다.
    /// 그 뒤엔 0.25초 스밈만.
    /// </summary>
    public async UniTask PlayOpenAsync(CancellationToken ct)
    {
        bool first = !s_openedThisSession;
        s_openedThisSession = true;
        _playing = true;
        try
        {
            if (!first)
            {
                foreach (var v in _nodes.Values) v.SetAlpha(0f);
                await Tween(0.25f, k => { foreach (var v in _nodes.Values) v.SetAlpha(k); }, ct);
                return;
            }

            // 0 — 모두 걷고 시작
            foreach (var v in _nodes.Values) v.SetAlpha(0f);
            foreach (var e in _edges) e.Line.Progress = 0f;
            if (_centerGlyph != null) { _centerGlyph.type = Image.Type.Filled; _centerGlyph.fillMethod = Image.FillMethod.Radial360; _centerGlyph.fillAmount = 0f; }

            // 1 — 제단 문양
            await Tween(0.2f, k => { if (_centerGlyph != null) _centerGlyph.fillAmount = k; }, ct);

            // 2 — 산 길이 깊이 순서로 자란다(갈래마다 0.05초 어긋나게)
            int maxDepth = 1;
            foreach (var p in _place.Values) maxDepth = Mathf.Max(maxDepth, p.Depth);
            for (int d = 1; d <= maxDepth; d++)
            {
                int depth = d;
                bool any = false;
                foreach (var e in _edges) if (IsBought(e.Child) && _place[e.Child].Depth == depth) { any = true; break; }
                if (!any) break;
                await Tween(0.12f, k =>
                {
                    foreach (var e in _edges)
                    {
                        if (!IsBought(e.Child) || _place[e.Child].Depth != depth) continue;
                        float stagger = BranchIndex(e.Child) * 0.05f / 0.12f;
                        float kk = Mathf.Clamp01(k * (1f + stagger) - stagger);
                        e.Line.Progress = kk;
                        _nodes[e.Child].SetAlpha(kk);
                    }
                }, ct);
            }

            // 3 — 열린 · 한 칸 앞 · 가려진 노드가 스며든다
            await Tween(0.2f, k =>
            {
                foreach (var e in _edges) if (!IsBought(e.Child)) e.Line.Progress = k;
                foreach (var kv in _nodes) if (!IsBought(kv.Key)) kv.Value.SetAlpha(k);
            }, ct);
        }
        finally
        {
            _playing = false;
            foreach (var v in _nodes.Values) if (v != null) v.SetAlpha(1f);
            foreach (var e in _edges) if (e.Line != null) e.Line.Progress = 1f;
            if (_centerGlyph != null) _centerGlyph.fillAmount = 1f;
        }
    }

    /// <summary>
    /// 해금 연출 — <paramref name="before"/>(해금 전 화면 상태)와 지금 상태를 비교해 새로 열린 것만 움직인다.
    /// 각인 → 점화(열쇠면 파문 두 겹 + 갈래를 훑는 빛) → 자식 쪽으로 빛실이 자라며 빛 알갱이가 앞서 가고 → 자식이 돋는다 →
    /// 그 너머 한 칸이 스며든다 → 새 깊이면 고리가 그려진다.
    /// </summary>
    public async UniTask PlayUnlockAsync(MemoryAltarNode node, IReadOnlyDictionary<string, AltarNodeVisual> before, CancellationToken ct)
    {
        if (node == null || !_nodes.TryGetValue(node.Id, out var view)) return;
        _playing = true;
        var bloom  = new List<string>(4);
        var reveal = new List<string>(6);
        foreach (var kv in _models)
        {
            before.TryGetValue(kv.Key, out var old);
            var now = kv.Value.Visual;
            if (kv.Key == node.Id || old == now) continue;
            if ((old == AltarNodeVisual.Hidden || old == AltarNodeVisual.Next) &&
                (now == AltarNodeVisual.Affordable || now == AltarNodeVisual.Open || now == AltarNodeVisual.OpenLocked))
                bloom.Add(kv.Key);
            else if (old == AltarNodeVisual.Hidden && now == AltarNodeVisual.Next)
                reveal.Add(kv.Key);
        }
        int oldDepth = _visibleDepth;

        try
        {
            // 준비 — 새로 열린 것은 걷어 두고 연출이 보인다
            foreach (var id in bloom)  { _nodes[id].SetAlpha(0f); SetEdgeProgress(id, 0f); }
            foreach (var id in reveal) { _nodes[id].SetAlpha(0f); SetEdgeProgress(id, 0f); }

            bool keystone = node.Size == AltarNodeSize.Keystone;
            ShopUIStyle.PlaySfx(keystone ? "altar_keystone" : "altar_engrave");
            if (keystone) SweepBranchAsync(node.Branch, ct).Forget();
            before.TryGetValue(node.Id, out var fromVisual);
            await view.PlayEngraveIgniteAsync(fromVisual, keystone, _fxRoot, ct);

            if (bloom.Count > 0)
            {
                ShopUIStyle.PlaySfx("altar_grow");
                var head = SpawnHeadMotes(bloom);
                await Tween(0.35f, k =>
                {
                    for (int i = 0; i < bloom.Count; i++)
                    {
                        SetEdgeProgress(bloom[i], k);
                        if (head[i] != null && _edgeTo.TryGetValue(bloom[i], out var ei))
                            head[i].rectTransform.anchoredPosition = _edges[ei].Line.HeadPoint;
                    }
                }, ct);
                foreach (var h in head) if (h != null) Destroy(h.gameObject);

                var tasks = new List<UniTask>(bloom.Count);
                foreach (var id in bloom) tasks.Add(_nodes[id].PlayBloomAsync(ct));
                await UniTask.WhenAll(tasks);
            }

            if (reveal.Count > 0)
                await Tween(0.25f, k => { foreach (var id in reveal) { _nodes[id].SetAlpha(k); SetEdgeProgress(id, k); } }, ct);

            int depth = VisibleDepth();
            if (depth > oldDepth)
            {
                _visibleDepth = depth;
                await RevealRingAsync(depth, ct);
            }
        }
        finally
        {
            _playing = false;
            foreach (var id in bloom)  if (_nodes.TryGetValue(id, out var v) && v != null) { v.SetAlpha(1f); SetEdgeProgress(id, 1f); }
            foreach (var id in reveal) if (_nodes.TryGetValue(id, out var v) && v != null) { v.SetAlpha(1f); SetEdgeProgress(id, 1f); }
            if (this != null) { StyleEdges(); ApplyRings(VisibleDepth(), animate: false); }
        }
    }

    /// <summary>지금 화면에 드러난 가장 깊은 고리(가려진 점 제외).</summary>
    public int VisibleDepth()
    {
        int d = 1;
        if (_models == null || _place == null) return d;
        foreach (var kv in _models)
            if (kv.Value.Visual != AltarNodeVisual.Hidden && _place.TryGetValue(kv.Key, out var p)) d = Mathf.Max(d, p.Depth);
        return d;
    }

    // ── Private Methods ──────────────────────────────────

    private void Build()
    {
        _rt = (RectTransform)transform;
        _content    = Layer("Content", _rt);
        _ringsRoot  = Layer("Rings", _content);
        _linesRoot  = Layer("Lines", _content);
        _fxRoot     = Layer("Fx", _content);
        _nodesRoot  = Layer("Nodes", _content);
        _labelsRoot = Layer("Labels", _content);

        var nodes = MemoryAltarCatalog.All;
        _place = MemoryAltarLayout.Compute(nodes);

        // 깊이 고리
        int maxDepth = 1;
        foreach (var p in _place.Values) maxDepth = Mathf.Max(maxDepth, p.Depth);
        for (int d = 1; d <= maxDepth; d++)
        {
            var ring = Line($"Ring{d}", _ringsRoot, 1.5f, RingColor);
            var radii = MemoryAltarLayout.RingRadii(d);
            ring.SetEllipse(radii.x, radii.y);
            _rings.Add(ring);
            var lab = Text($"RingLabel{d}", _ringsRoot, 16f, FontStyles.Normal, TextAlignmentOptions.Center, RingInk);
            lab.text = MemoryAltarCatalog.RingLabel(d);
            float a = 126f * Mathf.Deg2Rad;
            lab.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(a) * radii.x, Mathf.Sin(a) * radii.y);
            _ringLabels.Add(lab);
        }

        // 빛실 — 가운데 → 뿌리, 부모 → 자식
        foreach (var n in nodes)
        {
            if (!_place.TryGetValue(n.Id, out var p)) continue;
            if (n.IsRoot) AddEdge(null, n.Id, Vector2.zero, p.Position);
            foreach (var par in n.Parents)
                if (_place.TryGetValue(par, out var pp)) AddEdge(par, n.Id, pp.Position, p.Position);
        }

        // 노드
        foreach (var n in nodes)
        {
            if (!_place.TryGetValue(n.Id, out var p)) continue;
            var v = AltarTreeNodeView.Create(_nodesRoot, n, p.Diameter, BranchColor(n.Branch));
            v.Rect.anchoredPosition = p.Position;
            v.Label.transform.SetParent(_labelsRoot, true);   // 이름표는 노드 위 층 — 선 · 이웃 노드에 덮이지 않게
            v.Focused      += OnNodeFocused;
            v.Clicked      += OnNodeClicked;
            v.HoverChanged += OnNodeHover;
            _nodes[n.Id] = v;
        }

        BuildCenter();
        BuildBranchLabels(maxDepth);
        BuildMotes();
        ComputeBounds();
        FitToArea();
    }

    private void AddEdge(string parent, string child, Vector2 a, Vector2 b)
    {
        var line = Line($"Edge_{parent ?? "center"}_{child}", _linesRoot, 2f, LineHidden);
        line.SetSegment(a, b);
        int idx = _edges.Count;
        _edges.Add(new Edge { Line = line, Parent = parent, Child = child });
        string key = parent ?? "";
        if (!_edgesFrom.TryGetValue(key, out var list)) _edgesFrom[key] = list = new List<int>(3);
        list.Add(idx);
        if (!_edgeTo.ContainsKey(child)) _edgeTo[child] = idx;
    }

    private void BuildCenter()
    {
        const float D = 156f;
        _centerGlyph = Img("CenterGlyph", _content, AltarTreeNodeView.GlyphRing(), D * 1.32f, new Color(0.91f, 0.73f, 0.33f, 0.30f));
        _centerDisc  = Img("CenterDisc",  _content, UIProceduralSprites.Circle(128), D, new Color(0.07f, 0.06f, 0.10f, 0.96f));
        _centerTrack = Img("CenterTrack", _content, UIProceduralSprites.Ring(0.07f, 128), D, new Color(1f, 1f, 1f, 0.10f));
        _centerFill  = Img("CenterFill",  _content, UIProceduralSprites.Ring(0.07f, 128), D, UITheme.Gold);
        _centerFill.type = Image.Type.Filled; _centerFill.fillMethod = Image.FillMethod.Radial360; _centerFill.fillOrigin = 2;
        _centerTitle  = Text("CenterTitle",  _content, 16f, FontStyles.Normal, TextAlignmentOptions.Center, UITheme.Mute);
        _centerName   = Text("CenterName",   _content, 18f, FontStyles.Bold,   TextAlignmentOptions.Center, UITheme.Ink);
        _centerStatus = Text("CenterStatus", _content, 16f, FontStyles.Bold,   TextAlignmentOptions.Center, UITheme.Gold);
        _centerTitle.rectTransform.anchoredPosition  = new Vector2(0f, 36f);
        _centerName.rectTransform.anchoredPosition   = new Vector2(0f, 4f);
        _centerName.rectTransform.sizeDelta          = new Vector2(D - 30f, 48f);
        _centerName.textWrappingMode                 = TextWrappingModes.Normal;
        _centerName.enableAutoSizing = true; _centerName.fontSizeMax = 18f; _centerName.fontSizeMin = 16f;
        _centerStatus.rectTransform.anchoredPosition = new Vector2(0f, -36f);
        // 제단 문양은 선 위 · 노드 아래 — 뿌리 선이 문양 가장자리에서 시작해 보인다
        foreach (var g in new Graphic[] { _centerGlyph, _centerDisc, _centerTrack, _centerFill, _centerTitle, _centerName, _centerStatus })
            g.transform.SetSiblingIndex(_nodesRoot.GetSiblingIndex());
    }

    private void BuildBranchLabels(int maxDepth)
    {
        foreach (var b in MemoryAltarLayout.BranchOrder)
        {
            int deepest = 1;
            foreach (var kv in _place)
            {
                var n = MemoryAltarCatalog.Get(kv.Key);
                if (n != null && n.Branch == b) deepest = Mathf.Max(deepest, kv.Value.Depth);
            }
            var t = Text($"Branch_{b}", _labelsRoot, 20f, FontStyles.Bold, TextAlignmentOptions.Center, BranchColor(b));
            t.rectTransform.sizeDelta = new Vector2(240f, 50f);
            t.rectTransform.anchoredPosition = CaptionPosition(b, deepest);
            _branchLabels[b] = t;
        }
    }

    /// <summary>
    /// 갈래 제목 자리 — 가장 깊은 노드 바깥. 그 자리에 노드나 노드 이름표 자리가 있으면 가운데에서 먼 쪽(가로)으로 비킨다
    /// (09-29 실측: 원거리 제목이 「작렬의 탄두」 이름표와 겹쳤다). 세로 갈래(위)는 바깥으로 올린다.
    /// </summary>
    private Vector2 CaptionPosition(AltarBranch b, int deepest)
    {
        Vector2 pos = MemoryAltarLayout.AlongBranch(b, MemoryAltarLayout.CenterRadius + (deepest + 0.85f) * MemoryAltarLayout.RingStep);
        Vector2 dir = MemoryAltarLayout.AlongBranch(b, 1f).normalized;
        Vector2 step = Mathf.Abs(dir.x) > 0.3f ? new Vector2(Mathf.Sign(dir.x) * 14f, 0f) : dir * 14f;
        for (int k = 0; k < 12 && CaptionHitsNode(pos); k++) pos += step;
        return pos;
    }

    private bool CaptionHitsNode(Vector2 pos)
    {
        var cap = new Rect(pos.x - 78f, pos.y - 25f, 156f, 50f);
        foreach (var p in _place.Values)
        {
            float h = p.Diameter * 0.5f + 44f;   // 이름표가 위 · 아래 어느 쪽에 붙어도
            var zone = new Rect(p.Position.x - 72f, p.Position.y - h, 144f, h * 2f);
            if (cap.Overlaps(zone)) return true;
        }
        return false;
    }

    private void BuildMotes()
    {
        for (int i = 0; i < MotePool; i++)
        {
            var img = Img($"Mote{i}", _fxRoot, UI_RuneSelectPopup.SoftDot, 11f, new Color(1f, 0.88f, 0.55f, 0f));
            _moteImgs[i] = img;
        }
        for (int b = 0; b < _moteTimer.Length; b++) _moteTimer[b] = UnityEngine.Random.Range(0.5f, MoteInterval);
    }

    private void ApplyCenter(in AltarCenterModel c)
    {
        _centerTitle.text  = c.Title;
        _centerName.text   = c.GoalName;
        _centerStatus.text = c.Status;
        _centerStatus.color = c.Ready ? UITheme.Gold : UITheme.Mute;
        _centerFill.fillAmount = c.Fill;
        _centerFill.color = c.Ready ? UITheme.Gold : new Color(0.91f, 0.73f, 0.33f, 0.65f);
    }

    private void ApplyBranchLabels()
    {
        foreach (var kv in _branchLabels)
        {
            var (done, total) = MemoryAltarService.BranchProgress(kv.Key);
            bool complete = total > 0 && done >= total;
            string name = MemoryAltarCatalog.BranchLabel(kv.Key);
            string count = complete ? "<color=#E8BA54>◆</color>" : $"<color=#9A93A6>{done}/{total}</color>";
            kv.Value.text = $"{name}  {count}\n<size=16><color=#9A93A6>{MemoryAltarCatalog.BranchQuestion(kv.Key)}</color></size>";
            kv.Value.color = complete ? UITheme.Gold : BranchColor(kv.Key);
        }
    }

    private void ApplyRings(int depth, bool animate)
    {
        for (int i = 0; i < _rings.Count; i++)
        {
            bool on = i + 1 <= depth;
            _rings[i].gameObject.SetActive(on);
            _ringLabels[i].gameObject.SetActive(on);
            if (on && !animate) _rings[i].Progress = 1f;
        }
    }

    private async UniTask RevealRingAsync(int depth, CancellationToken ct)
    {
        int i = depth - 1;
        if (i < 0 || i >= _rings.Count) return;
        var ring = _rings[i]; var lab = _ringLabels[i];
        ring.gameObject.SetActive(true); lab.gameObject.SetActive(true);
        ring.Progress = 0f; var c = lab.color; lab.color = new Color(c.r, c.g, c.b, 0f);
        await Tween(0.6f, k => { ring.Progress = k; lab.color = new Color(c.r, c.g, c.b, c.a * k); }, ct);
    }

    /// <summary>선 굵기 · 색 — 자식 상태로 정한다. 마우스를 올린 노드까지의 길은 한 단계 밝게.</summary>
    private void StyleEdges()
    {
        if (_models == null) return;
        HashSet<string> path = null;
        if (!string.IsNullOrEmpty(_hoverId))
        {
            path = new HashSet<string>();
            var n = MemoryAltarCatalog.Get(_hoverId);
            while (n != null) { path.Add(n.Id); n = n.Parents.Count > 0 ? MemoryAltarCatalog.Get(n.Parents[0]) : null; }
        }
        foreach (var e in _edges)
        {
            _models.TryGetValue(e.Child, out var cm);
            bool parentBought = e.Parent == null || IsBought(e.Parent);
            Color col; float w;
            switch (cm.Visual)
            {
                case AltarNodeVisual.Bought:     col = parentBought ? LineBought : LineOpen; w = 3f; break;
                case AltarNodeVisual.Affordable:
                case AltarNodeVisual.Open:
                case AltarNodeVisual.OpenLocked: col = LineOpen;   w = 2f;   break;
                case AltarNodeVisual.Next:       col = LineNext;   w = 1.5f; break;
                default:                         col = LineHidden; w = 1.2f; break;
            }
            if (path != null && path.Contains(e.Child)) { col.a = Mathf.Min(1f, col.a + 0.35f); w += 1f; }
            e.Line.color = col;
            e.Line.Width = w;
        }
    }

    /// <summary>
    /// 이름표 자리 — 아래 · 위 · 바깥 옆 중 이웃 노드와 다른 이름표에 덜 겹치는 쪽(오프라인 검사 0건 규칙과 같다).
    /// </summary>
    private void PlaceLabels()
    {
        var placed = new List<Rect>(_nodes.Count);
        foreach (var n in MemoryAltarCatalog.All)
        {
            if (!_nodes.TryGetValue(n.Id, out var v) || !_place.TryGetValue(n.Id, out var p)) continue;
            if (v.Visual == AltarNodeVisual.Hidden) continue;
            var sides = new[] { AltarLabelSide.Below, AltarLabelSide.Above, p.Position.x >= 0f ? AltarLabelSide.Right : AltarLabelSide.Left };
            AltarLabelSide best = sides[0]; int bestHits = int.MaxValue; Rect bestRect = default;
            foreach (var side in sides)
            {
                var r = v.LabelRectFor(side);
                r.position += p.Position;
                int hits = CountHits(r, n.Id, placed);
                if (hits < bestHits) { best = side; bestHits = hits; bestRect = r; if (hits == 0) break; }
            }
            placed.Add(bestRect);
            v.PlaceLabel(best);
            // 이름표는 이름표 층으로 옮겨 두었으니 노드 자리를 따라가게 한다
            var lrt = v.Label.rectTransform;
            lrt.anchoredPosition = p.Position + LabelOffset(v, best);
        }
    }

    private static Vector2 LabelOffset(AltarTreeNodeView v, AltarLabelSide side)
    {
        var r = v.LabelRectFor(side);
        return side switch
        {
            AltarLabelSide.Below => new Vector2(0f, r.yMax),
            AltarLabelSide.Above => new Vector2(0f, r.yMin),
            AltarLabelSide.Left  => new Vector2(r.xMax, 0f),
            _                    => new Vector2(r.xMin, 0f),
        };
    }

    private int CountHits(Rect r, string self, List<Rect> placed)
    {
        int k = 0;
        foreach (var kv in _place)
        {
            if (kv.Key == self) continue;
            if (_nodes.TryGetValue(kv.Key, out var v) && v.Visual == AltarNodeVisual.Hidden) continue;
            var c = kv.Value.Position; float rad = kv.Value.Diameter * 0.5f + 2f;
            float nx = Mathf.Clamp(c.x, r.xMin, r.xMax), ny = Mathf.Clamp(c.y, r.yMin, r.yMax);
            if ((new Vector2(nx, ny) - c).sqrMagnitude < rad * rad) k++;
        }
        foreach (var o in placed) if (o.Overlaps(r)) k++;
        return k;
    }

    private void ComputeBounds()
    {
        float xMin = -90f, xMax = 90f, yMin = -90f, yMax = 90f;
        foreach (var p in _place.Values)
        {
            float r = p.Diameter * 0.5f + LabelAllowance;
            xMin = Mathf.Min(xMin, p.Position.x - r - 60f); xMax = Mathf.Max(xMax, p.Position.x + r + 60f);
            yMin = Mathf.Min(yMin, p.Position.y - r);       yMax = Mathf.Max(yMax, p.Position.y + r);
        }
        foreach (var t in _branchLabels.Values)
        {
            var c = t.rectTransform.anchoredPosition; var s = t.rectTransform.sizeDelta * 0.5f;
            xMin = Mathf.Min(xMin, c.x - s.x); xMax = Mathf.Max(xMax, c.x + s.x);
            yMin = Mathf.Min(yMin, c.y - s.y); yMax = Mathf.Max(yMax, c.y + s.y);
        }
        _bounds = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private void FitToArea()
    {
        var size = _rt.rect.size;
        if (size.x < 10f || size.y < 10f) return;
        _fit = Mathf.Min(MaxFitScale, Mathf.Min(size.x / _bounds.width, size.y / _bounds.height));
        ApplyTransform();
    }

    private void ApplyTransform()
    {
        float s = _fit * _zoom;
        _content.localScale = new Vector3(s, s, 1f);
        // 확대했을 때만 옮길 수 있다 — 한 화면에 들어오면 트리가 가운데에 고정된다.
        var size = _rt.rect.size;
        float maxX = Mathf.Max(0f, (_bounds.width * s - size.x) * 0.5f);
        float maxY = Mathf.Max(0f, (_bounds.height * s - size.y) * 0.5f);
        _pan = new Vector2(Mathf.Clamp(_pan.x, -maxX, maxX), Mathf.Clamp(_pan.y, -maxY, maxY));
        _content.anchoredPosition = -_bounds.center * s + _pan;
    }

    private void SetEdgeProgress(string child, float k)
    {
        foreach (var e in _edges) if (e.Child == child) e.Line.Progress = k;
    }

    private Image[] SpawnHeadMotes(List<string> children)
    {
        var arr = new Image[children.Count];
        for (int i = 0; i < children.Count; i++)
            arr[i] = Img("HeadMote", _fxRoot, UI_RuneSelectPopup.SoftDot, 16f, new Color(1f, 0.92f, 0.62f, 0.95f));
        return arr;
    }

    /// <summary>
    /// 열쇠 해금 — 그 갈래의 <b>이미 연 길</b>이 가운데에서 바깥으로 차례로 한 번 밝아진다(0.7초).
    /// 예전엔 부드러운 띠 한 장이 갈래를 따라 올라갔는데, 화면에선 흐린 네모로 읽혔다(09-29 실측).
    /// </summary>
    private async UniTaskVoid SweepBranchAsync(AltarBranch branch, CancellationToken ct)
    {
        var lit = new List<(UIPolylineGraphic line, Color col, float width, float peak)>();
        foreach (var e in _edges)
        {
            var child = MemoryAltarCatalog.Get(e.Child);
            if (child == null || child.Branch != branch || !IsBought(e.Child) || e.Line == null) continue;
            lit.Add((e.Line, e.Line.color, e.Line.Width, 0.08f + 0.1f * (_place[e.Child].Depth - 1)));
        }
        if (lit.Count == 0) return;
        var hot = new Color(1f, 0.93f, 0.72f, 1f);
        try
        {
            await Tween(0.7f, k =>
            {
                float t = k * 0.7f;
                foreach (var (line, col, width, peak) in lit)
                {
                    float i = Mathf.Clamp01(1f - Mathf.Abs(t - peak) / 0.14f);
                    i = i * i * (3f - 2f * i);
                    line.color = Color.Lerp(col, hot, i);
                    line.Width = width + 2f * i;
                }
            }, ct);
        }
        catch (OperationCanceledException) { }
        finally { StyleEdges(); }
    }

    /// <summary>쉬는 동안 — 갈래마다 3초에 한 번, 가운데에서 산 길을 따라 빛 알갱이가 바깥으로 흐른다(할당 없음).</summary>
    private void TickMotes(float dt)
    {
        if (_models == null) return;
        for (int b = 0; b < _moteTimer.Length; b++)
        {
            _moteTimer[b] -= dt;
            if (_moteTimer[b] > 0f) continue;
            _moteTimer[b] = MoteInterval + UnityEngine.Random.Range(-0.6f, 0.6f);
            int root = FirstBoughtRootEdge(MemoryAltarLayout.BranchOrder[b]);
            if (root < 0) continue;
            for (int i = 0; i < MotePool; i++)
            {
                if (_motes[i].Active) continue;
                _motes[i] = new Mote { Active = true, Edge = root, T = 0f, Alpha = 0f };
                break;
            }
        }
        for (int i = 0; i < MotePool; i++)
        {
            ref var m = ref _motes[i];
            var img = _moteImgs[i];
            if (!m.Active) { if (img.color.a > 0f) img.color = new Color(1f, 0.88f, 0.55f, 0f); continue; }
            var line = _edges[m.Edge].Line;
            float len = Mathf.Max(1f, Vector2.Distance(line.PointAt(0f), line.PointAt(1f)));
            m.T += dt * MoteSpeed / len;
            m.Alpha = Mathf.Min(0.8f, m.Alpha + dt * 3f);
            if (m.T >= 1f)
            {
                int next = NextBoughtEdge(_edges[m.Edge].Child);
                if (next < 0) { m.Active = false; continue; }
                m.Edge = next; m.T = 0f;
                line = _edges[m.Edge].Line;
            }
            img.rectTransform.anchoredPosition = line.PointAt(m.T);
            img.color = new Color(1f, 0.88f, 0.55f, m.Alpha);
        }
    }

    private int FirstBoughtRootEdge(AltarBranch branch)
    {
        if (!_edgesFrom.TryGetValue("", out var list)) return -1;
        int pick = -1, seen = 0;
        foreach (var i in list)
        {
            var n = MemoryAltarCatalog.Get(_edges[i].Child);
            if (n == null || n.Branch != branch || !IsBought(n.Id)) continue;
            seen++;
            if (UnityEngine.Random.Range(0, seen) == 0) pick = i;   // 산 뿌리 중 하나를 고르게
        }
        return pick;
    }

    private int NextBoughtEdge(string from)
    {
        if (!_edgesFrom.TryGetValue(from, out var list)) return -1;
        int pick = -1, seen = 0;
        foreach (var i in list)
        {
            if (!IsBought(_edges[i].Child)) continue;
            seen++;
            if (UnityEngine.Random.Range(0, seen) == 0) pick = i;
        }
        return pick;
    }

    private bool IsBought(string id) =>
        id != null && _models != null && _models.TryGetValue(id, out var m) && m.Visual == AltarNodeVisual.Bought;

    private static int BranchIndex(string id)
    {
        var n = MemoryAltarCatalog.Get(id);
        return n != null ? Mathf.Max(0, Array.IndexOf(MemoryAltarLayout.BranchOrder, n.Branch)) : 0;
    }

    private static async UniTask Tween(float dur, Action<float> step, CancellationToken ct)
    {
        for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
        {
            float x = Mathf.Clamp01(t / dur);
            step(1f - (1f - x) * (1f - x) * (1f - x));
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        step(1f);
    }

    private static RectTransform Layer(string name, RectTransform parent)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.zero;
        return rt;
    }

    private static UIPolylineGraphic Line(string name, RectTransform parent, float width, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var l = go.AddComponent<UIPolylineGraphic>();
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.sizeDelta = Vector2.zero;
        l.Width = width; l.color = color;
        return l;
    }

    private static Image Img(string name, RectTransform parent, Sprite sprite, float size, Color color)
    {
        var img = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
        img.transform.SetParent(parent, false);
        img.sprite = sprite; img.color = color; img.raycastTarget = false;
        img.rectTransform.sizeDelta = new Vector2(size, size);
        return img;
    }

    private static TMP_Text Text(string name, RectTransform parent, float size, FontStyles style, TextAlignmentOptions align, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = size; t.fontStyle = style; t.alignment = align; t.color = color;
        t.raycastTarget = false; t.richText = true;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.rectTransform.sizeDelta = new Vector2(200f, 24f);
        TMPOutlineHelper.ApplySoftShadow(t);
        return t;
    }

    // ── Event Handlers ───────────────────────────────────

    private void OnNodeFocused(AltarTreeNodeView v)
    {
        if (_focused == v) return;
        Focus(v.Node, select: false);
    }

    /// <summary>처음 누르면 초점, 초점이 있는 노드를 한 번 더 누르면(또는 게임패드 확인) 해금 요청.</summary>
    private void OnNodeClicked(AltarTreeNodeView v)
    {
        if (_focused == v) { NodeSubmitted?.Invoke(v.Node); return; }
        Focus(v.Node, select: true);
    }

    private void OnNodeHover(AltarTreeNodeView v, bool on)
    {
        _hoverId = on ? v.Node.Id : (_hoverId == v.Node.Id ? null : _hoverId);
        StyleEdges();
    }

    public void OnBeginDrag(PointerEventData eventData) { }

    public void OnDrag(PointerEventData eventData)
    {
        var canvas = GetComponentInParent<Canvas>();
        float sf = canvas != null ? canvas.scaleFactor : 1f;
        _pan += eventData.delta / Mathf.Max(0.01f, sf);
        ApplyTransform();
    }

    public void OnScroll(PointerEventData eventData)
    {
        float step = eventData.scrollDelta.y > 0f ? 1.1f : eventData.scrollDelta.y < 0f ? 1f / 1.1f : 1f;
        _zoom = Mathf.Clamp(_zoom * step, MinZoom, MaxZoom);
        ApplyTransform();
    }
}
