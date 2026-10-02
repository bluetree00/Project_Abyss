#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-02 「한 장의 서약서」 U1 실측 — 제단 판 세 가지 모습을 게임 화면으로 찍는다(Temp/ui_shots/CovWrite_*).
/// ① 첫 쓰기(문장 없음 — 조건 × 첫 결과) ② 이어 쓰기(2절 문장 · 둘째 카드 고름) ③ 고쳐 쓰기(첫 결과 줄 [고쳐]) ④ 가득 찬 문장(5절).
/// 판은 닫고 서약 목록은 원래대로 돌린다. 로그 「[CovWriteProbe] 끝」. ⚠️ 런 중(대기방이면 충분), 플레이 중.
/// </summary>
public static class CovenantWriteUiProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/10-02 서약서 쓰기 판 촬영 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.CovenantHandler == null)
        {
            Debug.LogWarning("[CovWriteProbe] 런 중에 실행해야 한다.");
            return;
        }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("서약서 쓰기 판 촬영\n");
        var handler = GameRunBootstrapper.Instance.Run.CovenantHandler;
        var list = (List<CovenantBase>)typeof(CovenantHandler).GetField("_covenants", Inst).GetValue(handler);
        var backup = new List<string>();
        foreach (var c in list) backup.Add(c.CovenantId);
        try
        {
            // ① 첫 쓰기
            Clear(list);
            var cts = new CancellationTokenSource();
            CovenantAssembleUI.ChooseAsync(false, new System.Random(7), cts.Token).Forget();
            await UniTask.Delay(1200, ignoreTimeScale: true);
            await Shot("CovWrite_1_First");
            sb.AppendLine("① 첫 쓰기 판 " + (Popup() != null ? "열림" : "없음"));
            ClosePopup(); cts.Cancel();
            await UniTask.Delay(400, ignoreTimeScale: true);

            // ② 이어 쓰기 — 2절 문장
            Clear(list);
            handler.TryWriteSentence("sen:streak@gold>supernova@silver>ember@ruby~i");
            var board = new CovenantWriteBoard(11);
            cts = new CancellationTokenSource();
            CovenantAssembleUI.WriteAsync(handler.Sentence, board, cts.Token).Forget();
            await UniTask.Delay(1200, ignoreTimeScale: true);
            var popup = Popup();
            sb.AppendLine("② 이어 쓰기 판 " + (popup != null ? "열림" : "없음") + $" · 카드 {board.Append?.Count ?? 0}");
            if (popup != null) Invoke(popup, "SelectWrite", 1);
            await UniTask.Delay(300, ignoreTimeScale: true);
            await Shot("CovWrite_2_Append");

            // ③ 고쳐 쓰기 — 첫 결과 줄
            if (popup != null) Invoke(popup, "OnFix", 1);
            await UniTask.Delay(300, ignoreTimeScale: true);
            await Shot("CovWrite_3_Rewrite");
            sb.AppendLine("③ 고쳐 쓰기 카드 " + (board.Rewrite.TryGetValue(1, out var rw) ? rw.Count : 0));
            ClosePopup(); cts.Cancel();
            await UniTask.Delay(400, ignoreTimeScale: true);

            // ④ 가득 찬 문장
            Clear(list);
            handler.TryWriteSentence("sen:streak@silver>ember@silver>detonate@gold~i>fury@silver~i>supernova@ruby~w>curse@silver~i");
            cts = new CancellationTokenSource();
            CovenantAssembleUI.WriteAsync(handler.Sentence, new CovenantWriteBoard(13), cts.Token).Forget();
            await UniTask.Delay(1200, ignoreTimeScale: true);
            await Shot("CovWrite_4_Full");
            sb.AppendLine("④ 가득 찬 문장 판 " + (Popup() != null ? "열림" : "없음"));
            ClosePopup(); cts.Cancel();
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Clear(list);
            foreach (var id in backup) if (id.StartsWith(CovenantSentence.Prefix)) handler.TryWriteSentence(id); else handler.TryAdd(id);
            File.WriteAllText("Temp/covenant_write_ui_probe.txt", sb.ToString());
            Debug.Log("[CovWriteProbe] 끝\n" + sb);
        }
    }

    private static void Clear(List<CovenantBase> list)
    {
        foreach (var c in list) c.Dispose();
        list.Clear();
    }

    private static UI_CovenantAssemble Popup() => Object.FindFirstObjectByType<UI_CovenantAssemble>();

    private static void ClosePopup()
    {
        var p = Popup();
        if (p != null) p.ClosePopupUI();
    }

    private static void Invoke(object target, string method, int arg)
        => target.GetType().GetMethod(method, Inst)?.Invoke(target, new object[] { arg });

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 300 }) : UniTask.CompletedTask;
    }
}
#endif
