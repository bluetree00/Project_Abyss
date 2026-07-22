#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 카타나 지상 콤보의 SpawnSlashEffect 번호를 <b>순차(0/1/2,3)</b>로 교정한다.
///
/// 문제: 기존 배치가 1타=0, 2타=2, 3타=3+4 였는데 KatanaAbility의 stepIndex는 0~3뿐이라
/// <b>SpawnSlashEffect4가 대응 step 없이 무판정</b>이었다(3타 두 번째 베기가 헛침). 동시에 step1은 미사용.
///
/// 교정: 1타=0 / 2타=1 / 3타=2,3 → 네 step을 빠짐없이 쓰고 초과 번호가 사라진다.
/// KatanaAbility의 step 0~3은 수치·이펙트가 <b>전부 동일</b>(baseDamage 2, SlashAttack, 0.4s)하므로
/// 번호 재배정 자체의 밸런스 영향은 없다.
/// ⚠️ 다만 무판정이던 3타 2번째 베기가 <b>실제 타격으로 살아나</b> 마무리 타 대미지는 올라간다(의도된 수정).
///
/// SpawnSlashEffect 이벤트의 functionName만 바꾸고 <b>시간과 다른 이벤트(AE_BeginTrail/EndTrail/AttackEnd)는
/// 그대로 보존</b>한다. 메뉴: Tools/RelicFairy/Fix Katana Slash Steps
/// </summary>
public static class KatanaSlashStepFix
{
    private const string Dir = "Assets/RelicFairy/Animations/Player/Test_01/Attack/";

    private struct Def
    {
        public string file;
        public Dictionary<string, string> remap;   // 기존 functionName → 새 functionName
        public string note;
    }

    private static readonly Def[] Defs =
    {
        new Def
        {
            file  = "NormalAttack_2.FBX",
            note  = "2타: 2 → 1",
            remap = new Dictionary<string, string> { { "SpawnSlashEffect2", "SpawnSlashEffect1" } },
        },
        new Def
        {
            file  = "NormalAttack_3.FBX",
            note  = "3타: 3 → 2, 4 → 3(무판정 해소)",
            remap = new Dictionary<string, string>
            {
                { "SpawnSlashEffect3", "SpawnSlashEffect2" },
                { "SpawnSlashEffect4", "SpawnSlashEffect3" },
            },
        },
        // NormalAttack_1은 이미 0이라 변경 없음.
    };

    [MenuItem("Tools/RelicFairy/Fix Katana Slash Steps")]
    public static void Fix()
    {
        int done = 0;
        foreach (var d in Defs)
        {
            string path = Dir + d.file;

            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) { Debug.LogWarning($"[KatanaFix] 임포터 없음: {path}"); continue; }

            var clips = imp.clipAnimations;
            if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;
            if (clips == null || clips.Length == 0) { Debug.LogWarning($"[KatanaFix] 클립 없음: {path}"); continue; }

            var events = clips[0].events;
            if (events == null || events.Length == 0) { Debug.LogWarning($"[KatanaFix] 이벤트 없음: {path}"); continue; }

            int changed = 0;
            for (int i = 0; i < events.Length; i++)
            {
                // 원본 이름 기준 1회 조회 — 연쇄 치환(3→2→1) 방지.
                if (d.remap.TryGetValue(events[i].functionName, out var newFn))
                {
                    Debug.Log($"[KatanaFix] {d.file}: {events[i].functionName} → {newFn} (t={events[i].time:F3})");
                    events[i].functionName = newFn;   // time·기타 이벤트는 건드리지 않는다
                    changed++;
                }
            }

            if (changed == 0) { Debug.Log($"[KatanaFix] 변경 없음(이미 교정됨?): {d.file}"); continue; }

            clips[0].events = events;
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
            done++;
            Debug.Log($"[KatanaFix] {d.note} — {changed}개 교정: {d.file}");
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[KatanaFix] 카타나 슬래시 step 교정 완료: {done}/{Defs.Length}");
    }
}
#endif
