#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-15 사용자 「HUD 서약 팝업의 디자인 · 가독성 · 위치 개선」 — 지금 모습 촬영 · 수치.
/// 서약서 1절 · 3절 · 5절을 차례로 맺고 HUD를 찍는다(5절은 연쇄가 켜지는 순간도). 패널 · 글자 · 이웃 HUD(버프 줄 · 무기 원 · 체력바 · 알림)의
/// 화면 사각형(1920×1080 기준)과 글자 크기 · 줄 수를 적는다. 플레이어 무적 · 끝나면 서약을 비운다.
/// 개선 뒤 검사(10-15): 미니맵 바로 아래 · 이웃 HUD와 겹침 0 · 설명 15px 이상 · 절 표식 = 결과 줄 머리(위에서 아래로).
/// 결과: Temp/covenant_hud_look.txt · Temp/ui_shots/CovHud_*.png · 로그 「[CovHudLook] 끝」. ⚠️ 전투방(적이 있는 방)에서, 플레이 중.
/// </summary>
public static class CovenantHudLook1015ProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static readonly (string name, string id)[] Sentences =
    {
        ("1", "sen:heartbeat@silver>fury@silver"),
        ("3", "sen:streak@gold>supernova@gold>ember@ruby~i"),
        ("5", "sen:streak@silver>ember@silver>detonate@gold~i>fury@silver~i>supernova@ruby~w>curse@silver~i"),
    };

    [MenuItem("RelicFairy/Debug/10-15 HUD 서약서 촬영 (전투방, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning("[CovHudLook] 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("HUD 서약서 — 지금 모습\n");
        _pass = _fail = 0;
        var run = GameRunBootstrapper.Instance.Run;
        var player = run.Player;
        var handler = run.CovenantHandler;
        typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(player, true);
        var list = (List<CovenantBase>)typeof(CovenantHandler).GetField("_covenants", Inst).GetValue(handler);
        var keep = new List<string>();
        foreach (var c in list) if (c != null && !string.IsNullOrEmpty(c.CovenantId)) keep.Add(c.CovenantId);
        try
        {
            foreach (var (name, id) in Sentences)
            {
                var s = Write(handler, list, id, sb);
                if (s == null) continue;
                await Wait(700);
                Dump(sb, $"{name}절");
                CheckLayout(sb, $"{name}절", s.ResultCount);
                await Shot($"CovHud_{name}");
                if (name == "5")
                {
                    // 연쇄 — 연격 3타로 원인을 채워 절이 차례로 켜지는 순간
                    var mons = Gather(player, 1);
                    if (mons.Count > 0)
                    {
                        mons[0].HpFloorMin1 = true;
                        for (int k = 0; k < 3; k++) { handler.OnAttackHit(mons[0].gameObject, 10f); await Wait(60); }
                        await Wait(140); await Shot("CovHud_5_chain");
                        mons[0].HpFloorMin1 = false;
                    }
                }
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            sb.AppendLine($"합계 통과 {_pass} · 실패 {_fail}");
            foreach (var cv in list) cv.Dispose();
            list.Clear();
            foreach (var id in keep) handler.TryAdd(id);   // 원래 서약 되돌림
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(player, false);
            File.WriteAllText("Temp/covenant_hud_look.txt", sb.ToString());
            Debug.Log("[CovHudLook] 끝\n" + sb);
        }
    }

    private static int _pass, _fail;

    private static void Check(StringBuilder sb, string what, bool ok, string note)
    {
        if (ok) _pass++; else _fail++;
        sb.AppendLine($"  {(ok ? "통과" : "실패")} — {what} · {note}");
    }

    /// <summary>화면 사각형(1080 기준 · 왼아래 원점).</summary>
    private static Rect SR(RectTransform rt)
    {
        var c = new Vector3[4]; rt.GetWorldCorners(c);
        var cv = rt.GetComponentInParent<Canvas>()?.rootCanvas;
        Camera cam = cv != null && cv.renderMode != RenderMode.ScreenSpaceOverlay ? cv.worldCamera : null;
        float k = 1080f / Screen.height;
        Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]) * k, b = RectTransformUtility.WorldToScreenPoint(cam, c[2]) * k;
        return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
    }

    private static void CheckLayout(StringBuilder sb, string label, int results)
    {
        var panel = Object.FindFirstObjectByType<CovenantPanelView>(FindObjectsInactive.Include);
        var hud = Object.FindFirstObjectByType<HudView>(FindObjectsInactive.Include);
        if (panel == null || hud == null) { Check(sb, $"{label} 패널", false, "없음"); return; }
        var pr = SR((RectTransform)panel.transform);
        var map = FindDeep(hud.transform, "Panel_Minimap") as RectTransform;
        if (map != null)
        {
            var mr = SR(map);
            Check(sb, $"{label} 미니맵 바로 아래(왼쪽 맞춤)", Mathf.Abs(pr.xMin - mr.xMin) <= 3f && pr.yMax <= mr.yMin - 30f && pr.yMax >= mr.yMin - 60f,
                  $"패널 x {pr.xMin:0} · 위 {pr.yMax:0} / 미니맵 x {mr.xMin:0} · 아래 {mr.yMin:0}");
        }
        int hits = 0; var names = new StringBuilder();
        foreach (var n in new[] { "BuffGridRoot", "ItemNoticeRoot", "Weapon_01", "Weapon_02", "HpBar", "QuestTracker" })
        {
            var t = FindDeep(hud.transform, n) as RectTransform;
            if (t == null || !t.gameObject.activeInHierarchy) continue;
            var r = SR(t);
            if (r.width < 1f || r.height < 1f) continue;
            if (r.Overlaps(pr)) { hits++; names.Append(n).Append(' '); }
        }
        Check(sb, $"{label} 이웃 HUD와 겹침 0", hits == 0, hits == 0 ? "" : names.ToString());
        float minFont = 99f;
        foreach (var t in panel.GetComponentsInChildren<TMP_Text>(false))
            if (!string.IsNullOrEmpty(t.text) && t.name == "DescText") minFont = Mathf.Min(minFont, t.fontSize);
        Check(sb, $"{label} 설명 글자 15px 이상", minFont >= 15f, $"{minFont:0.#}px");
        var pips = new System.Collections.Generic.List<RectTransform>();
        foreach (var img in panel.GetComponentsInChildren<UnityEngine.UI.Image>(false))
            if (img.name.StartsWith("Pip")) pips.Add(img.rectTransform);
        bool ordered = pips.Count == results;
        for (int i = 1; i < pips.Count; i++) ordered &= SR(pips[i]).center.y < SR(pips[i - 1]).center.y - 10f;
        var desc = FindDeep(panel.transform, "DescText") as RectTransform;
        if (desc != null && pips.Count > 0) ordered &= SR(pips[0]).center.x < SR(desc).xMin + 20f;
        Check(sb, $"{label} 절 표식 = 결과 줄 머리 {results}개(위 → 아래)", ordered, $"표식 {pips.Count}");
    }

    private static CovenantSentence Write(CovenantHandler handler, List<CovenantBase> list, string id, StringBuilder sb)
    {
        foreach (var cv in list) cv.Dispose();
        list.Clear();
        if (!handler.TryAdd(id)) { sb.AppendLine($"맺기 실패 {id}"); return null; }
        return list.Count > 0 ? list[0] as CovenantSentence : null;
    }

    /// <summary>패널 · 글자 · 이웃 HUD의 화면 사각형(1920×1080) · 글자 크기 · 줄 수.</summary>
    private static void Dump(StringBuilder sb, string label)
    {
        sb.AppendLine($"[{label}]");
        var panel = Object.FindFirstObjectByType<CovenantPanelView>(FindObjectsInactive.Include);
        if (panel == null) { sb.AppendLine("  서약 패널 없음"); return; }
        sb.AppendLine($"  패널 {R(panel.transform as RectTransform)} · 켜짐 {panel.isActiveAndEnabled}");
        foreach (var t in panel.GetComponentsInChildren<TMP_Text>(false))
        {
            if (string.IsNullOrEmpty(t.text)) continue;
            t.ForceMeshUpdate();
            sb.AppendLine($"  글자 {t.name}: {t.fontSize:0.#}px(기준 {t.fontSizeMax:0.#}) · 줄 {t.textInfo.lineCount} · 넘침 {t.isTextOverflowing} · {R(t.rectTransform)} · 「{t.GetParsedText().Replace("\n", " / ")}」");
        }
        foreach (var img in panel.GetComponentsInChildren<UnityEngine.UI.Image>(false))
            if (img.name.StartsWith("Skin") || img.name.StartsWith("Pip")) sb.AppendLine($"  그림 {img.name}: {R(img.rectTransform)} · 색 {img.color}");
        var hud = Object.FindFirstObjectByType<HudView>(FindObjectsInactive.Include);
        if (hud != null)
            foreach (var n in new[] { "BuffRow", "BuffGrid", "Panel_Buff", "Weapon_01", "Weapon_02", "HpBar", "ItemNoticeRoot", "Panel_Minimap", "MiniMap" })
            {
                var tr = FindDeep(hud.transform, n) as RectTransform;
                if (tr != null && tr.gameObject.activeInHierarchy) sb.AppendLine($"  이웃 {n}: {R(tr)}");
            }
    }

    /// <summary>화면 사각형(1080 기준 · 왼아래 원점) — x0~x1 · y0~y1.</summary>
    private static string R(RectTransform rt)
    {
        if (rt == null) return "-";
        var c = new Vector3[4]; rt.GetWorldCorners(c);
        var cv = rt.GetComponentInParent<Canvas>()?.rootCanvas;
        float k = 1080f / Screen.height;
        if (cv != null && cv.renderMode != RenderMode.ScreenSpaceOverlay && cv.worldCamera != null)
            for (int i = 0; i < 4; i++) c[i] = cv.worldCamera.WorldToScreenPoint(c[i]);
        return $"x {c[0].x * k:0}~{c[2].x * k:0} · y {c[0].y * k:0}~{c[2].y * k:0}";
    }

    private static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++) { var hit = FindDeep(t.GetChild(i), name); if (hit != null) return hit; }
        return null;
    }

    private static async UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        if (m != null) await (UniTask)m.Invoke(null, new object[] { name, 0 });
    }

    private static UniTask Wait(int ms) => UniTask.Delay(ms, ignoreTimeScale: true);

    private static List<MonsterBase> Gather(PlayerController player, int count)
    {
        var list = new List<MonsterBase>();
        foreach (var mb in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (!mb.IsDead && mb.Grade != MonsterGrade.Boss) list.Add(mb);
        Vector3 p = player.transform.position;
        list.Sort((x, y) => (x.transform.position - p).sqrMagnitude.CompareTo((y.transform.position - p).sqrMagnitude));
        if (list.Count > count) list.RemoveRange(count, list.Count - count);
        for (int i = 0; i < list.Count; i++)
        {
            Vector3 to = p + new Vector3(1.5f, 0f, 0f);
            if (list[i].TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var ag) && ag.enabled) ag.Warp(to);
            else list[i].transform.position = to;
        }
        return list;
    }
}
#endif
