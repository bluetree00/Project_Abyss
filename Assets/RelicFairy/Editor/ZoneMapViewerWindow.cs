#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// CHAPTER_N_ZONE_LAYOUT.csv 의 grid_csv 를 타일 색상으로 시각화.
/// Tools > RelicFairy > Zone Map Viewer 에서 열 수 있음.
/// </summary>
public class ZoneMapViewerWindow : EditorWindow
{
    // ── Constants ────────────────────────────────────────────────
    private static readonly string[] CsvPaths =
    {
        "Assets/RelicFairy/Docs/CHAPTER_1_ZONE_LAYOUT.csv",
        "Assets/RelicFairy/Docs/CHAPTER_2_ZONE_LAYOUT.csv",
        "Assets/RelicFairy/Docs/CHAPTER_3_ZONE_LAYOUT.csv",
        "Assets/RelicFairy/Docs/CHAPTER_4_ZONE_LAYOUT.csv",
    };
    private static readonly string[] ChapterLabels =
    {
        "Ch1  황혼의 숲",
        "Ch2  용암 지대",
        "Ch3  신성한 성역",
        "Ch4  천상의 성채",
    };
    private static readonly string[] CategoryLabels =
    {
        "전체", "Normal", "Shop", "Elite", "Boss", "Event", "Start",
    };
    private static readonly string[] CategoryKeys =
    {
        "", "Normal", "Shop", "Elite", "Boss", "Event", "Start",
    };

    // ── Data ─────────────────────────────────────────────────────
    private struct ZoneInfo
    {
        public int      index;
        public int      layer;
        public string   category;
        public string   label;
        public string   nextZones;
        public int      width;
        public int      height;
        public float    difficulty;
        public bool     hasHidden;
        public int      maxSpawners;
        public string[][] grid;
    }

    private readonly List<ZoneInfo> _zones    = new();
    private readonly List<ZoneInfo> _filtered = new();

    // ── State ─────────────────────────────────────────────────────
    private int     _selectedChapter;
    private int     _selectedCategory;   // index into CategoryLabels
    private int     _selectedZone;
    private Vector2 _scrollPos;
    private float   _cellSize   = 16f;
    private bool    _showLegend = true;

    [MenuItem("RelicFairy/Map/Zone Map Viewer")]
    public static void ShowWindow()
    {
        var win = GetWindow<ZoneMapViewerWindow>("Zone Map Viewer");
        win.minSize = new Vector2(900f, 600f);
        win.LoadZones();
    }

    private void OnEnable() => LoadZones();

    private void OnGUI()
    {
        DrawChapterBar();

        if (_zones.Count == 0)
        {
            EditorGUILayout.HelpBox($"CSV 로드 실패: {CsvPaths[_selectedChapter]}", MessageType.Warning);
            if (GUILayout.Button("다시 로드")) LoadZones();
            return;
        }

        DrawZoneToolbar();

        if (_filtered.Count == 0)
        {
            EditorGUILayout.HelpBox("선택한 카테고리의 존이 없습니다.", MessageType.Info);
            return;
        }

        if (_selectedZone >= _filtered.Count) _selectedZone = 0;
        var zone = _filtered[_selectedZone];

        DrawZoneInfoBar(zone);
        DrawGrid(zone);

        if (_showLegend) DrawLegend();
    }

    // ── Toolbar ──────────────────────────────────────────────────

    private void DrawChapterBar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("챕터:", EditorStyles.toolbarButton, GUILayout.Width(38f));

