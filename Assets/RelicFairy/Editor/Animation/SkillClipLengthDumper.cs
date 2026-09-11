#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

/// <summary>
/// 스킬 애니 점검용 — 베이스 컨트롤러의 스킬 상태(QSkill/ESkill/RelicQ)와
/// 무기 세트가 그 자리에 덮어쓰는 어드레서블 클립의 <b>실제 길이·프레임레이트·state speed 반영 유효길이</b>를 한 번에 찍는다.
/// 메뉴: RelicFairy/Animation/Dump Skill Clip Lengths
///
/// 왜 도구인가 — FBX 클립 길이는 .meta에 없다(firstFrame/lastFrame만 있고 샘플레이트가 없다).
/// 스킬 런타임 길이(코드 상수)와 모션 길이가 안 맞는 걸 눈대중으로 잡을 수 없어 실측한다.
/// </summary>
public static class SkillClipLengthDumper
{
    private const string ControllerPath = "Assets/RelicFairy/Characters/Player/PlayerBaseController.controller";

    private static readonly string[] SkillKeys =
    {
        "QSkill_Iasen", "GreatswordQSkill", "GreatswordESkill",
        "BowQSkill_01", "BowQSkill_02", "BowESkill_01", "BowESkill_02", "BowESkill_03",
        "RelicQ_Slash1", "RelicQ_Slash2", "RelicQ_Slash3",
    };

    [MenuItem("RelicFairy/Animation/Dump Skill Clip Lengths")]
    public static void Dump()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[스킬 클립 길이] ── 컨트롤러 상태 (speed 반영 유효길이) ──");

        var ac = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
        if (ac != null)
        {
            foreach (var layer in ac.layers)
                foreach (var cs in layer.stateMachine.states)
                {
                    var st = cs.state;
                    if (!(st.name.StartsWith("QSkill") || st.name.StartsWith("ESkill") || st.name.StartsWith("RelicQ"))) continue;
                    var clip = st.motion as AnimationClip;
                    if (clip == null) { sb.AppendLine($"  {st.name}: (모션 없음) speed={st.speed}"); continue; }
                    sb.AppendLine($"  {st.name}: clip='{clip.name}' len={clip.length:F2}s fps={clip.frameRate} speed={st.speed} → 유효 {clip.length / Mathf.Max(0.01f, st.speed):F2}s loop={clip.isLooping}");
                }
        }
        else sb.AppendLine("  컨트롤러 없음");

        sb.AppendLine("[스킬 클립 길이] ── 어드레서블 오버라이드 클립 ──");
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        foreach (var key in SkillKeys)
        {
            AnimationClip found = null; string path = "";
            if (settings != null)
            {
                foreach (var g in settings.groups)
                {
                    if (g == null) continue;
                    foreach (var e in g.entries)
                    {
                        if (e.address != key) continue;
                        path = AssetDatabase.GUIDToAssetPath(e.guid);
                        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path))
                            if (a is AnimationClip c && !c.name.StartsWith("__preview__")) { found = c; break; }
                    }
                }
            }
            if (found == null) { sb.AppendLine($"  {key}: (어드레서블 없음 또는 클립 없음) {path}"); continue; }
            sb.AppendLine($"  {key}: clip='{found.name}' len={found.length:F2}s fps={found.frameRate} loop={found.isLooping}  ← {System.IO.Path.GetFileName(path)}");
        }

        // 콘솔은 여러 줄 로그의 첫 줄만 보이고 리로드에 지워지므로 파일에도 남긴다(Library는 git 제외).
        string outPath = System.IO.Path.Combine(Application.dataPath, "..", "Library", "RelicFairy_SkillClipLengths.txt");
        System.IO.File.WriteAllText(outPath, sb.ToString());
        Debug.Log(sb.ToString());
    }
}
#endif
