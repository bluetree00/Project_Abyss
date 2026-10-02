using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// [실측 도구 · 플레이 중 · 전투방에서] 룬 속성 시너지 6속성 × 4단계가 <b>실제로 돌고 · 판정이 서고 · 이펙트가 제 리소스로 나오는지</b> 잰다(약 30초).
///
/// <para><b>재는 법</b> — 단계 효과는 저장소 표(Docs/MERLIN_RUNE_SYNERGY_DATA.csv)로 켠다. 타격은 피해 보고를
/// 아이템 효과 관리자(<see cref="ItemEffectManager.OnPostDealDamage"/>)에 넣는다 — 실제 공격이 룬 효과로 들어오는 바로 그 입구다.
/// 보고 자체는 체력을 깎지 않으므로 줄어든 체력은 전부 시너지가 준 피해다.</para>
/// <para><b>판정</b> — 적 넷을 자리에 세운다: A(앞 3m · 맞는 적) · B(A 곁 2m) · E(플레이어 뒤 4m) · C(멀리 11m↑).
/// 반경 · 원뿔 · 체인마다 「맞아야 하는 적 / 맞으면 안 되는 적」을 가른다. 적은 매 프레임 제자리로 되돌린다.</para>
/// <para><b>이펙트</b> — 속성 이펙트 재생기(<see cref="ElementVfxPlayer"/>)의 활성 목록을 읽어 프리팹 이름 · 크기 · 살아 있는 입자 수 ·
/// 재질/셰이더 이상(분홍) 여부를 적고, 순간마다 임시 카메라로 찍는다(Temp/syn_shots).</para>
/// 결과: Temp/rune_synergy_full_probe.txt
/// </summary>
public static class RuneSynergyFullProbeEditor
{
    private const string CsvPath = "Assets/RelicFairy/Docs/MERLIN_RUNE_SYNERGY_DATA.csv";
    private const string ShotDir = "Temp/syn_shots";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags Stat = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private const int BigHp = 1000000;

    private static readonly List<(float at, Action act, string name)> s_steps = new();
    private static readonly Dictionary<MonsterBase, Vector3> s_pin = new();
    private static readonly Dictionary<MonsterBase, long> s_hp = new();
    private static readonly List<BuffViewItem> s_views = new();
    private static StringBuilder s_sb;
    private static int s_next, s_pass, s_fail, s_boom;
    private static float s_t0;
    private static bool s_started, s_guideWas;
    private static double s_loadDeadline;
    private static readonly List<(Func<bool> cond, Action act, string name)> s_watch = new();
    private static readonly List<(float at, Action act, string name)> s_later = new();

    private static PlayerController s_player;
    private static RuneEffectDispatcher s_fx;
    private static PlayerRuntimeStats s_stats;
    private static ItemEffectManager s_mgr;
    private static Dictionary<string, RuneSynergyEntry> s_table;
    private static List<MonsterBase> s_all;
    private static MonsterBase s_A, s_B, s_E, s_C;
    private static Vector3 s_P, s_D;
    private static int s_atk => Atk();
    private static List<RuneSynergyEntry> s_restore;
    private static Dictionary<string, float> s_restoreZoneAmp;
    private static float s_restoreAmp;

    [MenuItem("RelicFairy/Debug/룬 시너지 실측 — 여섯 속성 판정·이펙트 (전투방에서)")]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[RuneSynergyFull] 플레이 모드에서만"); return; }
        var run = GameRunBootstrapper.Instance?.Run;
        s_player = run?.Player;
        s_fx = s_player != null ? s_player.RuneEffects : null;
        s_stats = s_player != null ? s_player.RuntimeStats : null;
        s_mgr = run?.EffectManager;
        if (s_fx == null || s_stats == null || s_mgr == null) { Debug.LogWarning("[RuneSynergyFull] 런이 없다"); return; }

        s_sb = new StringBuilder();
        s_pass = s_fail = 0;
        s_steps.Clear(); s_pin.Clear(); s_watch.Clear(); s_later.Clear(); s_next = 0; s_started = false;
        s_table = LoadCsv();
        Directory.CreateDirectory(ShotDir);

        typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(s_player, true);
        s_all = new List<MonsterBase>();
        foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (!m.IsDead) { m.HpFloorMin1 = true; s_all.Add(m); }
        s_P = s_player.transform.position;
        s_all.Sort((a, b) => (a.transform.position - s_P).sqrMagnitude.CompareTo((b.transform.position - s_P).sqrMagnitude));
        if (s_all.Count < 4)
        {
            s_sb.AppendLine($"적이 {s_all.Count}명 — 넷은 있어야 판정(안/밖)을 가른다. 전투방에서 다시.");
            WriteOut();
            return;
        }
        if (!Layout()) { s_sb.AppendLine("적을 세울 자리를 찾지 못했다(길 위 자리 부족)."); WriteOut(); return; }

        var sourceField = typeof(RuneEffectDispatcher).GetField("_sourceEntries", Inst);
        var zoneAmpField = typeof(RuneEffectDispatcher).GetField("_zoneAmp", Inst);
        s_restore = new List<RuneSynergyEntry>(((Dictionary<string, RuneSynergyEntry>)sourceField.GetValue(s_fx)).Values);
        s_restoreZoneAmp = new Dictionary<string, float>((Dictionary<string, float>)zoneAmpField.GetValue(s_fx));
        s_restoreAmp = s_fx.Amplifier;

        s_sb.AppendLine($"적 {s_all.Count} · 유효 공격력 {s_atk} · 공명 배수 {s_fx.Amplifier:0.##} · 자리: A 앞 3m · B A곁 2m · E 뒤 4m · C 멀리 {Vector3.Distance(s_pin[s_C], s_P):0.#}m");
        // 이펙트 재생기는 처음 불릴 때 만들어지고 목록(ElementVfxRegistry)을 비동기로 읽는다 — 그 전에 부른 이펙트는 버려진다.
        bool loadedAtStart = Registry() != null;
        s_sb.AppendLine($"이펙트 목록: 시작할 때 {(loadedAtStart ? "이미 읽혀 있음" : "아직 안 읽힘 — 이 상태에서 첫 이펙트를 부르면 안 나온다")}");
        ElementVfxPlayer.Warmup();
        s_guideWas = GuidelineVisual.Enabled;
        GuidelineVisual.Enabled = false;   // 개발용 원반 · 구체를 끈다 — 플레이어가 보는 것만 찍는다

