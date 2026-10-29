#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-22 E1 유물 진화 · 규칙 단순화 반입 실측(설계 기획/RelicFairy_유물진화_단순화_권장안_20261022.md · D1~D10).
/// 테스트 허브 Ch1 런에서(없으면 연다) — 시기는 StoryProgress 테스트 오버라이드 캐시만 잠깐 바꾸고(에디터 설정 · 세이브는 그대로) 끝나면 되돌린다.
/// <list type="number">
/// <item>진화 — 해방기 · 조각 2개 + 보스면 열림 · 봉인기엔 안 열림 · 각인 0이어도 열림(문턱 없음) · 드래프트 3번(Ch1 · Ch2에서 새 조각) → Ch3 창에 진화.</item>
/// <item>랜슬롯 공명 = 광기 칸 조각 수(같은 칸 둘이어도 2 · 빈틈 상관없음 · 메아리 +1).</item>
/// <item>HUD 역할 글 — 가웨인 「여명 — 쳐서 쌓기」 · 「정오! — 터짐 · Q」 · 「황혼 — Q 잠김」 / 랜슬롯 「광기 n/40」 · 「광란 … / Q 심판」(임시 게이지 + 지금 유물의 실제 HUD · 무기 · 물약 칸과 안 겹침).</item>
/// <item>처음 겪는 순간 한 줄 — 유물마다 한 번(기록 키가 유물별) · 실제 HUD 사건으로 불린다.</item>
/// <item>첫 설명 팝업 기록이 유물마다 · 봉인기 3줄(진화 없음) · 해방기 4줄 · 창 머리 진행 줄 · 새 조각 카드 ◇ 숨김(「선명해지면 +n줄」) · 진화 카드에 계열 낱말 0.</item>
/// </list>
/// 기록은 <see cref="RelicFirstTips.ProbeRecords"/>(임시 집합)에만 — 세이브를 쓰지 않는다. 로드아웃 조각은 건드리지 않는다(진화 시뮬은 빈 로드아웃).
/// 결과 Temp/fb1022_evo.txt · Temp/ui_shots/EVO22_*.png · 로그 「[Fb1022Evo] 끝」.
/// </summary>
public static class Fb1022EvoProbeEditor
{
    private const string Tag     = "[Fb1022Evo]";
    private const string OutTxt  = "Temp/fb1022_evo.txt";
    private const string ShotDir = "Temp/ui_shots";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static int s_pass, s_all;

    [MenuItem("RelicFairy/Debug/10-22 유물 진화 단순화 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중(테스트 허브)에만 동작한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-22 E1 유물 진화 단순화 실측\n");
        s_pass = s_all = 0;
        Directory.CreateDirectory(ShotDir);
        var eraField = typeof(StoryProgress).GetField("s_debugOverride", Any);
        object eraBefore = eraField?.GetValue(null);
        GameRunSession run = null;
        try
        {
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher != null && GameRunBootstrapper.Instance?.Run?.Player == null)
            {
                launcher.SelectChapter(1);
                typeof(TestHubLauncher).GetField("_bossApproach", Any)?.SetValue(launcher, false);
                launcher.TryLaunch();
            }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 60f && GameRunBootstrapper.Instance?.Run?.Player == null)
                await UniTask.Delay(500, DelayType.Realtime);
            run = GameRunBootstrapper.Instance?.Run;
            var lo = AppBootstrapper.Instance?.Loadout;
            if (run == null || run.Player == null || lo == null || Managers.RelicParts == null) { sb.AppendLine("런이 시작되지 않음"); return; }
            await Wait(3f);
            run.Player.SetInvincible(120f);
            RelicFirstTips.ProbeRecords = new HashSet<string>();
            sb.AppendLine($"유물 {(lo.Relic != null ? lo.Relic.DisplayName : "-")} · 시기(실측 전) {StoryProgress.Era} · 가진 조각 {lo.RelicPartIds.Count}");

