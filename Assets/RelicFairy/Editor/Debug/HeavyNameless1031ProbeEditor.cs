#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 10-31 사용자 「무형검 재련 중에 강타 이야기가 있는데 제대로 발동하지도 체감도 안 느껴진다」 실측(테스트 허브 런 중).
/// 무형검을 들고 앞 슬라임을 10초 평타 — ① 증강 없음 ② 묵직한 손 I(4번째마다) ③ 묵직한 손 III(2번째마다).
/// 휘두름 수(슬래시 이벤트) · 강타 예약 로그 · 실제 강타 적중(HitInfo.IsHeavy) · 강타 / 일반 적중 피해 평균을 적는다.
/// 장착 · 평타는 WeaponFormProbeEditor의 EquipAsync · AugSwingAsync를 그대로 쓴다(리플렉션).
/// 결과 Temp/heavy_nameless1031.txt · 로그 「[HN31] 끝」.
/// </summary>
public static class HeavyNameless1031ProbeEditor
{
    private const string Tag = "[HN31]";
    private const string Nameless = "Assets/RelicFairy/Weapon/Nameless/Data/T0_Nameless.asset";
    private const BindingFlags SAny = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-31 무형검 강타 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-31 무형검 강타 실측\n");
        var ct = CancellationToken.None;
        int heavyLogs = 0, heavyLanded = 0;
        void OnLog(string msg, string stack, LogType type) { if (msg.Contains("묵직한 손") && msg.Contains("강타 적중")) heavyLanded++; else if (msg.Contains("묵직한 손") && msg.Contains("강타")) heavyLogs++; }
        try
        {
            var run = GameRunBootstrapper.Instance?.Run;
            var p = run?.Player;
            if (p == null) { sb.AppendLine("런 · 플레이어 없음"); return; }
            var probe = typeof(WeaponFormProbeEditor);
            var equip = probe.GetMethod("EquipAsync", SAny);
            var swing = probe.GetMethod("AugSwingAsync", SAny);
            var diag  = probe.GetField("s_augSwingDiag", SAny);
            if (equip == null || swing == null) { sb.AppendLine("WeaponFormProbeEditor 도우미를 못 찾음"); return; }

            // ⓪ 전용 증강은 제 무기에만 — 제시 필터(FitsWeapon · IsMeaningful)를 무기마다 돌려 남의 전용이 섞이는지
            var meaningful = typeof(MeleeAugmentDraft).GetMethod("IsMeaningful", SAny);
            foreach (var path in new[] { Nameless, "Assets/RelicFairy/Weapon/Katana/Data/T1_Katana.asset", "Assets/RelicFairy/Weapon/Greatsword/Data/T1_Greatsword.asset" })
            {
                var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(path);
                var wd = so != null ? WeaponData.FromSO(so) : null;
                if (wd == null) { sb.AppendLine($"  {path} 없음"); continue; }
                var kind = MeleeAugmentCatalog.KindOf(wd);
                int own = 0, common = 0, foreign = 0;
                var heavyish = new StringBuilder();
                foreach (var d in MeleeAugmentCatalog.All)
                {
                    if (!MeleeAugmentCatalog.FitsWeapon(d, wd)) continue;
                    if (meaningful != null && !(bool)meaningful.Invoke(null, new object[] { d, wd })) continue;
                    if (d.Weapon == MeleeWeaponKind.None) common++; else if (d.Weapon == kind) own++; else { foreign++; sb.AppendLine($"  ✗ 남의 전용 {d.Name}({d.Weapon})"); }
                }
                sb.AppendLine($"⓪ {wd.displayName}({kind}) 제시될 수 있는 증강 — 전용 {own} · 공용 {common} · 남의 전용 {foreign}(기대 0)");
            }
            if (!await (UniTask<bool>)equip.Invoke(null, new object[] { p, Nameless, ct, PlayerWeaponManager.Slot0 })) { sb.AppendLine("무형검 장착 실패"); return; }
            var w = p.WeaponManager.Weapon0Data;
            sb.AppendLine($"무기 {w.displayName} · 평타 콤보 {w.groundEndCount}타 · 발동 계수(평타) {ProcCoefficient.Of(w.weaponType, WeaponActionType.GroundLight, false):0.##} · 강타 문턱 {ProcCoefficient.HeavyThreshold():0.##}");
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (m != null && !m.IsDead) m.gameObject.SetActive(false);   // 조준 보정이 다른 몬스터로 끌려가지 않게

            Application.logMessageReceived += OnLog;
            foreach (var augs in new[] { "", "aug_heavy_hand:1", "aug_heavy_hand:3" })
            {
                w.enhanceLevel = 0;
                w.augments = augs;
                run.EffectManager.Rebuild();
                await UniTask.Delay(500, ignoreTimeScale: true);
                heavyLogs = 0; heavyLanded = 0;
                int hits = 0, heavy = 0;
                float dmgHeavy = 0f, dmgNormal = 0f;
                Action<HitInfo> onHit = h =>
                {
                    if (h.Attacker != p.gameObject || h.ActionType != WeaponActionType.GroundLight) return;
                    hits++;
                    if (h.IsHeavy) { heavy++; dmgHeavy += h.Damage; } else dmgNormal += h.Damage;
                };
                HitFeedbackService.OnHit += onHit;
                int swings;
                try { swings = await (UniTask<int>)swing.Invoke(null, new object[] { p, 10f, ct, null }); }
                finally { HitFeedbackService.OnHit -= onHit; }
                int normal = hits - heavy;
                sb.AppendLine($"■ 증강 「{(augs == "" ? "없음" : augs)}」 — 휘두름 {swings} · 강타 예약 로그 {heavyLogs} · 강타 적중 로그(판정 쪽) {heavyLanded} · 평타 적중 {hits} · 그중 강타 {heavy}"
                            + $" · 피해 평균 강타 {(heavy > 0 ? dmgHeavy / heavy : 0f):0.0} / 일반 {(normal > 0 ? dmgNormal / normal : 0f):0.0}");
                sb.AppendLine($"    ({diag?.GetValue(null)})");
            }
            w.augments = "";
            run.EffectManager.Rebuild();
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Application.logMessageReceived -= OnLog;
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/heavy_nameless1031.txt", sb.ToString());
            Debug.Log($"{Tag} 끝 → Temp/heavy_nameless1031.txt");
        }
    }
}
#endif
