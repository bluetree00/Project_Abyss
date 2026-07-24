#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 무형검(T0_Nameless) 전용 애니메이션 이벤트 주입 — <see cref="GrruzamGreatswordEvents"/>와 동일 패턴.
///
/// 사무라이팩(GhostSamurai) 클립은 이벤트가 하나도 없어 그대로 두면 <b>타격판정·트레일이 전부 안 나간다</b>.
/// 여기서 기존 무기와 같은 계약을 주입한다:
///   • SpawnSlashEffect{N} → PlayerAnimationEventReceiver.AE_EffectStep(N) → 어빌리티 step의 <b>타격판정</b>
///   • AE_BeginTrail / AE_EndTrail → PlayerWeaponTrailVfx → <b>판정 없는 트레일</b>
///   • AE_AttackEnd → 공격 종료
///
/// <b>타이밍은 카타나(Test_01)의 비율을 그대로 복제</b>한다. 무형검 클립은 카타나(~1초)보다 훨씬 길어(2~2.7초)
/// 절대 초를 쓰면 타격이 늦게 나가므로, 카타나의 <b>정규화 비율(0~1)</b>을 각 클립 실제 길이에 곱해 넣는다.
///
/// 카타나 지상 3타는 마무리 <b>2연격</b>(step2 0.17 + step3 0.31)이다 — 무형검도 동일하게 맞춘다.
///
/// ⚠️ SpawnSlashEffect 번호는 해당 actionType 어빌리티의 stepIndex 범위 안이어야 한다(벗어나면 무판정).
///   지상(NamelessAbility)=0~3 / 강공·공중·낙하=0 뿐.
///
/// 메뉴: Tools/RelicFairy/Setup Nameless Anim Events
/// </summary>
public static class NamelessSwordEvents
{
    private const string Dir =
        "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/Nameless/";

    private struct Ev
    {
        public float  t;    // 정규화 비율(0~1) — 클립 길이에 곱해 초로 환산
        public string fn;
        public Ev(float t, string fn) { this.t = t; this.fn = fn; }
    }

    private struct Def
    {
        public string file;
        public Ev[]   events;
        public string note;
    }

    // 이펙트를 '공격 시작'에 내보낸다 — 슬래시·트레일을 클립 앞쪽(~10%)에 둔다.
    // 콤보/이동 해제(comboWindowOpen·attackEndAt)는 NamelessAnimation.asset에서 함께 앞당겨,
    // 이펙트 직후 다음 공격을 잇고 긴 후딜을 잘라 움직임이 빨리 풀리게 한다.
    private static readonly Def[] Defs =
    {
        // ── 지상 3연타 ──
        new Def { file = "GhostSamurai_APose_Attack01_1_ALL_Inplace.FBX", note = "지상 1타",
            events = new[] { new Ev(0.08f,"AE_BeginTrail"), new Ev(0.10f,"SpawnSlashEffect0"),
                             new Ev(0.50f,"AE_EndTrail"),   new Ev(1.00f,"AE_AttackEnd") } },

        new Def { file = "GhostSamurai_APose_Attack01_2_Inplace.FBX", note = "지상 2타",
            events = new[] { new Ev(0.08f,"AE_BeginTrail"), new Ev(0.10f,"SpawnSlashEffect1"),
                             new Ev(0.50f,"AE_EndTrail"),   new Ev(1.00f,"AE_AttackEnd") } },

        // 3타 = 무형검 애니는 '한 번 베기'라 슬래시 1개(step2)만.
        new Def { file = "GhostSamurai_APose_Attack01_4_Inplace.FBX", note = "지상 3타(단일 베기)",
            events = new[] { new Ev(0.08f,"AE_BeginTrail"), new Ev(0.12f,"SpawnSlashEffect2"),
                             new Ev(0.55f,"AE_EndTrail"),   new Ev(1.00f,"AE_AttackEnd") } },

        // ── 강공 (step0 뿐) ──
        new Def { file = "GhostSamurai_APose_SPAttack02_Inplace.FBX", note = "강공격",
            events = new[] { new Ev(0.08f,"AE_BeginTrail"), new Ev(0.10f,"SpawnSlashEffect0"),
                             new Ev(0.50f,"AE_EndTrail"),   new Ev(1.00f,"AE_AttackEnd") } },

        // ── 공중 3연타 (Air 어빌리티 step0 뿐 → 전부 0) ──
        new Def { file = "GhostSamurai_APose_Air_Attack01_1_Inplace.FBX", note = "공중 1타",
            events = new[] { new Ev(0.08f,"AE_BeginTrail"), new Ev(0.10f,"SpawnSlashEffect0"),
                             new Ev(0.50f,"AE_EndTrail"),   new Ev(1.00f,"AE_AttackEnd") } },
        new Def { file = "GhostSamurai_APose_Air_Attack01_2_Inplace.FBX", note = "공중 2타",
            events = new[] { new Ev(0.08f,"AE_BeginTrail"), new Ev(0.10f,"SpawnSlashEffect0"),
                             new Ev(0.50f,"AE_EndTrail"),   new Ev(1.00f,"AE_AttackEnd") } },
        new Def { file = "GhostSamurai_APose_Air_Attack02_Inplace.FBX", note = "공중 3타",
            events = new[] { new Ev(0.08f,"AE_BeginTrail"), new Ev(0.10f,"SpawnSlashEffect0"),
                             new Ev(0.50f,"AE_EndTrail"),   new Ev(1.00f,"AE_AttackEnd") } },

        // ── 낙하 (step0 뿐) ──
        new Def { file = "GhostSamurai_APose_JumpAttack01_Inplace.FBX", note = "낙하 공격",
            events = new[] { new Ev(0.08f,"AE_BeginTrail"), new Ev(0.10f,"SpawnSlashEffect0"),
                             new Ev(0.50f,"AE_EndTrail"),   new Ev(1.00f,"AE_AttackEnd") } },
    };

