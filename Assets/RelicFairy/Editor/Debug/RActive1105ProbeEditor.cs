#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 11-05 사용자 「(대검 R) 쿨타임이 속성 검이 끝난 후로 센다면 활성화 UI가 좋아야」 — HUD R 칸 「켜짐」 실측(런 중).
/// 대검 속성 검과 같은 길 — R 추적기 0(재사용은 버프 뒤로 미룸) + 강화 신호(RaiseSkillBuffStarted R 6초) → 칸 덮개 켜짐 · 금빛(속성 색) 부채꼴 · 흰 숫자 · 시간이 가면 줄어듦 →
/// 버프 끝 = 재사용 시작(StartCooldown R 9초) → 어두운 베일 · 잉크 숫자로 돌아옴 · 석궁처럼 재사용이 버프와 함께 돌고 있으면 켜짐 안 그림.
/// 결과 Temp/ractive1105.txt · Temp/ui_shots/RA1105_on.png · RA1105_cd.png · 로그 「[RA1105] 끝」.
/// </summary>
public static class RActive1105ProbeEditor
{
    private const string Tag = "[RA1105]";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static int s_fail;
    private static StringBuilder s_sb;

    [MenuItem("RelicFairy/Debug/11-05 HUD R 칸 켜짐 실측 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning($"{Tag} 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        s_sb = new StringBuilder("11-05 HUD R 칸 켜짐\n");
        s_fail = 0;
        var p = GameRunBootstrapper.Instance.Run.Player;
        var cd = p.CooldownTracker;
        try
        {
            var view = Object.FindFirstObjectByType<CombatPanelView>();
            var bg   = typeof(CombatPanelView).GetField("_rCooldownBg", Inst)?.GetValue(view) as GameObject;
            var fill = typeof(CombatPanelView).GetField("_rCooldownFill", Inst)?.GetValue(view) as Image;
            var txt  = typeof(CombatPanelView).GetField("_rCooldownTxt", Inst)?.GetValue(view) as TMP_Text;
            if (view == null || bg == null || fill == null || txt == null) { Check(false, "HUD R 칸 찾기"); return; }
            Color gold = UITheme.Thin.Gold;

            // ① 대검 속성 검과 같은 길 — R 추적기 0 + 강화 신호 → 켜짐
            cd.ResetCooldown(SkillType.R);
            await UniTask.Delay(200, DelayType.Realtime);
            Color veil = fill.color, ink = txt.color;
            p.RaiseSkillBuffStarted(SkillType.R, 6f);
            await UniTask.Delay(400, DelayType.Realtime);
            float f0 = fill.fillAmount;
            var toneM = typeof(CombatPanelView).GetMethod("RActiveTone", BindingFlags.Static | BindingFlags.NonPublic);
            Color want = toneM != null ? (Color)toneM.Invoke(null, new object[] { gold }) : Color.clear;
            bool goldFill = Near(fill.color, want) && !Near(fill.color, veil);
            Check(bg.activeInHierarchy && goldFill && f0 > 0.8f && f0 <= 1f, $"켜짐 — 덮개 {bg.activeInHierarchy} · 금빛 밴 어두운 부채꼴 {goldFill}(α {fill.color.a:0.00}) · 남은 비율 {f0:0.00}");
            Check(!string.IsNullOrEmpty(txt.text) && Near(txt.color, ink), $"켜짐 숫자 「{txt.text}」 흰 잉크(금빛 부채꼴 위에서 읽히게) {Near(txt.color, ink)}");
            await Shot("RA1105_on");
            await UniTask.Delay(1500, DelayType.Realtime);
            float f1 = fill.fillAmount;
            Check(f1 < f0 - 0.1f, $"시간이 가면 줄어듦 {f0:0.00} → {f1:0.00} · 숫자 「{txt.text}」");

            // ② 버프 끝 = 재사용 시작 → 예전 베일
            p.RaiseSkillBuffStarted(SkillType.R, 0.01f);
            cd.StartCooldown(SkillType.R, 9f, 0f);
            await UniTask.Delay(300, DelayType.Realtime);
            Check(bg.activeInHierarchy && Near(fill.color, veil) && Near(txt.color, ink) && fill.fillAmount > 0.9f,
                  $"재사용 시작 → 어두운 베일(α {fill.color.a:0.00}) · 잉크 숫자 「{txt.text}」 · 부채꼴 {fill.fillAmount:0.00}");
            await Shot("RA1105_cd");

            // ③ 석궁처럼 재사용이 버프와 함께 돌고 있으면 켜짐을 그리지 않는다
            p.RaiseSkillBuffStarted(SkillType.R, 6f);
            await UniTask.Delay(400, DelayType.Realtime);
            Check(Near(fill.color, veil), $"재사용 중 강화 신호 — 켜짐 안 그림(α {fill.color.a:0.00})");

            // ④ 재사용 끝 · 강화도 끝 → 덮개 숨김
            cd.ResetCooldown(SkillType.R);
            p.RaiseSkillBuffStarted(SkillType.R, 0.01f);
            await UniTask.Delay(400, DelayType.Realtime);
            Check(!bg.activeInHierarchy && string.IsNullOrEmpty(txt.text) && Near(fill.color, veil), $"둘 다 끝 — 덮개 숨김 {!bg.activeInHierarchy} · 색 돌아옴");
        }
        catch (System.Exception e) { s_fail++; s_sb.AppendLine("예외: " + e); }
        finally
        {
            cd?.ResetCooldown(SkillType.R);
            s_sb.Insert(0, s_fail == 0 ? "PASS\n" : $"FAIL {s_fail}\n");
            File.WriteAllText("Temp/ractive1105.txt", s_sb.ToString());
            Debug.Log($"{Tag} 끝\n{s_sb}");
        }
    }

    private static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f && Mathf.Abs(a.a - b.a) < 0.02f;

    private static void Check(bool ok, string what)
    {
        if (!ok) s_fail++;
        s_sb.AppendLine($"{(ok ? "PASS" : "FAIL")} {what}");
    }

    private static async UniTask Shot(string name)
    {
        Directory.CreateDirectory("Temp/ui_shots");
        ScreenCapture.CaptureScreenshot($"Temp/ui_shots/{name}.png");
        await UniTask.Delay(400, DelayType.Realtime);
    }
}
#endif
