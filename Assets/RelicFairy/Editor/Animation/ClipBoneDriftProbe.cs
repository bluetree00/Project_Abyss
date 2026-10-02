#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// "공격 뒤 제자리로 순간이동" 점검용 — 제네릭 클립의 <b>상위 뼈(루트 · 골반) 수평 이동량</b>을 잰다.
/// 메뉴: RelicFairy/Animation/Probe Clip Bone Drift → 콘솔(한 줄씩) + Library/RelicFairy_ClipBoneDrift.txt
///
/// 루트 모션 노드가 비어 있는 제네릭 리그는 클립 안의 골반 전진이 몸에 그대로 실린다. 코드가 transform을 따로 옮기면
/// 몸은 「transform 이동 + 클립 전진」만큼 갔다가, 클립이 끝나는 순간 원점(transform 자리)으로 튄다.
/// 시작 → 끝 수평 차이가 크면 그 클립이 범인이다(제자리판 선례: Treant@SlowWalk_InPlace.anim).
/// </summary>
public static class ClipBoneDriftProbe
{
    private const string Dir = "Assets/RelicFairy/Characters/Monster/Monster/ForestGuardian/Art/Animations/";
    private static readonly string[] Targets =
    {
        Dir + "Treant@Charge.fbx",
        Dir + "Treant@ChargeTackle01.fbx",
        Dir + "Treant@ChargeTackle02.fbx",
        Dir + "Treant@ChargeTackle03.fbx",
        Dir + "Treant@SlowWalk.fbx",
        Dir + "Treant@SlowWalk_InPlace.anim",
    };

    [MenuItem("RelicFairy/Animation/Probe Clip Bone Drift")]
    public static void Probe()
    {
        var sb = new StringBuilder();
        foreach (var path in Targets)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(asset is AnimationClip clip) || clip.name.StartsWith("__preview__")) continue;
                foreach (var b in AnimationUtility.GetCurveBindings(clip))
                {
                    if (b.propertyName != "m_LocalPosition.x" && b.propertyName != "m_LocalPosition.z") continue;
                    if (b.path.Split('/').Length > 2) continue;   // 루트 · 골반까지만
                    var c = AnimationUtility.GetEditorCurve(clip, b);
                    if (c == null || c.length == 0) continue;
                    float first = c.Evaluate(0f), last = c.Evaluate(clip.length);
                    float min = float.MaxValue, max = float.MinValue;
                    for (int i = 0; i <= 40; i++)
                    {
                        float v = c.Evaluate(clip.length * i / 40f);
                        min = Mathf.Min(min, v); max = Mathf.Max(max, v);
                    }
                    string line = $"[뼈이동] {clip.name} len={clip.length:F2} {b.path} {b.propertyName} " +
                                  $"시작={first:F3} 끝={last:F3} 차이={last - first:F3} 범위={max - min:F3}";
                    sb.AppendLine(line);
                    Debug.Log(line);
                }
            }
        }
        string outPath = System.IO.Path.Combine(Application.dataPath, "..", "Library", "RelicFairy_ClipBoneDrift.txt");
        System.IO.File.WriteAllText(outPath, sb.ToString());
    }
}
#endif
