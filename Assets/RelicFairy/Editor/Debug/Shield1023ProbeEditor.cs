#if UNITY_EDITOR
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
/// 10-23 사용자 「보호막이 적용되는지 · UI에도 적용되는지」 — 런 중(HUD가 보일 때) 보호막을 넣고 맞아 본다.
/// ① 보호막 +120(상한 = 최대 체력 × 0.3) → HUD 띠 · 「+N」 ② 40 피해 → 체력 그대로 · 보호막 −40 ③ 보호막보다 큰 피해 → 나머지만 체력.
/// 피해는 PlayerController.TakeDamage(실제 피격 길 · 방어 · 감소 뒤 흡수)로 넣는다 — 방어력 때문에 들어간 피해는 넣은 값보다 작다.
/// 결과 Temp/shield23.txt · Temp/ui_shots/SHIELD23_*.png · 로그 「[Shield23] 끝」.
/// </summary>
public static class Shield1023ProbeEditor
{
    private const string Tag = "[Shield23]";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("RelicFairy/Debug/10-23 보호막 적용 · HUD 실측 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-23 보호막 실측\n");
        int pass = 0, total = 0;
        void Check(string what, bool ok) { total++; if (ok) pass++; sb.AppendLine($"CHECK {ok} — {what}"); }
        try
        {
            var player = Object.FindFirstObjectByType<PlayerController>();
            var stats = player != null ? player.RuntimeStats : null;
            if (stats == null) { sb.AppendLine("플레이어 없음"); return; }
            var panel = Object.FindFirstObjectByType<CombatPanelView>();
            var hpText = panel != null ? typeof(CombatPanelView).GetField("hpText", Any)?.GetValue(panel) as TMP_Text : null;
            var invF = typeof(PlayerController).GetField("_invincibleEnd", Any);

            stats.AbsorbWithShield(100000);   // 남은 보호막 비우기
            await Wait(0.3f);
            int hp0 = stats.Hp, max = stats.MaxHp;
            sb.AppendLine($"체력 {hp0}/{max} · 보호막 상한 {stats.ShieldCap} · 방어 {stats.Defense}");

            stats.AddShield(120f);
            string last = hpText != null ? hpText.text : null;
            for (int f = 0; f < 40; f++)   // 프레임마다 글이 바뀌는 순간을 적는다(진단)
            {
                await UniTask.Yield();
                string now = hpText != null ? hpText.text : null;
                if (now != last) { Debug.Log($"[HpTrace] 프레임 {f}: 「{last}」 → 「{now}」"); sb.AppendLine($"    프레임 {f}: 「{last}」 → 「{now}」"); last = now; }
            }
            await Wait(0.1f);
            var band = panel != null ? typeof(CombatPanelView).GetField("_shieldBand", Any)?.GetValue(panel) as Image : null;
            int s1 = stats.Shield;
            Check($"① 보호막이 쌓인다(+120 → {s1} · 상한 {stats.ShieldCap})", s1 > 0 && s1 == Mathf.Min(120, stats.ShieldCap));
            Check($"① HUD 띠 보임 {(band != null && band.enabled)} · 길이 {(band != null ? band.rectTransform.anchorMax.x : -1f):0.000}(기대 {(float)s1 / max:0.000})",
                  band != null && band.enabled && Mathf.Abs(band.rectTransform.anchorMax.x - (float)s1 / max) < 0.002f);
            Check($"① 체력 숫자에 「+{s1}」 — 「{hpText?.GetParsedText()}」", hpText != null && hpText.GetParsedText().Contains("+" + s1));
            if (hpText != null)
            {
                hpText.ForceMeshUpdate();
                sb.AppendLine($"    진단: 원문 「{hpText.text}」 · 보이는 글자 {hpText.textInfo.characterCount} · 폭 {hpText.rectTransform.rect.width:0} · 선호 폭 {hpText.preferredWidth:0} · 넘침 {hpText.overflowMode} · 자동 크기 {hpText.enableAutoSizing} · 서식 {hpText.richText}" +
                              $" · _shield {typeof(CombatPanelView).GetField("_shield", Any)?.GetValue(panel)} · _hpShown {typeof(CombatPanelView).GetField("_hpShown", Any)?.GetValue(panel)} · 판 수 {Object.FindObjectsByType<CombatPanelView>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length}");
            }
            if (hpText != null && panel != null)
            {
                var wr = typeof(CombatPanelView).GetMethod("WriteHpText", Any);
                panel.SetShield(stats.Shield, max);
                string a = hpText.text;
                wr?.Invoke(panel, new object[] { stats.Hp, max });
                string b = hpText.text;
                await UniTask.Yield();
                string c = hpText.text;
                await UniTask.DelayFrame(3);
                string d = hpText.text;
                sb.AppendLine($"    진단2: SetShield 직접 → 「{a}」 · WriteHpText 직접 → 「{b}」 · 1프레임 뒤 「{c}」 · 4프레임 뒤 「{d}」");
            }
            await Shot("SHIELD23_1_gain");

            invF?.SetValue(player, 0f);
            player.TakeDamage(40, null, true);
            await Wait(0.4f);
            int s2 = stats.Shield, hp2 = stats.Hp;
            Check($"② 피해 40 → 체력 그대로({hp0} → {hp2}) · 보호막 {s1} → {s2}", hp2 == hp0 && s2 < s1);
            Check($"② HUD가 줄어든 보호막을 따른다 — 「{hpText?.GetParsedText()}」", hpText != null && hpText.GetParsedText().Contains("+" + s2));
            await Shot("SHIELD23_2_hit");

            await Wait(0.6f);
            invF?.SetValue(player, 0f);
            player.TakeDamage(400, null, true);
            await Wait(0.4f);
            int s3 = stats.Shield, hp3 = stats.Hp;
            Check($"③ 보호막보다 큰 피해 → 보호막 {s2} → {s3} · 체력 {hp2} → {hp3}(나머지만)", s3 == 0 && hp3 < hp2);
            Check($"③ 보호막이 다 깨지면 띠 · 「+」가 사라진다 — 「{hpText?.GetParsedText()}」", band != null && !band.enabled && hpText != null && !hpText.GetParsedText().Contains("+"));
            await Shot("SHIELD23_3_broken");
            stats.Heal(max);   // 체력을 돌려 놓는다(실측 뒤 런을 그대로 쓰지 않는다)
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            sb.Insert(0, $"통과 {pass}/{total}\n");
            File.WriteAllText("Temp/shield23.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n" + sb);
        }
    }

    private static UniTask Wait(float s) => UniTask.Delay((int)(s * 1000), DelayType.Realtime);

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }
}
#endif
