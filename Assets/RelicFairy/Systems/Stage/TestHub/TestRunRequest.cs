using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 테스트 허브(Scenes/Test/BaseCamp_Test)가 세우는 런 시작 요청.
///
/// 빌드 게임 흐름에서는 아무도 <see cref="Set"/>을 부르지 않으므로 항상 비활성이고, 아래 분기들은 전부 평소 동작으로 떨어진다.
///   · <see cref="ConsumeBossApproach"/> — 챕터 진입을 대기방 대신 보스 대기방(PreBoss)부터 시작할지
///   · <see cref="ResolveArenaKey"/>     — 테스트 대상 챕터의 보스방에 다른 아레나 프리팹을 쓸지
///   · <see cref="TryReturnToHub"/>      — 런이 끝나면 베이스캠프 대신 테스트 허브로 돌아갈지(에디터 전용)
/// </summary>
public static class TestRunRequest
{
    // ── Private ───────────────────────────────────────────────────
    private static bool _bossApproachPending;

    // ── Properties ────────────────────────────────────────────────
    public static bool      Active            { get; private set; }
    public static ChapterId Chapter           { get; private set; }
    /// <summary>테스트 대상 챕터 보스방에 쓸 아레나 Addressable 키. 비면 룸풀 값 그대로.</summary>
    public static string    BossArenaOverride { get; private set; }
    public static string    HubScenePath      { get; private set; }

    // ── Public Methods ────────────────────────────────────────────
    public static void Set(ChapterId chapter, bool startAtBossApproach, string bossArenaOverride, string hubScenePath)
    {
        Active               = true;
        Chapter              = chapter;
        _bossApproachPending = startAtBossApproach;
        BossArenaOverride    = string.IsNullOrEmpty(bossArenaOverride) ? null : bossArenaOverride;
        HubScenePath         = hubScenePath;
        Debug.Log($"[TestRun] 요청 — {chapter} · {(startAtBossApproach ? "보스 대기방 직행" : "대기방부터")} · 보스 아레나 {BossArenaOverride ?? "룸풀 기본"}");
    }

    public static void Clear()
    {
        Active               = false;
        Chapter              = default;
        _bossApproachPending = false;
        BossArenaOverride    = null;
        HubScenePath         = null;
    }

    /// <summary>이 챕터 진입이 보스 대기방 직행이면 true를 돌려주고 소비한다. 다음 챕터부터는 정상 시작.</summary>
    public static bool ConsumeBossApproach(ChapterId chapter)
    {
        if (!Active || !_bossApproachPending || chapter != Chapter) return false;
        _bossApproachPending = false;
        return true;
    }

    /// <summary>방을 지을 때 쓸 아레나 키. 테스트 대상 챕터의 보스방이면 대체 키를, 아니면 룸풀 값을 돌려준다.</summary>
    public static string ResolveArenaKey(ZonePoolEntry entry, ChapterId chapter)
    {
        if (entry == null) return null;
        if (Active && BossArenaOverride != null && chapter == Chapter
            && string.Equals(entry.category, "Boss", StringComparison.OrdinalIgnoreCase))
            return BossArenaOverride;
        return entry.arena_template_key;
    }

    /// <summary>
    /// 테스트 런이 끝났을 때 허브로 돌아간다. 처리했으면 true — 호출부는 베이스캠프 로드를 건너뛴다.
    /// 허브 씬은 빌드 설정에 없으므로 에디터 API로만 로드할 수 있다.
    /// </summary>
    public static bool TryReturnToHub()
    {
#if UNITY_EDITOR
        if (!Active || string.IsNullOrEmpty(HubScenePath)) return false;

        string path = HubScenePath;
        Clear();
        Managers.Sound?.StopBgm();
        Managers.UI.ClearOnSceneTransition();
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
            path, new LoadSceneParameters(LoadSceneMode.Single));
        Debug.Log($"[TestRun] 런 종료 — 테스트 허브로 복귀 ({path})");
        return true;
#else
        return false;
#endif
    }

    // ── Private Methods ───────────────────────────────────────────
    // 도메인 리로드를 끈 플레이 모드에서도 이전 플레이의 요청이 남지 않게 한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Clear();
}
