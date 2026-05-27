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

    // ── State ─────────────────────────────────────────────────────────────
    private int     _chapter    = 1;
    private Vector2 _pan        = Vector2.zero;
    private float   _zoom       = 4f;
    private Vector2 _dragStart, _panStart;
    private bool    _dragging;
    private bool    _showLabels = true;

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
        w.LoadZones();
        w.FitView();
    }

    private void OnEnable() { LoadZones(); FitView(); }

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
        if (newChapter != _chapter) { _chapter = newChapter; LoadZones(); FitView(); }

        GUILayout.Space(8f);
        _showLabels = GUILayout.Toggle(_showLabels, "이름", EditorStyles.toolbarButton, GUILayout.Width(38f));
        GUILayout.Space(4f);
        if (GUILayout.Button("맞춤", EditorStyles.toolbarButton, GUILayout.Width(40f))) FitView();
        GUILayout.Label($"zoom {_zoom:F1}×  |  존 {_zones.Count}개", EditorStyles.miniLabel, GUILayout.Width(110f));

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("PNG 저장", EditorStyles.toolbarButton, GUILayout.Width(70f))) ExportPng();
        if (GUILayout.Button("↺",        EditorStyles.toolbarButton, GUILayout.Width(26f))) { LoadZones(); FitView(); }

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
        foreach (var z in _zones)
        {
            Vector2 ctr = W2P(z.wx, z.wz, mapRect);   // 그룹 상대 좌표
            float   pw  = Mathf.Max(4f, z.hw * 2f * _zoom);
            float   ph  = Mathf.Max(4f, z.hh * 2f * _zoom);
            var     r   = new Rect(ctr.x - pw * 0.5f, ctr.y - ph * 0.5f, pw, ph);

            // 완전히 화면 밖이면 스킵
            if (r.xMax < 0f || r.yMax < 0f || r.x > mapRect.width || r.y > mapRect.height)
                continue;

            EditorGUI.DrawRect(r, CatColor(z.category));
            // 테두리
            EditorGUI.DrawRect(new Rect(r.x,         r.y,         r.width, 1f), Color.black);
            EditorGUI.DrawRect(new Rect(r.x,         r.yMax - 1f, r.width, 1f), Color.black);
            EditorGUI.DrawRect(new Rect(r.x,         r.y,         1f, r.height), Color.black);
            EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.y,         1f, r.height), Color.black);

            if (_showLabels && _zoom >= 1.5f && pw > 12f)
            {
                var style = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.UpperCenter,
                    fontSize  = Mathf.Clamp(Mathf.RoundToInt(_zoom * 4f), 7, 14),
                    wordWrap  = false,
                };
                style.normal.textColor = Color.white;
                string txt = _zoom >= 3f ? $"[{z.index}]\n{z.label}" : $"{z.index}";
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
