#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10-15 증강 고르기 창 실측 — 테스트 허브에서 플레이 중에 돌린다(1장 런을 연다).
///  ① 강화 단계에 처음 닿음 → 호스트가 창을 연다 · 두 장이 뒤집혀 등급이 드러난다(사진 뒤집는 중 · 다 드러남) · 글자 16 · 넘침 · 겹침
///  ② 다시 뽑기(한 장 · 남은 수 −1) ③ 고르기(무기에 증강 · 창 닫힘) ④ 놓친 단계 셋 — 고를 때마다 창이 닫히지 않고 이어 뒤집힌다
///  ⑤ 모양 — 프리즘 · 강화 카드(「II → III」 · 지금 단계) ⑥ 재련소 — 망치 · 결과 도장이 끝난 뒤에 뜬다(판정이 새지 않게).
/// 사진 Temp/ui_shots/AUG_*.png · 결과 Temp/melee_augment_probe.txt · 로그 「[AugProbe] 끝」.
/// </summary>
public static class MeleeAugmentPopupProbeEditor
{
    private const string Tag = "[AugProbe]";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static int s_pass, s_all;

    private sealed class SeqRandom : System.Random
    {
        private readonly double[] _v;
        private int _i;
        public SeqRandom(params double[] v) : base(1) { _v = v; }
        public override double NextDouble() => _v[Mathf.Min(_i++, _v.Length - 1)];
    }

