#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-30 사용자 「플레이어의 이펙트가 보스의 패턴과 가이드라인을 가려서 잘 안 보인다」 전후 사진 — 화룡방 Fight Sim 중에 부른다.
/// 화염구 비를 걸고 예고 칸이 30개 넘게 깔리면, 화면 안 예고 칸 세 곳에 플레이어 바닥 효과(전설 얼음 장판 · 빛 오라 · 전기 장판 — 정렬 순서 0~5)를 겹쳐 놓고 찍는다.
/// 결과 Temp/guide_cover/cover.png · view.txt(예고 칸 · 효과 렌더러 정렬 순서) · 로그 「[GuideCover] 끝」.
/// </summary>
public static class GuideCoverProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const string Dir = "Temp/guide_cover";
    private const string Fx  = "Assets/RelicFairy/_Imported/EffectSource/";
    private static readonly string[] Effects =
    {
        Fx + "SpecialSkillsEffectsPack/AllEffects/EffectsSet_1(NotScriptBased)/Effects/Effect_25_IceField/Effect_25_IceField.prefab",
        Fx + "Spells Pack/LWRP(URP)/Particles_LWRP/Prefabs/Auras/Aura_Light_LWRP.prefab",
        Fx + "SpecialSkillsEffectsPack/AllEffects/EffectsSet_2(ScriptBased)/Effects/Effect_16_ElectricField/Effect_16_ElectricField.prefab",
    };

    private static bool s_player47;   // 47 칸 — 효과를 플레이어 경로(룬 오라 · 증강 검기)로 낸다(PlayerFxCalm이 먹는 길)

    [MenuItem("RelicFairy/Debug/10-30 보스 예고 위 플레이어 효과 가림 사진 (화룡방 Fight Sim 중, Play)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[GuideCover] 플레이 중에"); return; }
        s_player47 = false;
        RunAsync().Forget();
    }

    [MenuItem("RelicFairy/Debug/10-30 보스 예고 가림 — 47 플레이어 경로 룬 오라 · 증강 검기 사진 (화룡방 Fight Sim 중, Play)")]
    private static void Run47()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[GuideCover] 플레이 중에"); return; }
        s_player47 = true;
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        Directory.CreateDirectory(Dir);
        var sb = new StringBuilder("보스 예고 위 플레이어 효과\n");
        var spawned = new List<GameObject>();
        try
        {
            DragonBossMonster dragon = null;
            for (float until = Time.realtimeSinceStartup + 40f; Time.realtimeSinceStartup < until && dragon == null; )
            {
                dragon = UnityEngine.Object.FindFirstObjectByType<DragonBossMonster>();
                if (dragon == null) await UniTask.Delay(250, DelayType.Realtime);
            }
            if (dragon == null) { sb.AppendLine("화룡 없음"); return; }
            for (float until = Time.realtimeSinceStartup + 20f; Time.realtimeSinceStartup < until; )
            {
                var r = GameRunBootstrapper.Instance?.Run;
                bool cut = r != null && r.TryGetHudMode(out var mode) && (mode == HUDIds.Mode.Cutscene || mode == HUDIds.Mode.BossCutscene);
                if (!cut) break;
                await UniTask.Delay(250, DelayType.Realtime);
            }
            await UniTask.Delay(1500, DelayType.Realtime);
            var p = GameRunBootstrapper.Instance?.Run?.Player;
            if (p != null) typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);

            var runner = typeof(DragonBossMonster).GetField("_runner", Inst)?.GetValue(dragon);
            var cfg = runner?.GetType().GetField("_config", Inst)?.GetValue(runner) as BossConfigSO;
            DragonFireballRainPatternSO rain = null;
            if (cfg != null)
                foreach (var e in cfg.patternEntries)
                    foreach (var pat in e.patterns)
                        if (pat is DragonFireballRainPatternSO rr) rain = rr;
            var st = rain != null ? rain.GetRuntimeState() : null;
            if (st == null) { sb.AppendLine("화염구 비 없음"); return; }
            dragon.ChangeState(st);

            var cam = Camera.main;
            List<Transform> tiles = null;
            for (float until = Time.realtimeSinceStartup + 20f; Time.realtimeSinceStartup < until; )
            {
                tiles = VisibleTiles(cam);
                if (tiles.Count >= 30) break;
                await UniTask.Delay(100, DelayType.Realtime);
            }
            if (tiles == null || tiles.Count == 0) { sb.AppendLine("화면 안 예고 칸 없음"); return; }

            // 서로 3 m 넘게 떨어진 칸 세 곳(화면 가운데 가까운 순)
            tiles.Sort((a, b) => ViewDist(cam, a).CompareTo(ViewDist(cam, b)));
            var picks = new List<Vector3>();
            foreach (var t in tiles)
            {
                bool far = true;
                foreach (var q in picks) if ((q - t.position).sqrMagnitude < 9f) far = false;
                if (far) picks.Add(t.position);
                if (picks.Count == Effects.Length) break;
            }
            if (s_player47)
            {
                var calm = Type.GetType("PlayerFxCalm");
                sb.AppendLine($"47 플레이어 경로 — PlayerFxCalm {(calm != null ? "있음" : "없음(고치기 전)")}");
                var els = new[] { RuneElement.Ice, RuneElement.Grass, RuneElement.Electric };
                for (int i = 0; i < picks.Count && i < els.Length; i++)
                {
                    var anchor = new GameObject("~Cover47_" + els[i]);
                    anchor.transform.position = picks[i] + Vector3.up * 0.05f;
                    spawned.Add(anchor);
                    ElementVfxPlayer.AttachAura(els[i], anchor.transform, 8f, 2f);
                }
                await UniTask.Delay(500, DelayType.Realtime);
                var root = GameObject.Find("[ElementVfxPlayer]");
                if (root != null)
                    foreach (Transform c in root.transform)
                    {
                        if (!c.gameObject.activeInHierarchy) continue;
                        int lo = int.MaxValue, hi = int.MinValue;
                        float a = -1f;
                        foreach (var rr in c.GetComponentsInChildren<Renderer>(true)) { lo = Mathf.Min(lo, rr.sortingOrder); hi = Mathf.Max(hi, rr.sortingOrder); }
                        var ps0 = c.GetComponentInChildren<ParticleSystem>(true);
                        if (ps0 != null) a = ps0.main.startColor.mode == ParticleSystemGradientMode.Color ? ps0.main.startColor.color.a : ps0.main.startColor.colorMax.a;
                        sb.AppendLine($"룬 이펙트 {c.name} · 정렬 순서 {lo}~{hi} · 첫 입자 시작 알파 {a:0.00}");
                    }
                if (picks.Count > 0) RelicStateVfx.PlayOneShot("vfx_form_windcut_red", picks[0], 1.5f, Vector3.forward);
                await UniTask.Delay(250, DelayType.Realtime);
            }
            for (int i = 0; i < picks.Count && !s_player47; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Effects[i]);
                if (prefab == null) { sb.AppendLine("효과 없음 " + Effects[i]); continue; }
                var go = UnityEngine.Object.Instantiate(prefab, picks[i] + Vector3.up * 0.05f, Quaternion.identity);
                go.transform.localScale = Vector3.one * 1.5f;
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) { var m = ps.main; m.loop = true; ps.Play(); }
                spawned.Add(go);
                int maxOrder = int.MinValue;
                foreach (var rr in go.GetComponentsInChildren<Renderer>(true)) maxOrder = Mathf.Max(maxOrder, rr.sortingOrder);
                sb.AppendLine($"효과 {prefab.name} @ {picks[i]:F0} · 렌더러 정렬 순서 최대 {maxOrder}");
            }
            var tile0 = tiles[0].GetComponent<MeshRenderer>();
            sb.AppendLine($"예고 칸 {tiles.Count}개(화면 안) · 정렬 순서 {tile0.sortingOrder} · 큐 {tile0.sharedMaterial.renderQueue}");
            var sig = typeof(PatternGuideHelper).GetProperty("AnyBossTelegraph", BindingFlags.Public | BindingFlags.Static);
            sb.AppendLine($"AnyBossTelegraph(화염구 비 중) {(sig != null ? sig.GetValue(null) : "없음")}");
            var guide = GameObject.Find("Guide_Disc") ?? GameObject.Find("Guide_Sector") ?? GameObject.Find("Guide_Beam");
            if (guide != null && guide.TryGetComponent<MeshRenderer>(out var gmr)) sb.AppendLine($"가이드 {guide.name} · 정렬 순서 {gmr.sortingOrder} · 큐 {gmr.sharedMaterial.renderQueue}");

            await UniTask.Delay(700, DelayType.Realtime);
            ScreenCapture.CaptureScreenshot(s_player47 ? $"{Dir}/cover_47.png" : $"{Dir}/cover.png");
            if (s_player47)
            {
                var roots = new Dictionary<Transform, (int n, float size)>();
                foreach (var ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                {
                    if (ps.particleCount <= 0 || !ps.TryGetComponent<ParticleSystemRenderer>(out var pr)) continue;
                    var v = cam.WorldToViewportPoint(ps.transform.position);
                    if (v.z <= 0f || v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f) continue;
                    var top = ps.transform;
                    while (top.parent != null && !top.name.EndsWith("(Clone)")) top = top.parent;
                    var b = pr.bounds;
                    float size = Mathf.Max(b.size.x, b.size.z);
                    roots.TryGetValue(top, out var cur);
                    roots[top] = (cur.n + ps.particleCount, Mathf.Max(cur.size, size));
                }
                var list = new List<KeyValuePair<Transform, (int n, float size)>>(roots);
                list.Sort((a, b) => b.Value.size.CompareTo(a.Value.size));
                for (int i = 0; i < list.Count && i < 12; i++)
                {
                    var tr = list[i].Key;
                    string owner = tr.root != null ? tr.root.name : "-";
                    sb.AppendLine($"화면 입자 {tr.name} · 맨 위 {owner} · 입자 {list[i].Value.n} · 가로 {list[i].Value.size:0.0} m");
                }
            }
            await UniTask.Delay(400, DelayType.Realtime);
            if (s_player47 && sig != null)   // 예고가 사라지면 신호도 꺼지나(77 부탁)
            {
                bool off = false;
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 15f && !(off = !(bool)sig.GetValue(null))) await UniTask.Delay(100, DelayType.Realtime);
                sb.AppendLine($"찍은 뒤 AnyBossTelegraph 꺼짐 {off}({Time.realtimeSinceStartup - t0:0.0}초 · 예고 칸 {VisibleTiles(cam).Count}개)");
            }
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            foreach (var go in spawned) if (go != null) UnityEngine.Object.Destroy(go);
            File.WriteAllText(s_player47 ? $"{Dir}/view_47.txt" : $"{Dir}/view.txt", sb.ToString(), Encoding.UTF8);
            Debug.Log($"[GuideCover] 끝\n{sb}");
        }
    }

    private static List<Transform> VisibleTiles(Camera cam)
    {
        var list = new List<Transform>();
        var qp = GameObject.Find("@QuadTilePool");
        if (qp == null || cam == null) return list;
        foreach (Transform t in qp.transform)
        {
            if (t.name != "FireballWarn" || !t.TryGetComponent<MeshRenderer>(out var mr) || !mr.enabled) continue;
            var v = cam.WorldToViewportPoint(t.position);
            if (v.z > 0f && v.x > 0.2f && v.x < 0.8f && v.y > 0.2f && v.y < 0.8f) list.Add(t);
        }
        return list;
    }

    private static float ViewDist(Camera cam, Transform t)
    {
        var v = cam.WorldToViewportPoint(t.position);
        return (new Vector2(v.x, v.y) - new Vector2(0.5f, 0.5f)).sqrMagnitude;
    }
}
#endif
