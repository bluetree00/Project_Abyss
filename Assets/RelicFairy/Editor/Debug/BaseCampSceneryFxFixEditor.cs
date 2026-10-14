#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-11 거점 배경 이펙트 결함 고침(1회) — 이펙트 톤 점검(구현설계/런_이펙트_톤점검_20261011.md D-6) 실측:
/// ① 심연 포탈 소용돌이 PortalVortex — 원본 입자계가 전부 반복 꺼짐(길이 9초)이라 로드 뒤 약 10초에 사라진다(로드 10.4초에 살아 있는 입자 0).
/// ② 원경 에너지체 EnergyOrb ×12 — 팩 스크립트 ObjectMoveDestroy(isDestroy)가 1초 뒤 스스로 터뜨린다(남은 수 0/12).
/// BaseCamp_Sanctum 프리팹을 열어 ① 입자계 반복을 켜고 ② 자폭 스크립트를 끄고(반복 안 하는 입자계는 반복으로) 저장한다.
/// 결과: Temp/basecamp_fx_fix.txt · 로그 「[BaseCampFxFix] 끝」.
/// </summary>
public static class BaseCampSceneryFxFixEditor
{
    private const string PrefabPath = "Assets/RelicFairy/Systems/Stage/StartRoom/Prefabs/BaseCamp_Sanctum.prefab";

    [MenuItem("RelicFairy/Debug/10-11 거점 포탈 · 에너지체 이펙트 고침 (편집 모드, 1회)")]
    private static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[BaseCampFxFix] 편집 모드에서"); return; }
        var sb = new StringBuilder("거점 배경 이펙트 고침\n");
        GameObject root = null;
        int loops = 0, scripts = 0;
        try
        {
            root = PrefabUtility.LoadPrefabContents(PrefabPath);
            var all = root.GetComponentsInChildren<Transform>(true);

            var vortex = all.FirstOrDefault(t => t.name == "PortalVortex");
            if (vortex == null) sb.AppendLine("  PortalVortex 없음");
            else
            {
                foreach (var ps in vortex.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    if (main.loop) continue;
                    main.loop = true;
                    loops++;
                }
                sb.AppendLine($"  PortalVortex — 입자계 반복 켬 {loops}개");
            }

            int orbs = 0, orbLoops = 0;
            foreach (var t in all.Where(t => t.name.StartsWith("EnergyOrb")))
            {
                orbs++;
                foreach (var mb in t.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null || mb.GetType().Name != "ObjectMoveDestroy" || !mb.enabled) continue;
                    mb.enabled = false;   // 원경 장식 — 움직이거나 터지지 않고 그 자리에 떠 있어야 한다
                    scripts++;
                }
                foreach (var ps in t.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    if (main.loop) continue;
                    main.loop = true;
                    orbLoops++;
                }
            }
            sb.AppendLine($"  EnergyOrb {orbs}개 — 자폭 스크립트 끔 {scripts} · 입자계 반복 켬 {orbLoops}");

            if (loops + scripts + orbLoops > 0)
            {
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                sb.AppendLine("  프리팹 저장");
            }
            else sb.AppendLine("  바꿀 것 없음 — 저장 안 함");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (root != null) PrefabUtility.UnloadPrefabContents(root);
            File.WriteAllText("Temp/basecamp_fx_fix.txt", sb.ToString());
            Debug.Log("[BaseCampFxFix] 끝\n" + sb);
        }
    }
}
#endif