    [MenuItem("RelicFairy/Debug/10-15 증강 고르기 창 촬영 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중에만 동작한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-15 증강 고르기 창 실측\n");
        s_pass = s_all = 0;
        Directory.CreateDirectory("Temp/ui_shots");
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
            var run = GameRunBootstrapper.Instance?.Run;
            if (run?.Player == null) { sb.AppendLine("런이 시작되지 않음"); return; }
            await Wait(3f);
            HubGui("HideTestHubGui");
            var w = run.Player.WeaponManager.Weapon0Data;
            Check(sb, "근접 무기(증강 갈래)", w != null && MeleeAugmentCatalog.KindOf(w) != MeleeWeaponKind.None, $"{w?.displayName} · {MeleeAugmentCatalog.KindOf(w)}");
            if (w == null) return;
            MeleeAugmentDraft.RerollsLeft = MeleeAugmentDraft.RerollsPerRun;

            // ① 처음 닿은 단계 — 창이 뜨고 두 장이 뒤집힌다
            w.enhanceLevel = 3; w.highestEnhanceReached = 2;
            bool offered = MeleeAugmentDraft.TryOfferForEnhance(w, 3, false, new System.Random(7));
            await Wait(0.62f);
            await Shot("AUG_1_Flip");
            await Wait(1.2f);
            var pop = UI_MeleeAugmentPopup.Current;
            Check(sb, "+3 처음 닿음 → 창이 열린다", offered && pop != null, $"제안 {offered} · 창 {pop != null}");
            if (pop == null) return;
            var tier = Field<TMP_Text>(pop, "_tier");
            var cards = Cards(pop);
            Check(sb, "두 장이 다 드러남 · 등급 이름", cards.Count == 2 && cards.TrueForAll(c => Face(c) > 0.99f) && tier.alpha > 0.99f,
                  $"앞면 {string.Join(" · ", cards.ConvertAll(c => Face(c).ToString("0.0")))} · 「{tier.text}」");
            ScreenCheck(sb, "공개", pop.transform);
            await Shot("AUG_2_Revealed");

            // ② 다시 뽑기
            string before = Name(cards[1]);
            int left = MeleeAugmentDraft.RerollsLeft;
            Call(pop, "OnRerollClicked", 1);
            await Wait(0.7f);
            Check(sb, "다시 뽑기 — 한 장 · 남은 수 −1", MeleeAugmentDraft.RerollsLeft == left - 1 && Name(cards[1]) != before,
                  $"{before} → {Name(cards[1])} · 남은 {left} → {MeleeAugmentDraft.RerollsLeft}");

            // ③ 고르기
            string pick = Name(cards[0]);
            Call(pop, "OnCardClicked", 0);
            await Wait(0.9f);
            int augs = 0; foreach (var _ in w.AugmentList()) augs++;
            Check(sb, "고르기 — 무기에 증강 · 창 닫힘", augs >= 1 && UI_MeleeAugmentPopup.Current == null && w.highestEnhanceReached == 3,
                  $"「{pick}」 · 증강 {augs} · 받은 단계 {w.highestEnhanceReached} · 창 {UI_MeleeAugmentPopup.Current != null}");

            // ④ 놓친 단계 셋(+4 · +5 · +6) — 고를 때마다 이어 뒤집힌다
            w.enhanceLevel = 6;
            MeleeAugmentDraft.TryOfferForEnhance(w, 6, false, new System.Random(11));
            await Wait(1.9f);
            var levels = new List<int>();
            for (int k = 0; k < 4 && UI_MeleeAugmentPopup.Current != null; k++)
            {
                var p = UI_MeleeAugmentPopup.Current;
                var offer = typeof(UI_MeleeAugmentPopup).GetField("_offer", Any)?.GetValue(p) as MeleeAugmentOffer;
                levels.Add(offer?.Level ?? -1);
                Call(p, "OnCardClicked", k % 2);
                await Wait(2.0f);
            }
            Check(sb, "놓친 단계 — +4 · +5 · +6 이어서 · 끝나면 닫힘", levels.Count == 3 && levels[0] == 4 && levels[2] == 6 && UI_MeleeAugmentPopup.Current == null,
                  $"{string.Join(" · ", levels)} · 받은 단계 {w.highestEnhanceReached}");

            // ⑤ 모양 — 프리즘 · 강화 카드
            string owned = null;
            foreach (var (id, _) in w.AugmentList()) { owned = id; break; }
            var shown = new MeleeAugmentOffer { Weapon = w, Level = 14, Tier = AugmentTier.Prismatic };
            // 강화 카드 — 실제 뽑기처럼 Ⅲ를 넘기지 않는다(가진 증강이 이미 Ⅲ면 그 증강의 Ⅱ → Ⅲ로 보인다)
            int ownedNext = owned != null ? Mathf.Min(w.AugmentRank(owned) + 1, MeleeAugmentDef.MaxRank) : 2;
            shown.Cards[0] = new MeleeAugmentCard(owned ?? Longest(), Mathf.Max(2, ownedNext));
            shown.Cards[1] = new MeleeAugmentCard(Longest(), 1);   // 10-15 규칙형 개편 — 글이 가장 긴 증강(약 45자)이 카드에 드는지
            var view = await Managers.UI.ShowPopupUIAndGetAsync<UI_MeleeAugmentPopup>();
            view.Show(shown);
            await Wait(1.9f);
            var vc = Cards(view);
            var rank = Field<TMP_Text>(vc[0], "Rank");
            var now  = Field<TMP_Text>(vc[0], "Now");
            Check(sb, "강화 카드 — 「II → III」 · 지금 단계", rank != null && rank.text.Contains("→") && now != null && now.text.StartsWith("지금"),
                  $"「{rank?.text}」 「{now?.text}」");
            ScreenCheck(sb, "프리즘 · 강화", view.transform);
            await Shot("AUG_3_PrismUpgrade");
            view.ClosePopupUI();
            await Wait(0.6f);

            await CrucibleAsync(run, w, sb);
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Managers.UI?.CloseAllPopupUI();
            HubGui("RestoreTestHubGui");
            sb.AppendLine($"통과 {s_pass}/{s_all}");
            File.WriteAllText("Temp/melee_augment_probe.txt", sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    /// <summary>⑥ 재련소 — 강화 성공(처음 닿은 단계) → 망치 · 결과 도장이 끝난 뒤 창이 뜬다.</summary>
    private static async UniTask CrucibleAsync(GameRunSession run, WeaponData w, StringBuilder sb)
    {
        var go = new GameObject("~AugProbeCrucible");
        try
        {
            w.enhanceLevel = 6; w.highestEnhanceReached = 6;
            w.RecomputeEnhancedStats();
            run.FuelBank.Add(FuelKind.EnhanceMaterial, 60);
            var ctrl = go.AddComponent<CrucibleRoomController>();
            ctrl.Initialize(run, await WeaponEnhanceService.EnsureLoadedAsync(), new System.Random(7), null);
            var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
            panel.Bind(ctrl);
            await Wait(0.9f);
            Field<Button[]>(panel, "_chooseCard")?[0]?.onClick.Invoke();
            await Wait(0.4f);
            typeof(CrucibleRoomController).GetField("_roomRng", Any)?.SetValue(ctrl, new SeqRandom(0.0, 0.99));
            float t0 = Time.realtimeSinceStartup, revealEnd = -1f, popAt = -1f;
            bool wasRevealing = false;
            Field<Button>(panel, "_enhanceBtn")?.onClick.Invoke();
            while (Time.realtimeSinceStartup - t0 < 6f)
            {
                float now = Time.realtimeSinceStartup - t0;
                if (UI_CruciblePanel.Revealing) wasRevealing = true;
                else if (wasRevealing && revealEnd < 0f) revealEnd = now;
                if (popAt < 0f && UI_MeleeAugmentPopup.Current != null) { popAt = now; break; }
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            Check(sb, "재련소 — 연출이 끝난 뒤 창이 뜬다", w.enhanceLevel == 7 && revealEnd > 0f && popAt > revealEnd,
                  $"+{w.enhanceLevel} · 연출 끝 {revealEnd:0.00}s · 창 {popAt:0.00}s");
            await Wait(1.6f);
            await Shot("AUG_4_Crucible");
            if (UI_MeleeAugmentPopup.Current != null) Call(UI_MeleeAugmentPopup.Current, "OnCardClicked", 0);
            await Wait(0.9f);
        }
        finally
        {
            Managers.UI?.CloseAllPopupUI();
            Object.Destroy(go);
        }
    }

    // ── 화면 CHECK ──
    private static void ScreenCheck(StringBuilder sb, string what, Transform root)
    {
        Canvas.ForceUpdateCanvases();
        var texts = new List<TMP_Text>();
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(false))
            if (Visible(t)) texts.Add(t);
        var small = new StringBuilder();
        var over  = new StringBuilder();
        foreach (var t in texts)
        {
            t.ForceMeshUpdate();
            if (t.fontSize < 15.95f) small.Append($"{t.name}({t.fontSize:0.#}) ");
            if (t.isTextTruncated) over.Append($"{t.name}(말줄임) ");
        }
        var hit = new StringBuilder();
        for (int i = 0; i < texts.Count; i++)
            for (int j = i + 1; j < texts.Count; j++)
            {
                var a = WorldRect(texts[i]); var b = WorldRect(texts[j]);
                float ox = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
                float oy = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                if (ox > 1f && oy > 1f) hit.Append($"{texts[i].name}×{texts[j].name} ");
            }
        Check(sb, $"{what} — 글자 최소 16", small.Length == 0, small.Length == 0 ? $"보이는 글 {texts.Count}" : small.ToString());
        Check(sb, $"{what} — 넘침 0", over.Length == 0, over.Length == 0 ? "없음" : over.ToString());
        Check(sb, $"{what} — 글자 겹침 0", hit.Length == 0, hit.Length == 0 ? "없음" : hit.ToString());
    }

    private static Rect WorldRect(TMP_Text t)
    {
        var b = t.textBounds;
        Vector3 a = t.transform.TransformPoint(b.min), c = t.transform.TransformPoint(b.max);
        return Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));
    }

