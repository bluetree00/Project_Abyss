#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-02 기억의 제단 「업적」 탭 실측 — 사용자 「업적도 텍스트가 겹치고 레이아웃도 이상해지는 문제 · 완료된 업적이 있다면 표기」.
/// 세 상태를 찍고 행마다 글자 겹침 · 행 밖으로 넘친 글자 · 행끼리 겹침을 센다:
///   ① 지금 계정 그대로 ② 「받음」 펼침 ③ 기록을 메모리에서 크게 올려 수령 가능이 여럿인 상태.
/// <b>세이브를 건드리지 않는다</b> — 퀘스트 저장을 막고(_suppressSave) 기록 · 정수를 끝나면 되돌린다. 실측 뒤 플레이를 멈출 것.
/// 결과: Temp/achievement_tab_probe.txt · Temp/ui_shots/Ach_*.png
/// </summary>
public static class AchievementTabProbeEditor
{
    private const BindingFlags NonPub  = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags SNonPub = BindingFlags.Static | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/UI/10-02 업적 탭 실측 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[AchTabProbe] 플레이 모드(로그인 뒤)에서 실행해야 한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb   = new StringBuilder("업적 탭 실측\n");
        var data = BackendGameData.Instance?.Data;
        var quest = Managers.Quest;
        if (data == null || quest == null) { Debug.LogWarning("[AchTabProbe] 계정 · 퀘스트 데이터가 없다."); return; }

        string keepRecords = data.records;
        int    keepEssence = data.abyssEssence;
        var suppress = typeof(QuestManager).GetField("_suppressSave", NonPub);
        suppress?.SetValue(quest, true);
        Probe("HideTestHubGui");
        try
        {
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_AwakeningPanel>();
            typeof(UI_AwakeningPanel).GetMethod("SetMode", NonPub)?.Invoke(panel, new object[] { true });
            var list = typeof(UI_AwakeningPanel).GetField("achievementList", NonPub)?.GetValue(panel) as AchievementListView;
            if (list == null) { sb.AppendLine("업적 목록 없음"); return; }
            await UniTask.Delay(700, ignoreTimeScale: true);

            sb.AppendLine($"업적 — 진행 {quest.ActiveAchievements.Count} · 받음 {quest.CompletedAchievements.Count} · 수령 가능 {quest.WaitingAchievementCount()}");
            await Shot("Ach_1_Now");
            Measure(sb, "① 지금", list);

            typeof(AchievementListView).GetField("_claimedExpanded", NonPub)?.SetValue(list, true);
            list.Refresh();
            await UniTask.Delay(400, ignoreTimeScale: true);
            await Shot("Ach_2_ClaimedOpen");
            Measure(sb, "② 받음 펼침", list);

            // ③ 수령 가능이 여럿 — 기록을 크게 올려 업적에 밀어 넣는다(메모리만)
            foreach (var key in MemoryAltarCatalog.Rec.All) data.SetRecordMax(key, 9999);
            typeof(UserGameData).GetMethod("PushRecordsToAchievements", NonPub)?.Invoke(data, null);
            list.Refresh();
            // 아래 행동 띠(모두 받기)는 창이 그린다 — 실제 게임에선 창을 열 때 이미 수령 가능이라 여기서도 창을 새로 그린다
            typeof(UI_AwakeningPanel).GetMethod("Refresh", NonPub)?.Invoke(panel, null);
            await UniTask.Delay(500, ignoreTimeScale: true);
            sb.AppendLine($"③ 기록 올린 뒤 — 진행 {quest.ActiveAchievements.Count} · 수령 가능 {quest.WaitingAchievementCount()}");
            await Shot("Ach_3_ManyClaimable");
            Measure(sb, "③ 수령 가능 여럿", list);

            // 스크롤 끝까지 — 아래쪽 행 · 머리줄이 겹치는지
            var scroll = list.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
            if (scroll != null) { scroll.verticalNormalizedPosition = 0f; await UniTask.Delay(300, ignoreTimeScale: true); await Shot("Ach_4_ScrolledEnd"); Measure(sb, "④ 스크롤 끝", list); }
            Managers.UI.CloseAllPopupUI();
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            data.records = keepRecords; data.abyssEssence = keepEssence;
            Probe("RestoreTestHubGui");
            File.WriteAllText("Temp/achievement_tab_probe.txt", sb.ToString());
            Debug.Log("[AchTabProbe] 끝 → Temp/achievement_tab_probe.txt (퀘스트 저장 막음 — 바로 플레이를 멈출 것)");
        }
    }

    /// <summary>켜진 행마다 글자 겹침 · 행 밖 넘침, 그리고 행 · 머리줄끼리 겹침을 센다(그려진 글자 기준).</summary>
    private static void Measure(StringBuilder sb, string label, AchievementListView list)
    {
        var rows = new List<RectTransform>();
        foreach (var r in list.GetComponentsInChildren<AchievementRowView>(false)) rows.Add((RectTransform)r.transform);

        int inRow = 0, overflow = 0, rowHit = 0;
        foreach (var row in rows)
        {
            var rr = WorldRect(row);
            var texts = new List<(string, Rect)>();
            foreach (var t in row.GetComponentsInChildren<TMP_Text>(false))
                if (!string.IsNullOrEmpty(t.text) && t.isActiveAndEnabled) texts.Add((t.name + "「" + Short(t.text) + "」", TextRect(t)));
            for (int i = 0; i < texts.Count; i++)
            {
                var a = texts[i].Item2;
                if (a.xMin < rr.xMin - 1f || a.xMax > rr.xMax + 1f || a.yMin < rr.yMin - 1f || a.yMax > rr.yMax + 1f)
                { overflow++; if (overflow <= 6) sb.AppendLine($"  {label} 행 밖 넘침: {texts[i].Item1}"); }
                for (int j = i + 1; j < texts.Count; j++)
                    if (a.Overlaps(texts[j].Item2))
                    { inRow++; if (inRow <= 8) sb.AppendLine($"  {label} 글자 겹침: {texts[i].Item1} ↔ {texts[j].Item1}"); }
            }
        }
        for (int i = 0; i < rows.Count; i++)
        for (int j = i + 1; j < rows.Count; j++)
        {
            var a = WorldRect(rows[i]); var b = WorldRect(rows[j]);
            var shrink = new Rect(a.x + 2f, a.y + 2f, a.width - 4f, a.height - 4f);
            if (shrink.Overlaps(b)) { rowHit++; if (rowHit <= 4) sb.AppendLine($"  {label} 행끼리 겹침: {rows[i].name} ↔ {rows[j].name}"); }
        }
        sb.AppendLine($"{label}: 행 {rows.Count} · 글자 겹침 {inRow} · 행 밖 넘침 {overflow} · 행끼리 겹침 {rowHit}");
    }

    private static Rect WorldRect(RectTransform rt)
    {
        var c = new Vector3[4]; rt.GetWorldCorners(c);
        return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
    }

    private static Rect TextRect(TMP_Text t)
    {
        var b = t.textBounds;
        var center = t.rectTransform.TransformPoint(b.center);
        var ext = Vector3.Scale(b.extents, t.rectTransform.lossyScale);
        return new Rect(center.x - ext.x, center.y - ext.y, ext.x * 2f, ext.y * 2f);
    }

    private static string Short(string s) => s.Length > 14 ? s.Substring(0, 14) + "…" : s;

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", SNonPub);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }

    private static void Probe(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, SNonPub)?.Invoke(null, null);
}
#endif
