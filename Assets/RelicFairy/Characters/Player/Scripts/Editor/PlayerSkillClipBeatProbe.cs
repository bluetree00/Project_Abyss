using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

/// <summary>
/// [실측 도구 · 편집 모드] 플레이어 스킬 클립이 <b>실제로 닿는 순간</b>을 잰다 — 판정을 고정 타이머 대신 이 순간에 맞추기 위한 근거.
///
/// 리치 도구(<c>RelicFairy/Boss/Lich/Measure Cast Clip Beats</c>, LichClipBeatProbe)의 방식을 플레이어용으로 옮겼다(리치 도구는 그대로 둔다).
/// 무기 애니메이션 세트마다 스킬 상태(이름에 Skill이 든 매핑)의 <b>교체 클립</b>을 Addressables 주소로 찾아,
/// 플레이어 휴머노이드 모델에 1/60초 간격으로 샘플하며 <b>손 속도</b> 곡선을 뽑는다. 휘두름은 손이 가장 빠른 순간에 닿는다.
/// 같은 표에 스킬 동작 데이터의 고정 타이머(○○Delay 등)를 나란히 적어, 지금 판정이 얼마나 어긋나는지 본다.
/// 프리팹을 격리 장면에 열어 샘플하므로 열린 씬은 건드리지 않는다. 결과: Logs/player_skill_clip_beats.txt
/// </summary>
public static class PlayerSkillClipBeatProbe
{
    private const string PlayerPrefab = "Assets/RelicFairy/Characters/Player/Gawain/Prefabs/PlayerCharacter.prefab";
    private const float  Step = 1f / 60f;
    private const string OutPath = "Logs/player_skill_clip_beats.txt";

