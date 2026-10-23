#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-22 C10 해방 대비 제단 3칸 실측(권장안 B · 설계 `기획/RelicFairy_해방기_기억의제단_권장안_20261022.md`).
/// ① 세 칸 정의 — 여정 · 고리 · 단계 · 값(합 8,190) · 「해방기 도달」 자물쇠 · 단계 글
/// ② 자물쇠 · ③ 「다음 목표」(D5) — 시기 테스트 오버라이드를 잠깐 봉인기 / 해방기로 바꿔 본다(끝나면 원래 값)
/// ④ 「해방의 내성」 3단계 — 같은 피해의 최종 피해 비 ×0.88(다른 받피감소가 있으면 그 몫을 뺀 기대값)
/// ⑤ 「결의」 — 체력 가득에서 쓰러질 한 방 → 체력 1 · 살아 있음 · 무적 · 부활 안 씀 / 같은 방 두 번째 안 됨 / 새 방이면 다시 / 2초 창 / 2단계 문턱 45%
/// ⑥ 「재정비의 샘」 — 샘을 세워 마셔 0단계 50% · 2단계 90%(악몽 30 · 50%)
/// 단계는 <b>메모리에서만</b> 바꾸고 끝나면 되돌린다(세이브 저장 없음). 런이 없고 테스트 허브면 Ch1 런을 띄워 ④ ⑤ ⑥까지 본다.
/// ⚠️ ④ ⑤는 지금 런에서 실제로 맞는다 — 체력 · 포션은 되돌리지만 무피격 기록은 깨진다.
/// 결과 Temp/fb1022_altar.txt · 로그 「[Fb1022Altar] 끝」. CHECK 줄이 전부 True면 합격.
/// </summary>
public static class Fb1022AltarLiberationProbeEditor
{
    // ── Constants ─────────────────────────────────────────────────
    private const string Tag    = "[Fb1022Altar]";
    private const string OutTxt = "Temp/fb1022_altar.txt";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    // ── Static ────────────────────────────────────────────────────
    private static int s_pass, s_fail;

