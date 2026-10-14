#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-11 이펙트 톤 2차(구현설계/런_이펙트_톤점검_20261011.md §3) — 프리팹을 고쳐야 하는 두 칸(1회, 편집 모드).
/// ① V-13 거점 횃불 10개(Luke Peek Fire - Calm × 0.35) — 입자 크기 모드가 Shape라 0.35가 불꽃 크기에 먹지 않아 큰 불꽃이
///    작은 받침에서 솟았다 → Hierarchy(배율이 불꽃 · 불티까지 먹는다 · PatternAttackOverrideSO.ApplyVfxHierarchyScaling과 같은 처리).
/// ② V-11 정제소 NPC 바닥 마법진(Hovl Magic circle water loop × 0.6) — 1초마다 물 · 반원 · 물보라가 다시 터졌다 →
///    그 하위(UpWater · SideWater · Semicircles · Splashes)만 끄고 원 · 반짝이 · 구슬은 잔잔한 반복으로 남긴다.
/// 다시 눌러도 같은 결과(이미 바뀐 것은 세지 않는다). 결과: Temp/vfx_tone2_fix.txt · 로그 「[VfxTone2Fix] 끝」.
/// </summary>
public static class VfxTone2PrefabFixEditor
{
    private const string SanctumPath  = "Assets/RelicFairy/Systems/Stage/StartRoom/Prefabs/BaseCamp_Sanctum.prefab";
    private const string RefineryPath = "Assets/RelicFairy/Systems/Stage/Refinery/Prefabs/RefineryNpc.prefab";
    private const string TorchMark    = "TorchFire";   // TorchFire_0~5 · TerraceTorchFire_L/R · SanctumTorchFire_L/R
    private const string CircleName   = "Circle";
    private static readonly string[] RefineryPulse = { "UpWater", "SideWater", "Semicircles", "Splashes" };

    [MenuItem("RelicFairy/Debug/10-11 이펙트 톤 2차 프리팹 손질 (편집 모드, 1회)")]
    private static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[VfxTone2Fix] 편집 모드에서"); return; }
        var sb = new StringBuilder("이펙트 톤 2차 프리팹 손질\n");
        Edit(SanctumPath, sb, root =>
        {
            int torches = 0, changed = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name.Contains(TorchMark)))
            {
                torches++;
                foreach (var ps in t.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    if (main.scalingMode == ParticleSystemScalingMode.Hierarchy) continue;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    changed++;
                }
            }
            sb.AppendLine($"  거점 횃불 {torches}개 — 입자 크기 모드 Hierarchy로 {changed}개");
            return changed;
        });
        Edit(RefineryPath, sb, root =>
        {
            var circle = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == CircleName);
            if (circle == null) { sb.AppendLine($"  정제소 마법진 「{CircleName}」 없음"); return 0; }
            int off = 0;
            foreach (var t in circle.GetComponentsInChildren<Transform>(true))
            {
                if (t == circle || !RefineryPulse.Contains(t.name) || !t.gameObject.activeSelf) continue;
                t.gameObject.SetActive(false);
                off++;
            }
            sb.AppendLine($"  정제소 마법진 — 1초 펄스 하위 끔 {off}개({string.Join(" · ", RefineryPulse)})");
            return off;
        });
        File.WriteAllText("Temp/vfx_tone2_fix.txt", sb.ToString());
        Debug.Log("[VfxTone2Fix] 끝\n" + sb);
    }

    /// <summary>프리팹을 열어 고치고, 바뀐 것이 있으면 저장한다.</summary>
    private static void Edit(string path, StringBuilder sb, System.Func<GameObject, int> fix)
    {
        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(path);
            int n = fix(root);
            if (n > 0)
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                sb.AppendLine($"  저장 {Path.GetFileName(path)}");
            }
            else sb.AppendLine($"  바꿀 것 없음 — {Path.GetFileName(path)} 저장 안 함");
        }
        catch (System.Exception e) { sb.AppendLine($"  예외({Path.GetFileName(path)}): {e}"); }
        finally
        {
            if (root != null) PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
#endif
