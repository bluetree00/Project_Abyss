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
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// 10-02 HUD 겹침 실측 — 사용자 「장비 근접과 원거리의 겹침 · 서약과 패시브를 보여주는 공간들의 겹침」.
/// 최악의 상태를 만든다: 무기 두 칸(근접 + 원거리) · 서약 4개(복원 경로로 상한까지) · 버프 칸 8개(가짜 출처) → 첫 전투방.
/// 상태 A(근접 손) · B(원거리 손)에서 화면을 찍고, 겹침 오버레이 캔버스 아래 <b>보이는 그림 · 글자 전부</b>의 화면 사각형을 덤프한다.
/// 결과: Temp/hud_overlap_dump_{A|B}.tsv · Temp/ui_shots/Hud_{A|B}.png · 로그 「[HudOverlap] 끝」. 실측 뒤 플레이를 멈출 것.
/// </summary>
public static class HudOverlapProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/UI/10-02 HUD 겹침 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[HudOverlap] 플레이 모드(테스트 허브)에서"); return; }
        RunAsync().Forget();
    }

    [MenuItem("RelicFairy/UI/10-02 HUD 겹침 덤프만 (런 중)")]
    private static void DumpOnly()
    {
        if (!EditorApplication.isPlaying) return;
        DumpOnlyAsync().Forget();
    }

    private static async UniTaskVoid DumpOnlyAsync()
    {
        await ShotAndDumpAsync("Now");
        Debug.Log("[HudOverlap] 끝");
    }

    private sealed class FakeBuffs : IBuffViewSource
    {
        private static readonly string[] Keys = { "atk", "speed", "def", "crit", "shield", "burn", "bleed", "shock" };
        public void Contribute(List<BuffViewItem> into)
        {
            for (int i = 0; i < Keys.Length; i++)
                into.Add(new BuffViewItem(Keys[i], $"실측 버프 {i + 1}", i % 3 + 1, i % 2 == 0 ? 0.6f : -1f, "", BuffSource.Relic, false));
        }
    }

    private static async UniTaskVoid RunAsync()
    {
        var log = new StringBuilder("HUD 겹침 실측\n");
        FakeBuffs fake = null;
        object aggregator = null;
        try
        {
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher == null) { log.AppendLine("테스트 허브가 아니다"); return; }
            var lt = typeof(TestHubLauncher);
            lt.GetField("_chapter", Inst)?.SetValue(launcher, 1);
            lt.GetField("_bossApproach", Inst)?.SetValue(launcher, false);
            var ranged = lt.GetField("rangedWeapons", Inst)?.GetValue(launcher) as Array;
            if (ranged != null && ranged.Length > 0) lt.GetField("_rangedIndex", Inst)?.SetValue(launcher, 0);
            log.AppendLine($"원거리 후보 {ranged?.Length ?? 0}");
            if (!launcher.TryLaunch()) { log.AppendLine("런 시작 실패"); return; }

            if (!await WaitRunAsync(60f)) { log.AppendLine("런 준비 대기 초과"); return; }
            await UniTask.Delay(4000, ignoreTimeScale: true);
            await SkipDialoguesAsync();   // 대기방 도착 대사가 시간을 멈춰 문 · 순간이동이 안 먹는다

            // 서약 — 칸까지 맺고, 복원 경로(상한 4)로 하나 더
            var run = GameRunBootstrapper.Instance.Run;
            var handler = run.CovenantHandler;
            var tryAddRestore = typeof(CovenantHandler).GetMethod("TryAdd", Inst, null, new[] { typeof(string), typeof(bool) }, null);
            for (int i = 0; i < 12 && handler.Covenants.Count < CovenantHandler.MaxCovenants; i++)
            {
                CovenantAssembleService.DraftBoard(3, null, false, handler.Covenants, out var causes, out var effects);
                if (causes == null || effects == null || causes.Count == 0 || effects.Count == 0) continue;
                string id = AssembledCovenant.MakeId(causes[0].id, causes[0].tier, effects[0].id, effects[0].tier);
                if (!handler.TryAdd(id)) tryAddRestore?.Invoke(handler, new object[] { id, true });
            }
            log.AppendLine($"서약 {handler.Covenants.Count}/{CovenantHandler.MaxCovenants} · 무기 슬롯 {DescribeWeapons(run.Player)}");

            // 첫 전투방으로
            foreach (var gate in Object.FindObjectsByType<StartRoomGate>(FindObjectsSortMode.None))
            {
                if ((int)(typeof(StartRoomGate).GetField("_fromZoneIndex", Inst)?.GetValue(gate) ?? 0) != -1) continue;
                typeof(StartRoomGate).GetMethod("HandleCovenantAssembled", Inst)?.Invoke(gate, null);
                await UniTask.Delay(9000, ignoreTimeScale: true);
                if (gate != null && gate.TryGetComponent<Collider>(out var col))
                {
                    var b = col.bounds; var pos = new Vector3(b.center.x, b.min.y + 0.3f, b.center.z);
                    run.Player.transform.position = pos;
                    if (run.Player.TryGetComponent<Rigidbody>(out var rb)) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
                }
                break;
            }
            await UniTask.Delay(7000, ignoreTimeScale: true);
            await SkipDialoguesAsync();
            await UniTask.Delay(1500, ignoreTimeScale: true);

            // 버프 칸 채우기(가짜 출처 8칸)
            var hud = Object.FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
            aggregator = hud != null ? typeof(HudPresenter).GetField("_buffAggregator", Inst)?.GetValue(hud) : null;
            fake = new FakeBuffs();
            aggregator?.GetType().GetMethod("AddSource")?.Invoke(aggregator, new object[] { fake });
            log.AppendLine($"버프 가짜 출처 {(aggregator != null ? "붙임" : "못 붙임")}");
            await UniTask.Delay(1500, ignoreTimeScale: true);

            await ShotAndDumpAsync("A");

            // 원거리 손으로 바꿔 한 번 더
            var wm = run.Player.WeaponManager;
            if (wm != null) { await wm.SwitchToSlotAsync(wm.CurrentSlotIndex == 0 ? 1 : 0); await UniTask.Delay(1500, ignoreTimeScale: true); }
            log.AppendLine($"B — 무기 슬롯 {DescribeWeapons(run.Player)}");
            await ShotAndDumpAsync("B");
        }
        catch (Exception e) { log.AppendLine("예외: " + e); }
        finally
        {
            if (fake != null) aggregator?.GetType().GetMethod("RemoveSource")?.Invoke(aggregator, new object[] { fake });
            File.WriteAllText("Temp/hud_overlap_probe.txt", log.ToString());
            Debug.Log("[HudOverlap] 끝");
        }
    }

    /// <summary>열린 대사창을 끝까지 넘긴다(최대 12번 · 0.4초 간격).</summary>
    private static async UniTask SkipDialoguesAsync()
    {
        for (int i = 0; i < 12; i++)
        {
            TestHubDebugMenu.AdvanceDialogue();
            await UniTask.Delay(400, ignoreTimeScale: true);
        }
    }

    private static string DescribeWeapons(PlayerController p)
    {
        var wm = p != null ? p.WeaponManager : null;
        if (wm == null) return "없음";
        return $"현재 칸 {wm.CurrentSlotIndex} · {wm.CurrentWeaponData?.weaponSOKey ?? "-"}";
    }

    private static async UniTask<bool> WaitRunAsync(float seconds)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            await UniTask.Delay(500, ignoreTimeScale: true);
            var run = GameRunBootstrapper.Instance?.Run;
            if (run?.Player != null && run.CovenantHandler != null) return true;
        }
        return false;
    }

    /// <summary>화면 한 장 + 화면 덮개 캔버스 아래 보이는 그림 · 글자의 화면 사각형(TSV: 경로 · 종류 · x0 y0 x1 y1 · 글자).</summary>
    private static async UniTask ShotAndDumpAsync(string tag)
    {
        Directory.CreateDirectory("Temp/ui_shots");
        ScreenCapture.CaptureScreenshot($"Temp/ui_shots/Hud_{tag}.png");
        await UniTask.Delay(400, ignoreTimeScale: true);
        await UniTask.WaitForEndOfFrame();

        var sb = new StringBuilder("path\tkind\tx0\ty0\tx1\ty1\ttext\n");
        float sw = Screen.width, sh = Screen.height;
        var corners = new Vector3[4];
        foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!canvas.isRootCanvas || !canvas.isActiveAndEnabled || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            foreach (var g in canvas.GetComponentsInChildren<Graphic>(false))
            {
                if (!g.isActiveAndEnabled || g.canvasRenderer.GetInheritedAlpha() < 0.05f || g.color.a < 0.05f) continue;
                Rect r;
                string text = "";
                if (g is TMP_Text t)
                {
                    if (string.IsNullOrWhiteSpace(t.text)) continue;
                    var bnd = t.textBounds;
                    if (bnd.size.x <= 0f) continue;
                    Vector3 a = t.rectTransform.TransformPoint(bnd.min), b = t.rectTransform.TransformPoint(bnd.max);
                    r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                    text = t.text.Replace("\n", " ").Replace("\t", " ");
                    if (text.Length > 30) text = text.Substring(0, 30);
                }
                else
                {
                    g.rectTransform.GetWorldCorners(corners);
                    r = Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
                }
                if (r.width < 2f || r.height < 2f) continue;
                if (r.width > sw * 0.6f && r.height > sh * 0.6f) continue;   // 화면 덮개 · 배경
                if (r.xMax < 0 || r.yMax < 0 || r.xMin > sw || r.yMin > sh) continue;
                sb.Append(PathOf(g.transform, canvas.transform)).Append('\t').Append(g is TMP_Text ? "text" : g.GetType().Name).Append('\t')
                  .Append(r.xMin.ToString("0")).Append('\t').Append(r.yMin.ToString("0")).Append('\t')
                  .Append(r.xMax.ToString("0")).Append('\t').Append(r.yMax.ToString("0")).Append('\t').Append(text).Append('\n');
            }
        }
        File.WriteAllText($"Temp/hud_overlap_dump_{tag}.tsv", sb.ToString());
    }

    private static string PathOf(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (var c = t; c != null && c != root; c = c.parent) parts.Add(c.name);
        parts.Add(root.name);
        parts.Reverse();
        return string.Join("/", parts);
    }
}
#endif
