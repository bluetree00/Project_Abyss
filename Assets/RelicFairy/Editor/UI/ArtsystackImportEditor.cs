#if UNITY_EDITOR
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-13 Artsystack UI 팩 반입 2단계(분석 문서 §6-0 5 · 6행 · §6-1) — <b>쓰는 것만</b> <c>_ThirdParty/Artsystack/Art</c>에서
/// <c>Assets/RelicFairy/_Imported/Artsystack/</c>로 옮긴다(AssetDatabase · GUID 유지 · 이름의 공백은 밑줄).
/// 부품 19 · 흰 아이콘 128 71장. 재색칠 PNG 10장(<c>Recolor/</c>)은 미리 복사돼 있어야 한다(스크래치 make_recolor.py).
/// 이어서 임포트 설정(<see cref="SkinArtImporter.ImportArtsystack"/>) · 업적 스킨 슬롯 · 업적/퀘스트 아이콘 30을 한 번에 건다.
/// 여러 번 눌러도 같다(이미 옮긴 것은 건너뛴다). 로그 「[ArtsystackImport] 끝」.
/// </summary>
public static class ArtsystackImportEditor
{
    private const string SrcRoot    = "Assets/_ThirdParty/Artsystack/Art/Sprites";
    public  const string DstRoot    = "Assets/RelicFairy/_Imported/Artsystack";
    public  const string CompDir    = DstRoot + "/Sprites/components";
    public  const string IconDir    = DstRoot + "/Sprites/flaticon/white/128";
    public  const string RecolorDir = DstRoot + "/Recolor";

    // 직접 쓰는 부품. 재색칠 원본으로만 쓰는 InfoBox · mission_slot · description_box_05 · header_box · store_item_frame_03 · skill bg · Hexagon_02는 _ThirdParty에 둔다.
    private static readonly string[] Components =
    {
        "slot_short_active", "slot_short_inactive", "icon_slot_active", "skill_slot",
        "selected_line", "splitt_divider", "Line 1", "heart_bg_frame",
        "placeholder", "Hexagon_01", "Rectangle_active ", "Rectangle_locked ",
        "stats_slider_bg", "stats_slider_top", "message_slot_1", "direction_line",
        "Design_01", "Glow_Circle", "slider_scroller",
    };

    // 흰 아이콘 128(분석 문서 §4 — 업적 · 퀘스트 30 · 제단 노드 · 되살아난 기억). 파일 = 이름 + "_128.png"
    private static readonly string[] Icons =
    {
        "Heart", "anchor", "archery", "arrow", "arrow_next",
        "axe 2", "bomb", "book", "book_2", "book_open",
        "check", "chest_1", "chest_2", "coin", "compass",
        "crown 1", "crown 2", "crystal_1", "crystal_2", "dagger",
        "danger", "diamond", "dice", "document", "explore",
        "eye_show", "feather", "flag_1", "flag_3", "flame",
        "foot_print", "gate", "gemstone", "hammer", "increse_health",
        "infinity", "inkpen", "lantern", "library", "lock_1",
        "lock_3", "map", "money_bag", "moon", "potion_1",
        "power_1", "quiver", "refresh", "ruby", "sand_glass_1",
        "scale", "scroll", "shield_2", "skull", "star_3",
        "star_and_moon", "store ", "sun 1", "sun", "sun_and_cloud ",
        "sword_1", "sword_2", "sword_3", "tag", "target_1",
        "tools", "tools_2", "trophy", "wand", "water_drop",
        "wind_spell",
    };

    [MenuItem("RelicFairy/Tools/10-13 Artsystack 쓰는 것만 _Imported로 · 임포트 설정 · 업적 아이콘")]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[ArtsystackImport] 플레이 중엔 하지 않는다"); return; }
        // 셸이 막 복사한 폴더(Recolor)를 에디터가 아직 모르면 CreateFolder가 「Artsystack 1」 같은 새 폴더를 만든다(10-13 ②c 실측) — 먼저 새로고침
        AssetDatabase.Refresh();
        RemoveStrayFolders();
        int moved = 0, skipped = 0, missing = 0;
        EnsureFolder(CompDir);
        EnsureFolder(IconDir);
        foreach (var c in Components) Move($"{SrcRoot}/components/{c}.png", $"{CompDir}/{Clean(c)}.png", ref moved, ref skipped, ref missing);
        foreach (var i in Icons)      Move($"{SrcRoot}/flaticon/white/128/{i}_128.png", $"{IconDir}/{Clean(i)}_128.png", ref moved, ref skipped, ref missing);
        AssetDatabase.Refresh();

