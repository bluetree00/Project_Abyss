using System.Collections.Generic;
using Steamworks;
using UnityEngine;

/// <summary>
/// Steam 도전과제(11-06 사용자 「클라우드 · 도전과제를 스팀에 맞게 · 등록하고 작동」) — 게임 안 업적(Data/Quest/QuestDefinition.csv)을 같은 이름의 Steam 도전과제로 연다.
/// API 이름 = <c>ACH_</c> + 업적 codeName 대문자(reach_ch2 → ACH_REACH_CH2). Steamworks App Admin 「도전과제」에 같은 이름으로 등록 · 게시해야 열린다
/// (등록 전이면 이름마다 경고 한 번 · 게임은 그대로).
/// <para>언제 여나: 조건을 채운 순간(<see cref="QuestManager.onAchievementAchieved"/> — 제단에서 받기 전) · 제단에서 받은 순간 · 슬롯을 읽을 때 이미 채운 것(따라잡기 —
/// 이 업데이트 전 달성 · Steam 없이 켠 때 달성). 게임 안 업적은 슬롯마다 · Steam은 계정마다라 어느 슬롯이든 처음 채우면 열린다.</para>
/// 해제 상태는 Steam이 정본 — 따로 저장하지 않는다(Steam 문서: 자체 캐시는 PC를 옮기면 진행이 되돌아간 것처럼 보이게 한다).
/// StoreStats는 한 프레임에 모아 한 번(<see cref="Tick"/> · SteamManager.Update) — 자주 부르면 제한이 걸린다.
/// Steamworks.NET 2025.163(SDK 1.63)은 통계를 Steam이 미리 불러 둔다 — RequestCurrentStats 없이 바로 쓴다.
/// 다만 11-06 실측에서 게시 직후 자동 로드가 15초 넘게 0개였다(RequestUserStats로 직접 받자 0.4초) → 붙을 때 직접 받고, 오면 따라잡기를 한 번 더.
/// </summary>
public static class SteamAchievements
{
    // ── Constants ─────────────────────────────────────────────
    public const string Prefix = "ACH_";

    // ── Static ────────────────────────────────────────────────
    private static readonly HashSet<string> s_warned = new();
    private static QuestManager s_bound;
    private static bool s_dirty;
    private static CallResult<UserStatsReceived_t> s_statsCall;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { s_warned.Clear(); s_bound = null; s_dirty = false; s_statsCall?.Dispose(); s_statsCall = null; }

    // ── Public Methods ────────────────────────────────────────
    /// <summary>업적 codeName → Steam API 이름.</summary>
    public static string ApiName(string codeName) => Prefix + codeName.ToUpperInvariant();

    /// <summary>업적 관리자에 붙는다(앱 동안 한 번 · 다시 불러도 같은 관리자면 무동작) — 붙는 순간 지금 슬롯 따라잡기.</summary>
    public static void Bind(QuestManager qm)
    {
        if (qm == null || s_bound == qm) return;
        Unbind();
        s_bound = qm;
        qm.onAchievementAchieved  += OnAchieved;
        qm.onAchievementCompleted += OnAchieved;
        qm.onInitialized          += OnSlotLoaded;
        qm.onReloaded             += OnSlotLoaded;
        SyncAll();
        RequestStats();
    }

    public static void Unbind()
    {
        if (s_bound == null) return;
        s_bound.onAchievementAchieved  -= OnAchieved;
        s_bound.onAchievementCompleted -= OnAchieved;
        s_bound.onInitialized          -= OnSlotLoaded;
        s_bound.onReloaded             -= OnSlotLoaded;
        s_bound = null;
    }

    /// <summary>지금 슬롯에서 조건을 채웠거나 받은 업적을 모두 연다(이미 열린 건 건너뜀) — 새로 연 수.</summary>
    public static int SyncAll()
    {
        if (s_bound == null) return 0;
        int n = 0;
        foreach (var a in s_bound.ActiveAchievements)    if (a != null && a.IsCompltable && Unlock(a.CodeName)) n++;
        foreach (var a in s_bound.CompletedAchievements) if (a != null && Unlock(a.CodeName)) n++;
        return n;
    }

    /// <summary>한 업적을 연다 — 이번에 새로 열었으면 true(보내기는 <see cref="Tick"/>이 모아서).</summary>
    public static bool Unlock(string codeName)
    {
        if (string.IsNullOrEmpty(codeName) || !SteamManager.Initialized) return false;
        string api = ApiName(codeName);
        if (!SteamUserStats.GetAchievement(api, out bool achieved))
        {
            if (s_warned.Add(api)) Debug.LogWarning($"[SteamAchievements] Steam에 「{api}」 도전과제가 없다(Steamworks 등록 · 게시 전이거나 통계를 아직 못 받음) — 건너뜀");
            return false;
        }
        if (achieved || !SteamUserStats.SetAchievement(api)) return false;
        s_dirty = true;
        Debug.Log($"[SteamAchievements] 열림 — {api}");
        return true;
    }

    /// <summary>매 프레임(SteamManager.Update) — 새로 연 게 있으면 한 번만 보낸다.</summary>
    public static void Tick()
    {
        if (!s_dirty || !SteamManager.Initialized) return;
        s_dirty = false;
        SteamUserStats.StoreStats();
    }

    // ── Private Methods ───────────────────────────────────────
    /// <summary>내 Steam 통계를 직접 받아 온다 — 부트 따라잡기가 통계보다 먼저 돌면 「없다」로 건너뛰기 때문(받으면 <see cref="OnStatsReceived"/>).</summary>
    private static void RequestStats()
    {
        if (!SteamManager.Initialized) return;
        s_statsCall ??= CallResult<UserStatsReceived_t>.Create(OnStatsReceived);
        s_statsCall.Set(SteamUserStats.RequestUserStats(SteamUser.GetSteamID()));
    }

    // ── Event Handlers ────────────────────────────────────────
    private static void OnAchieved(Quest quest)
    {
        if (quest != null) Unlock(quest.CodeName);
    }

    private static void OnSlotLoaded() => SyncAll();

    private static void OnStatsReceived(UserStatsReceived_t e, bool ioFailure)
    {
        if (ioFailure || e.m_eResult != EResult.k_EResultOK)
        {
            Debug.LogWarning($"[SteamAchievements] Steam 통계 못 받음 — {(ioFailure ? "IO 실패" : e.m_eResult.ToString())}");
            return;
        }
        s_warned.Clear();   // 통계 오기 전 「없다」 경고는 무효 — 진짜 없는 것만 다시 경고
        int n = SyncAll();
        Debug.Log($"[SteamAchievements] Steam 통계 받음 — 도전과제 {SteamUserStats.GetNumAchievements()}개 · 따라잡기 {n}");
    }
}