        for (int i = 0; i < ChapterLabels.Length; i++)
        {
            bool active = _selectedChapter == i;
            var style = active ? EditorStyles.toolbarButton : EditorStyles.toolbarButton;
            var col   = active ? new Color(0.35f, 0.65f, 1f) : GUI.backgroundColor;
            GUI.backgroundColor = col;
            if (GUILayout.Toggle(active, ChapterLabels[i], EditorStyles.toolbarButton, GUILayout.Width(130f)) && !active)
            {
                _selectedChapter = i;
                _selectedZone    = 0;
                LoadZones();
            }
            GUI.backgroundColor = Color.white;
        }

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("↺  다시 로드", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            LoadZones();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawZoneToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        // 카테고리 필터
        GUILayout.Label("카테고리:", GUILayout.Width(54f));
        int newCat = EditorGUILayout.Popup(_selectedCategory, CategoryLabels, GUILayout.Width(90f));
        if (newCat != _selectedCategory)
        {
            _selectedCategory = newCat;
            _selectedZone     = 0;
            ApplyFilter();
        }

        GUILayout.Space(6f);

        // 존 선택
        GUILayout.Label("존:", GUILayout.Width(22f));
        var names = new string[_filtered.Count];
        for (int i = 0; i < _filtered.Count; i++)
        {
            var z = _filtered[i];
            names[i] = $"[Z{z.index}  L{z.layer}]  {z.label}  ({z.category})";
        }
        int newZone = EditorGUILayout.Popup(_selectedZone, names, GUILayout.Width(320f));
        if (newZone != _selectedZone) _selectedZone = newZone;

        GUILayout.Space(8f);
        GUILayout.Label("셀:", GUILayout.Width(22f));
        _cellSize = EditorGUILayout.Slider(_cellSize, 6f, 36f, GUILayout.Width(120f));

        GUILayout.FlexibleSpace();
        _showLegend = GUILayout.Toggle(_showLegend, "범례", EditorStyles.toolbarButton, GUILayout.Width(42f));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawZoneInfoBar(ZoneInfo zone)
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

        // 카테고리 배지
        var badgeStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            fontStyle = FontStyle.Bold,
            normal    = { textColor = CategoryBadgeColor(zone.category) },
        };
        GUILayout.Label($"[{zone.category.ToUpper()}]", badgeStyle, GUILayout.Width(72f));

        // 기본 정보
        EditorGUILayout.LabelField(
            $"Z{zone.index}  |  Layer {zone.layer}  |  {zone.width}×{zone.height}  |  다음: {zone.nextZones}",
            EditorStyles.miniLabel);

        GUILayout.FlexibleSpace();

