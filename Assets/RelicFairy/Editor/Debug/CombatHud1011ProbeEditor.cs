#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10-11 전투 HUD 위계 · 가독성 실측(테스트 허브 → 전투방, 플레이 중) — 비교 리서치 A §1 「고칠 값」.
/// 세 장: ① 평상시(E · R 재사용 대기 · 유물 게이지 충전 60%) ② 스킬 준비됨(E · R 금 2px · 정오 → Q 바깥 고리 3px · Q 빛) ③ 체력 낮음(22% · 붉은 맥박 꼭대기).
/// 재는 것: 체력바 10 > 게이지 · 체력 숫자 17 · 선 굵기 세 가지(1 · 2 · 3) · 미니맵 · 재화 · 버프 칸 1px 잉크 · 공 · 방 글자 잉크 ·
/// HUD 본체 DNF · 가짜 굵게 0 · 글자 크기 단계 · 후광 한 곳 · 아이콘 ÷ 원.
/// 결과: Temp/ui_shots/U11_hud_1_rest.png · U11_hud_2_ready.png · U11_hud_3_lowhp.png · Temp/ui_hud_1011_probe.txt · 로그 「[CombatHud1011] 끝」.
/// 체력 · 재사용 대기 · 유물 게이지는 끝에 되돌린다. 플레이 중 Assets/ 쓰기 없음.
/// </summary>
public static class CombatHud1011ProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly float[] TextSteps = { 13f, 15f, 17f, 20f, 24f, 32f, 48f };
    private static StringBuilder _sb;
    private static int _pass, _fail;

    [MenuItem("RelicFairy/Debug/10-11 전투 HUD 촬영 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[CombatHud1011] 플레이 중에"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        _sb = new StringBuilder("10-11 전투 HUD 위계 · 가독성\n");
        _pass = _fail = 0;
        Directory.CreateDirectory("Temp/ui_shots");

        var player = GameRunBootstrapper.Instance?.Run?.Player ?? Object.FindFirstObjectByType<PlayerController>();
        var hud    = Object.FindFirstObjectByType<CombatPanelView>();
        var view   = Object.FindFirstObjectByType<HudView>();
        var stats  = player != null ? player.RuntimeStats : null;
        var cd     = player != null ? player.CooldownTracker : null;
        var zen    = Object.FindFirstObjectByType<ZenithGauge>();
        var mad    = zen == null ? Object.FindFirstObjectByType<MadnessStack>() : null;
        int hpBefore = stats != null ? stats.Hp : -1;
        var zenBefore = zen != null ? zen.Capture() : default;
        var madBefore = mad != null ? mad.Capture() : default;
        try
        {
            if (hud == null || player == null || stats == null || cd == null)
            {
                _sb.AppendLine($"전투 HUD · 플레이어 없음(HUD {hud != null} · 플레이어 {player != null}) — 테스트 허브에서 전투방에 들어간 뒤 실행");
                _fail++;
                return;
            }
            player.SetInvincible(30f);
            var root = hud.transform;

            // ① 평상시 — 체력 가득 · E · R 재사용 대기 · 유물 게이지 충전 60%
            stats.SetHp(stats.MaxHp);
            cd.StartCooldown(SkillType.E, 6.4f);
            cd.StartCooldown(SkillType.R, 9.6f);
            SetRelicCharging(zen, mad, 0.6f);
            await UniTask.Delay(500, ignoreTimeScale: true);
            await Shot("U11_hud_1_rest");
            _sb.AppendLine("① 평상시");
            CheckBars(hud, root);
            CheckHpText(hud);
            CheckRing("E 평소 1px", Frame(root, "HUD_ESkile"), 1f);
            CheckRing("R 평소 1px", Frame(root, "HUD_RSkile"), 1f);
            CheckRing("Q 안쪽 평소 1px", Frame(root, "HUD_QSkile"), 1f);
            CheckRing("Q 게이지 고리 3px", Find(root, "QGaugeRing")?.GetComponent<Image>(), 3f);
            CheckWeaponRings(hud);
            CheckStats(root);
            CheckMinimap(view);
            CheckCurrency(view);
            CheckBuffCells(root);
            CheckFonts(root, view);
            CheckIconRatio(root);

            // ② 스킬 준비됨 — 재사용 대기 끝 · 정오(절정) → Q를 지금 누를 수 있음
            cd.ResetCooldown(SkillType.E);
            cd.ResetCooldown(SkillType.R);
            cd.ResetCooldown(SkillType.Q);
            SetRelicReady(zen, mad);
            await UniTask.Delay(900, ignoreTimeScale: true);   // 테가 그려짐(0.25초) · 정오 진입 번쩍(0.4초)이 가라앉을 때까지
            await Shot("U11_hud_2_ready");
            _sb.AppendLine("② 스킬 준비됨");
            var skillE = typeof(CombatPanelView).GetField("skillE", Inst)?.GetValue(hud) as CombatPanelView.SkillSlotUI;
            if (skillE != null && skillE.IsLocked) _sb.AppendLine("  (E · R 스킬이 없는 무기 — 준비됨 2px 검사 건너뜀. 스킬 있는 무기를 들고 다시)");
            else
            {
                CheckRing("E 준비됨 2px", Frame(root, "HUD_ESkile"), 2f);
                CheckRing("R 준비됨 2px", Frame(root, "HUD_RSkile"), 2f);
            }
            var qFrame = Frame(root, "HUD_QSkile");
            CheckRing("Q 안쪽 준비됨도 1px", qFrame, 1f);
            Check("Q 안쪽 테 금색 아님(흰 선 — 준비됨은 바깥 고리가 말한다)", qFrame != null && qFrame.color.b > 0.9f, qFrame != null ? Col(qFrame.color) : "없음");
            var qGlow   = Find(root, "QGlow")?.GetComponent<Image>();
            var sunGlow = Find(root, "RelicSunGauge") is { } sun && sun.gameObject.activeInHierarchy ? Find(sun, "GaugeGlow")?.GetComponent<Image>()
                        : Find(root, "RelicIdentityBar") is { } bar ? Find(bar, "GaugeGlow")?.GetComponent<Image>() : null;
            Check("Q 빛 켜짐(지금 누를 것)", qGlow != null && qGlow.color.a > 0.2f, $"Q {qGlow?.color.a:0.00}");
            // 정오 · 절정의 가운데 빛은 10-08 사용자 확정 연출 — 후광 한 곳 규칙보다 앞선다. 기록만 한다.
            _sb.AppendLine($"  기록 — 정오 가운데 빛 {sunGlow?.color.a:0.00}(사용자 확정 연출 · 검사 안 함)");

            // ③ 체력 낮음 — 22% · 바탕 맥박이 가장 붉은 때(1.2초 주기의 가운데)
            stats.SetHp(Mathf.Max(1, Mathf.RoundToInt(stats.MaxHp * 0.22f)));
            await UniTask.Delay(700, ignoreTimeScale: true);   // 채움이 따라 내려올 때까지(초당 3)
            for (int guard = 0; guard < 120 && Mathf.Abs(Mathf.Repeat(Time.unscaledTime, 1.2f) - 0.6f) > 0.04f; guard++)
                await UniTask.Yield();
            await Shot("U11_hud_3_lowhp");
            _sb.AppendLine("③ 체력 낮음");
            var fill  = typeof(CombatPanelView).GetField("hpFillImage", Inst)?.GetValue(hud) as Image;
            var track = typeof(CombatPanelView).GetField("_wHpTrack", Inst)?.GetValue(hud) as Image;
            Check("채움 = 낮은 체력 색", fill != null && Near(fill.color, UITheme.Thin.HpLow), fill != null ? Col(fill.color) : "없음");
            Check("바탕이 붉게 맥박", track != null && track.color.r > track.color.b + 0.15f, track != null ? Col(track.color) : "없음");
        }
        catch (System.Exception e) { _sb.AppendLine("예외: " + e); _fail++; }
        finally
        {
            try
            {
                if (stats != null && hpBefore > 0) stats.SetHp(hpBefore);
                if (cd != null) { cd.ResetCooldown(SkillType.E); cd.ResetCooldown(SkillType.R); cd.ResetCooldown(SkillType.Q); }
                if (zen != null) { zen.Restore(zenBefore); zen.Paused = false; }
                if (mad != null) mad.Restore(madBefore);
            }
            catch (System.Exception e) { _sb.AppendLine("되돌리기 예외: " + e.Message); }
            _sb.AppendLine($"합계 통과 {_pass} · 실패 {_fail}");
            File.WriteAllText("Temp/ui_hud_1011_probe.txt", _sb.ToString());
            Debug.Log("[CombatHud1011] 끝\n" + _sb);
        }
    }

    // ── 상태 맞추기 ──

    private static void SetRelicCharging(ZenithGauge zen, MadnessStack mad, float ratio)
    {
        if (zen != null)
        {
            zen.Paused = true;
            zen.Restore(new RelicResourceState { phase = (int)ZenithGauge.ZPhase.Charging, fill = zen.DurationOf(ZenithGauge.ZPhase.Charging) * ratio });
        }
        else if (mad != null)
        {
            mad.Restore(new RelicResourceState { fill = 1000f });   // 상한으로 잘린다 → 상한 × 비율
            mad.Restore(new RelicResourceState { fill = Mathf.Round(mad.Capture().fill * ratio) });
        }
    }

    private static void SetRelicReady(ZenithGauge zen, MadnessStack mad)
    {
        if (zen != null) { zen.Paused = false; zen.OpenNoonNow(); zen.Paused = true; }
        else if (mad != null) mad.Restore(new RelicResourceState { fill = 0f, aux = 8f });   // 광란 8초
    }

    // ── 검사 ──

    private static void CheckBars(CombatPanelView hud, Transform root)
    {
        var hp = Find(root, "HpBar") as RectTransform;
        float gauge = Find(root, "RelicSunGauge") is { } sun && sun.gameObject.activeInHierarchy && Find(sun, "SunGauge") is RectTransform g ? g.rect.height
                    : Find(root, "RelicIdentityBar") is RectTransform bar && bar.gameObject.activeInHierarchy ? bar.rect.height : -1f;
        Check("체력바 600×10", hp != null && Mathf.Approximately(hp.rect.height, 10f) && Mathf.Approximately(hp.rect.width, 600f), hp != null ? $"{hp.rect.width:0}×{hp.rect.height:0}" : "없음");
        Check("체력바가 유물 게이지보다 굵다(정오 8 포함)", hp != null && gauge > 0f && hp.rect.height > gauge && hp.rect.height > 8f, $"체력 {hp?.rect.height:0} · 게이지 {gauge:0}");
    }

    private static void CheckHpText(CombatPanelView hud)
    {
        var t = typeof(CombatPanelView).GetField("hpText", Inst)?.GetValue(hud) as TMP_Text;
        Check("체력 숫자 TMP 17 · Noto Medium", t != null && Mathf.Approximately(t.fontSize, 17f) && t.font == UITheme.Font(UIWeight.Medium),
              t != null ? $"{t.fontSize:0} · {t.font?.name} · 「{t.GetParsedText()}」" : "없음");
    }

    /// <summary>얇은 원 테 스프라이트(UIThinShapes.Ring = 「ThinR{지름×2}_{굵기×2}」)의 굵기가 기대값인가.</summary>
    private static void CheckRing(string name, Image img, float strokePx)
    {
        string want = "_" + (strokePx * UIThinShapes.Oversample).ToString("0.##");
        string got  = img != null && img.sprite != null ? img.sprite.name : "없음";
        Check(name, img != null && img.sprite != null && got.StartsWith("ThinR") && got.EndsWith(want), got);
    }

    private static void CheckWeaponRings(CombatPanelView hud)
    {
        var roots  = typeof(CombatPanelView).GetField("_weaponSlotRoot", Inst)?.GetValue(hud) as RectTransform[];
        var frames = typeof(CombatPanelView).GetField("_weaponThin", Inst)?.GetValue(hud) as UIThinFrame[];
        if (roots == null || frames == null) { Check("무기 원 테", false, "필드 없음"); return; }
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] == null || frames[i] == null || !frames[i].TryGetComponent<Image>(out var img)) continue;
            float d = roots[i].sizeDelta.x;
            int want = Mathf.RoundToInt((d + 2f) * UIThinShapes.Oversample);
            Check($"무기 {i + 1} 원 테 = 원 크기({d:0})에 맞춘 1px", img.sprite != null && img.sprite.texture.width == want && img.sprite.name.EndsWith("_2"),
                  $"{img.sprite?.name} · {Col(img.color)}");
        }
    }

    private static void CheckStats(Transform root)
    {
        foreach (var name in new[] { "AtkText", "DefText" })
        {
            var t = Find(root, name)?.GetComponent<TMP_Text>();
            var icon = t != null ? t.transform.Find("StatIcon")?.GetComponent<Image>() : null;
            Check($"{name} 글자 잉크 85% · 색은 아이콘에만", t != null && Near(t.color, UITheme.Thin.InkA(0.85f)) && (icon == null || !Near(icon.color, Color.white)),
                  t != null ? $"글자 {Col(t.color)} · 아이콘 {(icon != null ? Col(icon.color) : "없음")}" : "없음");
        }
    }

    private static void CheckMinimap(HudView view)
    {
        var map  = view != null ? view.MinimapView : null;
        var edge = map != null ? map.transform.Find("Edge")?.GetComponent<Image>() : null;
        Check("미니맵 테 1px 잉크 18%(금 아님)", edge != null && edge.sprite != null && edge.sprite.name == "ThinMinimapEdge" && Near(edge.color, UITheme.Thin.LineIdle),
              edge != null ? $"{edge.sprite?.name} · {Col(edge.color)}" : "없음");
    }

    private static void CheckCurrency(HudView view)
    {
        var row = view != null ? Find(view.transform, "CurrencyRow") : null;
        if (row == null) { Check("재화 줄", false, "없음"); return; }
        var medium = UITheme.Font(UIWeight.Medium);
        foreach (Transform pill in row)
        {
            var frame = pill.Find("Frame")?.GetComponent<Image>();
            var value = pill.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.transform.parent?.name == "Content");
            Check($"{pill.name} 테 1px 잉크 18% · 숫자 Noto Medium 24{(pill.gameObject.activeSelf ? "" : " (숨김)")}",
                  frame != null && frame.sprite != null && frame.sprite.name == "ThinCurrencyPill" && Near(frame.color, UITheme.Thin.LineIdle)
                  && value != null && value.font == medium && Mathf.Approximately(value.fontSize, 24f) && value.fontStyle == FontStyles.Normal,
                  $"{frame?.sprite?.name} · {(frame != null ? Col(frame.color) : "")} · {value?.font?.name} {value?.fontSize:0} {value?.fontStyle}");
        }
    }

    private static void CheckBuffCells(Transform root)
    {
        var cells = root.GetComponentsInChildren<BuffCell>(false);
        if (cells.Length == 0) { _sb.AppendLine("  (버프 칸 없음 — 칸 테 검사 건너뜀)"); return; }
        var frame = cells[0].transform.Find("Frame")?.GetComponent<Image>();
        Check($"버프 칸 테 1px 잉크 18%({cells.Length}칸)", frame != null && frame.sprite != null && frame.sprite.name.StartsWith("ThinO") && Near(frame.color, UITheme.Thin.LineIdle),
              frame != null ? $"{frame.sprite?.name} · {Col(frame.color)}" : "없음");
    }

    /// <summary>HUD 본체(전투 패널 · 재화 줄 · 미니맵) 글자 — DNF 0 · 가짜 굵게 0 · 크기 단계.</summary>
    private static void CheckFonts(Transform root, HudView view)
    {
        var allowed = new HashSet<TMP_FontAsset> { UITheme.Font(UIWeight.Light), UITheme.Font(UIWeight.Regular), UITheme.Font(UIWeight.Medium) };
        var texts = root.GetComponentsInChildren<TMP_Text>(false).ToList();
        var row = view != null ? Find(view.transform, "CurrencyRow") : null;
        if (row != null) texts.AddRange(row.GetComponentsInChildren<TMP_Text>(false));
        texts = texts.Where(t => !string.IsNullOrEmpty(t.text)).ToList();
        var dnf  = texts.Where(t => !allowed.Contains(t.font)).Select(t => $"{t.name}({t.font?.name})").ToList();
        var bold = texts.Where(t => (t.fontStyle & FontStyles.Bold) != 0).Select(t => t.name).ToList();
        var off  = texts.Where(t => !TextSteps.Any(s => Mathf.Abs(s - t.fontSize) < 0.01f)).Select(t => $"{t.name} {t.fontSize:0.#}").ToList();
        Check("톤 v2 글꼴 올라옴", UISkin.ThemeFonts != null, "");
        Check($"HUD 본체 DNF 0({texts.Count}줄)", dnf.Count == 0, string.Join(", ", dnf));
        Check("가짜 굵게 0", bold.Count == 0, string.Join(", ", bold));
        // 알림 · 툴팁 등 이번 범위 밖 글자도 섞이므로 기록만 한다
        _sb.AppendLine($"  기록 — 글자 크기 단계(13 · 15 · 17 · 20 · 24 · 32 · 48) 밖: {(off.Count == 0 ? "없음" : string.Join(", ", off))}");
    }

    /// <summary>E · R 아이콘 ÷ 보이는 원(테) — 리서치 실측 0.77, 명조 0.68. 기록만 한다.</summary>
    private static void CheckIconRatio(Transform root)
    {
        foreach (var slot in new[] { "HUD_ESkile", "HUD_RSkile" })
        {
            var s = Find(root, slot) as RectTransform;
            var frame = Frame(root, slot);
            var icon = s != null ? s.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i != frame && i.sprite != null && i.preserveAspect && i.gameObject.activeInHierarchy) : null;
            if (s == null || frame == null || icon == null) { _sb.AppendLine($"  기록 — {slot} 아이콘 ÷ 원: 없음"); continue; }
            var r = icon.rectTransform.rect;
            float drawn = Mathf.Max(r.width, r.height);
            if (icon.sprite.rect.width > 0f && icon.sprite.rect.height > 0f)   // preserveAspect로 실제로 그려지는 긴 변
                drawn = Mathf.Min(r.width / icon.sprite.rect.width, r.height / icon.sprite.rect.height) * Mathf.Max(icon.sprite.rect.width, icon.sprite.rect.height);
            _sb.AppendLine($"  기록 — {slot} 아이콘 ÷ 원: {drawn / frame.rectTransform.rect.width:0.00} (아이콘 {drawn:0} · 원 {frame.rectTransform.rect.width:0})");
        }
    }

    // ── 도우미 ──

    private static Image Frame(Transform root, string slot) => Find(root, slot)?.Find("SkinFrame")?.GetComponent<Image>();

    private static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f && Mathf.Abs(a.a - b.a) < 0.02f;

    private static string Col(Color c) => $"({c.r:0.00},{c.g:0.00},{c.b:0.00},{c.a:0.00})";

    private static void Check(string name, bool ok, string note)
    {
        if (ok) _pass++; else _fail++;
        _sb.AppendLine($"  {(ok ? "통과" : "실패")} — {name}{(string.IsNullOrEmpty(note) ? "" : " · " + note)}");
    }

    private static Transform Find(Transform t, string name)
    {
        if (t == null) return null;
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++) { var hit = Find(t.GetChild(i), name); if (hit != null) return hit; }
        return null;
    }

    private static async UniTask Shot(string name)
    {
        ScreenCapture.CaptureScreenshot($"Temp/ui_shots/{name}.png");
        await UniTask.Yield();
        await UniTask.Yield();
    }
}
#endif