    [MenuItem("Tools/RelicFairy/Setup Nameless Anim Events")]
    public static void Setup()
    {
        int done = 0;
        foreach (var d in Defs)
        {
            string path = Dir + d.file;

            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) { Debug.LogWarning($"[NamelessEvents] 임포터 없음: {path}"); continue; }

            var clips = imp.clipAnimations;
            if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;

            // 샘플레이트(이 팩은 120fps 모카프) — take에서 읽는다.
            float sampleRate = 30f;
            var takes = imp.importedTakeInfos;
            if (takes != null && takes.Length > 0 && takes[0].sampleRate > 0f)
                sampleRate = takes[0].sampleRate;

            if (clips == null || clips.Length == 0)
            {
                if (takes == null || takes.Length == 0)
                {
                    Debug.LogWarning($"[NamelessEvents] take 없음: {path}");
                    continue;
                }
                var t = takes[0];
                clips = new[]
                {
                    new ModelImporterClipAnimation
                    {
                        takeName   = t.name,
                        name       = t.defaultClipName,
                        firstFrame = Mathf.RoundToInt(t.startTime * t.sampleRate),
                        lastFrame  = Mathf.RoundToInt(t.stopTime  * t.sampleRate),
                        loopTime   = false,
                    }
                };
            }

            // ⚠️ 게임이 재생하는 건 '트림된 서브클립'(firstFrame~lastFrame)이다.
            // LoadAllAssetRepresentationsAtPath는 전체 take(22초 등)를 돌려줘 이벤트가 밀렸다 →
            // 길이는 반드시 트림 프레임 범위 ÷ 샘플레이트로 계산한다(게임 클립과 일치).
            float len = Mathf.Max(0.01f, (clips[0].lastFrame - clips[0].firstFrame) / sampleRate);

            var events = new List<AnimationEvent>(d.events.Length);
            foreach (var e in d.events)
                events.Add(new AnimationEvent { time = Mathf.Clamp01(e.t) * len, functionName = e.fn });

            clips[0].events = events.ToArray();
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
            done++;
            Debug.Log($"[NamelessEvents] {d.note}: {d.file} (len {len:F2}s, 이벤트 {events.Count}개)");
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[NamelessEvents] 무형검 이벤트 주입 완료(카타나 비율): {done}/{Defs.Length}");
    }
}
#endif
