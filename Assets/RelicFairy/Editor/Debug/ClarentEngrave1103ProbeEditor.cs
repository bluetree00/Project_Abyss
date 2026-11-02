#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [촬영 · 실측 · 플레이 중] 11-03 클라렌트(대검) R 「속성 검」 2단계 각인(사용자 「클라렌트만 없으면 이상한데 · 변형할 수 있는 추가 이점 · 설명이랑 잘」).
/// ① 재련소 → 무형검 재련 3회 → 진화(둘째 = 클라렌트) → 각인 창 사진(한 · 영 · 1.75초) · 카드 글
/// ② 전투 — 슬라임 넷(A · B = 휘두름 안 · C = A 곁 2.5 m · 휘두름 밖 · D = 멀리)으로 번지는 속성 · 모이는 속성 각각 R을 써 받은 피해를 잰다.
///    번지는 속성: A · B · C가 맞고 D는 0 · 모이는 속성: A(체력 가장 많음)만 한 번 더 · C · D 0.
/// 결과 Temp/clarent1103.txt · Temp/ui_shots/CL1103_*.png · 로그 「[CL1103] 끝」.
/// </summary>
public static class ClarentEngrave1103ProbeEditor
{
    private const string Tag      = "[CL1103]";
    private const string AutoRoot = "RelicFairy/Debug/런 구조 자동 실측/";
    private const string ShotDir  = "Temp/ui_shots";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static bool s_armed;