        // 난이도
        var diffColor = DifficultyColor(zone.difficulty);
        var diffStyle = new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold };
        diffStyle.normal.textColor = diffColor;
        GUILayout.Label($"diff {zone.difficulty:F2}", diffStyle, GUILayout.Width(68f));

        // 스포너 수
        GUILayout.Label($"maxSP:{zone.maxSpawners}", EditorStyles.miniLabel, GUILayout.Width(60f));

        // 히든 보상
        if (zone.hasHidden)
        {
            var hidStyle = new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold };
            hidStyle.normal.textColor = new Color(1f, 0.85f, 0.1f);
            GUILayout.Label("💎 히든보상", hidStyle, GUILayout.Width(64f));
        }

        EditorGUILayout.EndHorizontal();
    }

    // ── Grid ─────────────────────────────────────────────────────

    private void DrawGrid(ZoneInfo zone)
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        if (zone.grid != null && zone.grid.Length > 0)
        {
            int rows   = zone.grid.Length;
            int cols   = zone.grid[0].Length;
            float totalW = cols * _cellSize;
            float totalH = rows * _cellSize;

            var gridRect = GUILayoutUtility.GetRect(totalW, totalH);
            EditorGUI.DrawRect(gridRect, new Color(0.08f, 0.08f, 0.08f));

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < zone.grid[row].Length; col++)
                {
                    string cell  = zone.grid[row][col].Trim();
                    var    color = TileColor(cell);

                    var r = new Rect(
                        gridRect.x + col * _cellSize + 1f,
                        gridRect.y + row * _cellSize + 1f,
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
    }

    // ── Legend ───────────────────────────────────────────────────

    private void DrawLegend()
    {
        EditorGUILayout.BeginHorizontal();
        Legend("W 벽",     C.Wall);
        Legend("F 바닥",   C.Floor);
        Legend("P 스폰",   C.Player);
        Legend("M 확정",   C.Monster);
        Legend("m 후보",   C.MonsterC);
        Legend("CP 캐릭",  C.CharPick);
        Legend("WP 무기",  C.WeapPick);
        Legend("O 장애",   C.Obstacle);
        Legend("Sw 무기상점", C.Shop);
        Legend("Si 아이템상점", new Color(0.05f, 0.75f, 0.50f));
        Legend("B 보스",   C.Boss);
        Legend("R/D 버프", C.Buff);
        Legend("N NPC",    C.NPC);
        Legend("SG 게이트",C.StartGate);
        Legend(". 빈칸",   C.Empty);
        EditorGUILayout.EndHorizontal();
    }

    // ── Tile Colors ───────────────────────────────────────────────

    private static class C
    {
        public static readonly Color Wall      = new Color(0.10f, 0.10f, 0.10f);
        public static readonly Color Floor     = new Color(0.70f, 0.70f, 0.70f);
        public static readonly Color Player    = new Color(0.20f, 0.45f, 0.95f);
        public static readonly Color Monster   = new Color(0.85f, 0.18f, 0.18f);
        public static readonly Color MonsterC  = new Color(0.95f, 0.58f, 0.15f);
        public static readonly Color CharPick  = new Color(0.08f, 0.82f, 0.82f);
        public static readonly Color WeapPick  = new Color(0.62f, 0.18f, 0.95f);
        public static readonly Color Entrance  = new Color(0.20f, 0.82f, 0.35f);
        public static readonly Color Exit      = new Color(0.95f, 0.82f, 0.08f);
        public static readonly Color Obstacle  = new Color(0.38f, 0.26f, 0.14f);
        public static readonly Color Shop      = new Color(0.08f, 0.62f, 0.40f);
        public static readonly Color Boss      = new Color(0.72f, 0.04f, 0.72f);
        public static readonly Color Trap      = new Color(0.95f, 0.32f, 0.04f);
        public static readonly Color Chest     = new Color(0.95f, 0.76f, 0.08f);
        public static readonly Color Buff      = new Color(0.32f, 0.72f, 0.32f);
        public static readonly Color NPC       = new Color(0.52f, 0.72f, 0.95f);
        public static readonly Color StartGate = new Color(0.95f, 0.80f, 0.08f);
        public static readonly Color Empty     = new Color(0.20f, 0.20f, 0.20f);
    }

    private static Color TileColor(string c)
    {
        if (string.IsNullOrEmpty(c) || c == "F") return C.Floor;
        if (c == "W")   return C.Wall;
        if (c == "P")   return C.Player;
        if (c[0] == 'M') return C.Monster;
        if (c[0] == 'm') return C.MonsterC;
        if (c.StartsWith("CP")) return C.CharPick;   // CP, CP0, CP1 ...
        if (c.StartsWith("WP")) return C.WeapPick;   // WP, WP0, WP1 ...
        if (c == "E")   return C.Entrance;
        if (c == "X")   return C.Exit;
        if (c == "O")   return C.Obstacle;
        if (c == "B")   return C.Boss;
        if (c == "S" || c == "Sw") return C.Shop;
        if (c == "Si")  return new Color(0.05f, 0.75f, 0.50f);
        if (c == "T")   return C.Trap;
        if (c == "C")   return C.Chest;
        if (c == "R" || c == "D") return C.Buff;
        if (c == "N")   return C.NPC;
        if (c == "SG")  return C.StartGate;
        if (c == ".")   return C.Empty;
        if (c == "Pt")  return C.Empty;
        if (c[0] == 'd') return C.Floor;
        return C.Floor;
    }

    private static Color LabelColor(string c)
    {
        if (c == "F" || c == "." || c == "Pt" || (c.Length > 0 && c[0] == 'd'))
            return Color.black;
        if (c == "W") return new Color(0.4f, 0.4f, 0.4f);
        return Color.black;
    }

    private static bool IsFlat(string c) =>
        c == "W" || c == "F" || c == "." || c == "Pt" ||
        (c.Length > 1 && c[0] == 'd');

    private static string ShortLabel(string c)
    {
        if (c == "P")   return "P";
        if (c[0] == 'M') return "M";
        if (c[0] == 'm') return "m";
        if (c.StartsWith("CP")) return "CP";
        if (c.StartsWith("WP")) return "WP";
        if (c == "E")   return "E";
        if (c == "X")   return "X";
        if (c == "O")   return "O";
        if (c == "B")   return "B";
        if (c == "S")   return "S";
        if (c == "Sw")  return "Sw";
        if (c == "Si")  return "Si";
        if (c == "SG")  return "SG";
        if (c == "T")   return "T";
        if (c == "C")   return "C";
        if (c == "R")   return "R";
        if (c == "D")   return "D";
        if (c == "N")   return "N";
        return c.Length > 2 ? c[..2] : c;
    }

    private static void Legend(string label, Color color)
    {
        var r = GUILayoutUtility.GetRect(12f, 12f, GUILayout.Width(12f), GUILayout.Height(12f));
        EditorGUI.DrawRect(r, color);
        GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(54f));
    }

    private static Color CategoryBadgeColor(string cat) => cat?.ToLower() switch
    {
        "boss"   => new Color(0.90f, 0.30f, 0.30f),
        "elite"  => new Color(0.70f, 0.40f, 1.00f),
        "shop"   => new Color(0.30f, 0.90f, 0.50f),
        "event"  => new Color(1.00f, 0.80f, 0.20f),
        "start"  => new Color(0.40f, 0.90f, 0.90f),
        _        => new Color(0.75f, 0.75f, 0.75f),
    };

    private static Color DifficultyColor(float diff)
    {
        if (diff <= 0f)   return new Color(0.5f, 0.5f, 0.5f);
        if (diff <= 1.0f) return new Color(0.5f, 1.0f, 0.5f);
        if (diff <= 2.0f) return new Color(1.0f, 0.9f, 0.3f);
        if (diff <= 4.0f) return new Color(1.0f, 0.5f, 0.1f);
        return new Color(1.0f, 0.2f, 0.2f);
    }

    // ── CSV Load ──────────────────────────────────────────────────

    private void LoadZones()
    {
        _zones.Clear();

        string csvPath = CsvPaths[_selectedChapter];
        string fullPath = System.IO.Path.Combine(
            Application.dataPath.Replace("Assets", ""), csvPath);

        if (!System.IO.File.Exists(fullPath))
        {
            Debug.LogWarning($"[ZoneMapViewer] 파일 없음: {fullPath}");
            ApplyFilter();
            Repaint();
            return;
        }

        var lines = System.IO.File.ReadAllLines(fullPath, Encoding.UTF8);
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var f = ParseCsvLine(lines[i]);
            if (f.Count < 24) continue;

            float.TryParse(f[14], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float diff);

            _zones.Add(new ZoneInfo
            {
                index       = int.TryParse(f[0],  out var idx) ? idx : i - 1,
                layer       = int.TryParse(f[1],  out var lay) ? lay : 0,
                category    = f[2],
                label       = f[3],
                nextZones   = f[9],
                width       = int.TryParse(f[7],  out var w)   ? w   : 0,
                height      = int.TryParse(f[8],  out var h)   ? h   : 0,
                difficulty  = diff,
                hasHidden   = f.Count > 15 && f[15].Equals("true", System.StringComparison.OrdinalIgnoreCase),
                maxSpawners = f.Count > 28 && int.TryParse(f[28], out var ms) ? ms : 0,
                grid        = SplitGridCsv(f[23]),
            });
        }

        ApplyFilter();
        Repaint();
        Debug.Log($"[ZoneMapViewer] Ch{_selectedChapter + 1} 로드 완료: {_zones.Count}개 존");
    }

    private void ApplyFilter()
    {
        _filtered.Clear();
        string filterKey = CategoryKeys[_selectedCategory];
        foreach (var z in _zones)
        {
            if (string.IsNullOrEmpty(filterKey) ||
                z.category.Equals(filterKey, System.StringComparison.OrdinalIgnoreCase))
                _filtered.Add(z);
        }
        if (_selectedZone >= _filtered.Count) _selectedZone = 0;
    }

    // ── Parsers ───────────────────────────────────────────────────

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
