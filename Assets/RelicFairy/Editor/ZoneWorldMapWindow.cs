#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// CHAPTER_N_ZONE_LAYOUT.csv 의 존을 world_center 좌표 기준으로 배치하고
/// next_zone_indices 연결선을 그리는 월드 오버뷰.
/// 챕터 전환, 줌/패닝, PNG 내보내기 지원.
/// </summary>
public class ZoneWorldMapWindow : EditorWindow
{
    // ── Constants ────────────────────────────────────────────────────────
    private static readonly int[] Chapters  = { 1, 2, 3, 4 };
    private const string CsvDir   = "Assets/RelicFairy/Docs";
    private const float  BlockCell = 1f;
    private const float  ToolbarH  = 22f;
    private const float  LegendH   = 50f;
    private const int    ExportRes = 2048;

    // ── Data ─────────────────────────────────────────────────────────────
    private struct ZoneNode
    {
        public int    index;
        public string label;
        public string category;
        public float  wx, wz;
        public float  hw, hh;
        public int[]  next;
    }

    private readonly List<ZoneNode> _zones = new();

    // 룸 풀(ROOM_POOL.csv) — 카테고리별 방 후보
    private struct PoolRoom
    {
        public string poolKey;
        public string category;
        public int    gw, gh;
        public string gridCsv;
    }
    private readonly List<PoolRoom> _pool = new();

    // 시드 기반 배정 결과 (존 인덱스 → 선택된 풀 방 + 미니맵 텍스처)
    private int[]       _zoneRoom;   // _pool 인덱스 (없으면 -1)
    private Texture2D[] _zoneTex;    // grid_csv 렌더 텍스처

    // ── State ─────────────────────────────────────────────────────────────
    private int     _chapter    = 1;
    private Vector2 _pan        = Vector2.zero;
    private float   _zoom       = 4f;
    private Vector2 _dragStart, _panStart;
    private bool    _dragging;
    private bool    _showLabels = true;
    private bool    _showRooms  = true;  // true=시드로 뽑은 실제 방 그리드, false=카테고리 색 박스
    private int     _seed       = 1;

    // ── Path helpers ──────────────────────────────────────────────────────
    private string CsvPath(int ch) =>
        $"{CsvDir}/CHAPTER_{ch}_ZONE_LAYOUT.csv";

    private string FullPath(int ch) =>
        Path.Combine(Application.dataPath.Replace("Assets", string.Empty), CsvPath(ch));

    // ── Menu ──────────────────────────────────────────────────────────────
    [MenuItem("RelicFairy/Map/Zone World Map")]
    public static void ShowWindow()
    {
        var w = GetWindow<ZoneWorldMapWindow>("Zone World Map");
        w.minSize = new Vector2(700f, 500f);
        w.ReloadAll();
        w.FitView();
    }

    private void OnEnable() { ReloadAll(); FitView(); }
    private void OnDisable() => ClearTex();

    /// <summary>존 레이아웃 + 룸 풀 로드 후 시드 배정까지 한 번에.</summary>
    private void ReloadAll()
    {
        LoadZones();
        LoadPool();
        AssignRooms();
    }

    // ── OnGUI ─────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        DrawToolbar();
        var mapRect    = new Rect(0f, ToolbarH, position.width, position.height - ToolbarH - LegendH);
        var legendRect = new Rect(0f, mapRect.yMax, position.width, LegendH);

