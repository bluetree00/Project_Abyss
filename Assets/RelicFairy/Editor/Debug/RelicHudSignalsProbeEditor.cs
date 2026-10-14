#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [실측 · 플레이 중] 10-13 유물 · 장비 HUD 신호(UI 요청서 83 §1~4) — 기존 실측이 만들지 못하는 상태를 공개 API · 리플렉션으로 맞춰 찍는다.
/// 지금 런의 유물(가웨인 또는 랜슬롯)만 본다 — 두 유물을 다 보려면 유물을 바꿔 한 번 더 돌린다. 테스트 허브에서 런을 열고 전투방에 들어선 뒤 실행.
/// 가웨인: ① 여명 — 태양흔 상한 ▮▮▮(맥동 · 흐림) · 1칸 + 짓누름 아이콘 · 흑점 7 · 엇갈린 빛 대기 별 · Q 잠김 ② 정오 — 태양흔 또렷 · 엇갈린 빛 켜짐 · Q 열림
///         ③ 황혼 사용권 2 — Q 「×2」.
/// 랜슬롯: ① 피 냄새 멈춤(멈춤 선 · 「멈춤 N초」) · 원한 3/5 · 낙인 상한 맥동 · 대기 별 ② 원한 가득(금 테) · 엇갈린 빛 켜짐 · 끝의 문턱(떨림 · 「멈춤」)
///         ③ 광란 심판 2회 — Q 「×2」.
/// 형(공통 · 근접 칸이 칼바람이 아니면 T1 카타나를 쥐여 준다): ④ 「다음 타에 발동」 테 ⑤ 시간 감소 = 회색(붉음 없음 · S9) ⑥ 돌려받은 칸 금 테(+ 맞음 = 붉음 기록).
/// 결과: Temp/ui_shots/RHS_*.png(6장) · Temp/relic_hud_signals.txt · 로그 「[RelicHudSignals] 끝」. 플레이 중 Assets/ 쓰기 없음.
/// 게이지 · 광기 · 원한 · 엇갈린 빛 · 사용권은 끝에 되돌린다(테스트 런의 근접 무기 교체 · 허브 생성은 남는다).
/// </summary>
public static class RelicHudSignalsProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const string Katana = "Assets/RelicFairy/Weapon/Katana/Data/T1_Katana.asset";
    private const string OutPath = "Temp/relic_hud_signals.txt";
    private static readonly string[] Monsters = { "Slime/Slime", "Skeleton/Skeleton", "Orc/Orc" };
    private static StringBuilder _sb;
    private static int _pass, _fail;

    [MenuItem("RelicFairy/Debug/10-13 유물 · 장비 HUD 신호 촬영 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null)
        {
            Debug.LogWarning("[RelicHudSignals] 런에 들어간 뒤(테스트 허브 → 전투방) 실행한다.");
            return;
        }
        RunAsync(GameRunBootstrapper.Instance.Run.Player).Forget();
    }

    private static async UniTaskVoid RunAsync(PlayerController p)
    {
        _sb = new StringBuilder("10-13 유물 · 장비 HUD 신호\n");
        _pass = _fail = 0;
        Directory.CreateDirectory("Temp/ui_shots");
        var mons = new List<MonsterBase>();
        var restore = new List<Action>();
        HubGui("HideTestHubGui");
        try
        {
            p.SetInvincible(120f);
            p.CooldownTracker?.ResetCooldown(SkillType.Q);
            var hud = UnityEngine.Object.FindFirstObjectByType<CombatPanelView>();
            if (hud == null) { Check("전투 HUD", false, "없음"); return; }
            await SpawnAsync(p, mons);

            if (p.RelicBehavior is GawainZenithRelic g) await GawainAsync(p, g, hud, mons, restore);
            else if (p.RelicBehavior is LancelotMadnessRelic l) await LancelotAsync(p, l, hud, mons, restore);
            else _sb.AppendLine($"유물 {p.RelicBehavior?.GetType().Name ?? "없음"} — 유물 칸 건너뜀");

            await FormAsync(p, hud);
        }
        catch (Exception e) { _sb.AppendLine("예외: " + e); _fail++; }
        finally
        {
            for (int i = restore.Count - 1; i >= 0; i--)
            {
                try { restore[i](); }
                catch (Exception e) { _sb.AppendLine("되돌리기 예외: " + e.Message); }
            }
            foreach (var mb in mons) if (mb != null) UnityEngine.Object.Destroy(mb.gameObject);
            HubGui("RestoreTestHubGui");
            _sb.AppendLine($"합계 통과 {_pass} · 실패 {_fail}");
            File.WriteAllText(OutPath, _sb.ToString());
            Debug.Log($"[RelicHudSignals] 끝 — 통과 {_pass} · 실패 {_fail} → {OutPath} · Temp/ui_shots/RHS_*.png\n{_sb}");
        }
    }

    // ── 몬스터 셋(머리 위 상태 칸) ──

    private static async UniTask SpawnAsync(PlayerController p, List<MonsterBase> into)
    {
        Vector3 fwd = p.transform.forward; fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        for (int i = 0; i < Monsters.Length; i++)
        {
            Vector3 pos = p.transform.position + fwd * 5.5f + right * ((i - 1f) * 2.8f);
            var mb = await Managers.ObjectPooler.SpawnAsync<MonsterBase>(Monsters[i], ObjectPoolerManager.PoolType.Monster, pos, Quaternion.LookRotation(-fwd));
            if (mb == null) { _sb.AppendLine($"스폰 실패 {Monsters[i]}"); continue; }
            mb.HpFloorMin1 = true;
            into.Add(mb);
        }
        await UniTask.Delay(1200, ignoreTimeScale: true);
        foreach (var mb in into) if (mb != null) mb.TakeDamage(mb.EffectiveMaxHp * 0.15f, p.gameObject, 0f);   // 바가 뜨게
        if (into.Count > 1 && into[1] != null) into[1].ApplyDamageTakenAmp(0.06f, 60f, "form_crush");   // 짓누름(대검 형 · 10-12 D4)
        await UniTask.Delay(400, ignoreTimeScale: true);
    }

    /// <summary>적 셋 — 0: 상한 · 1: 1칸(+ 짓누름) · 2: 가웨인 = 흑점 7 / 랜슬롯 = 낙인 2.</summary>
    private static void SetMarks(List<MonsterBase> mons, bool sun, int cap)
    {
        for (int i = 0; i < mons.Count; i++)
        {
            var mb = mons[i];
            if (mb == null || mb.IsDead) continue;
            var st = RelicMarkStatus.Of(mb.gameObject, true);
            int n = i == 0 ? cap : i == 1 ? 1 : 2;
            if (!sun) { st.ConsumeBrand(); st.AddBrand(n, cap, out _, ignoreGap: true); continue; }
            if (i == 2) { st.ConsumeSunmark(); st.ConsumeBlackspot(); st.AddBlackspot(7, ignoreGap: true); }
            else st.SetSunmark(n, cap);
        }
    }

    // ── 가웨인 ──

    private static async UniTask GawainAsync(PlayerController p, GawainZenithRelic relic, CombatPanelView hud, List<MonsterBase> mons, List<Action> restore)
    {
        _sb.AppendLine("■ 가웨인");
        var zen = relic.Gauge;
        var hub = GawainMemoryHub.Ensure(p);
        if (zen == null || hub == null) { Check("가웨인 게이지 · 허브", false, $"게이지 {zen != null} · 허브 {hub != null}"); return; }
        var zenBefore = zen.Capture();
        int bonusBefore = relic.BonusCasts;
        restore.Add(() => { zen.Restore(zenBefore); zen.Paused = false; });
        restore.Add(() => typeof(GawainZenithRelic).GetField("_bonusCasts", Inst)?.SetValue(relic, bonusBefore));
        restore.Add(() => SetCrossLight(hub, false));
        int cap = GawainMemoryHub.SunmarkCapOf(p);
        await UniTask.Delay(300, ignoreTimeScale: true);

        // ① 여명 60% — 태양흔 흐림 · 상한 맥동 · 흑점 · 대기 별 · Q 잠김
        zen.Paused = true;
        zen.Restore(new RelicResourceState { phase = (int)ZenithGauge.ZPhase.Charging, fill = zen.DurationOf(ZenithGauge.ZPhase.Charging) * 0.6f });
        SetMarks(mons, sun: true, cap);
        View()?.SetCrossLight(true, false, false);   // 대기 별 — 반응 조각을 런에 넣지 않고 화면만 맞춘다
        await UniTask.Delay(700, ignoreTimeScale: true);
        await Shot("RHS_1_gawain_dawn");
        _sb.AppendLine("① 여명");
        CheckPips(At(mons, 0), "태양흔 상한", cap, cap, dim: true);
        CheckPips(At(mons, 1), "태양흔 1칸", 1, cap, dim: true);
        CheckCount(At(mons, 2), "흑점 7");
        CheckCrush(At(mons, 1));
        await CheckPulse(mons);
        CheckStar(hud, "엇갈린 빛 대기 = 빈 별", on: false);
        CheckQ(hud, "여명 — Q 잠김", locked: true, uses: "");

        // ② 정오 — 태양흔 또렷 · 엇갈린 빛 켜짐 · Q 열림(수확이 거둔 태양흔은 다시 새긴다)
        zen.Paused = false; zen.OpenNoonNow(); zen.Paused = true;
        p.CooldownTracker?.ResetCooldown(SkillType.Q);
        await UniTask.Delay(300, ignoreTimeScale: true);
        SetMarks(mons, sun: true, cap);
        SetCrossLight(hub, true);
        await UniTask.Delay(700, ignoreTimeScale: true);
        await Shot("RHS_2_gawain_noon");
        _sb.AppendLine("② 정오");
        CheckPips(At(mons, 0), "정오 태양흔 상한 — 또렷", cap, cap, dim: false);
        CheckStar(hud, "엇갈린 빛 켜짐 = 금 별", on: true);
        CheckQ(hud, "정오 — Q 열림 · 숫자 없음", locked: false, uses: "");

        // ③ 황혼 사용권 2 — Q 「×2」
        SetCrossLight(hub, false);
        zen.Restore(new RelicResourceState { phase = (int)ZenithGauge.ZPhase.Cooldown, fill = zen.DurationOf(ZenithGauge.ZPhase.Cooldown) * 0.3f });
        typeof(GawainZenithRelic).GetField("_bonusCasts", Inst)?.SetValue(relic, 2);
        p.CooldownTracker?.ResetCooldown(SkillType.Q);
        await UniTask.Delay(500, ignoreTimeScale: true);
        await Shot("RHS_3_gawain_dusk_x2");
        _sb.AppendLine("③ 황혼 사용권 2");
        CheckQ(hud, "황혼 사용권 2 — Q 열림 · 「×2」", locked: false, uses: "×2");
        typeof(GawainZenithRelic).GetField("_bonusCasts", Inst)?.SetValue(relic, 1);
        await UniTask.Delay(200, ignoreTimeScale: true);
        CheckQ(hud, "황혼 사용권 1 — 숫자 없음", locked: false, uses: "");
    }

    // ── 랜슬롯 ──

    private static async UniTask LancelotAsync(PlayerController p, LancelotMadnessRelic relic, CombatPanelView hud, List<MonsterBase> mons, List<Action> restore)
    {
        _sb.AppendLine("■ 랜슬롯");
        var mad = relic.Madness;
        var hub = LancelotMemoryHub.Ensure(p);
        if (mad == null || hub == null || hub.Grudge == null) { Check("랜슬롯 광기 · 허브", false, $"광기 {mad != null} · 허브 {hub != null}"); return; }
        var madBefore = mad.Capture();
        int grudgeBefore = hub.Grudge.Value;
        restore.Add(() => { mad.Restore(madBefore); mad.HoldDecayUntil = 0f; hub.HoldingThreshold = false; });
        restore.Add(() => { hub.Grudge.TakeAll(); hub.Grudge.Add(grudgeBefore); });
        restore.Add(() => SetCrossLight(hub, false));
        int cap = LancelotMemoryHub.BrandCapOf(p);
        await UniTask.Delay(300, ignoreTimeScale: true);

        // ① 피 냄새 멈춤 4초 · 원한 3 · 낙인 상한 맥동 · 대기 별
        mad.Restore(new RelicResourceState { fill = 25f });   // 광란 아님 · 광기 25
        mad.HoldDecayUntil = Time.time + 4f;
        hub.Grudge.TakeAll(); hub.Grudge.Add(3);
        SetMarks(mons, sun: false, cap);
        View()?.SetCrossLight(true, false, false);   // 대기 별 — 반응 조각을 런에 넣지 않고 화면만 맞춘다
        await UniTask.Delay(700, ignoreTimeScale: true);
        await Shot("RHS_1_lancelot_hold");
        _sb.AppendLine("① 피 냄새 멈춤");
        CheckHold(hud, seconds: true);
        CheckGrudge(hud, $"3/{hub.Grudge.Cap}", rim: false);
        CheckPips(At(mons, 0), "낙인 상한", cap, cap, dim: false);
        CheckPips(At(mons, 1), "낙인 1칸", 1, cap, dim: false);
        CheckCrush(At(mons, 1));
        await CheckPulse(mons);
        CheckStar(hud, "엇갈린 빛 대기 = 빈 별", on: false);

        // ② 원한 가득 · 엇갈린 빛 켜짐 · 끝의 문턱(SetStacks는 광란을 열지 않는다)
        mad.HoldDecayUntil = 0f;
        mad.SetStacks(mad.MaxStacks);
        hub.HoldingThreshold = true;
        hub.Grudge.Add(hub.Grudge.Cap);
        SetCrossLight(hub, true);
        await UniTask.Delay(700, ignoreTimeScale: true);
        await Shot("RHS_2_lancelot_full");
        _sb.AppendLine("② 원한 가득 · 끝의 문턱");
        CheckGrudge(hud, $"{hub.Grudge.Cap}/{hub.Grudge.Cap}", rim: true);
        CheckHold(hud, seconds: false);
        CheckStar(hud, "엇갈린 빛 켜짐 = 금 별", on: true);
        await CheckTremble(hud);
        hub.HoldingThreshold = false;

        // ③ 광란 · 심판 2회 — Q 「×2」
        SetCrossLight(hub, false);
        mad.Restore(new RelicResourceState { fill = 0f, aux = 8f });   // 광란 8초 = 심판 1회
        relic.GrantJudgmentCharge();
        p.CooldownTracker?.ResetCooldown(SkillType.Q);
        await UniTask.Delay(500, ignoreTimeScale: true);
        await Shot("RHS_3_lancelot_frenzy_x2");
        _sb.AppendLine("③ 광란 심판 2회");
        CheckQ(hud, "심판 2회 — Q 열림 · 「×2」", locked: false, uses: "×2");
        typeof(MadnessStack).GetField("_frenzyEnd", Inst)?.SetValue(mad, 0f);   // 광란을 제 경로로 끝낸다(사건 · 오라 정리)
        await UniTask.Delay(200, ignoreTimeScale: true);
        CheckQ(hud, "광란 끝 — Q 잠김 · 숫자 없음", locked: true, uses: "");
    }

    // ── 형 게이지 ──

    private static async UniTask FormAsync(PlayerController p, CombatPanelView hud)
    {
        _sb.AppendLine("■ 형 게이지");
        var wm  = p.WeaponManager;
        var frt = WeaponFormRuntime.Of(p);
        if (wm == null || frt == null) { Check("형 구동기", false, $"무기 {wm != null} · 구동기 {frt != null}"); return; }
        if (wm.Weapon0Data == null || !wm.Weapon0Data.HasForm || wm.Weapon0Data.form.FormId != "windblade")
        {
            var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(Katana);
            await GameRunBootstrapper.EquipWeaponToPlayerAsync(so, p, PlayerWeaponManager.Slot0);
            _sb.AppendLine("  (근접 칸에 T1 카타나를 쥐여 줌 — 칼바람 5칸)");
        }
        if (wm.CurrentSlotIndex != PlayerWeaponManager.Slot0) await wm.SwitchToSlotAsync(PlayerWeaponManager.Slot0);
        float t0 = Time.realtimeSinceStartup;
        while ((frt.Current == null || frt.Current.Form.FormId != "windblade") && Time.realtimeSinceStartup - t0 < 5f) await UniTask.Yield();
        await UniTask.Delay(500, ignoreTimeScale: true);
        var form = frt.Current;
        var pill = Find(hud.transform, "FormGauge");
        var line = pill != null ? Find(pill, "Top")?.GetComponent<Image>() : null;
        if (form == null || pill == null || line == null) { Check("칼바람 형 게이지", false, $"형 {form?.Form.DisplayName} · 알약 {pill != null}"); return; }

        // ④ 「다음 타에 발동」 — 가득 + 사건 → 테가 한 번 밝아지고 밝게 남는다
        SetStacks(form, form.MaxStacks, WeaponFormBase.StackReason.Gain);
        await UniTask.Delay(300, ignoreTimeScale: true);
        float full = Lum(line.color);
        SetReadyNext(frt, true);
        await UniTask.Delay(70, ignoreTimeScale: true);
        float flash = Lum(line.color);
        await UniTask.Delay(600, ignoreTimeScale: true);
        float lit = Lum(line.color);
        await Shot("RHS_4_form_ready");
        Check("「다음 타에 발동」 — 테가 한 번 밝아졌다가 밝게 남음", flash > lit + 0.02f && lit > full + 0.05f, $"가득 {full:0.00} · 번쩍 {flash:0.00} · 남음 {lit:0.00}");
        SetReadyNext(frt, false);
        await UniTask.Delay(150, ignoreTimeScale: true);
        Check("풀리면 가득 테로 돌아감", Mathf.Abs(Lum(line.color) - full) < 0.03f, $"{Lum(line.color):0.00}");

        // ⑤ 시간 감소 — 회색으로 식고 붉지 않음(S9)
        bool red = false, grey = false, shot = false;
        SetStacks(form, form.MaxStacks - 1, WeaponFormBase.StackReason.Idle);
        t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 0.6f)
        {
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
            var c = PipColor(pill, form.MaxStacks - 1, "Fill");
            red  |= IsRed(c);
            grey |= Mathf.Abs(c.r - c.g) < 0.06f && Mathf.Abs(c.g - c.b) < 0.06f && c.r > 0.35f && c.a > 0.5f;
            if (!shot && Time.realtimeSinceStartup - t0 > 0.15f) { ScreenCapture.CaptureScreenshot("Temp/ui_shots/RHS_5_form_idle.png"); shot = true; }
        }
        Check("시간 감소 — 회색으로 식음 · 붉음 없음(S9)", grey && !red && Filled(pill, form) == form.MaxStacks - 1, $"회색 {grey} · 붉음 {red} · 찬 {Filled(pill, form)}");

        // ⑥ 돌려받음(폭풍의 눈) — 비운 뒤 남은 2칸에 금 테
        SetStacks(form, form.MaxStacks, WeaponFormBase.StackReason.Gain);
        await UniTask.Delay(300, ignoreTimeScale: true);
        SetStacks(form, 2, WeaponFormBase.StackReason.Refund);
        red = await WatchRed(pill, 0.6f);
        await Shot("RHS_6_form_refund");
        bool g0 = NearGold(PipColor(pill, 0, "Rim")), g1 = NearGold(PipColor(pill, 1, "Rim")), g2 = NearGold(PipColor(pill, 2, "Rim"));
        Check("돌려받음 — 붉음 없이 비우고 남은 2칸에 금 테", !red && g0 && g1 && !g2 && Filled(pill, form) == 2, $"붉음 {red} · 금 테 {g0}/{g1}/{g2} · 찬 {Filled(pill, form)}");

        // 맞음 — 붉게 비친 뒤 꺼짐 · 금 테도 지움(기록)
        SetStacks(form, 3, WeaponFormBase.StackReason.Gain);
        await UniTask.Delay(300, ignoreTimeScale: true);
        SetStacks(form, 0, WeaponFormBase.StackReason.Damaged);
        red = await WatchRed(pill, 0.6f);
        Check("맞음 — 붉게 비친 뒤 꺼짐 · 금 테 지움", red && Filled(pill, form) == 0 && !NearGold(PipColor(pill, 0, "Rim")), $"붉음 {red} · 찬 {Filled(pill, form)}");
    }

    // ── 검사 ──

    private static void CheckPips(MonsterBase mb, string name, int filled, int cap, bool dim)
    {
        var marks = ActiveChild(mb != null && mb.WorldHPBar != null ? mb.WorldHPBar.transform : null, "Marks");
        if (marks == null) { Check(name + " ▮ 줄", false, "줄 없음"); return; }
        int shown = 0, on = 0;
        float a = -1f;
        for (int i = 0; i < marks.childCount; i++)
        {
            var c = marks.GetChild(i);
            if (!c.gameObject.activeSelf || !c.name.StartsWith("Pip")) continue;
            shown++;
            var img = c.GetComponent<Image>();
            if (img != null && img.color.a > 0.3f) { on++; a = img.color.a; }
        }
        bool dimOk = dim ? a > 0f && a < 0.6f : a > 0.9f;
        Check($"{name} — {filled}/{cap} · {(dim ? "흐림" : "또렷")}", shown == cap && on == filled && dimOk, $"칸 {shown} · 찬 {on} · 알파 {a:0.00}");
    }

    private static void CheckCount(MonsterBase mb, string want)
    {
        var t = ActiveChild(mb != null && mb.WorldHPBar != null ? mb.WorldHPBar.transform : null, "MarkCount")?.GetComponent<TMP_Text>();
        string s = t != null ? t.GetParsedText() : "";
        Check($"흑점 — 「{want}」(상한 없음 · 숫자)", s == want, $"「{s}」");
    }

    /// <summary>짓누름 — 표시 경로(받피증 슬롯 → MakeItem → IconKeyForStatus = vulnerable)로 상태 칸에 취약 그림이 뜬다.</summary>
    private static void CheckCrush(MonsterBase mb)
    {
        var bar = mb != null ? mb.WorldHPBar : null;
        var want = EffectIconRegistry.GetSprite("vulnerable");
        bool found = false;
        if (bar != null)
            foreach (var img in bar.GetComponentsInChildren<Image>(false))
                if (img.name == "Icon" && img.sprite == want) found = true;
        Check("짓누름(form_crush) — 취약 그림 · 이름 「짓누름」", found && EffectDescriptionFormatter.LabelForStatus("form_crush") == "짓누름",
              $"그림 {found} · 아트 세트 {EffectIconRegistry.HasIconSet}");
    }

    /// <summary>상한 찬 줄만 숨쉰다(배율 ±8%) — 1칸 줄은 가만히.</summary>
    private static async UniTask CheckPulse(List<MonsterBase> mons)
    {
        var a = ActiveChild(At(mons, 0) != null && At(mons, 0).WorldHPBar != null ? At(mons, 0).WorldHPBar.transform : null, "Marks");
        var b = ActiveChild(At(mons, 1) != null && At(mons, 1).WorldHPBar != null ? At(mons, 1).WorldHPBar.transform : null, "Marks");
        float aMin = 9f, aMax = 0f, bMin = 9f, bMax = 0f;
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 1.2f)
        {
            await UniTask.Yield();
            if (a != null) { aMin = Mathf.Min(aMin, a.localScale.x); aMax = Mathf.Max(aMax, a.localScale.x); }
            if (b != null) { bMin = Mathf.Min(bMin, b.localScale.x); bMax = Mathf.Max(bMax, b.localScale.x); }
        }
        Check("상한 찬 줄만 은은히 맥동(배율 ≤ 1.08)", a != null && aMax - aMin > 0.04f && aMax < 1.1f && (b == null || bMax - bMin < 0.001f),
              $"상한 {aMin:0.000}~{aMax:0.000} · 1칸 {bMin:0.000}~{bMax:0.000}");
    }

    private static void CheckStar(CombatPanelView hud, string name, bool on)
    {
        var star = Find(hud.transform, "CrossLight")?.GetComponent<Image>();
        string tex = star != null && star.sprite != null ? star.sprite.texture.name : "없음";
        bool ok = star != null && star.gameObject.activeInHierarchy && tex == (on ? "RelicHudStar" : "RelicHudStarLine");
        Check(name, ok, $"{tex} · 켜짐 {star?.gameObject.activeInHierarchy} · {(star != null ? Col(star.color) : "")}");
    }

    private static void CheckQ(CombatPanelView hud, string name, bool locked, string uses)
    {
        var skillQ = typeof(CombatPanelView).GetField("skillQ", Inst)?.GetValue(hud) as CombatPanelView.SkillSlotUI;
        var label  = Find(hud.transform, "QUses")?.GetComponent<TMP_Text>();
        string u = label != null ? label.GetParsedText() : "";
        Check(name, skillQ != null && skillQ.IsLocked == locked && u == uses, $"잠김 {skillQ?.IsLocked} · 「{u}」");
    }

    private static void CheckHold(CombatPanelView hud, bool seconds)
    {
        var tick = Find(hud.transform, "HoldTick");
        var sec  = Find(hud.transform, "HoldSeconds")?.GetComponent<TMP_Text>();
        string s = sec != null ? sec.GetParsedText() : "";
        bool textOk = seconds ? s.StartsWith("멈춤 ") && s.EndsWith("초") : s == "멈춤";
        Check(seconds ? "피 냄새 — 멈춤 선 · 「멈춤 N초」" : "끝의 문턱 — 멈춤 선 · 「멈춤」", tick != null && tick.gameObject.activeInHierarchy && textOk,
              $"선 {tick?.gameObject.activeInHierarchy} · 「{s}」");
    }

    private static void CheckGrudge(CombatPanelView hud, string want, bool rim)
    {
        var label = Find(hud.transform, "GrudgeCount")?.GetComponent<TMP_Text>();
        var box   = Find(hud.transform, "GrudgeRim");
        string s = label != null ? label.GetParsedText() : "";
        bool gold = label != null && label.color.b < 0.5f;
        Check($"원한 「{want}」{(rim ? " · 금 테 · 금 숫자" : " · 테 없음")}", s == want && box != null && box.gameObject.activeSelf == rim && gold == rim,
              $"「{s}」 · 테 {box?.gameObject.activeSelf} · 숫자 {(label != null ? Col(label.color) : "")}");
    }

    /// <summary>끝의 문턱 — 광기 막대 자리가 떤다(±1.5 px).</summary>
    private static async UniTask CheckTremble(CombatPanelView hud)
    {
        var bar = Find(hud.transform, "RelicIdentityBar") as RectTransform;
        if (bar == null) { Check("끝의 문턱 — 막대 떨림", false, "막대 없음"); return; }
        Vector2 min = new(9999f, 9999f), max = new(-9999f, -9999f);
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 0.3f)
        {
            await UniTask.Yield();
            min = Vector2.Min(min, bar.anchoredPosition);
            max = Vector2.Max(max, bar.anchoredPosition);
        }
        float span = Mathf.Max(max.x - min.x, max.y - min.y);
        Check("끝의 문턱 — 막대 떨림(≤ 3 px)", span > 0.3f && span <= 3.01f, $"흔들림 폭 {span:0.00}px");
    }

    private static async UniTask<bool> WatchRed(Transform pill, float seconds)
    {
        bool red = false;
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
            for (int i = 0; i < pill.childCount; i++)
            {
                var f = pill.GetChild(i).Find("Fill")?.GetComponent<Image>();
                if (f != null && IsRed(f.color)) red = true;
            }
        }
        return red;
    }

    // ── 상태 맞추기 ──

    /// <summary>엇갈린 빛을 켜고 끈다 — 허브 칸 + 사건(HUD가 듣는 경로 그대로).</summary>
    private static void SetCrossLight(MonoBehaviour hub, bool on)
    {
        if (hub == null) return;
        var t = hub.GetType();
        t.GetField("_crossLight", Inst)?.SetValue(hub, on);
        (t.GetField("CrossLightChanged", Inst)?.GetValue(hub) as Action<bool>)?.Invoke(on);
    }

    /// <summary>쌓인 수를 까닭과 함께 넣는다 — 형이 스스로 줄이지 않게 마지막 적중 · 휘두름 시각을 미래로 둔다.</summary>
    private static void SetStacks(WeaponFormBase form, int n, WeaponFormBase.StackReason why)
    {
        foreach (var f in new[] { "_lastHitAt", "_lastSwingAt" })
            form.GetType().GetField(f, Inst)?.SetValue(form, Time.time + 1000f);
        typeof(WeaponFormBase).GetMethod("SetStacks", Inst)?.Invoke(form, new object[] { n, why });
    }

    /// <summary>「다음 타에 발동」 — 구동기 칸 + 오르는 사건(휘두름 없이).</summary>
    private static void SetReadyNext(WeaponFormRuntime frt, bool on)
    {
        typeof(WeaponFormRuntime).GetField("_readyNext", Inst)?.SetValue(frt, on);
        if (on) (typeof(WeaponFormRuntime).GetField("ReadyNext", Inst)?.GetValue(frt) as Action)?.Invoke();
    }

    // ── 도우미 ──

    private static RelicMemoryHudView View()
    {
        var presenter = UnityEngine.Object.FindFirstObjectByType<RelicMemoryHudPresenter>();
        return presenter != null ? presenter.GetComponent<RelicMemoryHudView>() : null;
    }

    private static MonsterBase At(List<MonsterBase> list, int i) => i < list.Count ? list[i] : null;

    private static Color PipColor(Transform pill, int index, string part)
    {
        var img = pill != null ? pill.Find("Pip" + index)?.Find(part)?.GetComponent<Image>() : null;
        return img != null ? img.color : Color.clear;
    }

    private static int Filled(Transform pill, WeaponFormBase form)
    {
        int n = 0;
        for (int i = 0; i < form.MaxStacks; i++) if (Near(PipColor(pill, i, "Fill"), form.Form.Tint)) n++;
        return n;
    }

    private static bool IsRed(Color c) => c.r > 0.85f && c.g < 0.45f && c.b < 0.45f;
    private static bool NearGold(Color c) => Near(c, UITheme.Thin.Gold);
    private static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.06f && Mathf.Abs(a.g - b.g) < 0.06f && Mathf.Abs(a.b - b.b) < 0.06f;
    private static float Lum(Color c) => (c.r + c.g + c.b) * c.a;
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

    /// <summary>켜진 것 중 첫 번째(상태 칸은 칸마다 같은 이름을 쓴다).</summary>
    private static Transform ActiveChild(Transform t, string name)
    {
        if (t == null || !t.gameObject.activeInHierarchy) return null;
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++) { var hit = ActiveChild(t.GetChild(i), name); if (hit != null) return hit; }
        return null;
    }

    private static void HubGui(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);

    private static async UniTask Shot(string name)
    {
        ScreenCapture.CaptureScreenshot($"Temp/ui_shots/{name}.png");
        await UniTask.Yield();
        await UniTask.Yield();
    }
}
#endif