    [MenuItem("RelicFairy/Debug/11-03 클라렌트 각인 — 카드 · 전투 실측 (테스트 허브, 플레이 중)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning($"{Tag} 플레이 모드에서만 동작한다."); return; }
        if (Object.FindFirstObjectByType<TestHubLauncher>() == null) { Debug.LogWarning($"{Tag} 테스트 허브에서 시작한다."); return; }
        s_armed = true;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.ExecuteMenuItem(AutoRoot + "시작 (테스트 허브에서, 플레이 중)");
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (!s_armed || !msg.StartsWith("[RunAuto]") || !msg.Contains("방 진입")) return;
        s_armed = false;
        Application.logMessageReceived -= OnLog;
        EditorApplication.delayCall += () =>
        {
            EditorApplication.ExecuteMenuItem(AutoRoot + "중지·기록");
            var p = GameRunBootstrapper.Instance?.Run?.Player;
            if (p == null) { Debug.LogWarning($"{Tag} 런 플레이어가 없다"); return; }
            RunAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
        };
    }

    private static async UniTaskVoid RunAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder("11-03 클라렌트 각인 — 카드 · 전투\n");
        Directory.CreateDirectory(ShotDir);
        var go   = new GameObject("~Cl1103Probe");
        var lang = Loc.Current;
        try
        {
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);
            foreach (var mo in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (mo != null && !mo.IsDead) mo.gameObject.SetActive(false);
            await Wait(1.2f, ct);

            // ① 각인 창
            var wm  = p.WeaponManager;
            var run = GameRunBootstrapper.Instance.Run;
            var w0  = wm.Weapon0Data;
            w0.enhanceLevel = 9; w0.highestEnhanceReached = 9; w0.augments = ""; w0.reforgeLog = ""; w0.engravings = ""; w0.RecomputeEnhancedStats();
            run.FuelBank.Add(FuelKind.EnhanceMaterial, 300);
            typeof(UILayoutRuntimeProbeEditor).GetMethod("HideTestHubGui", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
            var ctrl = go.AddComponent<CrucibleRoomController>();
            ctrl.Initialize(run, await WeaponEnhanceService.EnsureLoadedAsync(), new System.Random(7), null);
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
            panel.Bind(ctrl);
            await Wait(0.9f, ct);
            F<Button[]>(panel, "_chooseCard")?[0]?.onClick.Invoke();
            Call(panel, "RefreshAll");
            await Wait(0.5f, ct);
            Call(panel, "ShowEvolvePanel");
            await Wait(0.8f, ct);
            Call(panel, "OnBranchClicked", 1);   // 둘째 갈래 = 클라렌트(대검)
            float te = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - te < 10f && !(F<GameObject>(panel, "_engravePanel")?.activeSelf ?? false))
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            var eg = F<GameObject>(panel, "_engravePanel");
            sb.AppendLine($"진화 → {wm.Weapon0Data?.displayName} · 종류 {wm.Weapon0Data?.weaponType} · R 단계 {SkillTierResolver.Resolve(wm.Weapon0Data)}");
            if (eg == null || !eg.activeSelf) { sb.AppendLine("FAIL 각인 창이 안 열렸다"); return; }
            await Wait(1.75f, ct);
            await Shot("CL1103_ko");
            Dump(sb, "ko", eg);
            Loc.SetLanguage(Loc.Language.English);
            eg.SetActive(false);
            Call(panel, "TryShowEngraveOffer");
            await Wait(1.75f, ct);
            await Shot("CL1103_en");
            Dump(sb, "en", eg);
            Loc.SetLanguage(lang);
            Call(panel, "OnEngravePicked", 0);
            await Wait(0.6f, ct);
            sb.AppendLine($"고른 각인 {wm.Weapon0Data?.engravings}");
            Managers.UI?.CloseAllPopupUI();
            await Wait(0.8f, ct);

            // ② 전투 — 먼저 빈 허공에 R 한 번(방 입장 연출 뒤 파도가 시작되는 순간 표적이 등장 무적에 걸렸다) · 그때 R 실제 반경을 잰다 → 각인마다 R 한 번
            foreach (var wave in Object.FindObjectsByType<RoomWaveController>(FindObjectsSortMode.None)) wave.enabled = false;
            await Wait(3f, ct);
            p.CooldownTracker.ResetCooldown(SkillType.R);
            p.InputBuffer.Clear();
            p.InputBuffer.Push(Game.Inputs.Command.RSkill);
            await Wait(1.2f, ct);
            float rR = GiantSwordBuff.SwingRadiusFor(p, 2);
            sb.AppendLine($"\nR 휘두름 실제 반경 {rR:0.00} m(속성 검 켜짐 {GiantSwordBuff.IsActive(p)})");
            for (float t = 0f; t < 12f && GiantSwordBuff.IsActive(p); t += 0.25f) await Wait(0.25f, ct);
            await StrikeTestAsync(sb, p, ElementSwordBehaviorSO.EngraveSpread,   "번지는 속성", rR, ct);
            await StrikeTestAsync(sb, p, ElementSwordBehaviorSO.EngraveCondense, "모이는 속성", rR, ct);
        }
        catch (System.OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Loc.SetLanguage(lang);
            if (go != null) Object.Destroy(go);
            File.WriteAllText("Temp/clarent1103.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    /// <summary>
    /// A · B = 휘두름 안(좌우 3 m) · C = A 곁 3.2 m(휘두름 밖 — 몸 크기까지 판정에 들어 6.2 m) · D = 멀리(10 m). A 체력이 가장 많다.
    /// 방 파도를 멈추고 남은 몬스터를 걷은 뒤(첫 실측은 입장 연출이 끝나며 파도가 시작돼 플레이어가 방 안으로 옮겨졌다) 플레이어 자리에서 세운다.
    /// </summary>
    private static async UniTask StrikeTestAsync(StringBuilder sb, PlayerController p, string engraving, string label, float rR, CancellationToken ct)
    {
        var w = p.WeaponManager.Weapon0Data;
        w.engravings = engraving;
        foreach (var wave in Object.FindObjectsByType<RoomWaveController>(FindObjectsSortMode.None)) wave.enabled = false;
        foreach (var mo in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (mo != null && !mo.IsDead) mo.gameObject.SetActive(false);
        await Wait(0.5f, ct);
        var spawn = typeof(WeaponFormProbeEditor).GetMethod("SpawnAsync", BindingFlags.Static | BindingFlags.NonPublic);
        var setHp = typeof(WeaponFormProbeEditor).GetMethod("SetHp", BindingFlags.Static | BindingFlags.NonPublic);
        if (spawn == null) { sb.AppendLine("표적 소환 도구 없음"); return; }
        Vector3 c = p.transform.position, r = Vector3.right, f = Vector3.forward;
        // R 반경 기준 — A · B = 반경 안 1.2 m · C = 반경 밖 1.5 m(A에서 2.7 m: 번짐 3 m 안 · 모음 2 m 밖) · D = 반경 + 8 m
        var at = new[] { c + r * (rR - 1.2f), c - r * (rR - 1.2f), c + r * (rR + 1.5f), c + f * (rR + 8f) };
        string[] names = { "A(안 · 단단)", "B(안)", "C(A 곁 · 밖)", "D(멀리)" };
        var ms = new List<MonsterBase>();
        foreach (var pos in at)
        {
            var m = await (UniTask<MonsterBase>)spawn.Invoke(null, new object[] { pos, ct });
            ms.Add(m);
        }
        for (int i = 1; i < ms.Count; i++) if (ms[i] != null) setHp?.Invoke(null, new object[] { ms[i], 40000 });   // A만 50000 — 가장 단단한 적
        if (ms[0] != null)
        {
            float reach = 0f;
            foreach (var col in ms[0].GetComponentsInChildren<Collider>())
                if (((1 << col.gameObject.layer) & MonsterBase.HitLayerMask) != 0) reach = Mathf.Max(reach, col.bounds.extents.x, col.bounds.extents.z);
            sb.AppendLine($"  슬라임 판정 콜라이더 반지름(가로) 약 {reach:0.0} m");
        }
        sb.AppendLine($"  표적 거리(플레이어에서) A {Dist(p, ms[0])} · B {Dist(p, ms[1])} · C {Dist(p, ms[2])} · D {Dist(p, ms[3])} · A-C {(ms[0] != null && ms[2] != null ? Vector3.Distance(ms[0].transform.position, ms[2].transform.position).ToString("0.0") : "-")}");
        await Wait(0.4f, ct);
        var before = new int[ms.Count];
        for (int i = 0; i < ms.Count; i++) before[i] = ms[i] != null ? ms[i].CurrentHp : 0;

        p.CooldownTracker.ResetCooldown(SkillType.R);
        p.InputBuffer.Clear();
        p.InputBuffer.Push(Game.Inputs.Command.RSkill);
        await Wait(1.05f, ct);
        await Shot($"CL1103_{(engraving == ElementSwordBehaviorSO.EngraveSpread ? "spread" : "condense")}");
        await Wait(1.2f, ct);

        sb.AppendLine($"\n## {label}({engraving}) · R 단계 {SkillTierResolver.Resolve(w)} · 속성 {GiantSwordBuff.ElementOf(p)}");
        var dealt = new int[ms.Count];
        for (int i = 0; i < ms.Count; i++)
        {
            dealt[i] = ms[i] != null ? Mathf.Max(0, before[i] - ms[i].CurrentHp) : -1;
            sb.AppendLine($"  {names[i]} 받은 피해 {dealt[i]}");
        }
        bool pass = engraving == ElementSwordBehaviorSO.EngraveSpread
            ? dealt[0] > 0 && dealt[1] > 0 && dealt[2] > 0 && dealt[3] == 0
            : dealt[0] > dealt[1] * 1.5f && dealt[1] > 0 && dealt[3] == 0;   // C는 본 휘두름 넉백에 A 둘레 2 m 안으로 밀려 들면 같이 맞는다(정상)
        sb.AppendLine($"  {(pass ? "PASS" : "FAIL")} — " + (engraving == ElementSwordBehaviorSO.EngraveSpread
            ? "A · B · C 맞음(C는 번진 폭발만) · D 0"
            : $"A가 B의 1.5배 넘게(모아 내려침 ×{(dealt[1] > 0 ? (float)dealt[0] / dealt[1] : 0f):0.00}) · D 0"));
        foreach (var m in ms) if (m != null) m.gameObject.SetActive(false);
        for (float t = 0f; t < 12f && GiantSwordBuff.IsActive(p); t += 0.25f) await Wait(0.25f, ct);   // 속성 검이 끝난 뒤 다음
    }

    private static string Dist(PlayerController p, MonsterBase m)
        => m != null ? Vector3.Distance(p.transform.position, m.transform.position).ToString("0.0") : "-";

    private static void Dump(StringBuilder sb, string lang, GameObject eg)
    {
        sb.AppendLine($"\n## 각인 창 {lang}");
        foreach (var t in eg.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.gameObject.activeInHierarchy || t.color.a < 0.05f) continue;
            t.ForceMeshUpdate();
            string s = t.GetParsedText().Replace("\n", " ⏎ ").Trim();
            if (s.Length > 0) sb.AppendLine($"  [{t.transform.parent.name}/{t.name}] {s}{(t.isTextOverflowing ? "  ⚠넘침" : "")}");
        }
    }

    private static async UniTask Shot(string name)
    {
        var runner = Object.FindFirstObjectByType<Managers>();
        if (runner == null) return;
        await UniTask.WaitForEndOfFrame(runner);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        if (tex == null) return;
        File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    private static T F<T>(UI_CruciblePanel panel, string name) where T : class
        => typeof(UI_CruciblePanel).GetField(name, Inst)?.GetValue(panel) as T;

    private static void Call(UI_CruciblePanel panel, string method, params object[] args)
        => typeof(UI_CruciblePanel).GetMethod(method, Inst)?.Invoke(panel, args.Length == 0 ? null : args);

    private static UniTask Wait(float s, CancellationToken ct)
        => UniTask.Delay(System.TimeSpan.FromSeconds(s), DelayType.Realtime, PlayerLoopTiming.Update, ct);
}
#endif
