using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 11-02 Q27 빌드 끊김 기록 — 켠 범위(리치전 · 추방 추락) 동안만 50 ms 넘는 프레임을 Player.log에 한 줄로 남긴다(빌드엔 프로파일러가 없다).
/// 줄 = 「[Hitch] N ms · +T초 · 범위 · 보스 상태」, 범위가 모두 닫히면 「[Hitch] 끝 — 길이 · 프레임 · 50 ms 넘음 · 최대」.
/// 범위가 없으면 매 프레임 개수 하나만 본다. 글 만들기는 끊긴 프레임에서만(진단 기록).
/// </summary>
public sealed class HitchLog : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    private const float ThresholdSeconds = 0.05f;

    // ── Static ────────────────────────────────────────────────────
    private static HitchLog s_instance;
    private static readonly Dictionary<object, string> s_scopes = new();
    private static MonsterBase s_boss;
    private static float s_t0;
    private static int   s_frames, s_hitches;
    private static float s_maxMs;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Update()
    {
        if (s_scopes.Count == 0) return;
        s_frames++;
        float dt = Time.unscaledDeltaTime;
        if (dt >= ThresholdSeconds) Report(dt);
    }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>범위를 연다(같은 owner면 이름만 바꾼다). <paramref name="boss"/>를 주면 줄에 그 보스 상태를 적는다.</summary>
    public static void Enter(object owner, string label, MonsterBase boss = null)
    {
        if (owner == null) return;
        if (s_scopes.Count == 0)
        {
            s_t0 = Time.realtimeSinceStartup;
            s_frames = s_hitches = 0;
            s_maxMs = 0f;
        }
        s_scopes[owner] = label;
        if (boss != null) s_boss = boss;
        if (s_instance == null)
        {
            var go = new GameObject("@HitchLog");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<HitchLog>();
        }
    }

    /// <summary>범위를 닫는다. 모두 닫히면 요약 한 줄.</summary>
    public static void Exit(object owner)
    {
        if (owner == null || !s_scopes.Remove(owner)) return;
        if (ReferenceEquals(owner, s_boss)) s_boss = null;
        if (s_scopes.Count == 0 && s_frames > 0)
            Debug.Log($"[Hitch] 끝 — {Time.realtimeSinceStartup - s_t0:0}초 · 프레임 {s_frames} · 50 ms 넘음 {s_hitches} · 최대 {s_maxMs:0} ms");
    }

    // ── Private Methods ───────────────────────────────────────────
    private static void Report(float dt)
    {
        float ms = dt * 1000f;
        s_hitches++;
        if (ms > s_maxMs) s_maxMs = ms;
        string state = s_boss != null ? s_boss.DebugStateName : "-";
        Debug.Log($"[Hitch] {ms:0} ms · +{Time.realtimeSinceStartup - s_t0:0.0}초 · {string.Join("+", s_scopes.Values)} · {state}");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_instance = null;
        s_scopes.Clear();
        s_boss = null;
        s_frames = s_hitches = 0;
        s_maxMs = 0f;
    }
}