    private static bool Visible(TMP_Text t)
    {
        if (t == null || !t.isActiveAndEnabled || t.color.a < 0.05f || t.alpha < 0.05f) return false;
        if (t.GetParsedText().Trim().Length == 0) return false;
        float a = 1f;
        foreach (var g in t.GetComponentsInParent<CanvasGroup>(false)) { a *= g.alpha; if (g.ignoreParentGroups) break; }
        return a >= 0.05f;
    }

    // ── 도우미 ──
    private static List<object> Cards(UI_MeleeAugmentPopup p)
    {
        var list = new List<object>();
        if (typeof(UI_MeleeAugmentPopup).GetField("_cards", Any)?.GetValue(p) is System.Array arr)
            foreach (var c in arr) list.Add(c);
        return list;
    }

    private static float Face(object card) => Field<CanvasGroup>(card, "Face")?.alpha ?? 0f;
    private static string Name(object card) => Field<TMP_Text>(card, "Name")?.text ?? "";

    /// <summary>카드 글(단계 III 기준)이 가장 긴 증강 — 넘침 · 말줄임을 가장 나쁜 경우로 잰다.</summary>
    private static string Longest()
    {
        string best = null; int len = -1;
        foreach (var d in MeleeAugmentCatalog.All)
        {
            int n = d.Text(MeleeAugmentDef.MaxRank).Length;
            if (n > len) { len = n; best = d.Id; }
        }
        return best ?? FirstPrism();
    }

    private static string FirstPrism()
    {
        foreach (var d in MeleeAugmentCatalog.All) if (d.Tier == AugmentTier.Prismatic) return d.Id;
        return "aug_imprint_echo";
    }

    private static T Field<T>(object o, string name) where T : class
        => o == null ? null : o.GetType().GetField(name, Any)?.GetValue(o) as T;

    private static void Call(object o, string method, params object[] args)
        => o?.GetType().GetMethod(method, Any)?.Invoke(o, args);

    private static void Check(StringBuilder sb, string what, bool ok, string detail)
    {
        s_all++;
        if (ok) s_pass++;
        sb.AppendLine($"  CHECK {what}: {ok} ({detail})");
    }

    private static void HubGui(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 0 }) : UniTask.CompletedTask;
    }

    private static UniTask Wait(float s) => UniTask.Delay(System.TimeSpan.FromSeconds(s), DelayType.Realtime);
}
#endif
