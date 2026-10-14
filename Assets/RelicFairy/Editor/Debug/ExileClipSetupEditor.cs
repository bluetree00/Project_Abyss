#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-11 추방 클립 반입 · 연결(편집 모드 · 1회) — 리치 추방의 추락 · 엎드림 · 일어남 클립을 _ThirdParty에서 플레이어 피격 클립 폴더로 옮기고
/// @GameRun(GameRunBootstrapper)의 exileFallClip · exileDownClip · exileGetUpClip 칸에 잇는다.
///   ① <see cref="AssetDatabase.MoveAsset"/>(GUID 보존 · 파일탐색기 이동 금지). 같은 이름이 이미 있으면 옮기지 않고 그 파일을 쓴다(기록).
///      _ThirdParty에 남은 의존(아바타 원본 · 재질 · 텍스처)은 _Imported 아래 같은 구조로 함께 옮긴다(메모리 project_asset_pipeline_lfs).
///   ② 휴머노이드 · 클립을 확인하고, 추락(DownLoop)은 ModelImporter API로 loopTime을 켠다(.meta 직접 편집 없음).
///   ③ <see cref="PrefabUtility.LoadPrefabContents"/>로 세 칸만 바꿔 저장 — 포효 칸(미러 YAML로 넣은 값) 등 나머지는 읽은 그대로 다시 쓴다.
///      칸이 아직 없으면(미러 반입 · 컴파일 전) 프리팹을 건드리지 않고 멈춘다.
/// 순서: 미러 반입(@GameRun YAML 포함) → 컴파일 → 이 메뉴. 결과: Temp/exile_clip_setup.txt · 로그 「[ExileClipSetup] 끝」.
/// </summary>
public static class ExileClipSetupEditor
{
    private const string MenuPath   = "RelicFairy/Debug/10-11 추방 클립 반입 · 연결 (편집 모드, 1회)";
    private const string OutPath    = "Temp/exile_clip_setup.txt";
    private const string SrcDir     = "Assets/_ThirdParty/GhostSamurai_Animset/Animation/Bow/Hit/Inplace";
    private const string DstDir     = "Assets/RelicFairy/Animations/Player/Test_01/Hit";
    private const string ThirdParty = "Assets/_ThirdParty/";
    private const string Imported   = "Assets/RelicFairy/_Imported/";
    private const string PrefabPath = "Assets/RelicFairy/Systems/Bootstrapper/Prefabs/@GameRun.prefab";

    // 파일 이름(첫 파일은 이름에 공백이 있다) · @GameRun 칸 · 반복
    private static readonly (string file, string field, bool loop)[] Clips =
    {
        ("GhostSamurai_Bow_Hit_Air_ DownLoop_Inplace.FBX",  "exileFallClip",  true),
        ("GhostSamurai_Bow_Hit_Falldown_F_End_Inplace.FBX", "exileDownClip",  false),
        ("GhostSamurai_Bow_Hit_Up_GetUP_Inplace.FBX",       "exileGetUpClip", false),
    };

