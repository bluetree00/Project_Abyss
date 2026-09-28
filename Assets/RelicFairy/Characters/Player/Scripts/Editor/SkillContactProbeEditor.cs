using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Inputs;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 도구 · 런 중] 스킬 판정 시각에 클립의 <b>닿는 순간</b>이 오는지 본다(<see cref="SkillAnimSync"/> 검증, 09-25).
/// 무기를 실제 장착 경로로 들고 스킬을 쓴 뒤, 판정이 나가는 시각(스킬 시작 + 동작 데이터의 지연)에 클립이 몇 초 지점인지 읽는다.
/// 기대값은 클립 실측 닿음(대검 R 1.22 · 대검 E 1.27 · 발도 1.63초). 결과: Temp/skill_contact_probe.txt
/// </summary>
public static class SkillContactProbeEditor
{
    private const string OutPath = "Temp/skill_contact_probe.txt";

    private struct Case
    {
        public string label, weapon, clip; public SkillType slot; public float contact; public int enhance;
    }

    private static readonly Case[] Cases =
    {
        new Case { label = "대검 R 최후의 일격", weapon = "Assets/RelicFairy/Weapon/Greatsword/Data/T1_Greatsword.asset", slot = SkillType.R, clip = "GreatswordQSkill", contact = 1.22f },
        new Case { label = "대검 E 주변경계(2단계)", weapon = "Assets/RelicFairy/Weapon/Greatsword/Data/T1_Greatsword.asset", slot = SkillType.E, clip = "GreatswordESkill", contact = 1.27f, enhance = 5 },
        new Case { label = "카타나 E 성광베기", weapon = "Assets/RelicFairy/Weapon/Katana/Data/T1_Katana.asset", slot = SkillType.E, clip = "QSkill_Iasen", contact = 1.63f },
        new Case { label = "무형검 R 무형일섬", weapon = "Assets/RelicFairy/Weapon/Nameless/Data/T0_Nameless.asset", slot = SkillType.R, clip = "QSkill_Iasen", contact = 1.63f },
    };

    [MenuItem("RelicFairy/Debug/스킬 닿는 순간 맞춤 확인 (런 중)")]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[스킬닿음] 플레이 모드에서만 동작한다."); return; }
        var p = GameRunBootstrapper.Instance?.Run?.Player;
        if (p == null) { Debug.LogWarning("[스킬닿음] 런 플레이어가 없다."); return; }
        RunAsync(p, p.GetCancellationTokenOnDestroy()).Forget();
    }

    private static async UniTaskVoid RunAsync(PlayerController p, CancellationToken ct)
    {
        var sb = new StringBuilder();
        ProbeOutput.Begin(OutPath, "스킬닿음");
        int pass = 0, fail = 0;
        try
        {
            foreach (var c in Cases)
            {
                var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(c.weapon);
                if (so == null) { sb.AppendLine($"  ⚠ {c.label}: 무기 SO 없음"); fail++; continue; }
                await GameRunBootstrapper.EquipWeaponToPlayerAsync(so, p, 1);
                await Wait(1.0f, ct);
                var wd = p.WeaponManager?.CurrentWeaponData;
                if (wd != null && c.enhance > 0) wd.enhanceLevel = c.enhance;

                float delay = DamageDelay(wd, c.slot);
                p.CooldownTracker.ResetCooldown(c.slot);
                p.InputBuffer.Clear();
                p.InputBuffer.Push(c.slot == SkillType.E ? Command.ESkill : Command.RSkill);

                // 스킬 클립이 잡히는 첫 프레임 = 스킬 시작
                float t0 = -1f, deadline = Time.realtimeSinceStartup + 3f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    if (ClipTime(p.Anim, c.clip, out _)) { t0 = Time.realtimeSinceStartup; break; }
                }
                if (t0 < 0f) { sb.AppendLine($"  ❌ {c.label}: 스킬 클립 {c.clip}이 재생되지 않았다"); fail++; continue; }

                // 게임 시간 기준 판정 시각까지(히트스톱·슬로모가 섞여도 애니는 게임 시간으로 흐른다)
                float g0 = Time.time;
                while (Time.time - g0 < delay) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                ClipTime(p.Anim, c.clip, out float at);
                bool ok = Mathf.Abs(at - c.contact) <= 0.08f;
                sb.AppendLine($"  {(ok ? "✅" : "❌")} {c.label}: 판정 시각 {delay:F2}초에 클립 {at:F2}초 지점 (닿음 {c.contact:F2}초 · 차이 {at - c.contact:+0.00;-0.00})");
                if (ok) pass++; else fail++;
                await Wait(2.5f, ct);
            }
        }
        catch (OperationCanceledException) { sb.AppendLine("취소됨"); }
        catch (Exception e) { sb.AppendLine("예외: " + e); }

        sb.Insert(0, $"통과 {pass} · 실패 {fail}\n");
        ProbeOutput.Write(OutPath, "스킬닿음", sb.ToString());
        Debug.Log("[스킬닿음] 결과\n" + sb);
    }

    /// <summary>동작 데이터에서 첫 판정까지의 지연(초) — SkillAnimSync에 넘기는 값과 같은 식.</summary>
    private static float DamageDelay(WeaponData wd, SkillType slot)
    {
        var skill = slot == SkillType.E ? wd?.skillE : wd?.skillQ;
        var beh = skill != null ? skill.behavior : null;
        if (beh == null) return 0f;
        var so = new SerializedObject(beh);
        float F(string n) { var pr = so.FindProperty(n); return pr != null ? pr.floatValue : 0f; }
        return beh switch
        {
            FinalStrikeBehaviorSO   => F("chargeDuration"),
            PerimeterGuardBehaviorSO => 0f,
            HolySlashBehaviorSO     => F("dashDuration"),
            IasenSlashBehaviorSO    => F("dashDuration") + F("slashDelay"),
            _ => 0f,
        };
    }

    /// <summary>현재(또는 전환 중인 다음) 상태의 클립이 <paramref name="clip"/>이면 그 클립의 재생 지점(초).</summary>
    private static bool ClipTime(Animator anim, string clip, out float seconds)
    {
        seconds = 0f;
        if (anim == null) return false;
        if (Match(anim.GetNextAnimatorClipInfo(0), clip))
        {
            var st = anim.GetNextAnimatorStateInfo(0);
            seconds = st.normalizedTime * st.length * st.speed * st.speedMultiplier;
            return true;
        }
        if (Match(anim.GetCurrentAnimatorClipInfo(0), clip))
        {
            var st = anim.GetCurrentAnimatorStateInfo(0);
            seconds = st.normalizedTime * st.length * st.speed * st.speedMultiplier;
            return true;
        }
        return false;
    }

    private static bool Match(AnimatorClipInfo[] infos, string clip)
    {
        if (infos == null) return false;
        foreach (var i in infos) if (i.clip != null && i.clip.name == clip) return true;
        return false;
    }

    private static UniTask Wait(float s, CancellationToken ct)
        => UniTask.Delay(TimeSpan.FromSeconds(s), DelayType.Realtime, cancellationToken: ct);
}
