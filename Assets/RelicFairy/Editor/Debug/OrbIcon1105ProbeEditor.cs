#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 11-05 사용자 「룬 획득 오브젝트 위 그림이 레거시 룬 이미지」 실측 — 런 안에서(플레이 중).
/// 플레이어 앞에 보상 구슬 표시판 둘(고르는 보상 3장 · 한 장 보상)을 세우고 그림이 옛 등급 돌판(룬1~5)이 아닌가 ·
/// 고르는 보상은 후보마다 속성 블록 타일(룬판과 같은 그림)인가 · 한 장 보상은 그 룬 문양인가를 보고 사진을 찍는다.
/// 결과 Temp/orbicon1105.txt · Temp/ui_shots/OI1105.png · 로그 「[OI1105] 끝」.
/// </summary>
public static class OrbIcon1105ProbeEditor
{
    private const string Tag = "[OI1105]";

    [MenuItem("RelicFairy/Debug/11-05 보상 구슬 표시판 그림 실측 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning($"{Tag} 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-05 보상 구슬 표시판 그림\n");
        var made = new List<GameObject>();
        try
        {
            var run = GameRunBootstrapper.Instance.Run;
            var p = run.Player.transform;
            var cam = Camera.main != null ? Camera.main.transform : p;
            var legacy = new HashSet<string> { "룬1", "룬2", "룬3", "룬4", "룬5" };
            var tileNames = new HashSet<string>();
            foreach (var el in ElementDef.Order) { var t = RuneArt.GetBlockTile(el); if (t != null) tileNames.Add(t.name); }

            var picks = new List<(RuntimeItemData data, ItemSO so)>();
            foreach (var entry in Managers.ItemData.GetAllItems().Values)
            {
                var d = RuntimeItemData.FromServer(entry);
                if (d == null || d.shapeId <= 0) continue;
                d.EnsureElement();
                if (picks.Any(x => x.data.element == d.element)) continue;   // 속성이 다른 셋
                picks.Add((d, ItemSORegistry.Find(d.itemId)));
                if (picks.Count >= 4) break;
            }
            if (picks.Count < 4) { sb.AppendLine($"FAIL 후보 룬 {picks.Count}개"); return; }

            Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized, right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
            var choice = Spawn("OI1105_Choice", p.position + fwd * 5f - right * 1.6f, run, picks.Take(3).ToList(), true, made);
            var single = Spawn("OI1105_Single", p.position + fwd * 5f + right * 1.8f, run, picks.Skip(3).Take(1).ToList(), false, made);
            await UniTask.Delay(1200, DelayType.Realtime);

            var cImgs = choice.GetComponentsInChildren<Image>(true).Where(i => i.name != "IconBG").ToList();
            var cNames = cImgs.Select(i => i.sprite != null ? i.sprite.name : "(없음)").ToList();
            bool cLegacy = cNames.Any(legacy.Contains);
            bool cTiles  = cNames.Count == 3 && cNames.All(tileNames.Contains);
            sb.AppendLine($"{(!cLegacy ? "PASS" : "FAIL")} 고르는 보상 — 옛 돌판 아님 [{string.Join(", ", cNames)}]");
            sb.AppendLine($"{(cTiles ? "PASS" : "FAIL")} 고르는 보상 — 후보 3장 = 속성 블록 타일 3칸 [{string.Join(", ", picks.Take(3).Select(x => x.data.element))}]");

            var sImg = single.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.name == "Icon");
            var want = RuneArt.ResolveRuneIcon(picks[3].data);
            string sName = sImg != null && sImg.sprite != null ? sImg.sprite.name : "(없음)";
            bool sOk = sImg != null && want != null && sImg.sprite == want;
            sb.AppendLine($"{(sOk ? "PASS" : "FAIL")} 한 장 보상 — 그 룬 문양 「{sName}」(기대 「{(want != null ? want.name : "-")}」 · 룬 {picks[3].data.displayName})");

            Directory.CreateDirectory("Temp/ui_shots");
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/OI1105.png");
            await UniTask.Delay(500, DelayType.Realtime);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            foreach (var go in made) if (go != null) Object.Destroy(go);
            File.WriteAllText("Temp/orbicon1105.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    private static GameObject Spawn(string name, Vector3 at, GameRunSession run, List<(RuntimeItemData data, ItemSO so)> rewards, bool choice, List<GameObject> made)
    {
        var go = new GameObject(name);
        go.transform.position = at;
        made.Add(go);
        go.AddComponent<ClearRewardTrigger>().Initialize(run, rewards, isChoice: choice);
        return go;
    }
}
#endif
