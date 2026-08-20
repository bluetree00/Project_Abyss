using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 코드로 짓던 팝업을 <b>플레이 모드 없이</b> 굽는다.
///
/// <para><see cref="PrefabUtility.LoadPrefabContents"/>가 프리팹을 <b>격리된 미리보기 씬</b>에 연다 —
/// 열려 있는 씬은 건드리지 않으므로 「에디터 스크립트로 씬 수정 금지」 규칙을 지킨다.
/// 거기서 빌더를 한 번 돌리고 그대로 저장한다.</para>
///
/// <para><b>왜 Init()을 부르지 않는가</b> — <c>UI_Popup.Init()</c>은 <c>Managers.UI.SetCanvas</c>를 타서
/// 에디터에는 없는 서비스 로케이터를 깨운다. 계층을 짓는 것은 <c>BuildChrome</c>/<c>BuildLayout</c>뿐이라
/// 그것만 반사로 부른다. Canvas·GraphicRaycaster·CanvasGroup은 런타임 <c>Init</c>이 멱등하게 다시 붙인다.</para>
///
/// <para>스킨(Addressable SO)은 에디터에 로드돼 있지 않으므로 <see cref="UISkin"/>의 비공개 정적 필드에
/// AssetDatabase에서 읽은 SO를 반사로 꽂아 넣는다. 이래야 아트가 빠진 반쪽이 구워지지 않는다.</para>
/// </summary>
public static class UIPrefabBakeEditor
{
    private const string BackupFolder = "Assets/RelicFairy/UI/_PrefabBackup";

    /// <summary>굽기 대상 — 타입 이름과 계층을 짓는 메서드. 순서는 검증 순서다.</summary>
    private static readonly (string Type, string Build)[] Targets =
    {
        ("UI_ShopPanel",           "BuildChrome"),
        ("UI_RelicInfoPopup",      "BuildLayout"),
        ("UI_RefineryPanel",       "BuildUI"),
        ("UI_RangedForgePopup",    "BuildLayout"),
        ("UI_RuneSelectPopup",     "BuildChrome"),
        ("UI_RelicPartDraftPopup", "BuildChrome"),
        ("UI_CruciblePanel",       "BuildChrome"),
    };

    [MenuItem("RelicFairy/UI/Bake — 상점만")]
    private static void BakeShop() => Bake("UI_ShopPanel", "BuildChrome");

    [MenuItem("RelicFairy/UI/Bake — 코드 생성 팝업 전체")]
    private static void BakeAll()
    {
        foreach (var (type, build) in Targets) Bake(type, build);
        AssetDatabase.Refresh();
    }

    /// <summary>
    /// 이미 구운 것을 <b>비우고 다시</b> 굽는다. 직렬화 필드를 추가했거나 빌더를 고쳐
    /// 결과가 달라졌을 때 쓴다. 백업은 매번 새로 남으므로 되돌릴 길은 유지된다.
    /// </summary>
    [MenuItem("RelicFairy/UI/Bake — 강제 재굽기(전체)")]
    private static void RebakeAll()
    {
        foreach (var (type, build) in Targets) Bake(type, build, force: true);
        AssetDatabase.Refresh();
    }

    // ── 굽기 ─────────────────────────────────────────────────

    private static void Bake(string typeName, string buildMethod, bool force = false)
    {
        var type = FindType(typeName);
        if (type == null) { Debug.LogError($"[Bake] 타입 없음: {typeName}"); return; }

        string path = FindPrefabPath(typeName);
        if (path == null) { Debug.LogError($"[Bake] 프리팹 없음: {typeName}"); return; }

        InjectSkins();

        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (root.transform.childCount > 0)
            {
                if (!force)
                {
                    Debug.Log($"[Bake] 건너뜀 — 이미 구워져 있다: {typeName} ({root.transform.childCount}개 자식)");
                    return;
                }
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            }

            var comp = root.GetComponent(type);
            if (comp == null) { Debug.LogError($"[Bake] 프리팹에 {typeName} 없음"); return; }

            AssignSkinField(comp, type);

            var m = type.GetMethod(buildMethod, BindingFlags.NonPublic | BindingFlags.Instance);
            if (m == null) { Debug.LogError($"[Bake] {typeName}.{buildMethod}() 없음"); return; }

            m.Invoke(comp, null);

            int n = root.GetComponentsInChildren<Transform>(true).Length;
            if (n <= 1) { Debug.LogError($"[Bake] {typeName} — 계층이 생기지 않았다(자식 0). 저장하지 않는다."); return; }

            if (!Backup(path)) { Debug.LogError($"[Bake] 백업 실패 — 굽지 않는다: {path}"); return; }

            PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
            Debug.Log(ok ? $"[Bake] 구움: {typeName} — 오브젝트 {n}개"
                         : $"[Bake] 저장 실패: {path}");
        }
        catch (Exception e)
        {
            // 반사 호출은 예외를 TargetInvocationException으로 감싼다 — 안쪽을 보여줘야 진단이 된다.
            Debug.LogError($"[Bake] {typeName} 실패: {e.InnerException?.Message ?? e.Message}\n{e.InnerException?.StackTrace}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ── 스킨 주입 ────────────────────────────────────────────

    /// <summary>
    /// <see cref="UISkin"/>의 비공개 정적 필드를 AssetDatabase에서 읽은 SO로 채운다.
    /// 런타임 경로(Addressable 비동기)는 에디터에서 돌지 않아 여기서 대신 채워야 한다.
    /// </summary>
    private static void InjectSkins()
    {
        var skinType = FindType("UISkin");
        if (skinType == null) return;

        foreach (var f in skinType.GetFields(BindingFlags.NonPublic | BindingFlags.Static))
        {
            if (!typeof(ScriptableObject).IsAssignableFrom(f.FieldType)) continue;
            if (f.GetValue(null) != null) continue;

            var so = LoadFirst(f.FieldType);
            if (so != null) f.SetValue(null, so);
        }
    }

    /// <summary>컴포넌트가 자기 스킨 필드를 들고 있으면(<c>_skin</c>) 같은 타입 SO를 꽂는다.</summary>
    private static void AssignSkinField(Component comp, Type type)
    {
        var f = type.GetField("_skin", BindingFlags.NonPublic | BindingFlags.Instance);
        if (f == null || f.GetValue(comp) != null) return;

        var so = LoadFirst(f.FieldType);
        if (so != null) f.SetValue(comp, so);
    }

    private static UnityEngine.Object LoadFirst(Type soType)
    {
        var guids = AssetDatabase.FindAssets($"t:{soType.Name}");
        if (guids.Length == 0) return null;
        return AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guids[0]), soType);
    }

    // ── 잡일 ─────────────────────────────────────────────────

    private static Type FindType(string name) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
            .FirstOrDefault(t => t.Name == name);

    private static string FindPrefabPath(string name) =>
        AssetDatabase.FindAssets($"{name} t:Prefab")
            .Select(AssetDatabase.GUIDToAssetPath)
            .FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == name);

    private static bool Backup(string path)
    {
        if (!AssetDatabase.IsValidFolder(BackupFolder))
            AssetDatabase.CreateFolder("Assets/RelicFairy/UI", "_PrefabBackup");

        string name  = System.IO.Path.GetFileNameWithoutExtension(path);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return AssetDatabase.CopyAsset(path, $"{BackupFolder}/{name}_{stamp}.prefab");
    }
}
