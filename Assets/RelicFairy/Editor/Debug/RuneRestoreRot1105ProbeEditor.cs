#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-05 #15 사용자 「다시 게임을 시작했을 때 룬이 투명해지고 변경할 수 없다」 — 이어하기 순서 재현(런 중).
/// 이어하기는 꺼진 판(gridHost) 아래에 모양을 짓고 ApplyAsset → BindItem(회전)을 마친 뒤 판이 켜진다 — 그때 늦은 Shape.Awake가 모양을 다시 지으면
/// 회전이 0으로 풀리고 블록이 어긋난다(놓인 칸엔 바탕 그림만 남아 「투명한 룬」 · 끌 수 없음). 꺼진 부모 아래 짓기 → 회전 1 → 부모 켜기 → 회전 · 칸 · 블록 수.
/// 결과 Temp/runerot1105.txt · 로그 「[RR1105] 끝」.
/// </summary>
public static class RuneRestoreRot1105ProbeEditor
{
    private const string Tag = "[RR1105]";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/11-05 이어하기 룬 회전 실측 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning($"{Tag} 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-05 이어하기 룬 회전\n");
        GameObject host = null;
        RuntimeItemData item = null;
        var inv = GameRunBootstrapper.Instance.Run.ItemInventory;
        try
        {
            // 가로 · 세로가 다른 일반 룬 — 돌리면 칸이 바뀐다
            foreach (var entry in Managers.ItemData.GetAllItems().Values)
            {
                var d = RuntimeItemData.FromServer(entry);
                if (d == null || d.shapeId <= 0 || d.rarity == ItemRarity.Legendary) continue;
                var offs = RuneDataManager.ParseCellOffsets(Managers.RuneData.GetPiece(d.shapeId))?.ToList();
                if (offs == null || offs.Count < 3 || offs.Count > 4) continue;
                int w = offs.Max(o => o.x) - offs.Min(o => o.x), h = offs.Max(o => o.y) - offs.Min(o => o.y);
                if (w == h) continue;
                d.EnsureElement();
                if (!inv.AddToStaging(d)) break;
                item = d;
                break;
            }
            if (item == null) { sb.AppendLine("가로 · 세로가 다른 룬 없음"); return; }

            if (UI_GridPanel.Instance == null) Managers.UI?.ShowOverlayUI<UI_GridPanel>();
            var panel = UI_GridPanel.Instance;
            panel.ShowWithNewItem(null);
            await UniTask.Delay(1200, ignoreTimeScale: true);
            var staging = typeof(UI_GridPanel).GetField("_stagingArea", Inst)?.GetValue(panel) as StagingAreaView;
            var asset = staging?.GetShapeForItem(item)?.shapeAsset;
            if (asset == null) { sb.AppendLine("모양 에셋 없음"); return; }

            // 이어하기와 같은 순서 — 꺼진 부모 아래 짓기 → ApplyAsset → BindItem(회전 1) → 부모 켜기
            host = new GameObject("RR1105Host", typeof(RectTransform));
            host.transform.SetParent(panel.transform, false);
            host.SetActive(false);
            item.rotation = 1;
            var s = Object.Instantiate(BoardManager.Instance.shapePrefab, host.transform);
            s.ApplyAsset(asset);
            s.BindItem(item);
            var before = new List<Vector2Int>(s.cellOffsets);
            sb.AppendLine($"룬 「{item.displayName}」 · 켜기 전 회전 {s.Rotation} · 칸 [{string.Join(" ", before)}]");
            host.SetActive(true);
            await UniTask.Yield();
            await UniTask.Yield();
            bool rotOk = s.Rotation == 1 && item.rotation == 1;
            bool cellsOk = s.cellOffsets.SequenceEqual(before);
            bool blocksOk = s.transform.childCount == s.cellOffsets.Count;
            sb.AppendLine($"{(rotOk ? "PASS" : "FAIL")} 켠 뒤 회전 {s.Rotation}(1이어야) · 아이템 방향 {item.rotation}");
            sb.AppendLine($"{(cellsOk ? "PASS" : "FAIL")} 켠 뒤 칸 [{string.Join(" ", s.cellOffsets)}] = 켜기 전");
            sb.AppendLine($"{(blocksOk ? "PASS" : "FAIL")} 블록 수 {s.transform.childCount} = 칸 수 {s.cellOffsets.Count}");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (host != null) Object.Destroy(host);
            if (item != null) { item.rotation = 0; inv.DiscardFromStaging(item); }
            File.WriteAllText("Temp/runerot1105.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }
}
#endif
