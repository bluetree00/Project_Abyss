#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q13 마우스 커서 실측(테스트 허브 런 중) — 결과 Temp/game_cursor1102.txt · 커서 그림 Temp/ui_shots/GC1102_cursors.png · HUD 사진 GC1102_hud.png · 로그 「[GC1102] 끝」.
///  · 커서 셋(기본 · 조준 · 상호작용)이 만들어졌나 · 크기 · 그림을 한 장으로.
///  · 분류: 슬라임(소환) 콜라이더 = 조준 · 보상 구슬(EndEffect2) = 상호작용 · 바닥 = 기본.
///  · 씬의 상호작용 물체마다 콜라이더 수 · 가장 큰 크기 — 방만 한 콜라이더가 있으면 그 위 어디서나 금 화살표가 된다(점검).
/// </summary>
public static class GameCursor1102ProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags SAny = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/11-02 마우스 커서 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[GC1102] 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-02 마우스 커서\n");
        Directory.CreateDirectory("Temp/ui_shots");
        MonsterBase slime = null;
        GameObject orb = null;
        try
        {
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/GC1102_hud.png");
            await UniTask.Delay(300, ignoreTimeScale: true);
            var gc = Object.FindFirstObjectByType<GameCursor>();
            if (gc == null) { sb.AppendLine("GameCursor 없음"); return; }
            var tex = typeof(GameCursor).GetField("_tex", Inst).GetValue(gc) as Texture2D[];
            var hot = typeof(GameCursor).GetField("_hot", Inst).GetValue(gc) as Vector2[];
            sb.AppendLine($"커서 셋 — 기본 {Desc(tex[0], hot[0])} · 조준 {Desc(tex[1], hot[1])} · 상호작용 {Desc(tex[2], hot[2])} · 지금 {typeof(GameCursor).GetField("_shown", Inst).GetValue(gc)}");
            SaveSheet(tex);

            var run = GameRunBootstrapper.Instance?.Run;
            var p = run?.Player;
            var kindOf = typeof(GameCursor).GetMethod("KindOf", Inst);
            if (p != null)
            {
                Vector3 at = p.transform.position + p.transform.right * 4f;
                if (UnityEngine.AI.NavMesh.SamplePosition(at, out var nh, 4f, UnityEngine.AI.NavMesh.AllAreas)) at = nh.position;
                try { slime = await Managers.ObjectPooler.SpawnAsync<MonsterBase>("Slime/Slime", ObjectPoolerManager.PoolType.Monster, at, Quaternion.identity); }
                catch (System.Exception e) { sb.AppendLine($"슬라임 소환 실패: {e.Message}"); }
                await UniTask.Delay(800, ignoreTimeScale: true);
                if (slime != null)
                    foreach (var c in slime.GetComponentsInChildren<Collider>())
                        sb.AppendLine($"  슬라임 콜라이더 {c.name}(트리거 {c.isTrigger} · 크기 {c.bounds.size.magnitude:0.0} m) → {kindOf.Invoke(gc, new object[] { c })}");

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RelicFairy/Systems/Stage/UseEffect/EndEffect2.prefab");
                var pool = ItemSORegistry.GetByRarity(ItemRarity.Common);
                if (prefab != null && pool != null && pool.Count > 0)
                {
                    orb = Object.Instantiate(prefab, p.transform.position - p.transform.right * 5f + Vector3.up * 0.6f, Quaternion.identity);
                    orb.AddComponent<ClearRewardTrigger>().Initialize(run, new List<(RuntimeItemData, ItemSO)> { (null, pool[0]) }, isChoice: true);
                    await UniTask.Delay(300, ignoreTimeScale: true);
                    sb.AppendLine($"  보상 구슬 콜라이더 → {kindOf.Invoke(gc, new object[] { orb.GetComponent<Collider>() })}");
                }
                if (Physics.Raycast(p.transform.position + Vector3.up, Vector3.down, out var floor, 5f, ~0, QueryTriggerInteraction.Ignore))
                    sb.AppendLine($"  바닥 {floor.collider.name} → {kindOf.Invoke(gc, new object[] { floor.collider })}");
            }

            sb.AppendLine("[씬의 상호작용 물체 — 콜라이더 수 · 가장 큰 크기(방만 하면 점검)]");
            var types = typeof(GameCursor).GetField("InteractTypes", SAny).GetValue(null) as System.Type[];
            foreach (var t in types)
                foreach (var o in Object.FindObjectsByType(t, FindObjectsSortMode.None))
                {
                    var comp = (Component)o;
                    float big = 0f; int n = 0;
                    foreach (var c in comp.GetComponentsInChildren<Collider>()) { n++; big = Mathf.Max(big, c.bounds.size.magnitude); }
                    sb.AppendLine($"  {t.Name} {comp.name} — 콜라이더 {n} · 가장 큰 {big:0.0} m{(big > 12f ? "  ⚠ 큼" : "")}");
                }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (slime != null && !slime.IsDead) slime.TakeSynergyDamage(999999f, null);
            if (orb != null) Object.Destroy(orb);
            File.WriteAllText("Temp/game_cursor1102.txt", sb.ToString());
            Debug.Log("[GC1102] 끝 → Temp/game_cursor1102.txt");
        }
    }

    private static string Desc(Texture2D t, Vector2 h) => t != null ? $"{t.width}px(끝 {h.x:0},{h.y:0})" : "없음";

    /// <summary>커서 셋을 어두운 · 밝은 바탕 위에 4배로 한 장에.</summary>
    private static void SaveSheet(Texture2D[] tex)
    {
        int px = tex[0] != null ? tex[0].width : 32, z = 4, w = px * z;
        var sheet = new Texture2D(w * 3, w * 2, TextureFormat.RGBA32, false);
        var bg = new[] { new Color(0.12f, 0.16f, 0.14f), new Color(0.78f, 0.78f, 0.74f) };
        for (int row = 0; row < 2; row++)
        for (int i = 0; i < 3; i++)
        for (int y = 0; y < w; y++)
        for (int x = 0; x < w; x++)
        {
            var c = tex[i] != null ? tex[i].GetPixel(x / z, y / z) : Color.clear;
            sheet.SetPixel(i * w + x, row * w + y, Color.Lerp(bg[row], new Color(c.r, c.g, c.b, 1f), c.a));
        }
        sheet.Apply();
        File.WriteAllBytes("Temp/ui_shots/GC1102_cursors.png", sheet.EncodeToPNG());
        Object.Destroy(sheet);
    }
}
#endif
