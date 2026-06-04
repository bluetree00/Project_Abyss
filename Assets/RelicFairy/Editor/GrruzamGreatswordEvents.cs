#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 일회용: Grruzam 대검 클립에 기존(legacy) 패턴과 동일한 애니메이션 이벤트를 주입한다.
/// 콤보 단계별 SpawnSlashEffect{0/1/2} + AE_BeginTrail(0.25) / AE_EndTrail(0.75) / AE_AttackEnd(1.0).
/// 이벤트 time은 초 단위라 클립 길이에 정규화 시점을 곱해 넣는다.
/// user clipAnimations가 없으면 importedTakeInfos로 클립을 생성한다.
/// 메뉴: Tools/RelicFairy/Setup Greatsword Anim Events
/// </summary>
public static class GrruzamGreatswordEvents
{
    private const string A = "Assets/_ThirdParty/Grruzam Powerful Sword Animation(Great Sword, Katana)/Animation/M_Big_Sword";

    private struct Def { public string path; public int fx; }

    private static readonly Def[] Defs =
    {
        new Def{ path = A + "/2_Attacks/2__7Combos/M_Big_Sword@Attack_7Combo_1.FBX",                    fx = 0 },
        new Def{ path = A + "/2_Attacks/2__7Combos/M_Big_Sword@Attack_7Combo_2.FBX",                    fx = 1 },
        new Def{ path = A + "/2_Attacks/2__7Combos/M_Big_Sword@Attack_7Combo_3.FBX",                    fx = 2 },
        new Def{ path = A + "/2_Attacks/4__Jump_Attack/M_Big_Sword@Jump_Attack_Combo_1_ZeroHeight.FBX", fx = 0 },
        new Def{ path = A + "/2_Attacks/4__Jump_Attack/M_Big_Sword@Jump_Attack_Combo_2_ZeroHeight.FBX", fx = 1 },
        new Def{ path = A + "/2_Attacks/4__Jump_Attack/M_Big_Sword@Jump_Attack_Combo_3_ZeroHeight.FBX", fx = 2 },
        new Def{ path = A + "/2_Attacks/5__Upper_Attack/M_Big_Sword@UpperAttack_ZeroHeight.FBX",        fx = 0 },
        new Def{ path = A + "/5_Revenges/Guard_Revenges/M_Big_Sword@Revenge_Guard_Attack.FBX",          fx = 0 },
        new Def{ path = A + "/3_Skills/M_Big_Sword@Skill_C.FBX",                                        fx = 0 },
    };

    [MenuItem("Tools/RelicFairy/Setup Greatsword Anim Events")]
    public static void Setup()
    {
        int done = 0;
        foreach (var d in Defs)
        {
            var imp = AssetImporter.GetAtPath(d.path) as ModelImporter;
            if (imp == null) { Debug.LogWarning("[GSEvents] 임포터 없음: " + d.path); continue; }

            // user 클립 → default → importedTakeInfos 순으로 클립 확보
            var clips = imp.clipAnimations;
            if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;

            float len = 1f;
            if (clips != null && clips.Length > 0)
            {
                len = Mathf.Max(0.01f, (clips[0].lastFrame - clips[0].firstFrame) / Mathf.Max(1f, imp.resampleCurves ? 30f : 30f));
            }
            else
            {
                var takes = imp.importedTakeInfos;
                if (takes == null || takes.Length == 0) { Debug.LogWarning("[GSEvents] take 없음: " + d.path); continue; }
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

            // 클립의 실제 길이(초)로 보정 — 로드된 AnimationClip이 가장 정확
            foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(d.path))
                if (o is AnimationClip c) { len = Mathf.Max(0.01f, c.length); break; }

            clips[0].events = new[]
            {
                Ev(0.25f * len, "AE_BeginTrail"),
                Ev(0.25f * len, "SpawnSlashEffect" + d.fx),
                Ev(0.75f * len, "AE_EndTrail"),
                Ev(1.00f * len, "AE_AttackEnd"),
            };
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
            done++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[GSEvents] 대검 이벤트 주입 완료: {done}/{Defs.Length}");
    }

    [MenuItem("Tools/RelicFairy/Diagnose 3Combo Clips")]
    public static void Diagnose()
    {
        DumpBones(A + "/2_Attacks/0__3Combos/M_Big_Sword@Attack_3Combo_1.FBX", "3Combo_1");
        DumpBones(A + "/2_Attacks/1__4Combos/M_Big_Sword@Attack_4Combo_1A.FBX", "4Combo_1A");
        DumpBones(A + "/2_Attacks/2__7Combos/M_Big_Sword@Attack_7Combo_1.FBX", "7Combo_1");
        DumpBones(A + "/2_Attacks/3__Dash_Attack/M_Big_Sword@Dash_Attack_ver_A.FBX", "Dash_A");
        DumpBones(A + "/2_Attacks/5__Upper_Attack/M_Big_Sword@UpperAttack_ZeroHeight.FBX", "Upper(OK)");
    }

    private static void DumpBones(string path, string tag)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go == null) { Debug.Log($"[GSDiag] {tag}: GO 없음"); return; }
        var names = new List<string>();
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) names.Add(t.name);
        Debug.Log($"[GSDiag] {tag} boneCount={names.Count}");
    }

    private static AnimationEvent Ev(float time, string fn)
        => new AnimationEvent { time = time, functionName = fn };
}
#endif
