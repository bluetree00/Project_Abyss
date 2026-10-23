using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 무형검 3타(GhostSamurai_APose_Attack01_3_Inplace) — 두 번 베는 동작인데 슬래시 이벤트가 하나(SpawnSlashEffect2)뿐이라 판정이 한 번만 났다
/// (10-16 사용자 「무형검의 경우 마지막 타가 2타인 애니메이션이라 실제 타격도 2타가 적용되어야 해」).
/// ① 칼 뼈 속도를 재서 두 번째 베기 시점을 찾는다 → Temp/nameless_finisher_speed.txt
/// ② 그 시점에 SpawnSlashEffect3(능력 4번째 단계 = 둘째 베기) · 트레일 시작/끝 이벤트를 임포터로 넣는다(.meta는 Unity가 다시 쓴다).
/// ③ (10-19) 두 베기를 초반 두 휘두름으로 옮긴다 — 아래 <see cref="MoveSlashesEarly"/>. ②가 찾은 0.72는 칼을 거두는 느린 동작이었다.
/// </summary>
public static class NamelessFinisherSecondSlash
{
    private const string FbxPath  = "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/GhostSamurai_APose_Attack01_3_Inplace.FBX";
    private const string ClipName = "GhostSamurai_APose_Attack01_3_Inplace";
    private const string OutPath  = "Temp/nameless_finisher_speed.txt";
    private const string SecondSlashEvent = "SpawnSlashEffect3";
    private const int    Samples  = 120;
    private const float  TrailLead = 0.05f, TrailTail = 0.12f;   // 첫 베기와 같은 꼴(트레일 0.16 · 슬래시 0.22 · 끝 0.34)

    // 10-19 사용자 「무형검 막타의 2번째 타격 시각이 이상하다 — 애니는 2번째 휘두름이 초반에 나오니 거기에 맞게」.
    // 손 뼈 속도 실측(PlayerSkillClipBeatProbe.MeasureBasic): 휘두름 0.30초 · 0.53초(길이 2.50초 → 0.120 · 0.212), 1.88초는 거두는 동작(최고의 1/3).
    // 10-16 ②는 첫 이벤트(0.22) 뒤 0.12 넘게 떨어진 봉우리를 찾아 0.72를 골랐는데, 그 첫 이벤트부터 둘째 휘두름 자리였다. 이벤트는 봉우리 바로 앞(첫 베기와 같은 기준).
    private const float EarlySwing1 = 0.120f, EarlySwing2 = 0.212f;

    [MenuItem("RelicFairy/Animation/무형검 3타 — 두 베기를 초반 두 휘두름으로 (10-19)")]
    private static void MoveSlashesEarly()
    {
        var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (importer == null) { Debug.LogError("[NamelessFinisher] 임포터 없음"); return; }
        var clips = importer.clipAnimations;
        int ci = System.Array.FindIndex(clips, c => c.name == ClipName);
        if (ci < 0) { Debug.LogError("[NamelessFinisher] 클립 없음"); return; }

        var events = clips[ci].events.Where(e => e.functionName != "SpawnSlashEffect2" && e.functionName != SecondSlashEvent
                                              && e.functionName != "AE_BeginTrail" && e.functionName != "AE_EndTrail").ToList();
        float s1 = EarlySwing1 - 0.01f, s2 = EarlySwing2 - 0.01f;
        events.Add(new AnimationEvent { time = Mathf.Max(0.01f, s1 - TrailLead), functionName = "AE_BeginTrail" });
        events.Add(new AnimationEvent { time = s1, functionName = "SpawnSlashEffect2" });
        events.Add(new AnimationEvent { time = s2, functionName = SecondSlashEvent });
        events.Add(new AnimationEvent { time = s2 + TrailTail, functionName = "AE_EndTrail" });
        clips[ci].events = events.OrderBy(e => e.time).ToArray();
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        Debug.Log($"[NamelessFinisher] 두 베기 — SpawnSlashEffect2 @ {s1:0.000} · {SecondSlashEvent} @ {s2:0.000} · 트레일 {s1 - TrailLead:0.000}~{s2 + TrailTail:0.000} · " +
                  string.Join(" ", clips[ci].events.Select(e => $"{e.functionName}@{e.time:0.000}")));
    }

    [MenuItem("RelicFairy/Animation/무형검 3타 — 손 속도 재기")]
    private static void Measure()
    {
        var peaks = FindPeaks(out string report);
        File.WriteAllText(OutPath, report);
        Debug.Log($"[NamelessFinisher] 칼 속도 봉우리 {string.Join(" · ", peaks.Select(p => p.ToString("0.00")))} → {OutPath}");
    }