    [MenuItem("RelicFairy/Debug/플레이어 스킬 클립 닿는 순간 실측 (편집 모드)")]
    public static void Measure()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogWarning("[스킬클립] Addressables 설정이 없다"); return; }
        var byAddress = new Dictionary<string, string>();
        foreach (var g in settings.groups)
        {
            if (g == null) continue;
            foreach (var e in g.entries) if (!string.IsNullOrEmpty(e.address)) byAddress[e.address] = e.AssetPath;
        }

        var sb   = new StringBuilder();
        var root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
        try
        {
            var anim = root.GetComponentInChildren<Animator>(true);
            if (anim == null || !anim.isHuman) { sb.AppendLine("⚠ 휴머노이드 애니메이터가 없다"); }
            else
            {
                var right = anim.GetBoneTransform(HumanBodyBones.RightHand);
                var left  = anim.GetBoneTransform(HumanBodyBones.LeftHand);
                sb.AppendLine($"모델 {PlayerPrefab} · 오른손 {right?.name} · 왼손 {left?.name}");

                foreach (var setPath in AssetDatabase.FindAssets("t:WeaponAnimationSetSO").Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
                {
                    var set = AssetDatabase.LoadAssetAtPath<WeaponAnimationSetSO>(setPath);
                    if (set == null) continue;
                    sb.AppendLine($"\n■ {Path.GetFileNameWithoutExtension(setPath)}");
                    foreach (var m in SkillMappings(set))
                    {
                        var clip = LoadClip(byAddress, m.addressableKey);
                        if (clip == null) { sb.AppendLine($"  {m.baseClipName} → {m.addressableKey}: (클립 못 찾음)"); continue; }
                        sb.AppendLine("  " + MeasureClip(m.baseClipName, clip, anim.gameObject, root.transform, right, left));
                    }
                }
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        sb.AppendLine("\n■ 스킬 동작 데이터의 고정 타이머(초) — 판정이 이 시각에 나간다");
        foreach (var p in AssetDatabase.FindAssets("t:SkillBehaviorSO").Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
        {
            var so = AssetDatabase.LoadAssetAtPath<SkillBehaviorSO>(p);
            if (so == null) continue;
            var ser = new SerializedObject(so);
            var it = ser.GetIterator();
            var parts = new List<string>();
            string state = "";
            for (bool enter = true; it.NextVisible(enter); enter = false)
            {
                string n = it.name;
                if (it.propertyType == SerializedPropertyType.Float &&
                    (n.EndsWith("Delay") || n.EndsWith("Duration") || n == "chargeTime" || n == "castTime" || n == "hitDelay"))
                    parts.Add($"{n} {it.floatValue:0.###}");
                if (it.propertyType == SerializedPropertyType.String && n == "animationOverride" && !string.IsNullOrEmpty(it.stringValue))
                    state = it.stringValue;
            }
            sb.AppendLine($"  {Path.GetFileNameWithoutExtension(p),-22} {(state != "" ? "상태 " + state + " · " : "")}{string.Join(" · ", parts)}");
        }

        Directory.CreateDirectory("Logs");
        File.WriteAllText(OutPath, sb.ToString(), Encoding.UTF8);
        Debug.Log($"[스킬클립] {OutPath}\n" + sb);
    }

    /// <summary>매핑은 그룹(지상·공중)별 목록에 들어 있다 — <see cref="WeaponAnimationSetSO.GetAllMappings"/>로 모은다.</summary>
    private static IEnumerable<WeaponAnimationSetSO.ClipMapping> SkillMappings(WeaponAnimationSetSO set)
        => set.GetAllMappings().Where(m => m != null && !string.IsNullOrEmpty(m.baseClipName) && m.baseClipName.Contains("Skill"));

    private static AnimationClip LoadClip(Dictionary<string, string> byAddress, string key)
    {
        if (string.IsNullOrEmpty(key) || !byAddress.TryGetValue(key, out var path)) return null;
        var direct = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (direct != null) return direct;
        // FBX — 미리보기 클립(__preview__)을 뺀 첫 클립
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    }

    private static string MeasureClip(string state, AnimationClip clip, GameObject target, Transform root, Transform right, Transform left)
    {
        int frames = Mathf.Max(2, Mathf.CeilToInt(clip.length / Step) + 1);
        var vr = new float[frames];
        var vl = new float[frames];
        Vector3 pr = Vector3.zero, pl = Vector3.zero;
        for (int i = 0; i < frames; i++)
        {
            clip.SampleAnimation(target, Mathf.Min(clip.length, i * Step));
            Vector3 r = right != null ? root.InverseTransformPoint(right.position) : Vector3.zero;
            Vector3 l = left  != null ? root.InverseTransformPoint(left.position)  : Vector3.zero;
            if (i > 0) { vr[i] = (r - pr).magnitude / Step; vl[i] = (l - pl).magnitude / Step; }
            pr = r; pl = l;
        }
        // 더 빠르게 움직이는 손을 기준으로. 첫 두 프레임은 자세 튐이 섞여 뺀다.
        var v = Max(vr, 2) >= Max(vl, 2) ? vr : vl;
        var peaks = TopPeaks(v, 3, minGapFrames: 9);
        var curve = new StringBuilder();
        for (int i = 0; i < frames; i += 6) curve.Append($" {i * Step:F1}:{v[i]:F1}");
        return $"{state} → {clip.name}: 길이 {clip.length:F2}s · 손 속도 봉우리 " +
               string.Join(", ", peaks.Select(i => $"{i * Step:F2}s({v[i]:F1}m/s)")) +
               $"\n      곡선(초:m/s){curve}";
    }

    private static float Max(float[] a, int from) { float m = 0f; for (int i = from; i < a.Length; i++) m = Mathf.Max(m, a[i]); return m; }

    /// <summary>국소 최대점 중 큰 것부터 n개(서로 minGapFrames 이상 떨어진 것만), 시간순.</summary>
    private static List<int> TopPeaks(float[] v, int n, int minGapFrames)
    {
        var cands = new List<int>();
        for (int i = 3; i < v.Length - 1; i++) if (v[i] >= v[i - 1] && v[i] >= v[i + 1] && v[i] > 0.5f) cands.Add(i);
        var picked = new List<int>();
        foreach (int i in cands.OrderByDescending(i => v[i]))
        {
            if (picked.Any(p => Mathf.Abs(p - i) < minGapFrames)) continue;
            picked.Add(i);
            if (picked.Count >= n) break;
        }
        picked.Sort();
        return picked;
    }
}
