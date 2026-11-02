#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q11 L2 4차 실측(테스트 허브 런 중) — 결과 Temp/loc_l2d1102.txt · 사진 Temp/ui_shots/LD1102_hud.png · 로그 「[LD1102] 끝」.
/// 영어에서 룬 · 근접 증강 · 지금 든 무기 · 스킬 · 유물 이름이 표에 있나(없는 것 앞 몇 개) → HUD 사진 → 처음 언어로.
/// </summary>
public static class LocL2d1102ProbeEditor
{
    [MenuItem("RelicFairy/Debug/11-02 영어 데이터 이름 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[LD1102] 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("11-02 영어 데이터 이름\n");
        Directory.CreateDirectory("Temp/ui_shots");
        var lang0 = Loc.Current;
        try
        {
            await Loc.PreloadAsync();
            Loc.SetLanguage(Loc.Language.English);

            var runes = new List<string>();
            foreach (var so in ItemSORegistry.All) if (so != null) runes.Add(so.displayName);
            Cover(sb, "룬 이름", runes, 0);

            var aug = new List<string>();
            foreach (var d in MeleeAugmentCatalog.All) aug.Add(d.Name);
            Cover(sb, "근접 증강 이름", aug, 0);

            var gear = new List<string>();
            var loadout = AppBootstrapper.Instance?.Loadout;
            if (loadout?.WeaponSlot0 != null) AddWeapon(gear, loadout.WeaponSlot0);
            if (loadout?.WeaponSlot1 != null) AddWeapon(gear, loadout.WeaponSlot1);
            if (loadout?.Relic != null) gear.Add(loadout.Relic.DisplayName);
            Cover(sb, "지금 든 무기 · 스킬 · 유물", gear, 0);

            await UniTask.DelayFrame(5);
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/LD1102_hud.png");
            await UniTask.Delay(300, ignoreTimeScale: true);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Loc.SetLanguage(lang0);
            File.WriteAllText("Temp/loc_l2d1102.txt", sb.ToString());
            Debug.Log("[LD1102] 끝 → Temp/loc_l2d1102.txt");
        }
    }

    private static void AddWeapon(List<string> list, WeaponSO w)
    {
        list.Add(w.displayName);
        if (w.skillE != null) list.Add(w.skillE.skillName);
        if (w.skillQ != null) list.Add(w.skillQ.skillName);
    }

    private static void Cover(StringBuilder sb, string label, List<string> names, int allowMissing)
    {
        int hit = 0; var miss = new List<string>();
        foreach (var n in names)
        {
            if (string.IsNullOrEmpty(n)) continue;
            if (Loc.T(n) != n) hit++; else if (miss.Count < 6) miss.Add(n); else miss.Add(null);
        }
        int missN = miss.Count;
        miss.RemoveAll(x => x == null);
        sb.AppendLine($"{(missN <= allowMissing && hit > 0 ? "PASS" : "FAIL")} {label} — 영어 {hit} · 없음 {missN}{(miss.Count > 0 ? "  예) " + string.Join(" | ", miss) : "")}");
    }
}
#endif