    [MenuItem("RelicFairy/Animation/무형검 3타 — 둘째 베기 이벤트 넣기")]
    private static void InsertSecondSlash()
    {
        var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (importer == null) { Debug.LogError("[NamelessFinisher] 임포터 없음"); return; }
        var clips = importer.clipAnimations;
        int ci = System.Array.FindIndex(clips, c => c.name == ClipName);
        if (ci < 0) { Debug.LogError("[NamelessFinisher] 클립 없음"); return; }
        var events = clips[ci].events.ToList();
        if (events.Any(e => e.functionName == SecondSlashEvent)) { Debug.Log("[NamelessFinisher] 이미 있다 — 건너뜀"); return; }

        var peaks = FindPeaks(out string report);
        File.WriteAllText(OutPath, report);
        var first = events.FirstOrDefault(e => e.functionName == "SpawnSlashEffect2");
        float firstAt = first != null ? first.time : 0.22f;
        // 첫 슬래시 이벤트 뒤의 봉우리 중 가장 이른 것(첫 베기 봉우리와 0.12 넘게 떨어진 것)
        float second = peaks.Where(t => t > firstAt + 0.12f && t < 0.9f).DefaultIfEmpty(-1f).First();
        if (second < 0f) { Debug.LogError($"[NamelessFinisher] 둘째 봉우리를 못 찾음 — {string.Join(" · ", peaks)}"); return; }

        events.Add(new AnimationEvent { time = Mathf.Max(firstAt + 0.05f, second - TrailLead - 0.01f), functionName = "AE_BeginTrail" });
        events.Add(new AnimationEvent { time = second - 0.01f, functionName = SecondSlashEvent });   // 칼이 가장 빠른 순간 바로 앞(첫 베기 이벤트와 같은 기준)
        events.Add(new AnimationEvent { time = Mathf.Min(0.95f, second + TrailTail), functionName = "AE_EndTrail" });
        clips[ci].events = events.OrderBy(e => e.time).ToArray();
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        Debug.Log($"[NamelessFinisher] 둘째 베기 이벤트 — {SecondSlashEvent} @ {second - 0.01f:0.00}(봉우리 {second:0.00}) · 트레일 앞뒤");
    }

    /// <summary>
    /// 오른손 속도를 정규화 시간으로 재고, 최고의 30% 넘는 봉우리 시점을 돌려준다. 이 FBX는 뼈 계층을 숨기는 최적화(Optimize Game Objects)라
    /// 뼈를 샘플링할 수 없어, 휴머노이드 클립의 오른손 목표 위치 곡선(RightHandT.x/y/z — 몸 기준)으로 잰다.
    /// </summary>
    private static float[] FindPeaks(out string report)
    {
        var clip = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<AnimationClip>().FirstOrDefault(c => c.name == ClipName);
        var sb = new StringBuilder();
        if (clip == null) { report = "클립 없음"; return new float[0]; }
        AnimationCurve cx = null, cy = null, cz = null;
        foreach (var b in AnimationUtility.GetCurveBindings(clip))
        {
            if (b.propertyName == "RightHandT.x") cx = AnimationUtility.GetEditorCurve(clip, b);
            else if (b.propertyName == "RightHandT.y") cy = AnimationUtility.GetEditorCurve(clip, b);
            else if (b.propertyName == "RightHandT.z") cz = AnimationUtility.GetEditorCurve(clip, b);
        }
        if (cx == null || cy == null || cz == null) { report = "오른손 곡선(RightHandT) 없음"; return new float[0]; }
        var speeds = new float[Samples + 1];
        var pos = new Vector3[Samples + 1];
        float dt = clip.length / Samples;
        for (int i = 0; i <= Samples; i++)
        {
            float t = i * dt;
            pos[i] = new Vector3(cx.Evaluate(t), cy.Evaluate(t), cz.Evaluate(t));
            speeds[i] = i == 0 ? 0f : (pos[i] - pos[i - 1]).magnitude / Mathf.Max(1e-4f, dt);
        }
        float max = speeds.Max();
        var peaks = new System.Collections.Generic.List<float>();
        sb.AppendLine($"{ClipName} · 길이 {clip.length:0.00}초 · 오른손 곡선 · 최고 {max:0.00}/초");
        for (int i = 1; i < Samples; i++)
        {
            float n = (float)i / Samples;
            bool peak = speeds[i] >= speeds[i - 1] && speeds[i] >= speeds[i + 1] && speeds[i] > max * 0.3f;   // 내려 베기(0.70~0.78)는 올려 베기보다 느리지만 경로가 가장 길다
            if (peak && (peaks.Count == 0 || n - peaks[peaks.Count - 1] > 0.06f)) peaks.Add(n);
            sb.AppendLine($"{n:0.000}\t{speeds[i]:0.00}\t{pos[i].x:0.000}\t{pos[i].y:0.000}\t{pos[i].z:0.000}{(peak ? "\t봉우리" : "")}");
        }
        report = sb.ToString();
        return peaks.ToArray();
    }
}