        DrawMap(mapRect);
        DrawLegend(legendRect);
        HandleInput(mapRect);
    }

    // ── Toolbar ───────────────────────────────────────────────────────────
    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        // 챕터 버튼
        GUILayout.Label("챕터:", EditorStyles.miniLabel, GUILayout.Width(34f));
        int newChapter = _chapter;
        foreach (int ch in Chapters)
        {
            bool exists = File.Exists(FullPath(ch));
            using (new EditorGUI.DisabledScope(!exists))
            {
                bool on = GUILayout.Toggle(
                    _chapter == ch && exists,
                    $"Ch{ch}",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(32f));
                if (on && exists && ch != _chapter)
                    newChapter = ch;
            }
        }
        if (newChapter != _chapter) { _chapter = newChapter; ReloadAll(); FitView(); }

        GUILayout.Space(8f);
        _showLabels = GUILayout.Toggle(_showLabels, "이름", EditorStyles.toolbarButton, GUILayout.Width(38f));
        _showRooms  = GUILayout.Toggle(_showRooms,  "방",   EditorStyles.toolbarButton, GUILayout.Width(34f));
        GUILayout.Space(6f);

        // 시드: 입력 + 재생성 + 랜덤
        GUILayout.Label("시드", EditorStyles.miniLabel, GUILayout.Width(26f));
        int newSeed = EditorGUILayout.IntField(_seed, EditorStyles.toolbarTextField, GUILayout.Width(72f));
        if (newSeed != _seed) { _seed = newSeed; AssignRooms(); }
        if (GUILayout.Button("재생성", EditorStyles.toolbarButton, GUILayout.Width(48f))) AssignRooms();
        if (GUILayout.Button("🎲 새 시드", EditorStyles.toolbarButton, GUILayout.Width(72f)))
        { _seed = Random.Range(1, int.MaxValue); AssignRooms(); }

        GUILayout.Space(6f);
        if (GUILayout.Button("맞춤", EditorStyles.toolbarButton, GUILayout.Width(40f))) FitView();
        GUILayout.Label($"zoom {_zoom:F1}×  |  존 {_zones.Count}개", EditorStyles.miniLabel, GUILayout.Width(110f));

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("PNG 저장", EditorStyles.toolbarButton, GUILayout.Width(70f))) ExportPng();
        if (GUILayout.Button("↺",        EditorStyles.toolbarButton, GUILayout.Width(26f))) { ReloadAll(); FitView(); }

        EditorGUILayout.EndHorizontal();
    }

    // ── Map ───────────────────────────────────────────────────────────────
    private void DrawMap(Rect mapRect)
    {
        // 배경
        EditorGUI.DrawRect(mapRect, new Color(0.11f, 0.11f, 0.14f));

        if (_zones.Count == 0)
        {
            var s = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
            s.normal.textColor = new Color(0.55f, 0.55f, 0.55f);
            GUI.Label(mapRect, $"CHAPTER_{_chapter}_ZONE_LAYOUT.csv 없음", s);
            return;
        }

        if (Event.current.type != EventType.Repaint) return;

        // ── 연결선: Handles → 절대 좌표 (BeginGroup 밖) ─────────────────
        foreach (var z in _zones)
        {
            if (z.next == null) continue;
            Vector2 fp = W2PAbs(z.wx, z.wz, mapRect);
            foreach (int ni in z.next)
            {
                int idx = _zones.FindIndex(x => x.index == ni);
                if (idx < 0) continue;
                Vector2 tp = W2PAbs(_zones[idx].wx, _zones[idx].wz, mapRect);
                Handles.color = new Color(0.55f, 0.55f, 0.55f, 0.8f);
                Handles.DrawLine(new Vector3(fp.x, fp.y), new Vector3(tp.x, tp.y));
            }
        }

        // ── 존 박스: GUI.BeginGroup으로 mapRect에 클립 ──────────────────
        GUI.BeginGroup(mapRect);
        for (int zi = 0; zi < _zones.Count; zi++)
        {
            var z = _zones[zi];
            bool hasRoom = _zoneRoom != null && zi < _zoneRoom.Length && _zoneRoom[zi] >= 0;
            Texture2D tex = (_showRooms && hasRoom && _zoneTex != null) ? _zoneTex[zi] : null;

            // 박스 크기: 방 표시면 뽑힌 방 크기, 아니면 존 선언 크기 (world_center 기준이라 겹치지 않음)
            float halfW = z.hw, halfH = z.hh;
            if (_showRooms && hasRoom)
            {
                halfW = _pool[_zoneRoom[zi]].gw * BlockCell * 0.5f;
                halfH = _pool[_zoneRoom[zi]].gh * BlockCell * 0.5f;
            }

            Vector2 ctr = W2P(z.wx, z.wz, mapRect);   // 그룹 상대 좌표
            float   pw  = Mathf.Max(4f, halfW * 2f * _zoom);
            float   ph  = Mathf.Max(4f, halfH * 2f * _zoom);
            var     r   = new Rect(ctr.x - pw * 0.5f, ctr.y - ph * 0.5f, pw, ph);

            // 완전히 화면 밖이면 스킵
            if (r.xMax < 0f || r.yMax < 0f || r.x > mapRect.width || r.y > mapRect.height)
                continue;

            if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, false);
            else             EditorGUI.DrawRect(r, CatColor(z.category));

            // 테두리 (카테고리 색 — 방 종류 구분)
            Color bc = CatColor(z.category);
            EditorGUI.DrawRect(new Rect(r.x,         r.y,         r.width, 1f), bc);
            EditorGUI.DrawRect(new Rect(r.x,         r.yMax - 1f, r.width, 1f), bc);
            EditorGUI.DrawRect(new Rect(r.x,         r.y,         1f, r.height), bc);
            EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.y,         1f, r.height), bc);

            if (_showLabels && _zoom >= 1.5f && pw > 12f)
            {
                var style = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.UpperCenter,
                    fontSize  = Mathf.Clamp(Mathf.RoundToInt(_zoom * 4f), 7, 14),
                    wordWrap  = false,
                };
                style.normal.textColor = Color.white;
                string pk  = hasRoom ? _pool[_zoneRoom[zi]].poolKey : z.label;
                string txt = _zoom >= 3f ? $"[{z.index}] {z.category}\n{pk}" : $"{z.index}";
                GUI.Label(new Rect(r.x, r.y + 2f, r.width, r.height), txt, style);
            }
        }
        GUI.EndGroup();
    }

    // ── Legend ────────────────────────────────────────────────────────────
    private void DrawLegend(Rect r)
    {
        EditorGUI.DrawRect(r, new Color(0.17f, 0.17f, 0.2f));
        GUILayout.BeginArea(r);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(8f);
        foreach (var (cat, col) in new (string, Color)[]
        {
            ("Start",    CatColor("Start")),
            ("Normal",   CatColor("Normal")),
            ("Elite",    CatColor("Elite")),
            ("Boss",     CatColor("Boss")),
            ("Shop",     CatColor("Shop")),
            ("Event",    CatColor("Event")),
            ("Corridor", CatColor("Corridor")),
        })
        {
            var sq = GUILayoutUtility.GetRect(12f, 12f, GUILayout.Width(12f), GUILayout.Height(12f));
            sq.y += 4f;
            EditorGUI.DrawRect(sq, col);
            GUILayout.Label(cat, EditorStyles.miniLabel, GUILayout.Width(52f));
        }
        GUILayout.FlexibleSpace();
        GUILayout.Label("스크롤: 줌  |  드래그: 이동", EditorStyles.miniLabel);
        GUILayout.Space(8f);
        EditorGUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    // ── Input ─────────────────────────────────────────────────────────────
    private void HandleInput(Rect mapRect)
    {
        var e = Event.current;
        if (!mapRect.Contains(e.mousePosition)) return;

        if (e.type == EventType.ScrollWheel)
        {
            float delta = -e.delta.y * 0.08f * _zoom;
            _zoom = Mathf.Clamp(_zoom + delta, 0.3f, 40f);
            e.Use(); Repaint();
        }
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            _dragging  = true;
            _dragStart = e.mousePosition;
            _panStart  = _pan;
            e.Use();
        }
        if (e.type == EventType.MouseDrag && _dragging)
        {
            _pan = _panStart + (e.mousePosition - _dragStart);
            e.Use(); Repaint();
        }
        if (e.type == EventType.MouseUp) { _dragging = false; e.Use(); }
    }

    // ── Coordinate helpers ────────────────────────────────────────────────

    // mapRect 상대 좌표 — GUI.BeginGroup 안에서 사용
    private Vector2 W2P(float wx, float wz, Rect mapRect)
    {
        float px = _pan.x + mapRect.width  * 0.5f + wx * _zoom;
        float py = _pan.y + mapRect.height * 0.5f - wz * _zoom;
        return new Vector2(px, py);
    }

    // 절대 윈도우 좌표 — Handles에서 사용
    private Vector2 W2PAbs(float wx, float wz, Rect mapRect)
    {
        Vector2 rel = W2P(wx, wz, mapRect);
        return new Vector2(mapRect.x + rel.x, mapRect.y + rel.y);
    }

    // ── Fit View ──────────────────────────────────────────────────────────
    private void FitView()
    {
        if (_zones.Count == 0) return;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var z in _zones)
        {
            minX = Mathf.Min(minX, z.wx - z.hw); maxX = Mathf.Max(maxX, z.wx + z.hw);
            minZ = Mathf.Min(minZ, z.wz - z.hh); maxZ = Mathf.Max(maxZ, z.wz + z.hh);
        }

        float worldW = maxX - minX;
        float worldH = maxZ - minZ;
        if (worldW < 1f || worldH < 1f) return;

        float viewW = Mathf.Max(100f, position.width);
        float viewH = Mathf.Max(100f, position.height - ToolbarH - LegendH);

        _zoom = Mathf.Min(viewW / worldW, viewH / worldH) * 0.85f;
        _pan  = new Vector2(-(minX + maxX) * 0.5f * _zoom,
                             (minZ + maxZ) * 0.5f * _zoom);
        Repaint();
    }

    // ── PNG Export ────────────────────────────────────────────────────────
    private void ExportPng()
    {
        if (_zones.Count == 0) return;

        string path = EditorUtility.SaveFilePanel(
            "PNG 저장", Application.dataPath, $"ZoneWorldMap_Ch{_chapter}", "png");
        if (string.IsNullOrEmpty(path)) return;

        int sz  = ExportRes;
        var tex = new Texture2D(sz, sz, TextureFormat.RGBA32, false);

        var pixels = new Color[sz * sz];
        var bg     = new Color(0.11f, 0.11f, 0.14f);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = bg;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var z in _zones)
        {
            minX = Mathf.Min(minX, z.wx - z.hw); maxX = Mathf.Max(maxX, z.wx + z.hw);
            minZ = Mathf.Min(minZ, z.wz - z.hh); maxZ = Mathf.Max(maxZ, z.wz + z.hh);
        }

        float worldW = (maxX - minX) * 1.1f;
        float worldH = (maxZ - minZ) * 1.1f;
        float scale  = Mathf.Min(sz / worldW, sz / worldH) * 0.92f;
        float offX   =  sz * 0.5f - (minX + maxX) * 0.5f * scale;
        float offY   =  sz * 0.5f + (minZ + maxZ) * 0.5f * scale;

        Vector2Int Wp(float wx, float wz) =>
            new(Mathf.RoundToInt(offX + wx * scale),
                Mathf.RoundToInt(offY - wz * scale));

        foreach (var z in _zones)
        {
            if (z.next == null) continue;
            Vector2Int fp = Wp(z.wx, z.wz);
            foreach (int ni in z.next)
            {
                int idx = _zones.FindIndex(x => x.index == ni);
                if (idx < 0) continue;
                DrawLine(pixels, sz, fp, Wp(_zones[idx].wx, _zones[idx].wz),
                    new Color(0.55f, 0.55f, 0.55f, 0.9f));
            }
        }

        foreach (var z in _zones)
        {
            int pw = Mathf.Max(4, Mathf.RoundToInt(z.hw * 2f * scale));
            int ph = Mathf.Max(4, Mathf.RoundToInt(z.hh * 2f * scale));
            Vector2Int ctr = Wp(z.wx, z.wz);
            int x0 = ctr.x - pw / 2, y0 = ctr.y - ph / 2;
            Color fill = CatColor(z.category);
            for (int dy = 0; dy < ph; dy++)
            for (int dx = 0; dx < pw; dx++)
            {
                int px = x0 + dx, py = y0 + dy;
                if (px < 0 || px >= sz || py < 0 || py >= sz) continue;
                bool border = dx < 1 || dy < 1 || dx >= pw - 1 || dy >= ph - 1;
                pixels[py * sz + px] = border ? Color.black : fill;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        DestroyImmediate(tex);
        AssetDatabase.Refresh();
        Debug.Log($"[ZoneWorldMap] 저장 완료: {path}");
    }

    private static void DrawLine(Color[] pixels, int sz, Vector2Int a, Vector2Int b, Color col)
    {
        int dx = Mathf.Abs(b.x - a.x), dy = Mathf.Abs(b.y - a.y);
        int sx = a.x < b.x ? 1 : -1, sy = a.y < b.y ? 1 : -1;
        int err = dx - dy, x = a.x, y = a.y;
        while (true)
        {
            if (x >= 0 && x < sz && y >= 0 && y < sz) pixels[y * sz + x] = col;
            if (x == b.x && y == b.y) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 <  dx) { err += dx; y += sy; }
        }
    }

    // ── Category Color ────────────────────────────────────────────────────
    private static Color CatColor(string cat) => cat switch
    {
        "Start"    => new Color(0.20f, 0.55f, 0.90f),
        "Normal"   => new Color(0.25f, 0.65f, 0.30f),
        "Elite"    => new Color(0.75f, 0.35f, 0.15f),
        "Boss"     => new Color(0.70f, 0.08f, 0.08f),
        "Shop"     => new Color(0.85f, 0.72f, 0.10f),
        "Event"    => new Color(0.55f, 0.20f, 0.80f),
        "Corridor" => new Color(0.35f, 0.35f, 0.35f),
        _          => new Color(0.40f, 0.40f, 0.45f),
    };

    // ── CSV Load ──────────────────────────────────────────────────────────
    private void LoadZones()
    {
        _zones.Clear();
        string full = FullPath(_chapter);

        if (!File.Exists(full))
        {
            Debug.LogWarning($"[ZoneWorldMap] 파일 없음: {CsvPath(_chapter)}");
            Repaint();
            return;
        }

        var lines = File.ReadAllLines(full, Encoding.UTF8);
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var f = SplitCsv(lines[i]);
            if (f.Count < 10) continue;

            float wx = float.TryParse(f[4], out var fx) ? fx : 0f;
            float wz = float.TryParse(f[6], out var fz) ? fz : 0f;
            float gw = int.TryParse(f[7],   out var iw) ? iw : 10;
            float gh = int.TryParse(f[8],   out var ih) ? ih : 10;

            var next = new List<int>();
            foreach (var s in f[9].Split('|'))
                if (int.TryParse(s.Trim(), out var n)) next.Add(n);

            _zones.Add(new ZoneNode
            {
                index    = int.TryParse(f[0], out var idx) ? idx : i - 1,
                category = f[2],
                label    = f[3],
                wx       = wx,
                wz       = wz,
                hw       = gw * BlockCell * 0.5f,
                hh       = gh * BlockCell * 0.5f,
                next     = next.ToArray(),
            });
        }
        Repaint();
    }

    // ── Room Pool + Seed Assignment ───────────────────────────────────────
    private string PoolFullPath(int ch) =>
        Path.Combine(Application.dataPath.Replace("Assets", string.Empty),
                     $"{CsvDir}/CHAPTER_{ch}_ROOM_POOL.csv");

    private void LoadPool()
    {
        _pool.Clear();
        string full = PoolFullPath(_chapter);
        if (!File.Exists(full)) return;

        var lines = File.ReadAllLines(full, Encoding.UTF8);
        if (lines.Length < 2) return;

        var head = SplitCsv(lines[0]);
        var col  = new Dictionary<string, int>();
        for (int i = 0; i < head.Count; i++) col[head[i].Trim()] = i;
        int Idx(string k) => col.TryGetValue(k, out var i) ? i : -1;
        int cPk = Idx("pool_key"), cCat = Idx("category"),
            cW  = Idx("grid_width"), cH = Idx("grid_height"), cG = Idx("grid_csv");

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var f = SplitCsv(lines[i]);
            string Get(int idx) => idx >= 0 && idx < f.Count ? f[idx] : "";
            _pool.Add(new PoolRoom
            {
                poolKey  = Get(cPk),
                category = Get(cCat),
                gw       = int.TryParse(Get(cW), out var w) ? w : 0,
                gh       = int.TryParse(Get(cH), out var h) ? h : 0,
                gridCsv  = Get(cG),
            });
        }
    }

    /// <summary>시드로 각 존에 카테고리 매칭 풀 방을 결정적으로 배정(재현 가능).</summary>
    private void AssignRooms()
    {
        ClearTex();
        int n = _zones.Count;
        _zoneRoom = new int[n];
        _zoneTex  = new Texture2D[n];

        for (int i = 0; i < n; i++)
        {
            _zoneRoom[i] = -1;
            string cat = _zones[i].category;

            var cands = new List<int>();
            for (int p = 0; p < _pool.Count; p++)
                if (string.Equals(_pool[p].category, cat, System.StringComparison.OrdinalIgnoreCase))
                    cands.Add(p);
            if (cands.Count == 0) continue;

            // 시드 + 존 인덱스 → 결정적 픽 (같은 시드면 동일 결과)
            var rng  = new System.Random(unchecked(_seed * 73856093 ^ (_zones[i].index + 1) * 19349663));
            int pick = cands[rng.Next(cands.Count)];
            _zoneRoom[i] = pick;
            _zoneTex[i]  = BuildRoomTex(_pool[pick]);
        }
    }

    private void ClearTex()
    {
        if (_zoneTex == null) return;
        foreach (var t in _zoneTex) if (t != null) DestroyImmediate(t);
        _zoneTex = null;
    }

    // grid_csv → 타일 색 텍스처 (행 ';' / 셀 ',' / 토큰별 색). 행0이 위로 오도록 y 뒤집기.
    private static Texture2D BuildRoomTex(PoolRoom room)
    {
        if (string.IsNullOrEmpty(room.gridCsv)) return null;
        var rows = room.gridCsv.Split(';');
        int h = rows.Length;
        if (h == 0) return null;
        int w = room.gw > 0 ? room.gw : rows[0].Split(',').Length;
        if (w <= 0) return null;

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        for (int y = 0; y < h; y++)
        {
            var cells = rows[y].Split(',');
            for (int x = 0; x < w; x++)
            {
                string tok = x < cells.Length ? cells[x].Trim() : "";
                tex.SetPixel(x, h - 1 - y, TokenColor(tok));
            }
        }
        tex.Apply();
        return tex;
    }

    // grid 토큰 → 색 (현재 토큰 체계: W/F/P/B/DR<w>/Sw/Si/M·m+c/r/e등급)
    private static Color TokenColor(string t)
    {
        if (string.IsNullOrEmpty(t) || t == "F") return new Color(0.22f, 0.22f, 0.26f); // Floor
        if (t == "W")                            return new Color(0.07f, 0.07f, 0.09f); // Wall
        if (t == "P")                            return new Color(0.20f, 0.55f, 0.95f); // Player
        if (t == "B")                            return new Color(0.85f, 0.10f, 0.10f); // Boss
        if (t.StartsWith("DR"))                  return new Color(0.85f, 0.70f, 0.20f); // Door
        if (t == "Sw" || t == "Si" || t == "S")  return new Color(0.90f, 0.80f, 0.25f); // Shop
        if (t.StartsWith("M") || t.StartsWith("m"))
            return t.Contains("e") ? new Color(0.95f, 0.45f, 0.10f)  // Elite 포함 몬스터
                                   : new Color(0.60f, 0.28f, 0.34f); // 일반 몬스터 스포너
        return new Color(0.45f, 0.30f, 0.55f); // 기타/장식
    }

    private static List<string> SplitCsv(string line)
    {
        var  r  = new List<string>();
        var  sb = new StringBuilder();
        bool q  = false;
        foreach (char c in line)
        {
            if (c == '"') { q = !q; continue; }
            if (c == ',' && !q) { r.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(c);
        }
        r.Add(sb.ToString());
        return r;
    }
}
#endif