            EvolutionRules(sb, eraField, run);
            LancelotCount(sb);
            await HudRoleTextAsync(sb, eraField, run);
            await FirstTipsAsync(sb, eraField, run);
            await DraftPopupAsync(sb, eraField);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            eraField?.SetValue(null, eraBefore);
            RelicFirstTips.ProbeRecords = null;
            if (run != null) BuildImprint.Recount(run);
            Time.timeScale = 1f;
            sb.AppendLine($"\n통과 {s_pass}/{s_all}");
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝 — 통과 {s_pass}/{s_all}\n{sb}");
        }
    }

    // ── ① 진화 조건 · 시기 ─────────────────────────────────

    private static void EvolutionRules(StringBuilder sb, FieldInfo eraField, GameRunSession run)
    {
        sb.AppendLine("\n① 진화 — 해방기부터 보스마다(10-29 조각 조건 걷음) · 각인 문턱 없음");
        foreach (var relicId in new[] { "gawain", "lancelot" })
        {
            var all = new List<RelicPartEntry>(Managers.RelicParts.GetByRelic(relicId));
            var two = new PlayerLoadout();
            foreach (var e in all)
            {
                if (two.RelicPartIds.Count >= 2) break;
                if (e != null && !e.IsReaction) two.AddRelicPart(e.part_id, RelicMemoryGrade.Faint);
            }
            var one = new PlayerLoadout();
            one.AddRelicPart(two.RelicPartIds[0], RelicMemoryGrade.Faint);

            SetEra(eraField, 0);
            Check(sb, $"  [{relicId}] 봉인기 — 조각 2개여도 진화 없음 · 진행 줄 빈 글 · 안내에 「진화」 없음",
                  !RelicEvolution.Unlocked && RelicEvolution.Ready(two, relicId) == null && RelicEvolution.ProgressLine(two) == string.Empty
                  && !RelicPartPlainText.Guide(relicId, relicId, two).Contains("진화"));
            Check(sb, $"  [{relicId}] 봉인기 드래프트 3번 → 진화 없음", SimulateThreeBosses(sb, relicId, all, 0) == 0);

            SetEra(eraField, 1);
            BuildImprint.Clear();   // 각인 0 — 문턱이 남아 있으면 안 열린다
            var ready = RelicEvolution.Ready(two, relicId);
            Check(sb, $"  [{relicId}] 해방기 · 조각 2 · 각인 0(연격 {BuildImprint.Count(BuildFamily.Combo)} · 필살 {BuildImprint.Count(BuildFamily.Crit)} · 사격 {BuildImprint.Count(BuildFamily.Ranged)}) → 진화 {ready?.Id}(방향 {RelicEvolution.Direction()} = 「{RelicEvolution.DirectionWord(RelicEvolution.Direction())}」)",
                  ready != null && ready.RelicId == relicId);
            Check(sb, $"  [{relicId}] 해방기 · 조각 1 → 진화 열림 · 진행 줄 「{RelicEvolution.ProgressLine(one)}」",
                  RelicEvolution.Ready(one, relicId) != null && RelicEvolution.ProgressLine(one).Contains("보스마다"));
            Check(sb, $"  [{relicId}] 해방기 · 조각 0(Ch1 보스) → 진화 열림", RelicEvolution.Ready(new PlayerLoadout(), relicId) != null);
            Check(sb, $"  [{relicId}] 해방기 드래프트 3번(새 조각만 고름) → Ch1 · Ch2 · Ch3 창 모두 진화", SimulateThreeBosses(sb, relicId, all, 1) == 3);
            string guide = RelicPartPlainText.Guide(relicId, relicId, one);
            Check(sb, $"  [{relicId}] 해방기 안내 넷째 줄 「보스마다 진화」 · 「두 번째 바퀴」 · 「각인」 없음",
                  guide.Contains("보스마다 진화") && !guide.Contains("두 번째 바퀴") && !guide.Contains("각인"));
            if (run != null) BuildImprint.Recount(run);
        }
    }

    /// <summary>빈 로드아웃으로 보스 창 셋을 굴린다 — 새 조각만 고른다(진화는 안 고름). 진화 카드가 나온 창 수.</summary>
    private static int SimulateThreeBosses(StringBuilder sb, string relicId, List<RelicPartEntry> all, int era)
    {
        var l = new PlayerLoadout();
        var line = new StringBuilder();
        int evoWindows = 0;
        for (int boss = 1; boss <= 3; boss++)
        {
            var draft = RelicDraftComposer.Compose(relicId, all, l, era, 0, _ => false, new System.Random(1000 + boss));
            bool hasEvo = draft.Cards.Exists(c => c.Kind == RelicDraftCardKind.Evolution);
            line.Append($" Ch{boss}(조각 {l.RelicPartIds.Count}): ");
            foreach (var c in draft.Cards) line.Append(c.Kind).Append(' ');
            if (hasEvo) evoWindows++;
            var pick = draft.Cards.Find(c => c.Kind == RelicDraftCardKind.NewFragment);
            if (pick != null) l.AddRelicPart(pick.PartId, pick.Grade);
        }
        sb.AppendLine($"    시기 {era}:{line}");
        return evoWindows;
    }

    // ── ② 랜슬롯 광기 칸 조각 수 ────────────────────────────

    private static void LancelotCount(StringBuilder sb)
    {
        sb.AppendLine("\n② 랜슬롯 공명 = 광기 칸 조각 수(10부터 이어질 필요 없음 · 효과 수치 그대로)");
        var l = new PlayerLoadout();
        l.AddRelicPart("l_split_oath"); l.AddRelicPart("l_blood_scent");   // 둘 다 광기 10
        int c2 = RelicResonance.LancelotCount(l);
        Check(sb, $"  광기 10 조각 둘 → 칸 조각 {c2} · 단계 {RelicResonance.LancelotTier(c2)}(옛 규칙이면 이어진 계단 1 · 단계 0)", c2 == 2 && RelicResonance.LancelotTier(c2) == 2);
        l.AddRelicPart("l_madness_eye");   // 광기 30 — 20이 비어 있어도 센다
        int c3 = RelicResonance.LancelotCount(l);
        string prev = RelicResonance.PreviewLine(l, "lancelot", "r40");
        Check(sb, $"  + 광기 30 → {c3} · 광기 40 미리 보기 「{prev}」", c3 == 3 && prev != null && prev.Contains("3 → 4"));
        l.AddRelicEcho("r40");
        int c4 = RelicResonance.LancelotCount(l);
        Check(sb, $"  + 메아리(광기 40) → {c4} · 단계 {RelicResonance.LancelotTier(c4)} · 「{RelicResonance.LadderText(2)}」", c4 == 4 && RelicResonance.LancelotTier(c4) == 4);
    }

    // ── ③ HUD 역할 글 ──────────────────────────────────────

    private static async UniTask HudRoleTextAsync(StringBuilder sb, FieldInfo eraField, GameRunSession run)
    {
        sb.AppendLine("\n③ HUD 역할 글");
        var go = new GameObject("EVO22_TempGauges");
        try
        {
            var zg = go.AddComponent<ZenithGauge>();
            zg.Initialize();
            string dawn = zg.Label;
            zg.OpenNoonNow();
            string noon = zg.Label;
            Advance(zg);
            string dusk = zg.Label;
            zg.AddChargeAccel(0.4f);
            string duskAccel = zg.Label;
            sb.AppendLine($"  가웨인 「{dawn}」 · 「{noon}」 · 「{dusk}」 · 「{duskAccel}」");
            Check(sb, "  가웨인 시간대 역할 글", dawn == "여명 — 쳐서 쌓기" && noon == "정오! — 터짐 · Q" && dusk == "황혼 — Q 잠김" && duskAccel.Contains("처치로 앞당김 ×1.4"));

            var ms = go.AddComponent<MadnessStack>();
            ms.Initialize();
            ms.SetStacks(23);
            string mad = ms.Label;
            ms.EnterFrenzy(3.2f);
            string frenzy = ms.Label;
            sb.AppendLine($"  랜슬롯 「{mad}」 · 「{frenzy.Replace("\n", " / ")}」");
            Check(sb, $"  랜슬롯 「광기 23/{ms.MaxStacks}」 · 광란 「… 초 / Q 심판」(% 없음)",
                  mad == $"광기 23/{ms.MaxStacks}" && frenzy.StartsWith("광란 ") && frenzy.Contains("Q 심판") && !mad.Contains("%"));
        }
        finally { Object.Destroy(go); }

        // 지금 유물의 실제 HUD
        var p     = run.Player;
        var panel = Object.FindFirstObjectByType<CombatPanelView>(FindObjectsInactive.Include);
        if (panel == null || !panel.gameObject.activeInHierarchy) { sb.AppendLine("  (전투 HUD 꺼짐(테스트 허브) — 실제 HUD 확인 건너뜀 · 런 안 사진으로 본다)"); return; }
        if (p.RelicBehavior is GawainZenithRelic g && g.Gauge != null)
        {
            var label = GetField(panel, "_sunLabel") as TMP_Text;
            var gauge = g.Gauge;
            for (int i = 0; i < 3 && gauge.CurrentPhase != ZenithGauge.ZPhase.Charging; i++) Advance(gauge);
            gauge.OpenNoonNow();
            await Wait(0.6f);
            await ShotAsync("EVO22_hud_gawain_noon");
            string noonTxt = Parsed(label);
            Advance(gauge);
            gauge.Restore(gauge.Capture());   // 라벨은 OnChanged로 갈린다
            await Wait(0.4f);
            await ShotAsync("EVO22_hud_gawain_dusk");
            string duskTxt = Parsed(label);
            Check(sb, $"  실제 HUD(가웨인) 「{noonTxt}」 · 「{duskTxt}」", noonTxt.Contains("정오! — 터짐 · Q") && duskTxt.Contains("황혼 — Q 잠김"));
            OverlapAudit(sb, panel, label, "가웨인 시간대 글");
        }
        else if (p.RelicBehavior is LancelotMadnessRelic lr && lr.Madness != null)
        {
            var label = GetField(panel, "_relicBarLabel") as TMP_Text;
            var m = lr.Madness;
            m.SetStacks(23);
            await Wait(0.4f);
            await ShotAsync("EVO22_hud_lancelot_madness");
            string madTxt = Parsed(label);
            m.EnterFrenzy(2f);
            await Wait(0.4f);
            await ShotAsync("EVO22_hud_lancelot_frenzy");
            string frTxt = Parsed(label);
            Check(sb, $"  실제 HUD(랜슬롯) 「{madTxt}」 · 「{frTxt.Replace("\n", " / ")}」", madTxt.Contains($"광기 23/{m.MaxStacks}") && frTxt.Contains("Q 심판"));
            OverlapAudit(sb, panel, label, "랜슬롯 광란 글");
            await Wait(2f);   // 광란이 저절로 끝난다
        }
        else sb.AppendLine("  (유물이 가웨인 · 랜슬롯이 아님 — 실제 HUD 확인 건너뜀)");
    }

    /// <summary>글자 실제 칸(textBounds)이 무기 원 · 물약 · E · R · Q 칸과 겹치지 않는가 — 지금 해상도 하나(16:9 · 16:10은 게임 창을 바꿔 다시 돌린다).</summary>
    private static void OverlapAudit(StringBuilder sb, CombatPanelView panel, TMP_Text label, string what)
    {
        if (label == null) { Check(sb, $"  {what} 글 찾음", false); return; }
        var textRect = TextWorldRect(label);
        var others = new List<(string, RectTransform)>();
        if (GetField(panel, "_weaponSlotRoot") is RectTransform[] weapons)
            for (int i = 0; i < weapons.Length; i++) if (weapons[i] != null) others.Add(($"무기 {i}", weapons[i]));
        foreach (var name in new[] { "HUD_Active_01", "HUD_ESkile", "HUD_RSkile", "HUD_QSkile" })
        {
            var rt = FindRect(panel.transform, name);
            if (rt != null) others.Add((name, rt));
        }
        var hits = new List<string>();
        foreach (var (name, rt) in others)
            if (rt.gameObject.activeInHierarchy && WorldRect(rt).Overlaps(textRect)) hits.Add(name);
        Check(sb, $"  {what} 겹침 없음({Screen.width}×{Screen.height} · 글 칸 {textRect.xMin:0}~{textRect.xMax:0} × {textRect.yMin:0}~{textRect.yMax:0}){(hits.Count > 0 ? " — 겹침: " + string.Join(", ", hits) : "")}",
              hits.Count == 0);
    }

    // ── ④ 처음 겪는 순간 한 줄 ──────────────────────────────

    private static async UniTask FirstTipsAsync(StringBuilder sb, FieldInfo eraField, GameRunSession run)
    {
        sb.AppendLine("\n④ 처음 겪는 순간 한 줄 — 유물마다 한 번(세이브 대신 임시 기록)");
        var rec = RelicFirstTips.ProbeRecords;
        rec.Clear();
        bool g1 = RelicFirstTips.TryShowOnce(RelicFirstTips.GawainNoon), g2 = RelicFirstTips.TryShowOnce(RelicFirstTips.GawainNoon);
        bool l1 = RelicFirstTips.TryShowOnce(RelicFirstTips.LancelotFrenzy), l2 = RelicFirstTips.TryShowOnce(RelicFirstTips.LancelotFrenzy);
        Check(sb, $"  같은 순간 두 번째는 없음 · 유물마다 따로(가웨인 {g1}/{g2} · 랜슬롯 {l1}/{l2}) · 기록 {string.Join(", ", rec)}",
              g1 && !g2 && l1 && !l2 && rec.Contains(RelicFirstTips.RecordPrefix + RelicFirstTips.GawainNoon) && rec.Contains(RelicFirstTips.RecordPrefix + RelicFirstTips.LancelotFrenzy));
        foreach (var tip in new[] { RelicFirstTips.GawainDawn, RelicFirstTips.GawainNoon, RelicFirstTips.GawainDusk,
                                    RelicFirstTips.LancelotMadness, RelicFirstTips.LancelotFrenzy, RelicFirstTips.LancelotFrenzyEnd, RelicFirstTips.EvolutionOpen })
            sb.AppendLine($"    {RelicFirstTips.SequenceOf(tip)} — 「{RelicFirstTips.TextOf(tip)}」{(Managers.DialogueData != null && Managers.DialogueData.HasSequence(RelicFirstTips.SequenceOf(tip)) ? "" : " (대사 표에 없음 → 코드 폴백)")}");
        UI_BossBarkDismiss();
        await Wait(0.3f);

        // 실제 HUD 사건으로 불리는가(유물 HUD Presenter가 게이지 · 광기를 듣는다)
        rec.Clear();
        SetEra(eraField, 1);
        var p = run.Player;
        var panel = Object.FindFirstObjectByType<CombatPanelView>(FindObjectsInactive.Include);
        var presenter = panel != null ? panel.GetComponent<RelicMemoryHudPresenter>() : null;
        if (presenter == null) { sb.AppendLine("  (유물 HUD Presenter 없음 — 실제 사건 확인 건너뜀)"); return; }
        foreach (var f in new[] { "_tipFightDone", "_tipNoonDone", "_tipDuskDone", "_tipFrenzyDone", "_tipFrenzyEndDone" })
            typeof(RelicMemoryHudPresenter).GetField(f, Any)?.SetValue(presenter, false);
        if (p.RelicBehavior is GawainZenithRelic g && g.Gauge != null)
        {
            var gauge = g.Gauge;
            for (int i = 0; i < 3 && gauge.CurrentPhase != ZenithGauge.ZPhase.Charging; i++) Advance(gauge);
            gauge.OpenNoonNow();
            Advance(gauge);
            await Wait(0.3f);
            Check(sb, $"  실제 사건(가웨인) — 정오 · 황혼 한 줄 · 기록 {string.Join(", ", rec)}",
                  rec.Contains(RelicFirstTips.RecordPrefix + RelicFirstTips.GawainNoon) && rec.Contains(RelicFirstTips.RecordPrefix + RelicFirstTips.GawainDusk));
        }
        else if (p.RelicBehavior is LancelotMadnessRelic lr && lr.Madness != null)
        {
            lr.Madness.SetStacks(5);
            lr.Madness.EnterFrenzy(0.5f);
            await Wait(1.2f);
            Check(sb, $"  실제 사건(랜슬롯) — 첫 광기 · 광란 · 광란 끝 · 해방기 진화 한 줄 · 기록 {string.Join(", ", rec)}",
                  rec.Contains(RelicFirstTips.RecordPrefix + RelicFirstTips.LancelotMadness) && rec.Contains(RelicFirstTips.RecordPrefix + RelicFirstTips.LancelotFrenzy)
                  && rec.Contains(RelicFirstTips.RecordPrefix + RelicFirstTips.LancelotFrenzyEnd) && rec.Contains(RelicFirstTips.RecordPrefix + RelicFirstTips.EvolutionOpen));
        }
        await ShotAsync("EVO22_first_tip");
        UI_BossBarkDismiss();
    }

    // ── ⑤ 기억 창 — 첫 안내 · 진행 줄 · ◇ · 진화 카드 ────────

    private static async UniTask DraftPopupAsync(StringBuilder sb, FieldInfo eraField)
    {
        sb.AppendLine("\n⑤ 「되살아난 기억」 창");
        var rec = RelicFirstTips.ProbeRecords;
        rec.Clear();
        SetEra(eraField, 1);
        var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RelicPartDraftPopup>();
        if (popup == null) { Check(sb, "  창 열림", false); return; }
        try
        {
            var fresh = Card("g_dawn_oath", RelicMemoryGrade.Faint, RelicDraftCardKind.NewFragment);
            var evo   = new RelicDraftCard { Kind = RelicDraftCardKind.Evolution, EvolutionId = "evo_gawain_combo", Grade = RelicMemoryGrade.Radiant };
            Setup(popup, "gawain", "가웨인", fresh, evo);
            await Wait(1.2f);
            var guide = GetField(popup, "_guideRoot") as GameObject;
            string guideTxt = Parsed(Find(guide != null ? guide.transform : null, "GuideBody"));
            await ShotAsync("EVO22_1_guide_gawain_liberated");
            Check(sb, $"  [해방기] 가웨인 첫 안내 뜸 · 넷째 줄 「지금 n/2」 · 「두 번째 바퀴」 없음\n{Indent(guideTxt)}",
                  guide != null && guideTxt.Contains("/2") && guideTxt.Contains("진화") && !guideTxt.Contains("두 번째 바퀴"));
            Invoke(popup, "CloseGuide");
            await Wait(0.4f);
            await ShotAsync("EVO22_2_draft_gawain_liberated");
            string sub = Parsed(GetField(popup, "_subtitle") as TMP_Text);
            Check(sb, $"  [해방기] 창 머리 진행 줄 「{sub}」", sub.Contains("진화까지 조각") || sub.Contains("진화 조건 충족") || sub.Contains("유물 진화 완료"));
            var freshRt = CardRect(popup, 0);
            string rest = Parsed(Find(freshRt, "Rest"));
            Check(sb, $"  새 조각 카드(흐릿) ◇ 숨김 · 「선명해지면 +n줄」 — 「{rest.Replace("\n", " / ")}」", !rest.Contains("◇") && rest.Contains("선명해지면 +"));
            var evoRt = CardRect(popup, 1);
            string evoAll = AllText(evoRt);
            Check(sb, $"  진화 카드 — 계열 낱말 · 「각인」 없음 · 꼬리표 「{Parsed(Find(evoRt, "AnchorTag"))}」 · 조건 「{Parsed(Find(FindRect(evoRt, "EvoCheckBoss"), "Label"))}」",
                  !evoAll.Contains("각인") && !evoAll.Contains("연격") && !evoAll.Contains("필살") && !evoAll.Contains("사격")
                  && Find(evoRt, "FamilyTag") == null && FindRect(evoRt, "EvoCheckImprint") == null
                  && Parsed(Find(evoRt, "AnchorTag")).Contains(RelicEvolution.DirectionWord(BuildFamily.Combo)));

            // 같은 유물은 두 번째에 안 뜬다 · 다른 유물은 뜬다(기록이 유물마다)
            Setup(popup, "gawain", "가웨인", fresh, evo);
            await Wait(0.3f);
            bool gawainAgain = GetField(popup, "_guideRoot") as GameObject != null;
            var lance = Card("l_black_afterimage", RelicMemoryGrade.Faint, RelicDraftCardKind.NewFragment);
            var sharp = Card("l_split_oath", RelicMemoryGrade.Clear, RelicDraftCardKind.Sharpen);
            Setup(popup, "lancelot", "랜슬롯", lance, sharp);
            await Wait(0.8f);
            var lGuide = GetField(popup, "_guideRoot") as GameObject;
            string lGuideTxt = Parsed(Find(lGuide != null ? lGuide.transform : null, "GuideBody"));
            await ShotAsync("EVO22_3_guide_lancelot");
            Check(sb, $"  첫 안내 기록이 유물마다 — 가웨인 두 번째 {(gawainAgain ? "뜸" : "안 뜸")} · 랜슬롯 처음 {(lGuide != null ? "뜸" : "안 뜸")} · 기록 {string.Join(", ", rec)}",
                  !gawainAgain && lGuide != null && rec.Contains(RelicFirstTips.GuidePrefix + "gawain") && rec.Contains(RelicFirstTips.GuidePrefix + "lancelot"));
            Check(sb, "  랜슬롯 안내 셋째 줄 = 광기 칸 조각 수(「10부터」 이음 조건 없음)", lGuideTxt.Contains("광기 칸 조각") && !lGuideTxt.Contains("빠짐없이"));
            Invoke(popup, "CloseGuide");
            await Wait(0.4f);
            await ShotAsync("EVO22_4_draft_lancelot");
            string sharpRest = Parsed(Find(CardRect(popup, 1), "Rest"));
            Check(sb, $"  「더 선명하게」 카드는 잠긴 줄 ◇를 그대로 보인다 — 「{sharpRest.Replace("\n", " / ")}」", sharpRest.Contains("◇"));

            // 봉인기 — 진행 줄 · 안내에 진화 없음
            SetEra(eraField, 0);
            rec.Clear();
            Setup(popup, "gawain", "가웨인", fresh, Card("g_morning_hunt", RelicMemoryGrade.Clear, RelicDraftCardKind.NewFragment));
            await Wait(0.8f);
            var sGuide = GetField(popup, "_guideRoot") as GameObject;
            string sGuideTxt = Parsed(Find(sGuide != null ? sGuide.transform : null, "GuideBody"));
            await ShotAsync("EVO22_5_guide_gawain_sealed");
            Check(sb, $"  [봉인기] 안내 세 줄(진화 없음)\n{Indent(sGuideTxt)}", sGuide != null && !sGuideTxt.Contains("진화") && sGuideTxt.Contains("여명엔 맞힌 적에게 태양흔이 쌓이고"));
            Invoke(popup, "CloseGuide");
            await Wait(0.4f);
            await ShotAsync("EVO22_6_draft_gawain_sealed");
            string sSub = Parsed(GetField(popup, "_subtitle") as TMP_Text);
            Check(sb, $"  [봉인기] 창 머리에 진화 없음 「{sSub}」", !sSub.Contains("진화"));
        }
        finally
        {
            Managers.UI.CloseAllPopupUI();
            Time.timeScale = 1f;
            await Wait(0.4f);
        }
    }

    // ── 도우미 ──

    private static void SetEra(FieldInfo eraField, int era) => eraField?.SetValue(null, (int?)era);

    private static void Advance(ZenithGauge g) => typeof(ZenithGauge).GetMethod("Advance", Any)?.Invoke(g, null);

    private static void Invoke(object o, string method) => o?.GetType().GetMethod(method, Any)?.Invoke(o, null);

    private static void UI_BossBarkDismiss() => RelicFairy.UI.UI_BossBark.Dismiss(0f);

    private static RelicDraftCard Card(string id, RelicMemoryGrade grade, RelicDraftCardKind kind)
    {
        var e = Managers.RelicParts.GetById(id);
        return e == null ? null : new RelicDraftCard { Kind = kind, Entry = e, Anchor = e.anchor, Grade = grade };
    }

    private static void Setup(UI_RelicPartDraftPopup popup, string relicId, string relicName, params RelicDraftCard[] cards)
    {
        var draft = new RelicDraft { RelicId = relicId };
        foreach (var c in cards) if (c != null) draft.Cards.Add(c);
        popup.Setup(draft, false, relicName);
    }

    private static RectTransform CardRect(UI_RelicPartDraftPopup popup, int index)
    {
        var cards = GetField(popup, "_cards") as IList;
        if (cards == null || index >= cards.Count) return null;
        return cards[index]?.GetType().GetField("Rt")?.GetValue(cards[index]) as RectTransform;
    }

    private static string AllText(Transform root)
    {
        if (root == null) return string.Empty;
        var sb = new StringBuilder();
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true)) sb.Append(Parsed(t)).Append('\n');
        return sb.ToString();
    }

    private static object GetField(object o, string field) => o?.GetType().GetField(field, Any)?.GetValue(o);

    private static TMP_Text Find(Transform root, string name)
    {
        if (root == null) return null;
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            if (t.name == name) return t;
        return null;
    }

    private static RectTransform FindRect(Transform root, string name)
    {
        if (root == null) return null;
        foreach (var t in root.GetComponentsInChildren<RectTransform>(true))
            if (t.name == name) return t;
        return null;
    }

    private static string Parsed(TMP_Text t)
    {
        if (t == null) return string.Empty;
        t.ForceMeshUpdate();
        return t.GetParsedText();
    }

    private static string Indent(string s) => "      " + (s ?? string.Empty).Replace("\n", "\n      ");

    private static Rect WorldRect(RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
    }

    private static Rect TextWorldRect(TMP_Text t)
    {
        t.ForceMeshUpdate();
        var b = t.textBounds;
        var a = t.rectTransform.TransformPoint(b.min);
        var z = t.rectTransform.TransformPoint(b.max);
        return Rect.MinMaxRect(Mathf.Min(a.x, z.x), Mathf.Min(a.y, z.y), Mathf.Max(a.x, z.x), Mathf.Max(a.y, z.y));
    }

    private static void Check(StringBuilder sb, string what, bool ok)
    {
        s_all++;
        if (ok) s_pass++;
        sb.AppendLine($"CHECK {ok} — {what}");
    }

    private static UniTask Wait(float seconds) => UniTask.Delay(System.TimeSpan.FromSeconds(seconds), DelayType.Realtime);

    private static async UniTask ShotAsync(string name)
    {
        var runner = Object.FindFirstObjectByType<Managers>();
        if (runner == null) return;
        await UniTask.WaitForEndOfFrame(runner);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        if (tex == null) return;
        File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
        Object.Destroy(tex);
    }
}
#endif
