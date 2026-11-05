#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-05 Q32 남은 허점(사용자 「남은 개선 내용 다 진행」) 실측 — 런 안에서(플레이 중).
/// C3 방 전환 덮개(ScreenFade)를 직접 덮고 → 그 밑에서 ESC · Tab · 월드 [F] 게이트가 막히나 → 걷은 뒤 ESC 메뉴가 다시 열리나.
/// D3 도감 「써 봄」 — 아직 안 써 본 룬을 판에 놓았다 바로 빼도(도감을 안 열어도) 「써 봄」에 남나(끝나면 도감 기록을 되돌린다).
/// B1 보상 구슬 [F] 뒤 충전 연출 사이 런이 끝나는 중(사망)이면 룬 고르기 창을 열지 않나(_runEnding을 잠깐 켜고 직접 부른다 · 끝나면 끈다).
/// 결과 Temp/q32rest1105.txt · 로그 「[QR1105] 끝」.
/// </summary>
public static class Q32Rest1105ProbeEditor
{
    private const string Tag = "[QR1105]";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags Stat = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/11-05 Q32 남은 허점 실측 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning($"{Tag} 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-05 Q32 남은 허점\n");
        try
        {
            await C3Async(sb);
            D3(sb);
            await B1Async(sb);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/q32rest1105.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    // ── C3 ──
    private static async UniTask C3Async(StringBuilder sb)
    {
        var esc = Object.FindFirstObjectByType<EscKeyListener>();
        var handle = typeof(EscKeyListener).GetMethod("Handle", Inst);
        var player = GameRunBootstrapper.Instance.Run.Player;
        var toggle = player.GetType().GetMethod("TogglePuzzleGrid", Inst);
        if (esc == null || handle == null || toggle == null) { sb.AppendLine("FAIL C3 ESC 처리기 · 룬판 토글을 못 찾음"); return; }

        await ScreenFade.CoverAsync(Vector2.right, new Color(0.1f, 0.1f, 0.12f), 0.3f, null);
        bool gate = UIInputGate.Blocked;
        handle.Invoke(esc, null);
        await UniTask.Yield();
        bool menuUnder = UI_EscMenu.AnyOpen;
        if (menuUnder) Object.FindFirstObjectByType<UI_EscMenu>()?.Close();
        toggle.Invoke(player, null);
        await UniTask.Yield();
        bool gridUnder = UI_GridPanel.Instance != null && UI_GridPanel.Instance.IsOpen;
        if (gridUnder) UI_GridPanel.Instance.Close();
        sb.AppendLine($"{(gate ? "PASS" : "FAIL")} C3 덮개 동안 월드 [F] 게이트 막힘 {gate}");
        sb.AppendLine($"{(!menuUnder ? "PASS" : "FAIL")} C3 덮개 동안 ESC → 메뉴 안 열림(열림 {menuUnder})");
        sb.AppendLine($"{(!gridUnder ? "PASS" : "FAIL")} C3 덮개 동안 Tab → 룬판 안 열림(열림 {gridUnder})");

        await ScreenFade.RevealAsync(Vector2.right, 0.3f, null);
        bool gateAfter = UIInputGate.Blocked;
        handle.Invoke(esc, null);
        await UniTask.Yield();
        bool menuAfter = UI_EscMenu.AnyOpen;
        if (menuAfter) Object.FindFirstObjectByType<UI_EscMenu>()?.Close();
        await UniTask.Yield();
        sb.AppendLine($"{(!gateAfter ? "PASS" : "FAIL")} C3 걷은 뒤 게이트 풀림(막힘 {gateAfter})");
        sb.AppendLine($"{(menuAfter ? "PASS" : "FAIL")} C3 걷은 뒤 ESC → 메뉴 열림(그대로 작동)");
    }

    // ── D3 ──
    private static void D3(StringBuilder sb)
    {
        var run = GameRunBootstrapper.Instance.Run;
        var inv = run.ItemInventory;
        var data = BackendGameData.Instance?.Data;
        if (data == null) { sb.AppendLine("FAIL D3 슬롯 메타 없음"); return; }
        string before = data.codexSeen;
        RuntimeItemData item = null;
        try
        {
            foreach (var entry in Managers.ItemData.GetAllItems().Values)
            {
                var d = RuntimeItemData.FromServer(entry);
                if (d == null || d.shapeId <= 0 || CodexLog.IsSeen('r', d.itemId)) continue;
                d.EnsureElement();
                if (inv.AddToStaging(d)) item = d;
                break;
            }
            if (item == null) { sb.AppendLine("CHECK D3 안 써 본 룬이 없다(모두 써 봄)"); return; }
            inv.PlaceItem(item);
            inv.UnplaceItem(item);
            bool seen = CodexLog.IsSeen('r', item.itemId);
            sb.AppendLine($"{(seen ? "PASS" : "FAIL")} D3 놓았다 바로 뺀 룬 「{item.displayName}」 → 도감을 안 열어도 「써 봄」 {seen}");
        }
        finally
        {
            if (item != null) inv.DiscardFromStaging(item);
            data.codexSeen = before;                                                        // 실측이 남긴 기록을 되돌린다
            typeof(CodexLog).GetField("s_dirty", Stat)?.SetValue(null, false);
        }
    }

    // ── B1 ──
    private static async UniTask B1Async(StringBuilder sb)
    {
        var grb = GameRunBootstrapper.Instance;
        var run = grb.Run;
        var ending = typeof(GameRunBootstrapper).GetField("_runEnding", Inst);
        var open = typeof(ClearRewardTrigger).GetMethod("OpenRewardFlowAsync", Inst);
        if (ending == null || open == null) { sb.AppendLine("FAIL B1 _runEnding · OpenRewardFlowAsync를 못 찾음"); return; }

        var rewards = new List<(RuntimeItemData data, ItemSO so)>();
        foreach (var entry in Managers.ItemData.GetAllItems().Values)
        {
            var d = RuntimeItemData.FromServer(entry);
            if (d == null || d.shapeId <= 0) continue;
            d.EnsureElement();
            rewards.Add((d, ItemSORegistry.Find(d.itemId)));
            if (rewards.Count >= 3) break;
        }
        var go = new GameObject("QR1105_Reward");
        go.transform.position = run.Player.transform.position + Vector3.forward * 40f;
        var trig = go.AddComponent<ClearRewardTrigger>();
        trig.Initialize(run, rewards, isChoice: true);
        ending.SetValue(grb, true);
        try
        {
            open.Invoke(trig, null);
            await UniTask.Delay(2500, DelayType.Realtime);
            var popup = Object.FindFirstObjectByType<UI_RuneSelectPopup>();
            bool opened = popup != null && popup.isActiveAndEnabled;
            sb.AppendLine($"{(!opened ? "PASS" : "FAIL")} B1 충전 연출 사이 쓰러짐(런 끝내는 중) → 룬 고르기 창 안 열림(열림 {opened})");
        }
        finally
        {
            ending.SetValue(grb, false);
            if (go != null) Object.Destroy(go);
        }
    }
}
#endif