    // ── Menu ──────────────────────────────────────────────────────
    [MenuItem("RelicFairy/Debug/10-22 해방 대비 제단 3칸 실측 (테스트 허브 또는 런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중(테스트 허브 또는 런)에만 동작한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        s_pass = s_fail = 0;
        var sb = new StringBuilder("10-22 해방 대비 제단 3칸 실측\n");
        bool eraKey0 = EditorPrefs.HasKey(StoryProgress.DebugOverridePrefsKey);
        int  era0    = EditorPrefs.GetInt(StoryProgress.DebugOverridePrefsKey, -1);
        UserGameData data = null;
        string ids0 = null;
        try
        {
            data = BackendGameData.Instance?.Data;
            if (data == null) { sb.AppendLine("세이브 데이터 없음 — 중단"); s_fail++; return; }
            ids0 = data.unlockedIds ?? "";
            sb.AppendLine($"지금 — 시기 {StoryProgress.Era} · 해방기 {StoryProgress.IsLiberated} · 정수 {data.abyssEssence:N0} · 여정 포인트 {MemoryAltarService.BranchPoints(AltarBranch.Journey)}");

            Definitions(sb);
            LockAndGoal(sb, data, ids0);
            RestoreEra(eraKey0, era0);
            data.unlockedIds = ids0;

            var run = await EnsureRunAsync(sb);
            if (run?.Player == null)
            {
                sb.AppendLine("런이 없어 ④ ⑤ ⑥은 건너뜀 — 테스트 허브에서 런을 띄운 뒤 다시");
                s_fail++;
                return;
            }
            Ward(sb, run, data, ids0);
            Resolve(sb, run, data, ids0);
            await SpringAsync(sb, run, data, ids0);
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); s_fail++; }
        finally
        {
            if (data != null && ids0 != null) data.unlockedIds = ids0;
            RestoreEra(eraKey0, era0);
            sb.AppendLine($"합격 {s_pass} · 불합격 {s_fail}");
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    // ── ① 칸 정의 ─────────────────────────────────────────────────
    private static void Definitions(StringBuilder sb)
    {
        sb.AppendLine("[1 칸 정의]");
        var want = new (string id, int max, int pts, int tier, int[] cost)[]
        {
            (MemoryAltarCatalog.LibWard,    3, 3, 2, new[] { 900, 1260, 1710 }),
            (MemoryAltarCatalog.LibResolve, 2, 3, 2, new[] { 1200, 1680 }),
            (MemoryAltarCatalog.LibSpring,  2, 0, 1, new[] { 600, 840 }),
        };
        int sum = 0;
        foreach (var w in want)
        {
            var n = MemoryAltarCatalog.Get(w.id);
            if (n == null) { Check(sb, $"{w.id} 칸 있음", false); continue; }
            var costs = new List<int>();
            for (int lv = 1; lv <= n.MaxLevel; lv++) costs.Add(n.CostForLevel(lv, discounted: false));
            foreach (var c in costs) sum += c;
            bool costOk = costs.Count == w.cost.Length;
            for (int i = 0; costOk && i < costs.Count; i++) costOk = costs[i] == w.cost[i];
            int tier = MemoryAltarCatalog.Tier(n);
            Check(sb, $"「{n.DisplayName}」 {MemoryAltarCatalog.BranchLabel(n.Branch)} · 단계 {n.MaxLevel} · 고리 {tier}({n.RequiredPoints}포인트) · 값 {string.Join(" · ", costs)} · " +
                      $"자물쇠 {n.ConditionRequired}({n.ConditionKey} ≥ {n.ConditionTarget} · 「{n.ConditionLabel}」) · 할인 없음 {n.DiscountCost == n.BaseCost}",
                  n.Branch == AltarBranch.Journey && n.MaxLevel == w.max && n.RequiredPoints == w.pts && tier == w.tier && costOk
                  && n.ConditionRequired && n.ConditionKey == MemoryAltarCatalog.Rec.Liberated && n.DiscountCost == n.BaseCost);
            for (int lv = 0; lv <= n.MaxLevel; lv++) sb.AppendLine($"    Lv{lv} 「{n.ValueText(lv)}」");
            sb.AppendLine($"    설명 「{n.Description}」");
        }
        Check(sb, $"세 칸 값 합 {sum:N0} = 8,190", sum == 8190);

        int all = 0;
        foreach (var n in MemoryAltarCatalog.All)
            for (int lv = 1; lv <= n.MaxLevel; lv++) all += n.CostForLevel(lv, discounted: false);
        sb.AppendLine($"  제단 전체 값 합(기본가) {all:N0} · 칸 {MemoryAltarCatalog.All.Count}");
    }

    // ── ② 자물쇠 · ③ 다음 목표 ────────────────────────────────────
    private static void LockAndGoal(StringBuilder sb, UserGameData data, string ids0)
    {
        sb.AppendLine("[2 자물쇠 · 3 다음 목표(D5)]");
        data.unlockedIds = WithLevels(ids0, 0, 0, 0);

        SetEra(0);   // 봉인기로 보기
        var s  = MemoryAltarService.GetState(MemoryAltarCatalog.LibSpring);
        var g  = MemoryAltarService.GetNextGoal();
        var g0 = MemoryAltarService.GetNextGoal(preferLiberation: false);
        Check(sb, $"봉인기 — 「재정비의 샘」 잠김 {s.BlockedByRequirement} · 못 삼 {!s.CanBuy} · 다음 목표 「{Name(g)}」 = 가장 싼 칸 「{Name(g0)}」",
              s.BlockedByRequirement && !s.CanBuy && SameNode(g, g0));
        var detail = new AltarTreePresenter().Detail(MemoryAltarCatalog.Get(MemoryAltarCatalog.LibResolve), data.abyssEssence, readOnly: false);
        sb.AppendLine($"    봉인기 「결의」 상세 — 효과 「{detail.Effect}」 · 조건 「{detail.Condition}」 · 버튼 「{detail.ButtonLabel}」 · 이유 「{detail.Reason}」");

        SetEra(1);   // 해방기로 보기
        s  = MemoryAltarService.GetState(MemoryAltarCatalog.LibSpring);
        g  = MemoryAltarService.GetNextGoal();
        g0 = MemoryAltarService.GetNextGoal(preferLiberation: false);
        bool libGoal = g != null && Array.IndexOf(MemoryAltarCatalog.LiberationNodes, g.Value.Node.Id) >= 0;
        Check(sb, $"해방기 — 「재정비의 샘」 풀림 {!s.BlockedByRequirement} · 다음 목표 「{Name(g)}」({g?.Cost}) 해방 칸 {libGoal} · 선호 끔이면 「{Name(g0)}」({g0?.Cost})",
              !s.BlockedByRequirement && libGoal);
        foreach (var id in MemoryAltarCatalog.LiberationNodes)
        {
            var node = MemoryAltarCatalog.Get(id);
            var d    = new AltarTreePresenter().Detail(node, data.abyssEssence, readOnly: false);
            sb.AppendLine($"    해방기 「{node.DisplayName}」 상세 — 효과 「{d.Effect}」 · 값 「{d.CostLine}」 · 조건 「{d.Condition}」 · 버튼 「{d.ButtonLabel}」 · 이유 「{d.Reason}」");
        }

        data.unlockedIds = WithLevels(ids0, 3, 2, 2);   // 셋 다 참 → 예전처럼 가장 싼 칸
        g  = MemoryAltarService.GetNextGoal();
        g0 = MemoryAltarService.GetNextGoal(preferLiberation: false);
        Check(sb, $"해방기 · 세 칸 다 참 — 다음 목표 「{Name(g)}」 = 가장 싼 칸 「{Name(g0)}」", SameNode(g, g0));
        data.unlockedIds = ids0;
    }

    // ── ④ 해방의 내성 ─────────────────────────────────────────────
    private static void Ward(StringBuilder sb, GameRunSession run, UserGameData data, string ids0)
    {
        sb.AppendLine("[4 해방의 내성]");
        var p  = run.Player;
        var st = p.RuntimeStats;
        int hp0 = st.Hp;
        object dbg0 = Get(p, "debugInvincible");
        int last = -1;
        var outcome = PlayerController.DamageOutcome.Applied;
        Action<GameObject, int, int, PlayerController.DamageOutcome> on = (_, _, fin, o) => { last = fin; outcome = o; };
        p.OnDamageResolved += on;
        try
        {
            Set(p, "debugInvincible", false);
            int dmg = Mathf.Max(100, st.MaxHp / 4);
            sb.AppendLine($"  최대 체력 {st.MaxHp} · 방어 {st.Defense} · 받피감소 {st.DamageReduction:0.000} · 실드 {st.Shield}");

            int Hit(int ward)
            {
                data.unlockedIds = WithLevels(ids0, ward, 0, 0);
                Set(p, "_invincibleEnd", 0f);
                st.SetHp(st.MaxHp);
                last = -1;
                p.TakeDamage(dmg, null, false, HitWeight.Light);
                int got = outcome == PlayerController.DamageOutcome.Applied ? last : -1;
                st.SetHp(st.MaxHp);
                return got;
            }

            int a = Hit(0), b = Hit(3), c = Hit(0);
            data.unlockedIds = WithLevels(ids0, 3, 0, 0);
            float term = MemoryAltarService.LiberationWardReduction;
            float stance = Get(p, "_stanceDefenseAdd") is float f ? f : 0f;
            float dr0 = Mathf.Clamp(st.DamageReduction + stance, 0f, PlayerRuntimeStats.MaxDamageReduction);
            float dr1 = Mathf.Clamp(dr0 + term, 0f, PlayerRuntimeStats.MaxDamageReduction);
            float expect = (1f - dr1) / Mathf.Max(0.01f, 1f - dr0);
            float ratio  = a > 0 && c > 0 ? b / ((a + c) * 0.5f) : -1f;
            Check(sb, $"3단계 제단 항 {term:0.000} = 0.12", Mathf.Abs(term - 0.12f) < 0.0001f);
            Check(sb, $"같은 피해 {dmg} → 최종 {a} · {b}(내성 3단계) · {c} — 비 ×{ratio:0.000} · 기대 ×{expect:0.000}" +
                      $"(다른 받피감소 {dr0:0.000}{(dr0 <= 0f ? " → ×0.88" : "")})",
                  a > 0 && a == c && Mathf.Abs(ratio - expect) <= 0.005f + 1f / a);
        }
        finally
        {
            p.OnDamageResolved -= on;
            data.unlockedIds = ids0;
            Set(p, "debugInvincible", dbg0);
            Set(p, "_invincibleEnd", 0f);
            st.SetHp(hp0);
        }
    }

    // ── ⑤ 결의 ────────────────────────────────────────────────────
    private static void Resolve(StringBuilder sb, GameRunSession run, UserGameData data, string ids0)
    {
        sb.AppendLine("[5 결의]");
        var p  = run.Player;
        var st = p.RuntimeStats;
        int hp0 = st.Hp;
        object dbg0 = Get(p, "debugInvincible");
        bool revive0 = run.MetaReviveUsed;
        int last = -1;
        var outcome = PlayerController.DamageOutcome.Applied;
        Action<GameObject, int, int, PlayerController.DamageOutcome> on = (_, _, fin, o) => { last = fin; outcome = o; };
        var tryResolve = typeof(PlayerController).GetMethod("TryAltarResolve", Any);
        var note       = typeof(PlayerController).GetMethod("NoteResolveHp", Any);
        var room       = typeof(PlayerController).GetMethod("HandleRoomRevealedForResolve", Any);
        var page       = typeof(PlayerController).GetMethod("CurrentBossPage", Any);
        bool Try()      => (bool)tryResolve.Invoke(p, null);
        void NewRoom()  => room.Invoke(p, new object[] { null });
        void Note()     => note.Invoke(p, null);
        p.OnDamageResolved += on;
        try
        {
            Set(p, "debugInvincible", false);

            // ① 체력 가득 → 쓰러질 한 방(실제 피격 경로)
            data.unlockedIds = WithLevels(ids0, 0, 1, 0);
            NewRoom();
            Set(p, "_invincibleEnd", 0f);
            Set(p, "_resolveHighHpAt", float.NegativeInfinity);
            st.SetHp(st.MaxHp);
            int big = st.MaxHp * 3;
            p.TakeDamage(big, null, false, HitWeight.Light);
            bool dead = Get(p, "_dead") is true;
            Check(sb, $"① 1단계 · 체력 가득에서 쓰러질 한 방({big}) → 체력 {st.Hp}/{st.MaxHp} · 결과 {outcome} 최종 {last} · 살아 있음 {!dead} · 무적 {p.IsInvincible} · 부활 안 씀 {run.MetaReviveUsed == revive0}",
                  st.Hp == 1 && !dead && p.IsInvincible && run.MetaReviveUsed == revive0 && outcome == PlayerController.DamageOutcome.Applied);

            // ② 같은 방 두 번째 — 체력이 높아도 안 된다(죽지 않게 판정만 직접 부른다 — TakeDamage가 부르는 그 판정)
            Set(p, "_invincibleEnd", 0f);
            st.SetHp(st.MaxHp);
            Note();
            bool second = Try();
            Check(sb, $"② 같은 방 두 번째 쓰러질 한 방 — 결의 안 됨 {!second}", !second);

            // ③ 새 방이 드러나면 다시 찬다
            NewRoom();
            bool again = Try();
            Check(sb, $"③ 새 방(방 드러남) 뒤 — 다시 됨 {again}", again);

            // ④ 2초 창 — 지금 체력 30%(문턱 아래), 60% 이상이던 때가 2.5초 전 / 1.5초 전
            NewRoom();
            st.SetHp(Mathf.Max(1, st.MaxHp * 3 / 10));
            Set(p, "_resolveHighHpAt", Time.time - 2.5f);
            Note();
            bool stale = Try();
            Set(p, "_resolveHighHpAt", Time.time - 1.5f);
            bool fresh = Try();
            Check(sb, $"④ 체력 30% — 60% 이상이 2.5초 전이면 안 됨 {!stale} · 1.5초 전이면 됨 {fresh}", !stale && fresh);

            // ⑤ 2단계 문턱 45% — 체력 50%에서 1단계는 기록 안 함 · 2단계는 기록
            st.SetHp(st.MaxHp / 2);
            Set(p, "_resolveHighHpAt", float.NegativeInfinity);
            Note();
            bool lv1Noted = !(Get(p, "_resolveHighHpAt") is float t1 && float.IsNegativeInfinity(t1));
            data.unlockedIds = WithLevels(ids0, 0, 2, 0);
            Note();
            bool lv2Noted = !(Get(p, "_resolveHighHpAt") is float t2 && float.IsNegativeInfinity(t2));
            Check(sb, $"⑤ 체력 50% — 1단계(60%) 기록 {lv1Noted} · 2단계(45%) 기록 {lv2Noted} · 2단계 문턱 {MemoryAltarService.ResolveHpThreshold:0.00}",
                  !lv1Noted && lv2Noted && Mathf.Abs(MemoryAltarService.ResolveHpThreshold - 0.45f) < 0.001f);

            // ⑥ 안 산 상태 — 결의 없음
            data.unlockedIds = WithLevels(ids0, 0, 0, 0);
            NewRoom();
            Set(p, "_resolveHighHpAt", Time.time);
            bool none = Try();
            Check(sb, $"⑥ 안 샀으면 결의 없음 {!none}", !none);

            sb.AppendLine($"  지금 보스 페이지 {page?.Invoke(null, null)} (보스가 없으면 0 — 보스는 페이지가 바뀌면 다시 찬다)");
        }
        finally
        {
            p.OnDamageResolved -= on;
            data.unlockedIds = ids0;
            NewRoom();
            Set(p, "_resolveHighHpAt", float.NegativeInfinity);
            Set(p, "_invincibleEnd", 0f);
            Set(p, "debugInvincible", dbg0);
            st.SetHp(hp0);
        }
    }

    // ── ⑥ 재정비의 샘 ─────────────────────────────────────────────
    private static async UniTask SpringAsync(StringBuilder sb, GameRunSession run, UserGameData data, string ids0)
    {
        sb.AppendLine("[6 재정비의 샘]");
        var p     = run.Player;
        var st    = p.RuntimeStats;
        var state = run.PlayerState;
        int hp0   = st.Hp;
        int pot0  = state != null ? state.PotionCount : 0;
        int cap0  = state != null ? state.PotionCapacity : 0;
        bool nightmare = NightmareRules.IsActive;
        var ratioOf = typeof(ChapterSpring).GetMethod("HealRatioNow", Any);
        var drink   = typeof(ChapterSpring).GetMethod("Drink", Any);
        GameObject go = null;
        try
        {
            foreach (int lv in new[] { 0, 2 })
            {
                data.unlockedIds = WithLevels(ids0, 0, 0, lv);
                float ratio = (float)ratioOf.Invoke(null, new object[] { nightmare });
                float want  = nightmare ? 0.3f + 0.1f * lv : 0.5f + 0.2f * lv;

                var spring = ChapterSpring.SpawnAt(p.transform.position + p.transform.forward * 30f, null);
                go = spring.gameObject;
                st.SetHp(1);
                int expect = Mathf.Max(1, Mathf.RoundToInt(st.MaxHp * ratio));
                run.EffectManager?.ModifyHeal(ref expect);
                expect = Mathf.Min(expect, st.MaxHp - 1);
                drink.Invoke(spring, null);
                int healed = st.Hp - 1;
                Check(sb, $"{lv}단계 — 회복 비율 {ratio:0.00}(기대 {want:0.00}{(nightmare ? " · 악몽" : "")}) · 마셔서 체력 1 → {st.Hp}/{st.MaxHp}(+{healed} · 기대 +{expect})",
                      Mathf.Abs(ratio - want) < 0.001f && healed == expect);

                UnityEngine.Object.Destroy(go);
                go = null;
                await UniTask.Yield();
            }
        }
        finally
        {
            if (go != null) UnityEngine.Object.Destroy(go);
            data.unlockedIds = ids0;
            st.SetHp(hp0);
            state?.RestorePotions(pot0, cap0);
        }
    }

    // ── 런 띄우기(테스트 허브) ────────────────────────────────────
    private static async UniTask<GameRunSession> EnsureRunAsync(StringBuilder sb)
    {
        var run = GameRunBootstrapper.Instance?.Run;
        if (run?.Player != null) return run;

        var launcher = UnityEngine.Object.FindFirstObjectByType<TestHubLauncher>();
        if (launcher == null) return null;
        sb.AppendLine("  테스트 허브 — Ch1 런을 띄운다(일반 시작)");
        launcher.SelectChapter(1);
        typeof(TestHubLauncher).GetField("_bossApproach", Any)?.SetValue(launcher, false);
        launcher.TryLaunch();
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 60f && GameRunBootstrapper.Instance?.Run?.Player == null)
            await UniTask.Delay(500, DelayType.Realtime);
        await UniTask.Delay(3000, DelayType.Realtime);
        return GameRunBootstrapper.Instance?.Run;
    }

    // ── 도우미 ────────────────────────────────────────────────────
    /// <summary>산 칸 문자열에서 해방 3칸만 바꾼다(메모리 — 저장하지 않는다). 1단계는 「id」, 2단계 이상은 「id:n」.</summary>
    private static string WithLevels(string ids, int ward, int resolve, int spring)
    {
        var lib   = new HashSet<string>(MemoryAltarCatalog.LiberationNodes);
        var parts = new List<string>();
        foreach (var raw in (ids ?? "").Split(','))
        {
            if (string.IsNullOrEmpty(raw)) continue;
            int c = raw.IndexOf(':');
            if (!lib.Contains(c < 0 ? raw : raw.Substring(0, c))) parts.Add(raw);
        }
        void Add(string id, int lv) { if (lv > 0) parts.Add(lv == 1 ? id : $"{id}:{lv}"); }
        Add(MemoryAltarCatalog.LibWard, ward);
        Add(MemoryAltarCatalog.LibResolve, resolve);
        Add(MemoryAltarCatalog.LibSpring, spring);
        return string.Join(",", parts);
    }

    /// <summary>시기 테스트 오버라이드(0 봉인기 · 1 해방기) — 오버라이드가 켜진 동안 이야기 기록은 저장되지 않는다.</summary>
    private static void SetEra(int v)
    {
        EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, v);
        StoryProgress.RefreshDebugOverride();
    }

    private static void RestoreEra(bool hadKey, int v)
    {
        if (hadKey) EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, v);
        else        EditorPrefs.DeleteKey(StoryProgress.DebugOverridePrefsKey);
        StoryProgress.RefreshDebugOverride();
    }

    private static string Name(AltarNodeState? s) => s?.Node?.DisplayName ?? "없음";

    private static bool SameNode(AltarNodeState? a, AltarNodeState? b) => a?.Node?.Id == b?.Node?.Id;

    private static object Get(object o, string field) => o?.GetType().GetField(field, Any)?.GetValue(o);

    private static void Set(object o, string field, object v)
    {
        var f = o?.GetType().GetField(field, Any);
        if (f != null && v != null) f.SetValue(o, v);
    }

    private static void Check(StringBuilder sb, string label, bool ok)
    {
        if (ok) s_pass++; else s_fail++;
        sb.AppendLine($"  CHECK {ok} — {label}");
    }
}
#endif