    [MenuItem(MenuPath)]
    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[ExileClipSetup] 편집 모드에서만 — 플레이 중 Assets 쓰기는 도메인 리로드로 런을 깬다");
            return;
        }

        var sb = new StringBuilder("추방 클립 반입 · 연결 (10-11)\n");
        try
        {
            var clips = new AnimationClip[Clips.Length];
            for (int i = 0; i < Clips.Length; i++)
                clips[i] = Bring(Clips[i].file, Clips[i].loop, sb);
            Link(clips, sb);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText(OutPath, sb.ToString());
            Debug.Log("[ExileClipSetup] 끝\n" + sb);
        }
    }

    // ── ① 옮기기 · ② 확인 ───────────────────────────────────────────
    private static AnimationClip Bring(string file, bool loop, StringBuilder sb)
    {
        string src = $"{SrcDir}/{file}";
        string dst = $"{DstDir}/{file}";
        sb.AppendLine($"· {file}");

        if (Exists(dst))
        {
            sb.AppendLine($"  이미 있음 — 옮기지 않고 그 파일을 쓴다: {dst}");
        }
        else if (!Exists(src))
        {
            sb.AppendLine($"  원본 없음: {src} — 건너뜀");
            return null;
        }
        else
        {
            MoveThirdPartyDependencies(src, sb);
            string err = AssetDatabase.MoveAsset(src, dst);
            if (!string.IsNullOrEmpty(err))
            {
                sb.AppendLine($"  옮기기 실패 {src} → {dst}: {err}");
                return null;
            }
            sb.AppendLine($"  옮김 {src} → {dst}");
        }

        var importer = AssetImporter.GetAtPath(dst) as ModelImporter;
        if (importer == null)
        {
            sb.AppendLine("  ModelImporter 없음 — 건너뜀");
            return null;
        }
        bool   humanoid  = importer.animationType == ModelImporterAnimationType.Human;
        string avatarSrc = importer.sourceAvatar != null ? AssetDatabase.GetAssetPath(importer.sourceAvatar) : "(이 모델에서 만든 아바타)";
        if (avatarSrc.StartsWith(ThirdParty)) sb.AppendLine($"  ⚠ 아바타 원본이 아직 _ThirdParty: {avatarSrc}");
        if (loop) SetLoop(importer, sb);

        var clip = LoadClip(dst);
        sb.AppendLine(clip != null
            ? $"  클립 {clip.name} · 길이 {clip.length:0.00}초 · 루프 {clip.isLooping} · 휴머노이드 {humanoid} · 아바타 {avatarSrc}"
            : $"  클립 없음 · 휴머노이드 {humanoid}");
        if (!humanoid) sb.AppendLine("  ⚠ 휴머노이드가 아니다 — 플레이어 아바타에 맞지 않을 수 있다");
        return clip;
    }

    /// <summary>_ThirdParty에 남은 의존(아바타 원본 · 재질 · 텍스처)을 _Imported 아래 같은 구조로 먼저 옮긴다. 이미 있으면 건너뛰고 적는다.</summary>
    private static void MoveThirdPartyDependencies(string assetPath, StringBuilder sb)
    {
        foreach (var dep in AssetDatabase.GetDependencies(assetPath, true))
        {
            if (dep == assetPath || !dep.StartsWith(ThirdParty) || dep.EndsWith(".cs")) continue;
            string to = Imported + dep.Substring(ThirdParty.Length);
            if (Exists(to)) { sb.AppendLine($"  의존 이미 있음(건너뜀): {to}"); continue; }
            EnsureFolder(Path.GetDirectoryName(to)?.Replace('\\', '/'));
            string err = AssetDatabase.MoveAsset(dep, to);
            sb.AppendLine(string.IsNullOrEmpty(err) ? $"  의존 옮김 {dep} → {to}" : $"  의존 옮기기 실패 {dep}: {err}");
        }
    }

    /// <summary>추락 루프 — 클립 설정의 loopTime을 켠다(ModelImporter API · .meta 직접 편집 없음).</summary>
    private static void SetLoop(ModelImporter importer, StringBuilder sb)
    {
        var clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
        bool changed = false;
        foreach (var c in clips)
        {
            if (c.loopTime) continue;
            c.loopTime = true;
            changed    = true;
        }
        if (!changed) { sb.AppendLine("  loopTime 이미 켜짐"); return; }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        sb.AppendLine("  loopTime 켬(ModelImporter)");
    }

    // ── ③ 연결 ──────────────────────────────────────────────────────
    private static void Link(AnimationClip[] clips, StringBuilder sb)
    {
        if (clips.All(c => c == null)) { sb.AppendLine("연결할 클립 없음 — @GameRun을 건드리지 않는다"); return; }

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var boot = root.GetComponentInChildren<GameRunBootstrapper>(true);
            if (boot == null) { sb.AppendLine("@GameRun에 GameRunBootstrapper 없음 — 저장 안 함"); return; }

            var so = new SerializedObject(boot);
            for (int i = 0; i < Clips.Length; i++)
            {
                if (so.FindProperty(Clips[i].field) != null) continue;
                sb.AppendLine($"칸 없음 {Clips[i].field} — 미러 반입 · 컴파일 뒤 다시 실행(저장 안 함)");
                return;
            }

            bool any = false;
            for (int i = 0; i < Clips.Length; i++)
            {
                if (clips[i] == null) continue;
                var prop = so.FindProperty(Clips[i].field);
                prop.objectReferenceValue = clips[i];
                any = true;
                sb.AppendLine($"연결 {Clips[i].field} = {clips[i].name}");
            }
            var roars = so.FindProperty("exileRoars");
            if (roars != null)
                for (int i = 0; i < roars.arraySize; i++)
                {
                    var clip = roars.GetArrayElementAtIndex(i).objectReferenceValue;
                    sb.AppendLine($"포효 칸 [{i}] = {(clip != null ? AssetDatabase.GetAssetPath(clip) : "비어 있음")} (그대로 둔다)");
                }
            if (!any) return;

            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            sb.AppendLine($"@GameRun 저장 — {PrefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ── 도우미 ──────────────────────────────────────────────────────
    private static bool Exists(string path) => !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets));

    private static AnimationClip LoadClip(string path)
        => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));

    private static void EnsureFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
#endif
