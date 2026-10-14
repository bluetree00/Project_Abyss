#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-07 얇은 HUD 실측(런 중) — 쉬는 HUD · E를 쓴 직후(눌림 + 쿨다운) · E가 다시 준비되는 순간(테 그려짐 0.08 · 0.3초) ·
/// 무기 교체 · 체력 30% 이하 · 회복을 차례로 찍는다(7장).
/// 강제로 쿨다운을 넣은 뒤 도구가 직접 0으로 되돌려 「준비됨」 켜짐 연출을 부른다(프레젠터는 바뀔 때만 민다).
/// 결과: Temp/ui_shots/HUD_*.png · 로그 「[ThinHud] 끝」.
/// </summary>
public static class ThinHudProbeEditor
{
    [MenuItem("RelicFairy/Debug/10-07 얇은 HUD 실측 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) return;
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("얇은 HUD 실측\n");
        Directory.CreateDirectory("Temp/ui_shots");
        try
        {
            var hud = Object.FindFirstObjectByType<CombatPanelView>();
            if (hud == null) { sb.AppendLine("HUD 없음"); return; }
            GameRunBootstrapper.Instance?.Run?.Player?.SetInvincible(60f);
            await Shot("HUD_0_rest", 300);

            hud.SetSkillCooldown(SkillType.E, 4f, 6f);          // 쓴 순간 — 눌림 · 잉크 테 · 부채꼴
            await Shot("HUD_1_used", 60);
            // 프레젠터는 쿨다운이 바뀔 때만 밀어 준다 — 강제로 넣은 값이 그대로 남으므로 직접 「준비됨」으로 되돌려 켜짐 연출을 부른다.
            hud.SetSkillCooldown(SkillType.E, 0f, 6f);
            float t0 = Time.unscaledTime;
            await UniTask.Delay(80, ignoreTimeScale: true);
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/HUD_2_trace.png");
            await UniTask.Delay(220, ignoreTimeScale: true);
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/HUD_3_ready.png");
            sb.AppendLine($"  켜짐 촬영 {Time.unscaledTime - t0:0.00}초");

            hud.SetActiveWeapon(1);
            await Shot("HUD_4_swap", 120);
            hud.SetActiveWeapon(0);

            hud.SetHp(220, 1000);
            await Shot("HUD_5_low", 600);
            hud.SetHp(900, 1000);
            await Shot("HUD_6_heal", 200);

            // I3 — Awake 뒤에 생기는 글자(물약 개수 등)까지 Noto · 굵게 없음이어야 한다.
            hud.SetPotion(2, 3);
            await UniTask.Delay(100, ignoreTimeScale: true);
            var thinFonts = new[] { UITheme.Font(UIWeight.Light), UITheme.Font(UIWeight.Regular), UITheme.Font(UIWeight.Medium) };
            int bad = 0;
            var names = new StringBuilder();
            foreach (var t in hud.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                bool thin = System.Array.IndexOf(thinFonts, t.font) >= 0;
                bool bold = (t.fontStyle & TMPro.FontStyles.Bold) != 0;
                if (thin && !bold) continue;
                bad++;
                if (bad <= 8) names.Append(t.name).Append(thin ? "(굵게) " : "(옛 글꼴) ");
            }
            sb.AppendLine($"  CHECK I3 HUD 글자 전부 Noto · 굵게 없음: {bad == 0} (어긋남 {bad}: {names})");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/thin_hud_probe.txt", sb.ToString());
            Debug.Log("[ThinHud] 끝\n" + sb);
        }
    }

    [MenuItem("RelicFairy/Debug/10-08 스킬 HUD 준비 · 거절 · 강화 막대 실측 (런 중)")]
    private static void RunA9()
    {
        if (!EditorApplication.isPlaying) return;
        RunA9Async().Forget();
    }

    /// <summary>
    /// A9(10-08) — E 칸만 3장: 준비 반짝임(장식 테가 흰빛으로 그려져 금빛으로 가라앉음) · 거절(재사용 대기 중 누름 → 칸 살짝 흔들림) ·
    /// 강화 남은 시간 막대(2초 강화 → 1초 뒤 절반 · 끝나면 사라짐). 거절 · 강화는 플레이어 이벤트를 그대로 쏴 프레젠터 배선까지 본다.
    /// 결과: Temp/ui_shots/A9_*.png · Temp/a9_hud_probe.txt · 로그 「[A9Hud] 끝」.
    /// </summary>
    private static async UniTaskVoid RunA9Async()
    {
        var sb = new StringBuilder("스킬 HUD A9 실측\n");
        Directory.CreateDirectory("Temp/ui_shots");
        try
        {
            var hud    = Object.FindFirstObjectByType<CombatPanelView>();
            var player = GameRunBootstrapper.Instance?.Run?.Player;
            var eSlot  = hud != null ? FindDeep(hud.transform, "HUD_ESkile") as RectTransform : null;
            var frame  = eSlot != null ? eSlot.Find("SkinFrame")?.GetComponent<UnityEngine.UI.Image>() : null;
            if (player == null || frame == null) { sb.AppendLine("HUD · 플레이어 · E 칸 없음"); return; }
            player.SetInvincible(60f);

            // 1) 준비 반짝임 — 쿨다운을 넣었다 빼서 켜짐 연출을 부르고, 첫 0.15초 동안 테 색의 파랑(흰빛일수록 큼) 최댓값을 잰다.
            hud.SetSkillCooldown(SkillType.E, 4f, 6f);
            await UniTask.Delay(250, ignoreTimeScale: true);
            hud.SetSkillCooldown(SkillType.E, 0f, 6f);
            float peakB = 0f; bool shot = false;
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 0.15f)
            {
                peakB = Mathf.Max(peakB, frame.color.b);
                if (!shot) { ScreenCapture.CaptureScreenshot("Temp/ui_shots/A9_1_flash.png"); shot = true; }
                await UniTask.Yield();
            }
            await UniTask.Delay(400, ignoreTimeScale: true);
            float restB = frame.color.b;
            sb.AppendLine($"  CHECK 준비 반짝임 — 켜지는 순간 테가 쉬는 금빛보다 밝다: {peakB > restB + 0.15f} (파랑 {peakB:0.00} → {restB:0.00})");

            // 2) 거절 — 재사용 대기 중에 E를 누른 신호(PlayerController.SkillRejected).
            hud.SetSkillCooldown(SkillType.E, 4f, 6f);
            await UniTask.Delay(250, ignoreTimeScale: true);   // 눌림(0.14초)이 끝난 뒤
            Vector2 basePos = eSlot.anchoredPosition;
            Raise(player, "SkillRejected", SkillType.E);
            float maxDx = 0f; shot = false;
            t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 0.4f)
            {
                maxDx = Mathf.Max(maxDx, Mathf.Abs(eSlot.anchoredPosition.x - basePos.x));
                if (!shot && Time.unscaledTime - t0 > 0.03f) { ScreenCapture.CaptureScreenshot("Temp/ui_shots/A9_2_reject.png"); shot = true; }
                await UniTask.Yield();
            }
            float back = (eSlot.anchoredPosition - basePos).magnitude;
            sb.AppendLine($"  CHECK 거절 — 칸이 살짝 흔들렸다(0.5~4px): {maxDx > 0.5f && maxDx <= 4f} ({maxDx:0.0}px)");
            sb.AppendLine($"  CHECK 거절 — 제자리로 돌아왔다: {maxDx > 0.5f && back < 0.01f} ({back:0.000}px)");
            hud.SetSkillCooldown(SkillType.E, 0f, 6f);

            // 3) 강화 남은 시간 막대 — 2초 강화 신호(PlayerController.SkillBuffStarted). 강화는 게임 시간으로 끝나니 막대도 게임 시간.
            Raise(player, "SkillBuffStarted", SkillType.E, 2f);
            await UniTask.Delay(1000);
            var bar   = eSlot.Find("SkillBuffBar");
            float k   = bar != null && bar.childCount > 0 ? ((RectTransform)bar.GetChild(0)).anchorMax.x : -1f;
            ScreenCapture.CaptureScreenshot("Temp/ui_shots/A9_3_buff.png");
            sb.AppendLine($"  CHECK 강화 막대 — 1초 뒤 절반쯤(0.35~0.65): {k > 0.35f && k < 0.65f} ({k:0.00} · timeScale {Time.timeScale:0.00})");
            await UniTask.Delay(1300);
            sb.AppendLine($"  CHECK 강화 막대 — 끝나면 사라짐: {bar != null && !bar.gameObject.activeSelf}");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            File.WriteAllText("Temp/a9_hud_probe.txt", sb.ToString());
            Debug.Log("[A9Hud] 끝\n" + sb);
        }
    }

    /// <summary>플레이어 이벤트를 밖에서 그대로 쏜다(필드형 이벤트의 숨은 대리자) — 구독한 쪽(프레젠터)이 실제로 받는지 본다.</summary>
    private static void Raise(PlayerController p, string evt, params object[] args)
    {
        var f = typeof(PlayerController).GetField(evt, BindingFlags.Instance | BindingFlags.NonPublic);
        (f?.GetValue(p) as System.Delegate)?.DynamicInvoke(args);
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindDeep(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    private static async UniTask Shot(string name, int waitMs)
    {
        await UniTask.Delay(waitMs, ignoreTimeScale: true);
        ScreenCapture.CaptureScreenshot($"Temp/ui_shots/{name}.png");
        await UniTask.Yield();
    }
}
#endif
