#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 10-02 챕터 로스터(레벨디자인 설계서 §3) — ① 몬스터 실물 사진(편집 모드 · 미리보기 씬) ② 챕터 로스터 적용(poolTags).
/// 로스터 정본 = 이 표(아래 <see cref="Roster"/>). 몬스터 스폰 표(SO)의 poolTags만 바꾼다 — 새 에셋 0.
/// ⚠️ 스폰 표 편집기의 Auto-Populate는 CSV의 옛 pool_tag로 poolTags를 덮어쓴다 — 누르지 말 것(10-02 점검).
/// </summary>
public static class MonsterRosterEditor
{
    private const string TablePath = "Assets/RelicFairy/Characters/Monster/Monster/Core/MonsterSpawnTable.asset";
    private const string ShotDir   = "Temp/monster_shots";
    private const int    ShotPx    = 384;

    // 챕터 로스터(설계서 §3 · 사용자 결정 D2 「권장대로」) — displayName → 나오는 챕터
    // Ch1 잊혀진 숲(늪 숲 — 어인 · 가오리 유지) · Ch2 불꽃 동굴 · Ch3 성채 · Ch4 대제단
    private static readonly (string name, int[] chapters)[] Roster =
    {
        // 일반
        ("SlimeMonster",         new[] { 1, 2, 4 }),
        ("SpiderMonster",        new[] { 1, 2, 3 }),
        ("BattleBeeMonster",     new[] { 1 }),
        ("MushroomAngryMonster", new[] { 1 }),
        ("RatAssassinMonster",   new[] { 2, 3 }),
        ("SkeletonMonster",      new[] { 3, 4 }),
        ("FairyBatMonster",      new[] { 4 }),
        ("SnailMonster",         new int[0]),
        // 희귀
        ("MonsterPlantMonster",  new[] { 1, 2 }),
        ("FishmanMonster",       new[] { 1 }),
        ("StingRayMonster",      new[] { 1 }),
        ("GolemMonster",         new[] { 2, 3, 4 }),
        ("OrcMonster",           new[] { 2, 3 }),
        ("LichSkeletonMonster",  new[] { 4 }),
        ("CactusMonster",        new int[0]),
        // 정예
        ("LizardWarriorMonster", new[] { 1 }),
        ("WerewolfMonster",      new[] { 1 }),
        ("SpecterMonster",       new[] { 1 }),
        ("FlyingDemonMonster",   new[] { 2 }),
        ("CyclopsMonster",       new[] { 2, 4 }),
        ("SalamanderMonster",    new[] { 2 }),
        ("EvilMageMonster",      new[] { 3 }),
        ("BeholderMonster",      new[] { 3 }),
        ("BishopKnightMonster",  new[] { 3, 4 }),
        ("BlackKnightMonster",   new[] { 4 }),
        ("NagaWizardMonster",    new int[0]),
    };

    [MenuItem("RelicFairy/Stage/10-02 몬스터 실물 사진 (편집 모드)")]
    private static void Shots()
    {
        var table = AssetDatabase.LoadAssetAtPath<MonsterSpawnTableSO>(TablePath);
        if (table == null) { Debug.LogError("[MonsterRoster] 스폰 표 없음"); return; }
        Directory.CreateDirectory(ShotDir);

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var byAddress = new Dictionary<string, string>();
        foreach (var g in settings.groups)
        {
            if (g == null) continue;
            foreach (var e in g.entries) if (!string.IsNullOrEmpty(e.address)) byAddress[e.address] = e.AssetPath;
        }

        var scene = EditorSceneManager.NewPreviewScene();
        var camGo = new GameObject("~ShotCam");
        SceneManager.MoveGameObjectToScene(camGo, scene);
        var cam = camGo.AddComponent<Camera>();
        cam.scene = scene;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.16f, 0.17f, 0.20f, 1f);
        cam.fieldOfView = 30f;
        foreach (var (rot, inten) in new[] { (new Vector3(40f, -30f, 0f), 1.3f), (new Vector3(20f, 150f, 0f), 0.6f) })
        {
            var lg = new GameObject("~Light");
            SceneManager.MoveGameObjectToScene(lg, scene);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Directional; l.intensity = inten;
            lg.transform.eulerAngles = rot;
        }
        var rt = new RenderTexture(ShotPx, ShotPx, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;

        var sb = new StringBuilder("몬스터 실물 사진\n");
        int n = 0;
        foreach (var e in table.entries)
        {
            if (e == null || !e.enabled || e.grade == MonsterGrade.Boss) continue;
            if (!byAddress.TryGetValue(e.addressableKey ?? "", out var path)) { sb.AppendLine($"{e.displayName}: 주소 없음 {e.addressableKey}"); continue; }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            var go = Object.Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.Euler(0f, 160f, 0f);

            var b = new Bounds(go.transform.position, Vector3.zero);
            bool has = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r.name.StartsWith("~")) continue;
                if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
            }
            float size = has ? Mathf.Max(b.size.x, b.size.y, b.size.z) : 2f;
            Vector3 c = has ? b.center : Vector3.up;
            cam.transform.position = c + new Vector3(0f, size * 0.35f, -size * 2.2f);
            cam.transform.LookAt(c);
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(ShotPx, ShotPx, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, ShotPx, ShotPx), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            string tags = e.poolTags != null ? string.Join("-", e.poolTags) : "";
            File.WriteAllBytes(Path.Combine(ShotDir, $"{(int)e.grade}_{e.displayName}_{tags}.png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(go);
            n++;
        }
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        EditorSceneManager.ClosePreviewScene(scene);
        sb.AppendLine($"찍음 {n}");
        File.WriteAllText(Path.Combine(ShotDir, "report.txt"), sb.ToString());
        Debug.Log("[MonsterRoster] 사진 끝\n" + sb);
    }

    [MenuItem("RelicFairy/Stage/10-02 챕터 로스터 적용")]
    private static void Apply()
    {
        var table = AssetDatabase.LoadAssetAtPath<MonsterSpawnTableSO>(TablePath);
        if (table == null) { Debug.LogError("[MonsterRoster] 스폰 표 없음"); return; }
        var sb = new StringBuilder("챕터 로스터 적용\n");
        var want = new Dictionary<string, int[]>();
        foreach (var (name, chapters) in Roster) want[name] = chapters;

        foreach (var e in table.entries)
        {
            if (e == null || !want.TryGetValue(e.displayName, out var tags)) continue;
            string before = e.poolTags != null ? string.Join(",", e.poolTags) : "";
            e.poolTags = tags;
            string after = string.Join(",", tags);
            if (before != after) sb.AppendLine($"{e.displayName}: [{before}] → [{after}]");
        }
        EditorUtility.SetDirty(table);
        AssetDatabase.SaveAssets();

        // 챕터 × 등급 표(확인용)
        for (int ch = 1; ch <= 4; ch++)
        {
            sb.Append($"Ch{ch}:");
            foreach (MonsterGrade g in new[] { MonsterGrade.Common, MonsterGrade.Rare, MonsterGrade.Elite })
            {
                var names = new List<string>();
                foreach (var e in table.entries)
                    if (e != null && e.enabled && e.grade == g && e.poolTags != null && System.Array.IndexOf(e.poolTags, ch) >= 0)
                        names.Add(e.displayName.Replace("Monster", ""));
                sb.Append($" {g} {names.Count}({string.Join("/", names)})");
            }
            sb.AppendLine();
        }
        File.WriteAllText("Temp/monster_roster.txt", sb.ToString());
        Debug.Log("[MonsterRoster] 로스터 적용 끝\n" + sb);
    }
}
#endif
