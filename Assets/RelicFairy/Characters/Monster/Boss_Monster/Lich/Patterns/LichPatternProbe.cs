using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 패턴 검증 기록기(에디터 전용) — 09-19 사용자 지시 「패턴 하나하나 검증 · 가이드라인이 사전에 적절히 제시되는지」.
/// 패턴 실행마다 예고가 뜬 시각 · 빨강 신호 시각 · 타격 순간(Impact)을 모아
/// 「예고 → 타격」 「빨강 → 타격」 간격을 잰다. 간격이 <see cref="MinLead"/>보다 짧으면 예고 부족으로 표시한다.
/// 빌드에서는 호출이 사라진다(Conditional).
/// </summary>
public static class LichPatternProbe
{
    /// <summary>반응 가능한 최소 예고(초) — 사람 반응 약 0.25초 + 회피 시동.</summary>
    public const float MinLead = 0.4f;

#if UNITY_EDITOR
    private const float InsideSampleGap = 0.2f;

    private sealed class Run
    {
        public string Pattern;
        public float  Start;
        public readonly List<string> Strikes = new();
        public int    Short;
        public int    Landed;
        public int    Inside;
    }

    private static readonly List<Run>    s_runs       = new();
    private static readonly Queue<float> s_telegraphs = new();   // 아직 타격과 짝짓지 않은 예고의 뜬 시각(먼저 뜬 것부터)
    private static Run   s_current;
    private static float s_lastTelegraph = -1f;
    private static float s_lastSignal    = -1f;
    private static float s_lastInside    = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Clear();
#endif

    /// <summary>패턴이 시작됐다(자동 선택 · 강제 실행).</summary>
    [Conditional("UNITY_EDITOR")]
    public static void BeginPattern(string name)
    {
#if UNITY_EDITOR
        s_current = new Run { Pattern = name, Start = Time.time };
        s_runs.Add(s_current);
        s_telegraphs.Clear();
        s_lastTelegraph = s_lastSignal = -1f;
#endif
    }

    /// <summary>예고가 떴다.</summary>
    [Conditional("UNITY_EDITOR")]
    public static void Telegraph()
    {
#if UNITY_EDITOR
        s_lastTelegraph = Time.time;
        s_lastSignal    = -1f;
        s_telegraphs.Enqueue(Time.time);
#endif
    }

    /// <summary>예고가 빨강(신호)으로 바뀌었다.</summary>
    [Conditional("UNITY_EDITOR")]
    public static void Signal()
    {
#if UNITY_EDITOR
        s_lastSignal = Time.time;
#endif
    }

    /// <summary>타격 순간(휘두름 · 폭발 · 착탄) — 맞았으면 <paramref name="landed"/>.</summary>
    [Conditional("UNITY_EDITOR")]
    public static void Strike(string kind, bool landed)
    {
#if UNITY_EDITOR
        if (s_current == null) return;
        float now  = Time.time;
        // 먼저 뜬 예고부터 짝짓는다 — 예고가 여러 개 겹쳐 떠 있는 패턴(어둠의 비 · 마력탄)에서 가장 최근 예고로 재면 짧게 나온다.
        float from = s_telegraphs.Count > 0 ? s_telegraphs.Dequeue() : s_lastTelegraph;
        float lead = from >= 0f ? now - from : -1f;
        float sig  = s_lastSignal    >= 0f ? now - s_lastSignal    : -1f;
        bool  tooShort = lead < MinLead;
        if (tooShort) s_current.Short++;
        if (landed)   s_current.Landed++;
        s_current.Strikes.Add($"{kind} t={now - s_current.Start:0.00} 예고→{Fmt(lead)} 빨강→{Fmt(sig)}{(tooShort ? " ⚠예고부족" : "")}{(landed ? " 맞음" : "")}");
#endif
    }

    /// <summary>판정 안에 플레이어가 있었다(회피 무적으로 흘렸어도) — 0.2초에 한 번만 센다.</summary>
    [Conditional("UNITY_EDITOR")]
    public static void Inside()
    {
#if UNITY_EDITOR
        if (s_current == null || Time.time - s_lastInside < InsideSampleGap) return;
        s_lastInside = Time.time;
        s_current.Inside++;
#endif
    }

    public static void Clear()
    {
#if UNITY_EDITOR
        s_runs.Clear();
        s_telegraphs.Clear();
        s_current = null;
        s_lastTelegraph = s_lastSignal = s_lastInside = -1f;
#endif
    }

    /// <summary>지금까지의 기록 — 패턴 실행마다 한 줄 + 타격별 간격.</summary>
    public static string Report()
    {
#if UNITY_EDITOR
        var sb = new StringBuilder();
        sb.AppendLine($"[LichProbe] 패턴 실행 {s_runs.Count}회 — 예고 최소 {MinLead:0.00}초");
        foreach (var r in s_runs)
        {
            sb.AppendLine($"■ {r.Pattern} — 타격 {r.Strikes.Count} · 예고부족 {r.Short} · 판정 안 {r.Inside} · 맞음 {r.Landed}");
            foreach (var s in r.Strikes) sb.AppendLine("   " + s);
        }
        return sb.ToString();
#else
        return string.Empty;
#endif
    }

#if UNITY_EDITOR
    private static string Fmt(float seconds) => seconds < 0f ? "없음" : $"{seconds:0.00}s";
#endif
}
}
