#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// CHAPTER_1_ZONE_LAYOUT.csv 의 grid_csv 를 타일 색상으로 시각화.
/// Tools > Zone Map Viewer 에서 열 수 있음.
/// </summary>
public class ZoneMapViewerWindow : EditorWindow
{
    private const string CsvPath = "Assets/RelicFairy/Docs/CHAPTER_1_ZONE_LAYOUT.csv";

    private struct ZoneInfo
    {
        public int      index;
        public string   label;
        public string   category;
        public string   nextZones;
        public int      width;
        public int      height;
        public string[][] grid;
    }

    private readonly List<ZoneInfo> _zones = new List<ZoneInfo>();
    private int     _selectedZone;
    private Vector2 _scrollPos;
    private float   _cellSize   = 16f;
    private bool    _showLegend = true;

    [MenuItem("Tools/Zone Map Viewer")]
    public static void ShowWindow()
    {
        var win = GetWindow<ZoneMapViewerWindow>("Zone Map Viewer");
        win.minSize = new Vector2(820f, 600f);
        win.LoadZones();
    }

    private void OnEnable() => LoadZones();

    private void OnGUI()
    {
        if (_zones.Count == 0)
        {
            EditorGUILayout.HelpBox($"CSV 로드 실패: {CsvPath}", MessageType.Warning);
            if (GUILayout.Button("다시 로드")) LoadZones();
            return;
        }

        // ── 툴바 ──────────────────────────────────────────────────────
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        GUILayout.Label("존:", GUILayout.Width(26f));
        var names = new string[_zones.Count];
        for (int i = 0; i < _zones.Count; i++)
            names[i] = $"[{_zones[i].index}] {_zones[i].label}  ({_zones[i].category})";
        _selectedZone = EditorGUILayout.Popup(_selectedZone, names, GUILayout.Width(300f));

        GUILayout.Space(8f);
        GUILayout.Label("셀:", GUILayout.Width(22f));
        _cellSize = EditorGUILayout.Slider(_cellSize, 6f, 36f, GUILayout.Width(130f));

        GUILayout.FlexibleSpace();
        _showLegend = GUILayout.Toggle(_showLegend, "범례", EditorStyles.toolbarButton, GUILayout.Width(40f));
        if (GUILayout.Button("↺", EditorStyles.toolbarButton, GUILayout.Width(26f))) LoadZones();

        EditorGUILayout.EndHorizontal();

        if (_selectedZone >= _zones.Count) _selectedZone = 0;
        var zone = _zones[_selectedZone];

        // ── 존 정보 ───────────────────────────────────────────────────
        EditorGUILayout.LabelField(
            $"크기: {zone.width} × {zone.height}    다음 존: {zone.nextZones}",
            EditorStyles.miniLabel);

        // ── 그리드 스크롤 ─────────────────────────────────────────────
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        if (zone.grid != null && zone.grid.Length > 0)
        {
            int rows = zone.grid.Length;
            int cols = zone.grid[0].Length;
            float totalW = cols * _cellSize;
            float totalH = rows * _cellSize;

            var gridRect = GUILayoutUtility.GetRect(totalW, totalH);
            EditorGUI.DrawRect(gridRect, new Color(0.1f, 0.1f, 0.1f));

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < zone.grid[row].Length; col++)
                {
                    string cell  = zone.grid[row][col].Trim();
                    var    color = TileColor(cell);

                    var r = new Rect(
                        gridRect.x + col  * _cellSize + 1f,
                        gridRect.y + row  * _cellSize + 1f,
                        _cellSize - 2f,
                        _cellSize - 2f);

                    EditorGUI.DrawRect(r, color);

                    if (_cellSize >= 14f && !IsFlat(cell))
                    {
                        var style = new GUIStyle(EditorStyles.miniLabel)
                        {
                            fontSize  = Mathf.Clamp(Mathf.FloorToInt(_cellSize * 0.44f), 6, 14),
                            alignment = TextAnchor.MiddleCenter,
                        };
                        style.normal.textColor = LabelColor(cell);
                        GUI.Label(r, ShortLabel(cell), style);
                    }
                }
            }
        }

        EditorGUILayout.EndScrollView();

        // ── 범례 ──────────────────────────────────────────────────────
        if (_showLegend)
        {
            EditorGUILayout.BeginHorizontal();
            Legend("W 벽",   C.Wall);
            Legend("F 바닥", C.Floor);
            Legend("P 스폰", C.Player);
            Legend("M 몬",   C.Monster);
            Legend("m 후보", C.MonsterC);
            Legend("CP 캐",  C.CharPick);
            Legend("WP 무",  C.WeapPick);
            Legend("E 입구", C.Entrance);
            Legend("X 출구", C.Exit);
            Legend("O 장애", C.Obstacle);
            Legend("S 상점", C.Shop);
            Legend("B 보스", C.Boss);
            Legend(". 빈",   C.Empty);
            EditorGUILayout.EndHorizontal();
        }
    }

    // ── 타일 색상 정의 ───────────────────────────────────────────────

    private static class C
    {
        public static readonly Color Wall     = new Color(0.10f, 0.10f, 0.10f);
        public static readonly Color Floor    = new Color(0.70f, 0.70f, 0.70f);
        public static readonly Color Player   = new Color(0.20f, 0.45f, 0.95f);
        public static readonly Color Monster  = new Color(0.85f, 0.18f, 0.18f);
        public static readonly Color MonsterC = new Color(0.95f, 0.58f, 0.15f);
        public static readonly Color CharPick = new Color(0.08f, 0.82f, 0.82f);
        public static readonly Color WeapPick = new Color(0.62f, 0.18f, 0.95f);
        public static readonly Color Entrance = new Color(0.20f, 0.82f, 0.35f);
        public static readonly Color Exit     = new Color(0.95f, 0.82f, 0.08f);
        public static readonly Color Obstacle = new Color(0.38f, 0.26f, 0.14f);
        public static readonly Color Shop     = new Color(0.08f, 0.62f, 0.40f);
        public static readonly Color Boss     = new Color(0.72f, 0.04f, 0.72f);
        public static readonly Color Trap     = new Color(0.95f, 0.32f, 0.04f);
        public static readonly Color Chest    = new Color(0.95f, 0.76f, 0.08f);
        public static readonly Color Buff     = new Color(0.32f, 0.72f, 0.32f);
        public static readonly Color NPC      = new Color(0.52f, 0.72f, 0.95f);
        public static readonly Color StartGate= new Color(0.95f, 0.80f, 0.08f);
        public static readonly Color Empty    = new Color(0.90f, 0.90f, 0.90f);
    }

    private static Color TileColor(string c)
    {
        if (string.IsNullOrEmpty(c) || c == "F") return C.Floor;
        if (c == "W")  return C.Wall;
        if (c == "P")  return C.Player;
        if (c[0] == 'M') return C.Monster;
        if (c[0] == 'm') return C.MonsterC;
        if (c == "CP") return C.CharPick;
        if (c == "WP") return C.WeapPick;
        if (c == "E")  return C.Entrance;
        if (c == "X")  return C.Exit;
        if (c == "O")  return C.Obstacle;
        if (c == "B")  return C.Boss;
        if (c == "S" || c == "Sw" || c == "Si") return C.Shop;
        if (c == "T")  return C.Trap;
        if (c == "C")  return C.Chest;
        if (c == "R" || c == "D") return C.Buff;
        if (c == "N")  return C.NPC;
        if (c == "SG") return C.StartGate;
        if (c == ".")  return C.Empty;
        if (c[0] == 'd') return C.Floor;
        return C.Floor;
    }

    private static Color LabelColor(string c)
    {
        if (c == "F" || c == "." || c[0] == 'd') return Color.black;
        if (c == "W") return new Color(0.5f, 0.5f, 0.5f);
        return Color.black;
    }

    private static bool IsFlat(string c) =>
        c == "W" || c == "F" || c == "." || (c.Length > 1 && c[0] == 'd');

    private static string ShortLabel(string c)
    {
        if (c == "P")  return "P";
        if (c[0] == 'M') return "M";
        if (c[0] == 'm') return "m";
        if (c == "CP") return "CP";
        if (c == "WP") return "WP";
        if (c == "E")  return "E";
        if (c == "X")  return "X";
        if (c == "O")  return "O";
        if (c == "B")  return "B";
        if (c == "S")  return "S";
        if (c == "Sw") return "Sw";
        if (c == "Si") return "Si";
        if (c == "T")  return "T";
        if (c == "C")  return "C";
        if (c == "R")  return "R";
        if (c == "D")  return "D";
        if (c == "N")  return "N";
        if (c == "SG") return "SG";
        return c.Length > 2 ? c.Substring(0, 2) : c;
    }

    private static void Legend(string label, Color color)
    {
        var r = GUILayoutUtility.GetRect(14f, 14f, GUILayout.Width(14f), GUILayout.Height(14f));
        EditorGUI.DrawRect(r, color);
        GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(40f));
    }

    // ── CSV 로드 ─────────────────────────────────────────────────────

    private void LoadZones()
    {
        _zones.Clear();
        string fullPath = System.IO.Path.Combine(
            Application.dataPath.Replace("Assets", string.Empty), CsvPath);

        if (!System.IO.File.Exists(fullPath))
        {
            Debug.LogWarning($"[ZoneMapViewer] 파일 없음: {fullPath}");
            return;
        }

        var lines = System.IO.File.ReadAllLines(fullPath, Encoding.UTF8);
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var f = ParseCsvLine(lines[i]);
            if (f.Count < 24) continue;

            _zones.Add(new ZoneInfo
            {
                index     = int.TryParse(f[0], out var idx) ? idx : i - 1,
                category  = f[2],
                label     = f[3],
                nextZones = f[9],
                width     = int.TryParse(f[7], out var w) ? w : 0,
                height    = int.TryParse(f[8], out var h) ? h : 0,
                grid      = SplitGridCsv(f[23]),
            });
        }

        Repaint();
    }

    private static string[][] SplitGridCsv(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var raw  = csv.Contains(';') ? csv.Split(';') : csv.Split('\n');
        var rows = new List<string[]>();
        foreach (var r in raw)
        {
            var t = r.Trim();
            if (t.Length > 0) rows.Add(t.Split(','));
        }
        return rows.Count > 0 ? rows.ToArray() : null;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var  result  = new List<string>();
        var  sb      = new StringBuilder();
        bool inQuote = false;

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"')
            {
                if (inQuote && i + 1 < line.Length && line[i + 1] == '"')
                { sb.Append('"'); i++; }
                else inQuote = !inQuote;
            }
            else if (ch == ',' && !inQuote)
            { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        result.Add(sb.ToString());
        return result;
    }
}
#endif