        int imported = SkinArtImporter.ImportArtsystack();
        int skin     = AssignAchievementSkin();
        int icons    = QuestIconAssigner.Apply();
        Debug.Log($"[ArtsystackImport] 끝 — 옮김 {moved} · 이미 있음 {skipped} · 없음 {missing} · 임포트 {imported} · 업적 스킨 슬롯 {skin}/11 · 아이콘 {icons}/30");
    }

    /// <summary>10-13 ②c에서 잘못 생긴 빈 폴더(「Artsystack 1」 · 「Artsystack 2」)만 지운다 — 안에 에셋이 하나라도 있으면 그대로 둔다.</summary>
    private static void RemoveStrayFolders()
    {
        foreach (var path in new[] { "Assets/RelicFairy/_Imported/Artsystack 1", "Assets/RelicFairy/_Imported/Artsystack 2" })
        {
            if (!AssetDatabase.IsValidFolder(path)) continue;
            if (AssetDatabase.FindAssets("", new[] { path }).Length > 0) { Debug.LogWarning($"[ArtsystackImport] {path}가 비어 있지 않다 — 그대로 둔다"); continue; }
            AssetDatabase.DeleteAsset(path);
            Debug.Log($"[ArtsystackImport] 잘못 생긴 빈 폴더 정리: {path}");
        }
    }

    /// <summary>파일 이름의 공백을 밑줄로(끝 공백은 지운다).</summary>
    public static string Clean(string name) => Regex.Replace(name.Trim().Replace(' ', '_'), "_+", "_");

    private static void Move(string from, string to, ref int moved, ref int skipped, ref int missing)
    {
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(to) != null) { skipped++; return; }
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(from) == null) { missing++; Debug.LogWarning($"[ArtsystackImport] 없음: {from}"); return; }
        string err = AssetDatabase.MoveAsset(from, to);
        if (string.IsNullOrEmpty(err)) moved++;
        else { missing++; Debug.LogWarning($"[ArtsystackImport] 옮기기 실패 {from}: {err}"); }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int cut = path.LastIndexOf('/');
        string parent = path.Substring(0, cut);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(cut + 1));
    }

    /// <summary>업적 스킨(분석 문서 §3-2) — 행 판 3 · 표식 3 · 게이지 2 · 받기 버튼 · 하단 바. 칩 · 모두 받기는 그대로.</summary>
    private static int AssignAchievementSkin()
    {
        var skin = AssetDatabase.LoadAssetAtPath<AchievementSkinSO>("Assets/RelicFairy/UI/Skin/Achievement/AchievementSkin.asset");
        if (skin == null) { Debug.LogWarning("[ArtsystackImport] AchievementSkin.asset 없음"); return 0; }
        var so = new SerializedObject(skin);
        int n = 0;
        void Set(string field, string path)
        {
            var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sp == null) { Debug.LogWarning($"[ArtsystackImport] 스프라이트 없음: {path}"); return; }
            so.FindProperty(field).objectReferenceValue = sp;
            n++;
        }
        Set("rowClaimable",  $"{RecolorDir}/mission_slot_01_navy.png");
        Set("rowProgress",   $"{RecolorDir}/mission_slot_02_navy.png");
        Set("rowClaimed",    $"{RecolorDir}/mission_slot_02_navy_dim.png");
        Set("markClaimable", $"{IconDir}/star_3_128.png");
        Set("markProgress",  $"{IconDir}/arrow_next_128.png");
        Set("markClaimed",   $"{IconDir}/check_128.png");
        Set("gaugeTrack",    $"{CompDir}/stats_slider_bg.png");
        Set("gaugeFill",     $"{CompDir}/stats_slider_top.png");
        Set("claimButton",   $"{CompDir}/Rectangle_active.png");
        Set("bottomBar",     $"{RecolorDir}/header_box_navy.png");
        Set("iconRing",      $"{CompDir}/icon_slot_active.png");
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(skin);
        AssetDatabase.SaveAssets();
        return n;
    }
}
#endif
