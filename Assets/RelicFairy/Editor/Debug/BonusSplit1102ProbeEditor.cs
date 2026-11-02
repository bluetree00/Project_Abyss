#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 11-02 Q29 「추가 피해」 숫자 나누기 실측(테스트 허브 런 중) — 결과 Temp/bonus_split1102.txt · 사진 Temp/ui_shots/BS1102_*.png · 로그 「[BS1102] 끝」.
/// 허수아비(앞 3 m) · 슬라임(소환)에 주 피해(평타 100 · 30)를 넣는다:
///  · 보너스 없음 → 주 숫자 하나 = 받은 피해.
///  · 다음 공격 강화 +50%(QueueNextAttackBonus) → 주 숫자 ≈ 받은 피해 ÷ 1.5 · 추가 피해 숫자 ≈ 나머지 · 합 = 받은 피해(±1).
/// </summary>
public static class BonusSplit1102ProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags SAny = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly StringBuilder s_sb = new();

    [MenuItem("RelicFairy/Debug/11-02 추가 피해 숫자 나누기 실측 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[BS1102] 플레이 중에만"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        s_sb.Clear().AppendLine("11-02 추가 피해 숫자 나누기");
        Directory.CreateDirectory("Temp/ui_shots");
        GameObject dummyGo = null;
        MonsterBase slime = null;
        PlayerController p = null;
        bool invWas = false;
        try
        {
            var run = GameRunBootstrapper.Instance?.Run;
            p = run?.Player != null ? run.Player : Object.FindFirstObjectByType<PlayerController>();
            if (run == null || p == null || run.EffectManager == null) { s_sb.AppendLine("런 · 플레이어 · 효과 관리자 없음"); return; }
            invWas = typeof(PlayerController).GetField("debugInvincible", Inst)?.GetValue(p) is bool b && b;
            typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, true);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RelicFairy/Characters/Monster/TrainingDummy/TrainingDummy.prefab");
            dummyGo = prefab != null ? Object.Instantiate(prefab, p.transform.position + p.transform.forward * 3f, Quaternion.identity) : null;
            await UniTask.Delay(500, ignoreTimeScale: true);
            var dummy = dummyGo != null ? dummyGo.GetComponentInChildren<TrainingDummy>() : null;
            if (dummy == null) s_sb.AppendLine("허수아비 프리팹 없음");
            else
            {
                s_sb.AppendLine("[허수아비]");
                for (int i = 0; i < 3; i++) await HitAsync(run, p, dummy.gameObject, dummy.GetInstanceID(), 100f, 0f, () => (float)GetField(dummy, "_lastHit"), $"보너스 없음 {i + 1}");
                for (int i = 0; i < 3; i++) await HitAsync(run, p, dummy.gameObject, dummy.GetInstanceID(), 100f, 0.5f, () => (float)GetField(dummy, "_lastHit"), $"다음 공격 +50% {i + 1}", i == 0 ? "dummy" : null);
            }

            Vector3 at = p.transform.position + p.transform.right * 4f;
            if (NavMesh.SamplePosition(at, out var hit, 4f, NavMesh.AllAreas)) at = hit.position;
            try { slime = await Managers.ObjectPooler.SpawnAsync<MonsterBase>("Slime/Slime", ObjectPoolerManager.PoolType.Monster, at, Quaternion.identity); }
            catch (System.Exception e) { s_sb.AppendLine($"슬라임 소환 실패: {e.Message}"); }
            await UniTask.Delay(1200, ignoreTimeScale: true);
            if (slime == null) s_sb.AppendLine("슬라임 없음 — 몬스터 경로 건너뜀");
            else
            {
                s_sb.AppendLine($"[슬라임] 체력 {slime.CurrentHp}");
                int hpBox = slime.CurrentHp;
                for (int i = 0; i < 2 && !slime.IsDead; i++) await HitAsync(run, p, slime.gameObject, slime.GetInstanceID(), 30f, 0f, () => Drop(slime, ref hpBox), $"보너스 없음 {i + 1}");
                for (int i = 0; i < 2 && !slime.IsDead; i++) await HitAsync(run, p, slime.gameObject, slime.GetInstanceID(), 30f, 0.5f, () => Drop(slime, ref hpBox), $"다음 공격 +50% {i + 1}", i == 0 ? "slime" : null);
            }
        }
        catch (System.Exception e) { s_sb.AppendLine("예외: " + e); }
        finally
        {
            if (p != null) typeof(PlayerController).GetField("debugInvincible", Inst)?.SetValue(p, invWas);
            if (slime != null && !slime.IsDead) slime.TakeSynergyDamage(999999f, null);
            if (dummyGo != null) Object.Destroy(dummyGo);
            File.WriteAllText("Temp/bonus_split1102.txt", s_sb.ToString());
            Debug.Log("[BS1102] 끝 → Temp/bonus_split1102.txt");
        }
    }

    /// <summary>한 타 — 뜬 새 숫자(주 줄 = 대상 ID · 추가 줄 = ~ID)를 읽어 받은 피해와 맞춘다.</summary>
    private static async UniTask HitAsync(GameRunSession run, PlayerController p, GameObject target, int id, float dmg, float bonus,
                                          System.Func<float> taken, string label, string shot = null)
    {
        await UniTask.Delay(400, ignoreTimeScale: true);   // 합치기 창(0.12초)을 넘겨 새 숫자로 뜨게
        var before = new HashSet<DamagePopup>();
        foreach (var pp in Live()) if (pp != null && pp.IsShowing) before.Add(pp);
        if (bonus > 0f) run.EffectManager.QueueNextAttackBonus(bonus);
        CombatDamage.Deal(new CombatDamage.Request
        {
            Target = target, BaseDamage = dmg, Owner = p.gameObject, ActionType = WeaponActionType.GroundLight,
            KnockbackMultiplier = 0f, SkipHitVfx = true, NoHitFeel = true, HitPoint = target.transform.position + Vector3.up,
        });
        float got = taken();
        await UniTask.Delay(150, ignoreTimeScale: true);
        float main = 0f, extra = 0f;
        int nMain = 0, nExtra = 0;
        foreach (var pp in Live())
        {
            if (pp == null || !pp.IsShowing || before.Contains(pp)) continue;
            float v = Number(pp);
            if (pp.Column == id) { main += v; nMain++; }
            else if (pp.Column == ~id) { extra += v; nExtra++; }
        }
        bool ok = bonus <= 0f ? nExtra == 0 && Mathf.Abs(main - got) <= 1f
                              : nExtra > 0 && Mathf.Abs(main + extra - got) <= 1.5f && Mathf.Abs(main - got / (1f + bonus)) <= 1.5f;
        s_sb.AppendLine($"  {(ok ? "PASS" : "FAIL")} {label} — 받은 피해 {got:0.#} · 주 숫자 {nMain}개 {main:0} · 추가 피해 숫자 {nExtra}개 {extra:0} · 합 {main + extra:0}");
        if (shot != null) ScreenCapture.CaptureScreenshot($"Temp/ui_shots/BS1102_{shot}.png");
    }

    private static float Drop(MonsterBase m, ref int hpBox)
    {
        int now = m.CurrentHp;
        float d = hpBox - now;
        hpBox = now;
        return d;
    }

    private static IEnumerable<DamagePopup> Live()
        => typeof(DamagePopupSpawner).GetField("_live", SAny)?.GetValue(null) as List<DamagePopup> ?? new List<DamagePopup>();

    private static float Number(DamagePopup pp)
    {
        var t = typeof(DamagePopup).GetField("label", Inst)?.GetValue(pp) as TMP_Text;
        if (t == null) return 0f;
        var m = Regex.Match(Regex.Replace(t.text, "<[^>]*>", ""), @"\d[\d,]*");
        return m.Success ? float.Parse(m.Value.Replace(",", "")) : 0f;
    }

    private static object GetField(object o, string name) => o.GetType().GetField(name, Inst)?.GetValue(o);
}
#endif
