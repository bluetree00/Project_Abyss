#if UNITY_EDITOR
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
/// ⚠️ N은 반드시 해당 actionType 어빌리티가 보유한 stepIndex 범위 안이어야 한다. 벗어나면 그 타는 무판정이다.
///   지상(KatanaAbility)=0~3 / 강공·공중·낙하=0 뿐 → 아래 fx 배정이 그 제약을 지킨다.
///   (참고: 기존 카타나 3타는 SpawnSlashEffect4를 쏘는데 지상 step 최대가 3이라 그 타가 무판정이다.
///    무형검은 그 결함을 복제하지 않고 0/1/2로 배정했다.)
///
/// 이벤트 time은 <b>초 단위</b>라 클립 실제 길이에 비율을 곱해 넣는다.
/// 메뉴: Tools/RelicFairy/Setup Nameless Anim Events
/// </summary>
public static class NamelessSwordEvents
{
    private const string Dir =
        "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/Nameless/";

    // 타이밍 비율(0~1). 트레일이 스윙 시작에 켜지고, 판정은 칼이 지나가는 지점에 오도록 약간 뒤에 둔다.
    // 모션을 눈으로 보고 조정할 여지가 큰 값이라 한 곳에 모아둔다.
    private const float TrailBegin = 0.18f;
    private const float Slash      = 0.24f;
    private const float TrailEnd   = 0.72f;
    private const float AttackEnd  = 1.00f;

    private struct Def
    {
        public string file;   // Dir 기준 파일명
        public int    fx;     // SpawnSlashEffect 번호 = 어빌리티 stepIndex
        public string note;
    }

    private static readonly Def[] Defs =
    {
        // 지상 3연타 — KatanaAbility step 0/1/2 (step3은 여분)
        new Def{ file = "GhostSamurai_APose_Attack01_1_ALL_Inplace.FBX", fx = 0, note = "지상 1타" },
        new Def{ file = "GhostSamurai_APose_Attack01_2_Inplace.FBX",     fx = 1, note = "지상 2타" },
        new Def{ file = "GhostSamurai_APose_Attack01_4_Inplace.FBX",     fx = 2, note = "지상 3타(마무리)" },

        // 강공 — KatanaHeavyAbility step 0 뿐
        new Def{ file = "GhostSamurai_APose_SPAttack02_Inplace.FBX",     fx = 0, note = "강공격" },

        // 공중 3연타 — KatanaAirAbility step 0 뿐이라 전부 0
        new Def{ file = "GhostSamurai_APose_Air_Attack01_1_Inplace.FBX", fx = 0, note = "공중 1타" },
        new Def{ file = "GhostSamurai_APose_Air_Attack01_2_Inplace.FBX", fx = 0, note = "공중 2타" },
        new Def{ file = "GhostSamurai_APose_Air_Attack02_Inplace.FBX",   fx = 0, note = "공중 3타" },

        // 낙하 — KatanaPlungeAbility step 0 뿐
        new Def{ file = "GhostSamurai_APose_JumpAttack01_Inplace.FBX",   fx = 0, note = "낙하 공격" },
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

            // user 클립 → default → importedTakeInfos 순으로 클립 확보(대검 스크립트와 동일)
            var clips = imp.clipAnimations;
            if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;

            float len = 1f;
            if (clips == null || clips.Length == 0)
            {
                var takes = imp.importedTakeInfos;
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
                len = Mathf.Max(0.01f, t.stopTime - t.startTime);
            }

            // 실제 로드된 AnimationClip 길이가 가장 정확 — 있으면 그걸로 덮는다.
            foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                if (o is AnimationClip c) { len = Mathf.Max(0.01f, c.length); break; }

            clips[0].events = new[]
            {
                Ev(TrailBegin * len, "AE_BeginTrail"),
                Ev(Slash      * len, "SpawnSlashEffect" + d.fx),
                Ev(TrailEnd   * len, "AE_EndTrail"),
                Ev(AttackEnd  * len, "AE_AttackEnd"),
            };

            imp.clipAnimations = clips;
            imp.SaveAndReimport();
            done++;
            Debug.Log($"[NamelessEvents] {d.note}: {d.file} ← SpawnSlashEffect{d.fx} (len {len:F2}s)");
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[NamelessEvents] 무형검 이벤트 주입 완료: {done}/{Defs.Length}");
    }

    private static AnimationEvent Ev(float time, string fn)
        => new AnimationEvent { time = time, functionName = fn };
}
#endif
