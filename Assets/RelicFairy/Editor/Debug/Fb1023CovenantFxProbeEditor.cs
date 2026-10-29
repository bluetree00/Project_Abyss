#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-23 U8 사용자 「서약 효과도 제대로 작동해야 하고 체크해」 — 런 전투방에서, 플레이 중.
/// <list type="bullet">
/// <item>① 한 절 서약: 원인 11 × 결과 16(봉인된 짝은 건너뜀). 서약서를 그 한 줄만 맺고 원인을 실제 훅(적중 · 처치 · 스킬 · 방 입장 · 주기 …)으로 건 뒤
///       0.65초 — 첫 절이 일어났나 · 결과창 「발동」 수 · 서약 피해(<see cref="DamageSource.Covenant"/>) · 결과별 흔적
///       (화상 · 출혈 · 저주 · 감전 · 기절 · 보호막 · 무적 · 버프 · 골드 · 쿨 · 쓰러짐 막기).</item>
/// <item>② 이음 사슬: 쓰러지면(화상 · 출혈 <b>틱이 죽인</b> 적 · 폭발이 죽인 적도 듣는가 — 사진 04 「연주」 꼴) · 그동안 · 견디면 · 곧바로 · 지금 런의 서약서.</item>
/// </list>
/// 진짜로 쓰러뜨리는 건 ②의 화상 · 출혈 · 폭발 세 마리뿐 — 나머지 「쓰러지면 · 그동안」은 처치 통지(MonsterBase가 부르는 것과 같은 CovenantHandler.OnKill)로 흉내 낸다.
/// 결과: Temp/fb1023_cov.txt · 로그 「[Cov23fx] 끝」. 플레이어는 디버그 무적 · 원래 서약은 끝나면 되돌린다. 적이 3마리 이상 있는 전투방에서.
/// </summary>
public static class Fb1023CovenantFxProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Log = "[Cov23fx]";
    private const string OutPath = "Temp/fb1023_cov.txt";
    private const int SettleMs = 650;   // 화상 · 출혈 첫 틱(0.5초) + 여유

    private static readonly string[] AllEffects =
    {
        "supernova", "fury", "curse", "execute", "ember", "hemorrhage", "detonate", "harvest",
        "arcflash", "stasis", "bloodmark", "aegis", "lastbreath", "ward", "momentum", "goldrain",
    };
    private static readonly string[] AllCauses =
    {
        "streak", "slaughter", "swap", "clear", "heartbeat", "concerto", "hunt", "opener", "besiege", "aim", "march",
    };

    private static GameObject s_corpse;   // 학살 · 사냥 개시 — 「쓰러진 자리」(살아 있는 적이 아닌 곳)

    [MenuItem("RelicFairy/Debug/10-23 서약 효과 발동 실측 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null)
        {
            Debug.LogWarning($"{Log} 런 · 전투방에서 플레이 중에 실행해야 한다.");
            return;
        }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder("10-23 서약 효과 발동 실측\n");
        var run = GameRunBootstrapper.Instance.Run;
        var player = run.Player;
        var handler = run.CovenantHandler;
        var list = (List<CovenantBase>)typeof(CovenantHandler).GetField("_covenants", Inst).GetValue(handler);
        var ctx  = (CovenantContext)typeof(CovenantHandler).GetField("_ctx", Inst).GetValue(handler);
        var invF = typeof(PlayerController).GetField("debugInvincible", Inst);
        bool invWas = (bool)invF.GetValue(player);
        var saved = new List<CovenantBase>(list);
        string savedSentence = null;
        foreach (var c in saved) if (c is CovenantSentence ss) savedSentence = ss.CovenantId;
        list.Clear();
        invF.SetValue(player, true);
        s_corpse = new GameObject("Cov23fx_Corpse");
        var touched = new HashSet<MonsterBase>();
        int aOk = 0, aRun = 0, bOk = 0, bRun = 0;
        try
        {
            // ── ① 한 절 서약 ─────────────────────────────
            sb.AppendLine("\n① 한 절 서약 — 원인 × 결과(실버 · 실버). 열: 첫 절 · 발동(결과창) · 서약 피해 · 흔적");
            var fails = new List<string>();
            foreach (var cause in AllCauses)
            foreach (var effect in AllEffects)
            {
                if (CovenantPalette.IsBannedPair(cause, effect)) { sb.AppendLine($"  {cause} × {effect}: 봉인된 짝 — 건너뜀"); continue; }
                var mobs = Gather(player, 3);
                if (mobs.Count == 0) { sb.AppendLine("  적이 없다 — ① 중단"); break; }
                foreach (var m in mobs) touched.Add(m);
                aRun++;
                var r = await SingleCase(run, player, handler, list, ctx, cause, effect, mobs);
                if (r.ok) aOk++; else fails.Add($"{cause} × {effect}");
                sb.AppendLine($"  {(r.ok ? "통과" : "실패")} {cause} × {effect}: 첫 절 {(r.fired ? "O" : "X")} · 발동 +{r.fires} · 서약 피해 {r.dmg} · {r.note}");
            }
            sb.AppendLine($"① 합계 {aOk}/{aRun}" + (fails.Count > 0 ? " · 실패: " + string.Join(", ", fails) : string.Empty));

            // ── ② 이음 사슬 ─────────────────────────────
            sb.AppendLine("\n② 이음 사슬(기대 절 번호 = 일어나야 할 절)");
            var chains = new List<(string name, string id, string expect, string evt)>
            {
                ("사진04 연주 — 화상 틱 처치 → 폭발 처치 → 화상", "sen:concerto@ruby>ember@ruby>supernova@gold~d>ember@ruby~d", "0,1,2", "burn-kill"),
                ("출혈 틱 처치 → 폭발",            "sen:streak@silver>hemorrhage@silver>supernova@silver~d", "0,1", "bleed-kill"),
                ("저주 → 쓰러지면 폭발",            "sen:streak@silver>curse@silver>supernova@silver~d", "0,1", "kill-first"),
                ("격노 → 그동안 폭발",              "sen:streak@silver>fury@silver>supernova@silver~w", "0,1", "kill-other"),
                ("학살 · 박차 → 그동안 화상",        "sen:slaughter@silver>momentum@silver>ember@silver~w", "0,1", "kill-other"),
                ("포위 · 결계 → 그동안 저주",        "sen:besiege@silver>ward@silver>curse@silver~w", "0,1", "kill-other"),
                ("성역 → 무적이 끝나면 폭발",        "sen:streak@silver>aegis@silver>supernova@silver~e", "0,1", "wait"),
                ("피의 보호막 → 깨지면 화상",        "sen:streak@silver>bloodmark@silver>ember@silver~e", "0,1", "break-shield"),
                ("곧바로 3절(화상 → 기폭 → 격노)",   "sen:streak@silver>ember@silver>detonate@silver~i>fury@silver~i", "0,1,2", "none"),
                ("방전 → 정지 → 쓰러지면 폭발",      "sen:heartbeat@silver>arcflash@silver>stasis@silver~i>supernova@silver~d", "0,1,2", "kill-first"),   // 줄 셋 = 절 0 · 1 · 2(옛 기대 「0,1,2,3」은 오타)
                ("화상 → 곧바로 처형",               "sen:streak@silver>ember@silver>execute@silver~i", "0,1", "low-hp"),
            };
            if (!string.IsNullOrEmpty(savedSentence))
                chains.Add(("지금 런의 서약서(조건만 — 이음 사건 없음)", savedSentence, "0", "none"));

            foreach (var (name, id, expect, evt) in chains)
            {
                var mobs = Gather(player, 3);
                int need = evt == "burn-kill" ? 3 : 2;
                if (mobs.Count < need) { sb.AppendLine($"  건너뜀 {name}: 살아 있는 적 {mobs.Count}마리(필요 {need})"); continue; }
                foreach (var m in mobs) touched.Add(m);
                bRun++;
                var r = await ChainCase(run, player, handler, list, ctx, id, evt, mobs);
                bool pass = name.StartsWith("지금 런")
                    ? r.got.StartsWith("0")   // 지금 런의 서약서 — 첫 절이 일어나면 통과(뒤 절은 이음 사건이 있어야 한다)
                    : r.got == expect && (!id.Contains("detonate") || r.dmg > 0);
                if (pass) bOk++;
                sb.AppendLine($"  {(pass ? "통과" : "실패")} {name}: 일어난 절 [{r.got}] · 기대 [{expect}] · 발동 +{r.fires} · 서약 피해 {r.dmg} · {r.note}");
                foreach (var line in r.lines) sb.AppendLine("      " + line);
            }
            sb.AppendLine($"② 합계 {bOk}/{bRun}");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            foreach (var c in list) c.Dispose();
            list.Clear();
            list.AddRange(saved);   // 원래 서약(지금 런의 서약서)을 그대로 되돌린다 — Dispose하지 않았다
            invF.SetValue(player, invWas);
            foreach (var m in touched) if (m != null) m.HpFloorMin1 = false;
            if (s_corpse != null) Object.Destroy(s_corpse);
            sb.AppendLine($"\n합계 ① {aOk}/{aRun} · ② {bOk}/{bRun}");
            File.WriteAllText(OutPath, sb.ToString());
            Debug.Log($"{Log} 끝\n" + sb);
        }
    }

    // ── ① 한 절 ─────────────────────────────────────────
    private static async UniTask<(bool ok, bool fired, int fires, long dmg, string note)> SingleCase(
        GameRunSession run, PlayerController player, CovenantHandler handler, List<CovenantBase> list, CovenantContext ctx,
        string cause, string effect, List<MonsterBase> mobs)
    {
        foreach (var m in mobs) ResetMob(m, floor: true);
        player.RuntimeStats.AbsorbWithShield(100000);
        player.CooldownTracker?.StartCooldown(SkillType.E, 10f);
        Prepare(effect, player, mobs);
        await UniTask.Delay(60, ignoreTimeScale: true);   // 걷어 낸 화상 핸들러가 프레임 끝에 사라진 뒤

        var s = Equip(list, ctx, player, CovenantSentence.Prefix + cause + "@silver>" + effect + "@silver");
        if (s == null) return (false, false, 0, 0, "맺기 실패");
        bool fired = false;
        s.ClauseFired += i => { if (i == 0) fired = true; };

        long dmg0 = run.RunStats.DamageOf(DamageSource.Covenant);
        int fires0 = run.RunStats.CovenantFires;
        int gold0 = run.PlayerState?.TempGold ?? 0;
        int shield0 = player.RuntimeStats.Shield;
        float cd0 = player.CooldownTracker != null ? player.CooldownTracker.GetRemaining(SkillType.E) : 0f;
        var invF = typeof(PlayerController).GetField("_invincibleEnd", Inst);
        float inv0 = (float)invF.GetValue(player);

        Trigger(handler, s, cause, mobs[0], player);
        // 짧은 상태(정지 기절 0.7초 안팎 · 성역 0.5초)는 걸린 순간에 본다
        int stunNow = 0; foreach (var m in mobs) if (m != null && m.Status != null && m.Status.HasCc("stun")) stunNow++;
        bool invNow = (float)invF.GetValue(player) > inv0;
        await UniTask.Delay(SettleMs, ignoreTimeScale: true);

        long dmg = run.RunStats.DamageOf(DamageSource.Covenant) - dmg0;
        int fires = run.RunStats.CovenantFires - fires0;
        int gold = (run.PlayerState?.TempGold ?? 0) - gold0;
        float cdDrop = cd0 - (player.CooldownTracker != null ? player.CooldownTracker.GetRemaining(SkillType.E) : 0f) - SettleMs / 1000f;
        var (trace, note) = Trace(effect, s, player, mobs, dmg, gold, player.RuntimeStats.Shield - shield0, cdDrop, stunNow, invNow);
        bool ok = fired && trace && (cause == "clear" || fires == 1);
        if (cause == "clear" && !fired) { ok = true; note += " · 개선은 방이 빈 뒤라 받을 적이 없는 게 정상(드래프트 제외 원인)"; }
        return (ok, fired, fires, dmg, note);
    }

    /// <summary>결과마다 「일어났다」의 흔적 — 적 상태 · 내 상태 · 피해 · 골드.</summary>
    private static (bool ok, string note) Trace(string effect, CovenantSentence s, PlayerController player, List<MonsterBase> mobs,
                                                long dmg, int gold, int shieldGain, float cdDrop, int stunNow, bool invNow)
    {
        switch (effect)
        {
            case "supernova": return (dmg > 0, $"폭발 피해 {dmg}");
            case "ember":
            {
                int n = 0; foreach (var m in mobs) if (m != null && m.TryGetComponent<MonsterBurnHandler>(out var b) && b.Remaining > 0f) n++;
                return (n > 0 && dmg > 0, $"불붙은 적 {n} · 틱 피해 {dmg}");
            }
            case "hemorrhage":
            {
                int n = 0; foreach (var m in mobs) if (m != null && m.Status != null && m.Status.GetDotDamagePerTick("bleed") > 0f) n++;
                return (n > 0 && dmg > 0, $"출혈 적 {n} · 틱 피해 {dmg}");
            }
            case "curse":
            {
                int n = 0; foreach (var m in mobs) if (m != null && m.HasDamageTakenAmpSlot("cov_curse")) n++;
                return (n > 0, $"저주 걸린 적 {n}" + (n == 0 ? CurseElsewhere(player) : string.Empty));
            }
            case "execute":
                return (mobs[0] != null && mobs[0].CurrentHp <= 1 && dmg > 0, $"체력 10% 적 → 체력 {mobs[0]?.CurrentHp}(하한 1로 붙잡음) · 피해 {dmg}");
            case "detonate": return (dmg > 0, $"터뜨린 피해 {dmg}");
            case "harvest":  return (gold >= CovenantMath.HarvestGold && cdDrop > 0.05f, $"골드 +{gold} · E 쿨 -{cdDrop:0.00}초");
            case "arcflash":
            {
                int n = 0; foreach (var m in mobs) if (m != null && CovenantStatus.ShockStacks(m.gameObject) > 0) n++;
                return (n > 0, $"감전된 적 {n}");
            }
            case "stasis":   return (stunNow > 0, $"기절한 적 {stunNow}(걸린 순간)");
            case "bloodmark": return (shieldGain > 0, $"보호막 +{shieldGain}");
            case "aegis":    return (invNow, invNow ? "무적 걸림(걸린 순간)" : "무적 없음");
            case "lastbreath":
            {
                int charges = DeathSaveCharges(s);
                return (charges > 0, $"쓰러짐 막기 {charges}개");
            }
            case "goldrain": return (gold > 0, $"골드 +{gold}");
            case "fury":
            case "momentum":
            case "ward":
            {
                bool has = s.TryGetBuffView(out var item);
                return (has, has ? $"버프 「{item.Label}」" : "버프 없음");
            }
        }
        return (false, "모르는 결과");
    }

    // ── ② 이음 사슬 ─────────────────────────────────────
    private static async UniTask<(string got, int fires, long dmg, string note, List<string> lines)> ChainCase(
        GameRunSession run, PlayerController player, CovenantHandler handler, List<CovenantBase> list, CovenantContext ctx,
        string id, string evt, List<MonsterBase> mobs)
    {
        var lines = new List<string>();
        foreach (var m in mobs) ResetMob(m, floor: true);
        player.RuntimeStats.AbsorbWithShield(100000);
        // 자리: A(1 m) · B(2.4 m) · C(B 옆 1.4 m) — 「가장 가까운 적」이 차례로 A → B → C가 되게
        Vector3 p = player.transform.position;
        Vector3 fwd = player.transform.forward; fwd.y = 0f; if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward; fwd.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, fwd);
        Place(mobs[0], p + fwd * 1.0f);
        if (mobs.Count > 1) Place(mobs[1], p + fwd * 2.4f);
        if (mobs.Count > 2) Place(mobs[2], p + fwd * 3.4f + side * 1.0f);
        Physics.SyncTransforms();
        await UniTask.Delay(60, ignoreTimeScale: true);

        var s = Equip(list, ctx, player, id);
        if (s == null) return ("맺기 실패", 0, 0, id, lines);
        for (int i = 0; i < s.ResultCount; i++)
            lines.Add($"줄 {i}: {CovenantGrammar.NumericLine(s.CauseId, s.CauseTier, s.ResultId(i), s.ResultTier(i), s.ResultLink(i), i > 0 ? s.ResultId(i - 1) : null, i)}");
        var fired = new List<int>();
        s.ClauseFired += i => fired.Add(i);

        long dmg0 = run.RunStats.DamageOf(DamageSource.Covenant);
        int fires0 = run.RunStats.CovenantFires;
        string note = string.Empty;
        var a = mobs[0];
        var b = mobs.Count > 1 ? mobs[1] : null;
        var c = mobs.Count > 2 ? mobs[2] : null;

        switch (evt)
        {
            case "burn-kill":
                // A는 체력 1 · 하한 없음 → 화상 첫 틱이 죽인다(틱 처치가 「쓰러지면」을 부르는가). B도 체력 1 → 폭발이 죽인다(폭발 처치가 다음 절을 부르는가).
                SetHp(a, 1); a.HpFloorMin1 = false;
                SetHp(b, 1); b.HpFloorMin1 = false;
                break;
            case "bleed-kill":
                SetHp(a, 1); a.HpFloorMin1 = false;
                break;
            case "low-hp":
                SetHp(a, Mathf.Max(2, Mathf.RoundToInt(a.EffectiveMaxHp * 0.1f)));
                break;
        }

        Trigger(handler, s, s.CauseId, a, player);
        await UniTask.Delay(120, ignoreTimeScale: true);

        switch (evt)
        {
            case "burn-kill":
            case "bleed-kill":
                await UniTask.Delay(1400, ignoreTimeScale: true);
                note = $"A 쓰러짐 {(a == null || a.IsDead ? "O" : "X")}" + (evt == "burn-kill"
                    ? $" · B 쓰러짐 {(b == null || b.IsDead ? "O" : "X")} · C 불탐 {(c != null && c.TryGetComponent<MonsterBurnHandler>(out var cb) && cb.Remaining > 0f ? "O" : "X")}"
                    : string.Empty);
                break;
            case "kill-first":
                // 처치 통지 — MonsterBase가 플레이어 처치에 부르는 것과 같은 길(적은 실제로 죽이지 않는다)
                lines.Add("진단: " + DeathEarsOf(s, a));
                handler.OnKill(a.gameObject);
                note = "A 처치 통지";
                break;
            case "kill-other":
                handler.OnKill(b != null ? b.gameObject : a.gameObject);
                note = "창 안 처치 통지";
                break;
            case "wait":
                await UniTask.Delay(800, ignoreTimeScale: true);
                note = "0.8초 기다림";
                break;
            case "break-shield":
                await UniTask.Delay(100, ignoreTimeScale: true);
                int had = player.RuntimeStats.Shield;
                player.RuntimeStats.AbsorbWithShield(100000);
                note = $"보호막 {had} → 0";
                break;
            case "low-hp":
                note = $"A 체력 → {a.CurrentHp}";
                break;
        }
        await UniTask.Delay(SettleMs, ignoreTimeScale: true);

        long dmg = run.RunStats.DamageOf(DamageSource.Covenant) - dmg0;
        int fires = run.RunStats.CovenantFires - fires0;
        if (evt == "low-hp") note = $"A 체력 10% → {(a != null ? a.CurrentHp : 0)}(하한 1로 붙잡음)";
        return (string.Join(",", fired), fires, dmg, note, lines);
    }

    // ── 공통 ────────────────────────────────────────────
    /// <summary>서약 목록을 이 문장 하나로 — 복원 경로처럼 부작용(저장 · 퀘스트 · 토스트) 없이.</summary>
    private static CovenantSentence Equip(List<CovenantBase> list, CovenantContext ctx, PlayerController player, string id)
    {
        foreach (var c in list) c.Dispose();
        list.Clear();
        if (CovenantFactory.Create(id) is not CovenantSentence s) return null;
        s.Initialize(ctx);
        s.OnBoundToPlayer(player);
        list.Add(s);
        return s;
    }

    /// <summary>원인을 실제 훅 순서대로 건다(CovenantHandler가 받는 것과 같은 사건).</summary>
    private static void Trigger(CovenantHandler handler, CovenantSentence s, string cause, MonsterBase target, PlayerController player)
    {
        var go = target.gameObject;
        s_corpse.transform.position = target.transform.position + (target.transform.position - player.transform.position).normalized * 0.3f;
        switch (cause)
        {
            case "streak":    for (int i = 0; i < 3; i++) handler.OnAttackHit(go, 10f); break;
            case "aim":
                CovenantCauseTracker.CurrentHitRanged = true;
                try { for (int i = 0; i < 3; i++) handler.OnAttackHit(go, 10f); }
                finally { CovenantCauseTracker.CurrentHitRanged = false; }
                break;
            case "slaughter": for (int i = 0; i < 5; i++) handler.OnKill(s_corpse); break;   // 다섯째 처치 = 쓰러진 자리(살아 있는 적이 아님)
            case "swap":      s.OnWeaponSwap(null, null); handler.OnAttackHit(go, 10f); break;
            case "clear":     handler.OnRoomClear(); break;
            case "heartbeat": for (int i = 0; i < 8; i++) s.Tick(0.6f); break;   // 전투 게이트 + 4초 주기
            case "concerto":  handler.OnSkillUse(SkillType.E); break;
            case "hunt":      handler.OnKill(s_corpse); handler.OnAttackHit(go, 10f); break;
            case "opener":    handler.OnRoomEnter(); handler.OnAttackHit(go, 10f); break;
            case "besiege":   s.Tick(0.6f); break;                                  // 5 m 안 3마리
            case "march":
            {
                var tracker = typeof(CovenantSentence).GetField("_tracker", Inst).GetValue(s);
                var t = typeof(CovenantCauseTracker);
                t.GetField("_movePrimed", Inst).SetValue(tracker, true);
                t.GetField("_moveAccum", Inst).SetValue(tracker, 11.8f);
                t.GetField("_lastPos", Inst).SetValue(tracker, player.transform.position + new Vector3(0.5f, 0f, 0f));
                s.Tick(0.6f);
                break;
            }
        }
    }

    /// <summary>소모형 · 처형이 먹을 것을 미리 건다(서약 밖 — 이 피해는 「서약」으로 세지 않는다).</summary>
    private static void Prepare(string effect, PlayerController player, List<MonsterBase> mobs)
    {
        foreach (var m in mobs)
        {
            if (m == null || m.IsDead) continue;
            var go = m.gameObject;
            switch (effect)
            {
                case "detonate":
                case "harvest":
                    CovenantStatus.Apply(go, StatusCurrency.Burn, 20f, 6f, player.gameObject);
                    CovenantStatus.Amplify(go, StatusCurrency.Bleed, 20f, 6f, player.gameObject);
                    break;
                case "stasis":
                    CovenantStatus.Amplify(go, StatusCurrency.Shock, 0f, 0f, player.gameObject);
                    CovenantStatus.Amplify(go, StatusCurrency.Shock, 0f, 0f, player.gameObject);
                    break;
            }
        }
        if (effect == "execute" && mobs.Count > 0 && mobs[0] != null)
        {
            // 경계 원인(개선 · 선제)은 12 m 안 체력 비율이 가장 낮은 적을 처형한다 — 앞 경우들에 다친 다른 적이 고르지 않게 다 채운다(10-23 실측: 다른 적이 뽑혀 A가 남았다)
            foreach (var other in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (other != null && !other.IsDead && !mobs.Contains(other) && other.Grade != MonsterGrade.Boss) SetHp(other, other.EffectiveMaxHp);
            SetHp(mobs[0], Mathf.Max(2, Mathf.RoundToInt(mobs[0].EffectiveMaxHp * 0.1f)));   // 기준(15% 이상) 아래 — 경계 원인이면 가장 약한 적
        }
    }

    /// <summary>앞 경우의 흔적을 지운다 — 화상 · 출혈 · 감전 · 기절 · 저주 · 체력.</summary>
    private static void ResetMob(MonsterBase m, bool floor)
    {
        if (m == null || m.IsDead) return;
        m.HpFloorMin1 = floor;
        if (m.TryGetComponent<MonsterBurnHandler>(out var burn)) burn.CancelForPooling();
        if (m.Status != null)
        {
            m.Status.ConsumeDot("bleed", 1f);
            m.Status.ConsumeSlow(CovenantStatus.ShockId);
            m.Status.ClearCc("stun");
        }
        m.ApplyDamageTakenAmp(0f, 0.01f, "cov_curse");
        SetHp(m, m.EffectiveMaxHp);
    }

    private static void SetHp(MonsterBase m, int hp)
    {
        if (m == null) return;
        var rt = typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(m);
        rt?.GetType().GetField("CurrentHp")?.SetValue(rt, hp);
    }

    private static int DeathSaveCharges(CovenantSentence s)
    {
        var clauses = (List<CovenantClause>)typeof(CovenantSentence).GetField("_clauses", Inst).GetValue(s);
        int n = 0;
        foreach (var c in clauses) n += (int)typeof(CovenantClause).GetField("_deathSaveCharges", Inst).GetValue(c);
        return n;
    }

    /// <summary>가까운 적 count마리(보스 제외)를 플레이어 앞 1 · 2 · 3 m로 옮긴다 — 첫째가 늘 「가장 가까운 적」.</summary>
    private static List<MonsterBase> Gather(PlayerController player, int count)
    {
        var list = new List<MonsterBase>();
        foreach (var mb in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (mb != null && !mb.IsDead && mb.isActiveAndEnabled && mb.Grade != MonsterGrade.Boss) list.Add(mb);
        Vector3 p = player.transform.position;
        list.Sort((a, b) => (a.transform.position - p).sqrMagnitude.CompareTo((b.transform.position - p).sqrMagnitude));
        if (list.Count > count) list.RemoveRange(count, list.Count - count);
        Vector3 fwd = player.transform.forward; fwd.y = 0f; if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward; fwd.Normalize();
        for (int i = 0; i < list.Count; i++)
        {
            float ang = (i - 1) * 50f;
            Place(list[i], p + Quaternion.Euler(0f, ang, 0f) * fwd * (1.0f + i * 0.5f));
        }
        Physics.SyncTransforms();
        return list;
    }

    /// <summary>진단 — 같은 서약 줄의 3마리 밖에서 저주가 걸린 적(이름 · 플레이어와 거리).</summary>
    private static string CurseElsewhere(PlayerController player)
    {
        var sb = new StringBuilder(" · 진단: 저주 걸린 다른 적 ");
        int n = 0;
        foreach (var mb in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
        {
            if (mb == null || mb.IsDead || !mb.HasDamageTakenAmpSlot("cov_curse")) continue;
            sb.Append($"{mb.name}({Vector3.Distance(mb.transform.position, player.transform.position):0.0}m) ");
            n++;
        }
        return n > 0 ? sb.ToString() : sb.Append("없음").ToString();
    }

    /// <summary>진단 — 「쓰러지면」 귀가 달린 적 목록 · A가 그중에 있나 · A 기절 · 등급.</summary>
    private static string DeathEarsOf(CovenantSentence s, MonsterBase a)
    {
        var ears = typeof(CovenantSentence).GetField("_deathEars", Inst)?.GetValue(s) as System.Collections.IList;
        var sb = new StringBuilder($"귀 {ears?.Count ?? -1}개 [");
        bool hasA = false;
        if (ears != null)
            foreach (var e in ears)
            {
                var mb = e.GetType().GetField("mb")?.GetValue(e) as MonsterBase;
                int next = (int)(e.GetType().GetField("next")?.GetValue(e) ?? -1);
                if (mb == a) hasA = true;
                sb.Append($"{(mb != null ? mb.name : "null")}→{next} ");
            }
        sb.Append($"] · A={a.name} 들어 있음 {hasA} · A 기절 {(a.Status != null && a.Status.HasCc("stun"))} · A 등급 {a.Grade}");
        return sb.ToString();
    }

    private static void Place(MonsterBase mb, Vector3 to)
    {
        if (mb == null) return;
        if (mb.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var agent) && agent.isOnNavMesh) agent.Warp(to);
        else mb.transform.position = to;
    }
}
#endif