        float t = 0.2f;
        t = Fire(t);
        t = Ice(t);
        t = Electric(t);
        t = Grass(t);
        t = Light(t);
        t = Dark(t);
        At(t, Restore, "되돌리기");

        s_steps.Sort((a, b) => a.at.CompareTo(b.at));
        s_loadDeadline = EditorApplication.timeSinceStartup + 10.0;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Debug.Log($"[RuneSynergyFull] 시작 — 약 {t:0}초");
    }

    // ── 진행 ─────────────────────────────────────────────────────
    private static void At(float t, Action act, string name) => s_steps.Add((t, act, name));

    /// <summary>조건이 참이 되는 첫 프레임에 한 번 실행(만료 순간처럼 정확한 시각을 모를 때).</summary>
    private static void When(Func<bool> cond, Action act, string name) => s_watch.Add((cond, act, name));

    /// <summary>지금부터 seconds 뒤에 실행.</summary>
    private static void AtLater(float seconds, Action act, string name) => s_later.Add((Time.time + seconds, act, name));

    private static void Guard(Action act, string name)
    {
        try { act(); }
        catch (Exception e) { s_fail++; s_sb.AppendLine($"✗ [{name}] 예외: {e.GetType().Name} {e.Message}"); Debug.LogException(e); }
    }

    private static void Tick()
    {
        if (!Application.isPlaying || s_player == null) { EditorApplication.update -= Tick; return; }
        Pin();
        if (!s_started)
        {
            if (Registry() == null && EditorApplication.timeSinceStartup < s_loadDeadline) return;   // 목록이 읽힐 때까지
            s_started = true;
            s_t0 = Time.time;
            s_sb.AppendLine($"이펙트 목록: {(Registry() != null ? "읽힘 — 실측 시작" : "✗ 10초 안에 안 읽힘(ElementVfxRegistry)")}");
        }
        float now = Time.time - s_t0;
        while (s_next < s_steps.Count && now >= s_steps[s_next].at)
        {
            var st = s_steps[s_next++];
            Guard(st.act, st.name);
        }
        for (int i = s_watch.Count - 1; i >= 0; i--)
        {
            var w = s_watch[i];
            bool hit = false;
            try { hit = w.cond(); } catch (Exception) { hit = false; }
            if (!hit) continue;
            s_watch.RemoveAt(i);
            Guard(w.act, w.name);
        }
        for (int i = s_later.Count - 1; i >= 0; i--)
        {
            var l = s_later[i];
            if (Time.time < l.at) continue;
            s_later.RemoveAt(i);
            Guard(l.act, l.name);
        }
        if (s_next >= s_steps.Count && s_later.Count == 0)
        {
            EditorApplication.update -= Tick;
            GuidelineVisual.Enabled = s_guideWas;
            WriteOut();
        }
    }

    private static void WriteOut()
    {
        s_sb.Insert(0, $"룬 시너지 6속성 실측 — 통과 {s_pass} · 실패 {s_fail}\n");
        File.WriteAllText(Path.Combine("Temp", "rune_synergy_full_probe.txt"), s_sb.ToString());
        Debug.Log("[RuneSynergyFull] 완료 — Temp/rune_synergy_full_probe.txt\n" + s_sb);
    }

    /// <summary>유효 공격력 — 유물(가웨인 태양 주기) 때문에 시간에 따라 움직이므로 잴 때마다 읽는다.</summary>
    private static int Atk()
    {
        var wd = s_player.WeaponManager?.CurrentWeaponData;
        return s_stats.GetEffectiveAttack((wd != null ? wd.weaponType : WeaponType.None).GetAttackStatKind());
    }

    private static void Check(bool ok, string line)
    {
        if (ok) s_pass++; else s_fail++;
        s_sb.AppendLine((ok ? "✓ " : "✗ ") + line);
    }

    private static void Head(string title) => s_sb.AppendLine($"\n── {title} ──");

    // ── 불 ───────────────────────────────────────────────────────
    private static float Fire(float t)
    {
        var ember = Row("FireEmber"); var ignite = Row("FireIgnite"); var blaze = Row("FireBlaze"); var scorch = Row("FireScorch");
        At(t, () =>
        {
            Head("불 — 잔불 · 점화 · 폭염 · 작열");
            Begin(ember, ignite, blaze, scorch);
            int period = Mathf.Max(1, (int)ignite.value3);
            Snap();
            for (int i = 0; i < period; i++) Hit(s_A, 2f);
            long emberDealt = Delta(s_A);
            int emberExpect = period * Syn(ember.value * s_atk) + Syn(2f * blaze.value);   // 점화를 건 그 적중은 폭염도 받는다
            Check(emberDealt == emberExpect, $"잔불: 적중 {period}번에 {emberDealt} (기대 {period} × {Syn(ember.value * s_atk)} + 점화 건 적중의 폭염 {Syn(2f * blaze.value)})");
            Check(s_A.Status.HasDot("ignite") && !s_B.Status.HasDot("ignite"), $"점화: {period}번째 적중에 A 점화 {Yes(s_A.Status.HasDot("ignite"))} · B는 {Yes(s_B.Status.HasDot("ignite"))}");

            Snap();
            Hit(s_A, 20f);
            Hit(s_B, 20f);
            long a = Delta(s_A), b = Delta(s_B);
            int expectA = Syn(ember.value * s_atk) + Syn(20f * blaze.value);
            Check(a == expectA && b == Syn(ember.value * s_atk),
                  $"폭염(점화된 적만): 피해 20 적중 — 점화된 A {a} (기대 잔불 {Syn(ember.value * s_atk)} + 폭염 {Syn(20f * blaze.value)}) · 점화 안 된 B {b} (기대 잔불만 {Syn(ember.value * s_atk)})");
            Snap();
            s_boom = s_atk;   // 폭발 피해는 점화를 건 순간의 공격력으로 정해진다
            When(() => !s_A.Status.HasDot("ignite"), () => AtLater(0.1f, () =>
            {
                Vfx("작열 폭발(직후)", Area(RuneElement.Fire), scale: AreaScale(RuneElement.Fire, 4f), radius: 4f);
                Shot("fire_2_scorch_a", s_A.transform.position);
            }, "작열 직후"), "작열 감시");
        }, "불 시작");
        At(t + 0.45f, () =>
        {
            Vfx("점화 몸 이펙트", Body(RuneElement.Fire), follow: s_A);
            Shot("fire_1_ignite", s_A.transform.position);
        }, "불 점화 이펙트");
        float expire = ignite.duration > 0f ? ignite.duration : 3f;
        At(t + expire + 0.45f, () =>
        {
            int boom = Syn(scorch.value * s_boom);
            long b = Delta(s_B), e = Delta(s_E), c = Delta(s_C), a = Delta(s_A);
            Check(b == boom && e == 0 && c == 0,
                  $"작열(점화 만료 · 반경 4m): 안쪽 B {b} (기대 {boom}) · 밖 E {e} · 밖 C {c} · A {a} (폭발 {boom} + 점화 틱)");
            Check(!s_A.Status.HasDot("ignite"), $"점화 만료 {Yes(!s_A.Status.HasDot("ignite"))}");
            Shot("fire_2_scorch_b", s_A.transform.position);
        }, "불 작열");
        return t + expire + 1.0f;
    }

    // ── 얼음 ─────────────────────────────────────────────────────
    private static float Ice(float t)
    {
        var frost = Row("IceFrost"); var freeze = Row("IceFreeze"); var shatter = Row("IceShatter"); var glacier = Row("IceGlacier");
        At(t, () =>
        {
            Head("얼음 — 서리 · 빙결 · 분쇄 · 빙하");
            Begin(frost, freeze, shatter, glacier);
            int need = Mathf.Max(1, (int)freeze.value2);
            for (int i = 0; i < need; i++) Hit(s_A, 2f);
            Check(s_A.Status.GetSlowStacks("frost") == need && s_B.Status.GetSlowStacks("frost") == 0,
                  $"서리: A {s_A.Status.GetSlowStacks("frost")}중첩 (기대 {need}) · 이동 배율 {s_A.Status.MoveSpeedMultiplier:0.##} · B {s_B.Status.GetSlowStacks("frost")}중첩");

            Hit(s_A, 20f);
            Check(!s_A.Status.HasCc("freeze"), $"빙결(스킬 적중만): 스킬 없이 친 적중엔 빙결 {Yes(s_A.Status.HasCc("freeze"))} (기대 아니오)");
            s_fx.NotifySkillUsed();
            Snap();
            Hit(s_A, 20f);
            long first = Delta(s_A);
            Check(s_A.Status.HasCc("freeze"), $"빙결: 스킬 직후 적중에 A 빙결 {Yes(s_A.Status.HasCc("freeze"))}");
            Snap();
            Hit(s_A, 20f);
            Hit(s_B, 20f);
            Check(Delta(s_A) == Syn(20f * shatter.value) && Delta(s_B) == 0,
                  $"분쇄(빙결된 적만): 피해 20 적중 — 빙결된 A {Delta(s_A)} (기대 {Syn(20f * shatter.value)}) · B {Delta(s_B)} (기대 0) · 빙결 건 그 적중 {first}");
            Snap();
            s_boom = s_atk;   // 빙하 피해는 빙결을 건 순간의 공격력으로 정해진다
            When(() => !s_A.Status.HasCc("freeze"), () => AtLater(0.1f, () =>
            {
                Vfx("빙하 폭발(직후)", Area(RuneElement.Ice), scale: AreaScale(RuneElement.Ice, 4f), radius: 4f);
                Shot("ice_2_glacier_a", s_A.transform.position);
            }, "빙하 직후"), "빙하 감시");
        }, "얼음 시작");
        At(t + 0.45f, () =>
        {
            Vfx("빙결 몸 이펙트", Body(RuneElement.Ice), follow: s_A);
            Shot("ice_1_freeze", s_A.transform.position);
        }, "얼음 빙결 이펙트");
        float dur = freeze.value > 0f ? freeze.value : 1.5f;
        At(t + dur + 0.45f, () =>
        {
            int boom = Syn(glacier.value * s_boom);
            long b = Delta(s_B), e = Delta(s_E), c = Delta(s_C), a = Delta(s_A);
            Check(b == boom && e == 0 && c == 0,
                  $"빙하(빙결 해제 · 반경 4m): 안쪽 B {b} (기대 {boom}) · 밖 E {e} · 밖 C {c} · A {a} (분쇄 +{shatter.value2 * 100f:0}% 걸린 뒤라 {Syn(glacier.value * s_boom, shatter.value2)})");
            Check(Mathf.Abs(s_A.DamageTakenAmpTotal - shatter.value2) < 0.001f, $"분쇄(해제 뒤): A 받는 피해 +{s_A.DamageTakenAmpTotal * 100f:0.#}% (기대 +{shatter.value2 * 100f:0.#}%) · 남은 {s_A.DamageTakenAmpRemaining:0.0}초");
            Check(s_B.Status.GetSlowStacks("frost") >= 1, $"빙하: 맞은 B에 서리 {s_B.Status.GetSlowStacks("frost")}중첩");
            Shot("ice_2_glacier_b", s_A.transform.position);
        }, "얼음 빙하");
        return t + dur + 1.0f;
    }

    // ── 전기 ─────────────────────────────────────────────────────
    private static float Electric(float t)
    {
        var stat = Row("ElecStatic"); var dis = Row("ElecDischarge"); var shock = Row("ElecShock"); var over = Row("ElecOverload");
        At(t, () =>
        {
            Head("전기 — 정전기 · 방전 · 감전 · 과부하");
            Begin(stat, dis, shock);
            for (int i = 0; i < stat.max_stack; i++) Hit(s_A, 2f);
            s_fx.Tick(0f);
            Check(s_fx.Resources.GetStack("ElecStatic") == stat.max_stack && Near(Layer("_synergyDynAttackSpeed"), stat.value + stat.max_stack * stat.value2),
                  $"정전기: 적중 {stat.max_stack}번 → {s_fx.Resources.GetStack("ElecStatic")}중첩 · 공속 층 +{Layer("_synergyDynAttackSpeed") * 100f:0.#}% (기대 +{(stat.value + stat.max_stack * stat.value2) * 100f:0.#}%)");

            Snap();
            int beams0 = Beams();
            s_fx.NotifySkillUsed();
            int dmg = Syn(stat.max_stack * dis.value * s_atk);
            Check(Delta(s_A) == dmg && Delta(s_B) == 0 && Delta(s_E) == 0,
                  $"방전(가장 가까운 적 하나): A {Delta(s_A)} (기대 {stat.max_stack}중첩 × {dis.value * 100f:0}% × 공격력 = {dmg}) · B {Delta(s_B)} · E {Delta(s_E)}");
            Check(Beams() > beams0, $"방전 번개 줄기 {Beams() - beams0}개");
            Check(s_A.Status.HasCc("stun") && Near(s_A.DamageTakenAmpTotal, shock.value3) && !s_B.Status.HasCc("stun"),
                  $"감전: A 기절 {Yes(s_A.Status.HasCc("stun"))} · 받는 피해 +{s_A.DamageTakenAmpTotal * 100f:0.#}% (기대 +{shock.value3 * 100f:0.#}%) · B 기절 {Yes(s_B.Status.HasCc("stun"))}");
            s_views.Clear(); s_A.CollectStatuses(s_views);
            var labels = new List<string>();
            foreach (var v in s_views) labels.Add(v.Label);
            Check(labels.Contains("감전 취약"), $"감전: 디버프 줄 「{string.Join(" · ", labels)}」");
            s_fx.Tick(0f);
            Check(Near(Layer("_synergyDynAttackSpeed"), stat.value), $"방전 뒤 공속 층 +{Layer("_synergyDynAttackSpeed") * 100f:0.#}% (기대 기본 +{stat.value * 100f:0.#}%)");
        }, "전기 시작");
        At(t + 0.3f, () =>
        {
            Vfx("방전 낙뢰", Area(RuneElement.Electric), scale: AreaScale(RuneElement.Electric, 2f), radius: 2f);
            Vfx("기절 몸 이펙트", Body(RuneElement.Electric), follow: s_A);
            Shot("elec_1_discharge", s_A.transform.position);
        }, "전기 방전 이펙트");
        At(t + 0.6f, () =>
        {
            s_fx.Activate(over);
            ClearAmp(s_all);
            Snap();
            int beams0 = Beams();
            Hit(s_A, 20f);
            int each = Syn(20f * over.value);
            Check(Delta(s_B) == each && Delta(s_E) == 0 && Delta(s_C) == 0 && Delta(s_A) == 0,
                  $"과부하(맞은 적 곁 5m): 곁의 B {Delta(s_B)} (기대 {each}) · 7m 떨어진 E {Delta(s_E)} · C {Delta(s_C)} · 맞은 A 추가 {Delta(s_A)}");
            Check(Beams() > beams0, $"과부하 번개 줄기 {Beams() - beams0}개");
        }, "전기 과부하");
        At(t + 0.72f, () =>
        {
            Vfx("과부하 맞은 자리", Impact(RuneElement.Electric));
            Shot("elec_2_overload", s_A.transform.position);
        }, "전기 과부하 이펙트");
        return t + 1.4f;
    }

    // ── 풀 ───────────────────────────────────────────────────────
    private static float Grass(float t)
    {
        var mist = Row("GrassMist"); var plus = Row("GrassMistPlus"); var insight = Row("GrassMistInsight"); var dom = Row("GrassMistDominion");
        float radius = 2.5f * (1f + plus.value + insight.value);
        int shield0 = 0;
        At(t, () =>
        {
            Head("풀 — 독안개 · 강화 · 간파 · 지배");
            Begin(mist, plus, insight, dom);
            shield0 = s_stats.Shield;
            Hit(s_A, 2f);
            Check(PoisonFields() == 1, $"독안개: A 자리에 장판 {PoisonFields()}개 · 반경 {radius:0.#}m (기본 2.5 × {1f + plus.value + insight.value:0.#})");
        }, "풀 시작");
        At(t + 0.7f, () =>
        {
            Hit(s_B, 2f);   // 장판 재생성 대기 0.5초 뒤 — 두 번째 장판(겹침)
            Check(PoisonFields() == 2, $"독안개: 0.5초 뒤 B 자리에 둘째 장판 → {PoisonFields()}개");
        }, "풀 둘째 장판");
        At(t + 1.4f, () =>
        {
            bool a = s_A.Status.HasDot("poison"), b = s_B.Status.HasDot("poison"), e = s_E.Status.HasDot("poison"), c = s_C.Status.HasDot("poison");
            Check(a && b && !c, $"독(장판 안만): A {Yes(a)} · B {Yes(b)} · 밖 C {Yes(c)} · E(장판에서 {Vector3.Distance(s_pin[s_E], s_pin[s_B]):0.#}m) {Yes(e)}");
            int tick = Syn(mist.value * s_atk * dom.value * (1f + plus.value2));
            s_sb.AppendLine($"  독 틱당 {mist.value * 100f:0}% × 지배 ×{dom.value:0.#} × 겹침 +{plus.value2 * 100f:0}% = {mist.value * s_atk * dom.value * (1f + plus.value2):0.##} → 표시 피해 {tick}");
            Check(s_A.Status.AttackSpeedMultiplier < 0.999f, $"겹침(지배 = 장판끼리 닿으면 전체 겹침): A 공격 배율 {s_A.Status.AttackSpeedMultiplier:0.##} (1 미만이어야)");
            int gained = s_stats.Shield - shield0;
            Check(gained > 0, $"보호막(안개 안에 선 동안 초당 {dom.value2 * 100f:0.##}%): +{gained} (최대 체력 {s_stats.MaxHp} · 상한 {s_stats.ShieldCap})");
            s_sb.AppendLine("  · 보스 받는 피해 +15%는 일반 전투방이라 재지 못했다(보스에게만 걸림)");
            Vfx("독안개 장판", Aura(RuneElement.Grass), scale: AuraScale(RuneElement.Grass, radius), radius: radius);
            Vfx("중독 몸 이펙트", Body(RuneElement.Grass), follow: s_A);
            Shot("grass_1_mist", s_A.transform.position);
        }, "풀 확인");
        return t + 2.0f;
    }

    // ── 빛 ───────────────────────────────────────────────────────
    private static float Light(float t)
    {
        var rad = Row("LightRadiance"); var burst = Row("LightBurst"); var sanc = Row("LightSanctuary"); var field = Row("LightField");
        int need = burst.value2 > 0f ? (int)burst.value2 : 10;
        At(t, () =>
        {
            Head("빛 — 광채 · 광폭발 · 성역 · 빛의 장판");
            Begin(rad, burst, sanc, field);
            Hit(s_A, 2f);
            Check(s_fx.Resources.GetStack("LightRadiance") == 0, $"광채(치명타만): 보통 적중엔 {s_fx.Resources.GetStack("LightRadiance")}중첩 (기대 0)");
            for (int i = 0; i < need - 1; i++) Hit(s_A, 2f, crit: true);
            s_fx.Tick(0f);
            Check(s_fx.Resources.GetStack("LightRadiance") == need - 1 && Near(Layer("_synergyDynCritChance"), (need - 1) * rad.value),
                  $"광채: 치명타 {need - 1}번 → {s_fx.Resources.GetStack("LightRadiance")}중첩 · 치명타 확률 층 +{Layer("_synergyDynCritChance") * 100f:0.#}%p (기대 +{(need - 1) * rad.value * 100f:0.#})");

            FacePlayer();
            Snap();
            Hit(s_A, 2f, crit: true);   // 임계 → 광폭발
            int boom = Syn(burst.value * s_atk);
            Check(Delta(s_A) == boom && Delta(s_B) == boom && Delta(s_E) == 0 && Delta(s_C) == 0,
                  $"광폭발(앞 원뿔 7m · 좌우 50°): 앞 A {Delta(s_A)} · 앞 B {Delta(s_B)} (기대 {boom}) · 뒤 E {Delta(s_E)} · 멀리 C {Delta(s_C)}");
            Check(s_fx.Resources.GetStack("LightRadiance") == 0, $"광폭발 뒤 광채 {s_fx.Resources.GetStack("LightRadiance")}중첩 (기대 0)");
        }, "빛 시작");
        At(t + 0.35f, () =>
        {
            s_fx.Tick(0f);
            Check(Near(Layer("_synergyDynCritChance"), field.value) && Near(Layer("_synergyDynCritDamage"), field.value2),
                  $"빛의 장판(장판 위): 치명타 확률 층 +{Layer("_synergyDynCritChance") * 100f:0.#}%p (기대 +{field.value * 100f:0.#}) · 치명타 피해 층 +{Layer("_synergyDynCritDamage") * 100f:0.#}% (기대 +{field.value2 * 100f:0.#})");
            Vfx("광폭발", Area(RuneElement.Light), scale: AreaScale(RuneElement.Light, 3.5f), radius: 3.5f);
            Vfx("빛 장판", Aura(RuneElement.Light), scale: AuraScale(RuneElement.Light, 3f), radius: 3f);
            Shot("light_1_burst", s_P + s_D * 1.5f);
            Hit(s_A, 2f, crit: true);
            int gain = sanc.value2 > 0f ? (int)sanc.value2 : 3;
            Check(s_fx.Resources.GetStack("LightRadiance") == gain, $"성역(장판 위 치명타): 광채 +{s_fx.Resources.GetStack("LightRadiance")} (기대 +{gain})");
        }, "빛 장판");
        float life = sanc.duration > 0f ? sanc.duration : 3f;
        At(t + life + 0.5f, () =>
        {
            s_fx.Tick(0f);
            Check(Near(Layer("_synergyDynCritDamage"), 0f), $"장판 만료({life:0.#}초) 뒤 치명타 피해 층 +{Layer("_synergyDynCritDamage") * 100f:0.#}% (기대 0)");
        }, "빛 만료");
        return t + life + 1.0f;
    }

    // ── 어둠 ─────────────────────────────────────────────────────
    private static float Dark(float t)
    {
        var ero = Row("DarkErosion"); var rel = Row("DarkRelease"); var after = Row("DarkAfterimage"); var abyss = Row("DarkAbyss");
        float dur = rel.duration + abyss.value3;
        At(t, () =>
        {
            Head("어둠 — 잠식 · 암흑 해방 · 그림자 잔상 · 심연 각성");
            Begin(ero, rel, after, abyss);
            for (int i = 0; i < 3; i++) s_fx.NotifyDamaged(5f, s_A.gameObject);
            int g1 = s_fx.Resources.GetGauge("darkGauge");
            for (int i = 0; i < 10; i++) Hit(s_A, 2f);
            int g2 = s_fx.Resources.GetGauge("darkGauge");
            s_fx.Tick(0f);
            Check(g1 == 3 * (int)ero.value2 && g2 == g1 + 10 * (int)ero.value3 && Near(Layer("_synergyDynAttackPct"), g2 / (float)ero.max_stack * ero.value),
                  $"잠식: 피격 3번 → 게이지 {g1} (기대 {3 * (int)ero.value2}) · 적중 10번 → {g2} (기대 +{10 * (int)ero.value3}) · 공격력 층 +{Layer("_synergyDynAttackPct") * 100f:0.##}% (기대 +{g2 / (float)ero.max_stack * ero.value * 100f:0.##})");

            ClearAmp(s_all);
            int left = Mathf.CeilToInt((ero.max_stack - g2) / ero.value2);
            for (int i = 0; i < left; i++) s_fx.NotifyDamaged(5f, s_A.gameObject);   // 게이지 가득 → 암흑 해방
            s_fx.Tick(0f); s_fx.Tick(0f);
            float until = (float)typeof(DarkReleaseEffect).GetField("_releaseUntil", Inst).GetValue(s_fx.GetActive("DarkRelease"));
            Check(Near(until - Time.time, dur, 0.1f) && s_fx.Resources.GetGauge("darkGauge") == 0,
                  $"암흑 해방: 게이지 가득 → {until - Time.time:0.0}초 (기대 {rel.duration:0.#} + 각성 {abyss.value3:0.#}) · 게이지 {s_fx.Resources.GetGauge("darkGauge")}");
            Check(Near(Layer("_synergyDynAttackPct"), rel.value + abyss.value) && Near(Layer("_synergyDynDamageReduction"), rel.value2),
                  $"해방 중: 공격력 층 +{Layer("_synergyDynAttackPct") * 100f:0.#}% (기대 해방 {rel.value * 100f:0.#} + 각성 {abyss.value * 100f:0.#}) · 받는 피해 −{Layer("_synergyDynDamageReduction") * 100f:0.#}%");
            Check(Near(s_A.DamageTakenAmpTotal, abyss.value2) && Near(s_B.DamageTakenAmpTotal, abyss.value2) && Near(s_E.DamageTakenAmpTotal, abyss.value2) && Near(s_C.DamageTakenAmpTotal, 0f),
                  $"심연 각성(플레이어 곁 6m): A +{s_A.DamageTakenAmpTotal * 100f:0.#}% · B +{s_B.DamageTakenAmpTotal * 100f:0.#}% · 뒤 E +{s_E.DamageTakenAmpTotal * 100f:0.#}% (기대 +{abyss.value2 * 100f:0.#}) · 멀리 C +{s_C.DamageTakenAmpTotal * 100f:0.#}% (기대 0)");
            Hit(s_A, 20f);   // 그림자 잔상 예약
            Snap();
        }, "어둠 시작");
        At(t + 0.3f, () =>
        {
            Vfx("암흑 해방", Impact(RuneElement.Dark));
            Shot("dark_1_release", s_P + s_D * 1.5f);
        }, "어둠 이펙트");
        float delay = after.duration > 0f ? after.duration : 0.5f;
        At(t + delay + 0.35f, () =>
        {
            int echo = Syn(20f * after.value, abyss.value2);
            Check(Delta(s_A) == echo && Delta(s_B) == 0,
                  $"그림자 잔상(해방 중 적중의 {after.value * 100f:0}% · {delay:0.#}초 뒤): A {Delta(s_A)} (기대 {20f * after.value:0.#} × 받는 피해 +{abyss.value2 * 100f:0}% = {echo}) · B {Delta(s_B)}");
        }, "어둠 잔상");
        At(t + dur + 0.5f, () =>
        {
            s_fx.Tick(0f); s_fx.Tick(0f);
            Check(s_fx.Resources.GetRegister("darkReleaseActive") == 0 && Layer("_synergyDynAttackPct") < 0.02f && Near(s_A.DamageTakenAmpTotal, 0f),
                  $"해방 끝({dur:0.#}초): 공격력 층 +{Layer("_synergyDynAttackPct") * 100f:0.##}% · 받는 피해 −{Layer("_synergyDynDamageReduction") * 100f:0.#}% · A 받는 피해 +{s_A.DamageTakenAmpTotal * 100f:0.#}%");
        }, "어둠 끝");
        return t + dur + 1.0f;
    }

    private static void Restore()
    {
        s_fx.Clear();
        GroundFieldsClear();
        foreach (var e in s_restore) s_fx.Activate(e);
        if (!Mathf.Approximately(s_restoreAmp, 1f)) s_fx.SetAmplifier(s_restoreAmp);
        if (s_restoreZoneAmp.Count > 0) s_fx.SetZoneAmplifiers(s_restoreZoneAmp);
        s_fx.Tick(0f);
        var rt = typeof(MonsterBase).GetField("_runtime", Inst);
        foreach (var m in s_all)
        {
            if (m == null) continue;
            if (rt?.GetValue(m) is MonsterRuntimeData data) data.CurrentHp = m.EffectiveMaxHp;
            m.Status.Reset();
            m.HpFloorMin1 = false;
        }
        ClearAmp(s_all);
        s_pin.Clear();
        s_sb.AppendLine($"\n되돌림 — 켜져 있던 시너지 {s_restore.Count}단계 다시 켬 · 적 체력 · 상태 원래대로");
    }

    // ── 공통: 단계 켜기 · 타격 · 체력 ─────────────────────────────
    private static RuneSynergyEntry Row(string type) => s_table[type];

    private static void Begin(params RuneSynergyEntry[] rows)
    {
        s_fx.Clear();
        GroundFieldsClear();
        var rt = typeof(MonsterBase).GetField("_runtime", Inst);
        foreach (var m in s_all)
        {
            if (rt?.GetValue(m) is MonsterRuntimeData data) data.CurrentHp = BigHp;   // 몹 체력이 10 안팎이라 바닥에 걸린다 → 잴 동안만 크게
            m.Status.Reset();
        }
        ClearAmp(s_all);
        foreach (var r in rows) s_fx.Activate(r);
        if (s_stats.Shield > 0) s_stats.AbsorbWithShield(s_stats.Shield);
    }

    private static void Hit(MonsterBase target, float damage, bool crit = false)
        => s_mgr.OnPostDealDamage(new DamageReport
        {
            DamageDealt = damage, Target = target.gameObject, Attacker = s_player.gameObject, IsCrit = crit,
            HitPosition = target.transform.position, ActionType = WeaponActionType.GroundLight,
        });

    private static void Snap()
    {
        s_hp.Clear();
        foreach (var m in s_all) s_hp[m] = m.CurrentHp;
    }

    private static long Delta(MonsterBase m) => s_hp.TryGetValue(m, out var h) ? h - m.CurrentHp : 0;

    /// <summary>시너지 피해가 체력에서 깎는 양 — 최소 1, 소수점 버림(MonsterBase.TakeSynergyDamage와 같다). amp = 받는 피해 증폭.</summary>
    private static int Syn(float amount, float amp = 0f) => (int)Mathf.Max(1f, amount * (1f + amp));

    private static bool Near(float a, float b, float eps = 0.0006f) => Mathf.Abs(a - b) <= eps;
    private static string Yes(bool b) => b ? "예" : "아니오";

    private static float Layer(string field) => (float)typeof(PlayerRuntimeStats).GetField(field, Inst).GetValue(s_stats);

    private static void ClearAmp(List<MonsterBase> list)
    {
        var amp = typeof(MonsterBase).GetField("_dmgTakenAmpSlots", Inst);
        foreach (var m in list) (amp?.GetValue(m) as IDictionary)?.Clear();
    }

    private static int PoisonFields()
        => (typeof(PoisonField).GetField("s_fields", Stat)?.GetValue(null) as ICollection)?.Count ?? -1;

    private static void GroundFieldsClear()
    {
        foreach (var f in UnityEngine.Object.FindObjectsByType<GroundFieldBase>(FindObjectsSortMode.None))
            if (f.IsActive) f.Despawn();
    }

    // ── 자리 ─────────────────────────────────────────────────────
    private static bool OnMesh(Vector3 want, out Vector3 pos)
    {
        if (NavMesh.SamplePosition(want, out var hit, 0.8f, NavMesh.AllAreas)) { pos = hit.position; return true; }
        pos = want; return false;
    }

    /// <summary>플레이어 앞 방향 D를 골라 A · B · E · C 자리를 잡는다. 지금 보는 쪽이 안 되면 16방향을 돌려 본다.</summary>
    private static bool Layout()
    {
        Vector3 f0 = s_player.transform.forward; f0.y = 0f;
        if (f0.sqrMagnitude < 0.01f) f0 = Vector3.forward;
        f0.Normalize();
        for (int k = 0; k < 16; k++)
        {
            Vector3 d = Quaternion.Euler(0f, k * 22.5f, 0f) * f0;
            Vector3 r = Vector3.Cross(Vector3.up, d);
            if (!OnMesh(s_P + d * 3f, out var a)) continue;
            if (!OnMesh(s_P + d * 3f + r * 2f, out var b)) continue;
            if (!OnMesh(s_P - d * 4f, out var e)) continue;
            Vector3 c = default; bool found = false;
            for (int j = 0; j < 16 && !found; j++)
                for (float dist = 13f; dist >= 11f && !found; dist -= 1f)
                {
                    Vector3 dir = Quaternion.Euler(0f, j * 22.5f, 0f) * d;
                    if (!OnMesh(s_P + dir * dist, out var cand)) continue;
                    if (Vector3.Distance(cand, a) < 9f || Vector3.Distance(cand, b) < 9f) continue;
                    c = cand; found = true;
                }
            if (!found) continue;

            s_D = d;
            s_A = s_all[0]; s_B = s_all[1]; s_E = s_all[2]; s_C = s_all[3];
            s_pin[s_A] = a; s_pin[s_B] = b; s_pin[s_E] = e; s_pin[s_C] = c;
            Vector3 away = (c - s_P).normalized, side = Vector3.Cross(Vector3.up, away);
            for (int i = 4; i < s_all.Count; i++)
            {
                Vector3 want = c + away * (1.2f * ((i - 4) / 2 + 1)) + side * (((i - 4) % 2 == 0) ? 1.2f : -1.2f);
                s_pin[s_all[i]] = OnMesh(want, out var p) ? p : c;
            }
            FacePlayer();
            Pin();
            Physics.SyncTransforms();
            return true;
        }
        return false;
    }

    private static void FacePlayer() => s_player.transform.rotation = Quaternion.LookRotation(s_D, Vector3.up);

    private static void Pin()
    {
        foreach (var kv in s_pin)
        {
            var m = kv.Key;
            if (m == null) continue;
            if ((m.transform.position - kv.Value).sqrMagnitude < 0.0225f) continue;
            if (m.TryGetComponent<NavMeshAgent>(out var agent) && agent.isOnNavMesh) agent.Warp(kv.Value);
            else m.transform.position = kv.Value;
        }
    }

    // ── 이펙트 ───────────────────────────────────────────────────
    private static object VfxPlayer() => typeof(ElementVfxPlayer).GetField("s_instance", Stat)?.GetValue(null);

    private static ElementVfxRegistry Registry()
    {
        var p = VfxPlayer();
        return p != null ? typeof(ElementVfxPlayer).GetField("_registry", Inst).GetValue(p) as ElementVfxRegistry : null;
    }

    private static GameObject Body(RuneElement e)  => Registry()?.GetStatusBody(e);
    private static GameObject Aura(RuneElement e)  => Registry()?.GetAura(e);
    private static GameObject Impact(RuneElement e) => Registry()?.GetImpact(e, out _);
    private static GameObject Area(RuneElement e)   => Registry()?.GetArea(e, out _, out _);

    /// <summary>광역 이펙트가 반경 r에서 받아야 하는 배율(반경 ÷ 공칭 반경).</summary>
    private static float AreaScale(RuneElement e, float r)
    {
        var reg = Registry();
        if (reg == null || reg.GetArea(e, out float nominal, out _) == null) return r;
        return r / nominal;
    }

    private static float AuraScale(RuneElement e, float r)
    {
        var reg = Registry();
        if (reg == null || reg.GetAura(e, out float nominal) == null) return r;
        return r / nominal;
    }

    private static int Beams()
    {
        var p = VfxPlayer();
        return p != null ? ((ICollection)typeof(ElementVfxPlayer).GetField("_activeBeams", Inst).GetValue(p)).Count : 0;
    }

    /// <summary>
    /// 지금 떠 있는 이펙트 중 이 프리팹으로 난 것을 찾아 크기 · 입자 · 재질을 적는다.
    /// scale = 기대 배율(판정 반경), follow = 붙어 있어야 하는 대상.
    /// </summary>
    private static void Vfx(string label, GameObject prefab, float scale = -1f, MonsterBase follow = null, float radius = -1f)
    {
        if (prefab == null) { Check(false, $"이펙트 [{label}]: 목록에 프리팹이 없다"); return; }
        var p = VfxPlayer();
        var active = p != null ? (IList)typeof(ElementVfxPlayer).GetField("_active", Inst).GetValue(p) : null;
        if (active == null) { Check(false, $"이펙트 [{label}]: 재생기가 없다"); return; }

        object best = null; float bestErr = float.MaxValue;
        foreach (var it in active)
        {
            var t = it.GetType();
            if ((GameObject)t.GetField("prefab").GetValue(it) != prefab) continue;
            var tf = (Transform)t.GetField("tf").GetValue(it);
            var fol = (Transform)t.GetField("follow").GetValue(it);
            if (follow != null && fol != follow.transform) continue;
            float s = tf.localScale.x / Mathf.Max(0.0001f, prefab.transform.localScale.x);
            float err = scale > 0f ? Mathf.Abs(s - scale) : 0f;
            if (err < bestErr) { best = it; bestErr = err; }
        }
        if (best == null) { Check(false, $"이펙트 [{label}]: 「{prefab.name}」이 떠 있지 않다 (떠 있는 것 {active.Count}개)"); return; }

        var go = (GameObject)best.GetType().GetField("go").GetValue(best);
        float got = go.transform.localScale.x / Mathf.Max(0.0001f, prefab.transform.localScale.x);
        int systems = 0, alive = 0, renderers = 0, bad = 0, texSlots = 0, texSet = 0;
        var badNames = new List<string>();
        int local = 0;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            systems++; alive += ps.particleCount;
            if (ps.main.scalingMode == ParticleSystemScalingMode.Local) local++;
        }
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            renderers++;
            if (r.sharedMaterial == null) { bad++; badNames.Add(r.name + "(재질 없음)"); continue; }
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;   // 입자 렌더러의 빈 트레일 재질 칸은 흔하다 — 주 재질은 위에서 봤다
                var sh = m.shader;
                if (sh == null || !sh.isSupported || sh.name.Contains("InternalErrorShader")) { bad++; badNames.Add(m.name); continue; }
                foreach (var prop in m.GetTexturePropertyNames())
                {
                    texSlots++;
                    if (m.GetTexture(prop) != null) texSet++;
                }
            }
        }
        bool sizeOk = scale <= 0f || Mathf.Abs(got - scale) < 0.05f;
        // 반경에 맞춰 키운 이펙트는 입자계가 계층 크기를 따라야 실제로 커진다(자기 크기만 따르면 조각만 흩어진다).
        bool scaleOk = radius <= 0f || local == 0;
        Check(bad == 0 && alive > 0 && sizeOk && scaleOk,
              $"이펙트 [{label}]: 「{prefab.name}」 · 배율 {got:0.##}{(scale > 0f ? $" (기대 {scale:0.##})" : "")}{(radius > 0f ? $" · 판정 {radius:0.#}m" : "")} · 입자 {alive}개({systems}계, 자기 크기만 따르는 계 {local}) · 렌더러 {renderers} · 깨진 재질 {bad}{(bad > 0 ? " [" + string.Join(", ", badNames) + "]" : "")} · 텍스처 {texSet}/{texSlots}칸");
    }

    /// <summary>플레이어 뒤 위에서 자리 전체를 찍는다(UI 레이어 제외).</summary>
    private static void Shot(string name, Vector3 focus)
    {
        var go = new GameObject("~SynProbeCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        var rt = new RenderTexture(960, 540, 24);
        try
        {
            Vector3 mid = Vector3.Lerp(s_P, focus, 0.6f) + Vector3.up * 0.4f;
            go.transform.position = mid + s_D * 5.5f + Vector3.up * 10f;   // 방 안쪽에서 입구 쪽을 내려다본다 — 입구 벽에 가리지 않게
            go.transform.LookAt(mid);
            int ui = LayerMask.NameToLayer("UI");
            if (ui >= 0) cam.cullingMask = ~(1 << ui);
            cam.fieldOfView = 58f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.06f, 0.08f, 1f);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            s_sb.AppendLine($"  [찍음] {name}.png");
        }
        finally
        {
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    // ── 표 ───────────────────────────────────────────────────────
    private static Dictionary<string, RuneSynergyEntry> LoadCsv()
    {
        var map = new Dictionary<string, RuneSynergyEntry>();
        var lines = File.ReadAllLines(CsvPath, Encoding.UTF8);
        var head = Split(lines[0].TrimStart('﻿'));
        int Col(string name) => head.IndexOf(name);
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var c = Split(lines[i]);
            var e = new RuneSynergyEntry
            {
                zone_id = c[Col("zone_id")], zone_name = c[Col("zone_name")], threshold = int.Parse(c[Col("threshold")]),
                effect_type = c[Col("effect_type")], trigger = c[Col("trigger")],
                value = F(c[Col("value")]), value2 = F(c[Col("value2")]), value3 = F(c[Col("value3")]),
                max_stack = (int)F(c[Col("max_stack")]), duration = F(c[Col("duration")]),
                description = c[Col("description")], stat_version = 1,
            };
            map[e.effect_type] = e;
        }
        return map;
    }

    private static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    private static List<string> Split(string line)
    {
        var res = new List<string>();
        var cur = new StringBuilder();
        bool quoted = false;
        foreach (char ch in line)
        {
            if (ch == '"') quoted = !quoted;
            else if (ch == ',' && !quoted) { res.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(ch);
        }
        res.Add(cur.ToString());
        return res;
    }
}
