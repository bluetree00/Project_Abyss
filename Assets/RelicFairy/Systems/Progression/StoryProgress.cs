using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 이야기 진행(계정 영구) — <b>봉인기 → 붕괴 → 악몽기 → 엔딩</b>.
///
/// 처음 목표는 네 보스를 봉인하는 것이다. 리치를 봉인하는 순간 봉인이 버티지 못하고 무너지고(붕괴),
/// 이후 세계는 악몽기 — 모든 보스의 2페이지가 열리고 완전히 처치해야 한다. 악몽기 리치를 처음 쓰러뜨리면 엔딩.
///
/// <para><b>저장소를 새로 만들지 않는다.</b>
/// · 붕괴(악몽기) = 모든 보스의 봉인 해제 — <see cref="BossSealService"/>(기존 「깨짐 = 2페이지 열림」 의미 그대로).
/// · 봉인·처치·장면 기록 = <see cref="UserGameData.records"/>(<c>seal.lich:1</c> 형식).</para>
///
/// 설계: 바탕화면 기획 「최종장이후_사이클시나리오」 v2 · 「리치보스_완전설계」.
/// </summary>
public static class StoryProgress
{
    // ── 보스 id (BossSealService 키와 같다) ────────────────
    public const string ForestGuardian = "forestguardian";
    public const string Dragon         = "dragon";
    public const string DeathKnight    = "deathknight";
    public const string Lich           = "lich";

    private static readonly string[] AllBosses = { ForestGuardian, Dragon, DeathKnight, Lich };

    /// <summary>사망 몇 번째에 그림자가 멀린의 이름을 부르는가(<c>RunFail_T25_*</c>와 같은 문턱).</summary>
    private const int MerlinNameDeathCount = 25;

    // ── 기록 키 ────────────────────────────────────────
    public static class Rec
    {
        public const string Ending     = "story.ending";    // 악몽기 리치 첫 처치
        public const string LichMet    = "story.lichMet";   // 리치 첫 조우(멀린 이름 공개)
        public const string SealPrefix = "seal.";           // 봉인기에 쓰러뜨림
        public const string KillPrefix = "kill.";           // 악몽기에 쓰러뜨림
        public const string SeenPrefix = "seen.";           // 1회성 장면·대사를 봤음
    }

    // 붕괴를 일으킨 쓰러뜨림은 붕괴 시점에 「봉인」으로 이미 기록했다 — 뒤따르는 방 클리어 알림이
    // 악몽기 기준으로 「처치」를 또 적지 않게 한 번 건너뛴다.
    private static string s_skipNextDefeatOf;

    // 악몽기 리치를 처음 쓰러뜨린 전투 — 보스 클리어 흐름이 엔딩 연출을 틀고 내린다.
    private static bool s_endingPending;

#if UNITY_EDITOR
    /// <summary>에디터 설정 키 — 테스트 허브 메뉴(RelicFairy/Test Run/Story)가 쓴다.</summary>
    public const string DebugOverridePrefsKey = "RelicFairy.Story.NightmareOverride";

    /// <summary>
    /// 에디터 테스트 전용 — -1 = 저장값, 0 = 봉인기로 보기, 1 = 악몽기로 보기.
    /// 플레이 진입(도메인 리로드)을 넘어 유지되도록 에디터 설정에 둔다. <b>켜져 있으면 이야기 기록을 저장하지 않는다</b>
    /// (가짜 상태로 진짜 세이브를 오염시키지 않게).
    /// </summary>
    // 매 프레임 판정 경로라 캐시한다. 메뉴가 값을 바꾸면 RefreshDebugOverride로 다시 읽는다.
    private static int? s_debugOverride;

    public static int DebugNightmareOverride => s_debugOverride ??= UnityEditor.EditorPrefs.GetInt(DebugOverridePrefsKey, -1);

    public static void RefreshDebugOverride() => s_debugOverride = null;
#endif

    /// <summary>테스트 오버라이드가 켜져 있으면 기록을 남기지 않는다.</summary>
    private static bool PersistenceSuspended
    {
        get
        {
#if UNITY_EDITOR
            return DebugNightmareOverride >= 0;
#else
            return false;
#endif
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_skipNextDefeatOf = null;
        s_endingPending    = false;
#if UNITY_EDITOR
        s_debugOverride = null;
        if (DebugNightmareOverride >= 0)
            Debug.LogWarning($"[Story] 테스트 오버라이드 켜짐 — {(DebugNightmareOverride == 1 ? "악몽기" : "봉인기")}로 본다. 이야기 기록은 저장하지 않는다.");
#endif
    }

    // ── 상태 ───────────────────────────────────────────

    /// <summary>붕괴를 거쳐 악몽기에 들어섰는가. 리치의 봉인이 깨졌다 = 붕괴가 일어났다.</summary>
    public static bool IsNightmare
    {
        get
        {
#if UNITY_EDITOR
            if (DebugNightmareOverride >= 0) return DebugNightmareOverride == 1;
#endif
            return BossSealService.IsSealBroken(Lich);
        }
    }

    public static bool HasEnded  => Get(Rec.Ending) > 0;
    public static bool HasMetLich => Get(Rec.LichMet) > 0;

    /// <summary>대사창에 멀린의 이름을 보여도 되는가 — 리치가 이름을 부른 뒤, 또는 그림자가 이름을 부르는 사망 횟수 이후.</summary>
    public static bool IsMerlinNamed => HasMetLich || RunReturnTracker.DeathCount >= MerlinNameDeathCount;

    public static bool IsSealed(string bossId) => Get(Rec.SealPrefix + bossId) > 0;
    public static bool IsKilled(string bossId) => Get(Rec.KillPrefix + bossId) > 0;
    public static bool IsSeen(string key)      => Get(Rec.SeenPrefix + key) > 0;

    /// <summary>봉인기에 봉인한 보스 수(0~4).</summary>
    public static int SealedCount  => Count(Rec.SealPrefix);
    /// <summary>악몽기에 처치한 보스 수(0~4).</summary>
    public static int KilledCount  => Count(Rec.KillPrefix);

    /// <summary>챕터 → 보스 id. 챕터마다 보스가 하나로 고정돼 있다.</summary>
    public static string BossIdForChapter(ChapterId chapter) => chapter switch
    {
        ChapterId.Chapter1 => ForestGuardian,
        ChapterId.Chapter2 => Dragon,
        ChapterId.Chapter3 => DeathKnight,
        ChapterId.Chapter4 => Lich,
        _                  => null,
    };

    // ── 기록 ───────────────────────────────────────────

    /// <summary>
    /// 보스를 쓰러뜨렸다(방 클리어 시점). 봉인기면 「봉인」, 악몽기면 「처치」로 남긴다. 처음 남겼으면 true.
    /// </summary>
    public static bool RecordBossDefeat(string bossId)
    {
        if (string.IsNullOrEmpty(bossId)) return false;
        if (s_skipNextDefeatOf == bossId)
        {
            s_skipNextDefeatOf = null;
            return false;
        }

        string key = (IsNightmare ? Rec.KillPrefix : Rec.SealPrefix) + bossId;
        bool changed = SetFlag(key);
        if (changed) Save();
        Debug.Log($"[Story] {(IsNightmare ? "처치" : "봉인")} 기록 — {bossId}{(changed ? " (처음)" : "")}");
        return changed;
    }

    /// <summary>
    /// 리치를 봉인하는 순간 — 모든 봉인이 무너진다. 리치는 「봉인」으로 남기고(붕괴 직전의 사건),
    /// 네 보스의 봉인을 모두 깨 악몽기로 넘어간다. 이미 악몽기면 아무것도 하지 않는다.
    /// </summary>
    public static void TriggerCollapse()
    {
        if (PersistenceSuspended)
        {
            s_skipNextDefeatOf = Lich;
            Debug.Log("[Story] 붕괴 (테스트 오버라이드 — 저장 안 함)");
            return;
        }
        if (BossSealService.IsSealBroken(Lich)) return;

        var data = BackendGameData.Instance?.Data;
        if (data == null)
        {
            Debug.LogWarning("[Story] 백엔드 데이터 없음 — 붕괴를 저장하지 못했다");
            return;
        }

        SetFlag(Rec.SealPrefix + Lich);
        s_skipNextDefeatOf = Lich;
        foreach (var id in AllBosses)
            data.BreakBossSeal(id);

        Save();
        Debug.Log("[Story] 붕괴 — 모든 봉인이 무너졌다. 악몽기 시작");
    }

    /// <summary>
    /// 저장 정합 — 리치 봉인만 깨진 옛 세이브(붕괴 규칙 이전)면 나머지 보스 봉인도 깨서 악몽기와 맞춘다.
    /// 거점 진입 시 한 번 부른다.
    /// </summary>
    public static void EnsureConsistency()
    {
        if (PersistenceSuspended) return;
        var data = BackendGameData.Instance?.Data;
        if (data == null || !data.IsBossSealBroken(Lich)) return;

        bool changed = false;
        foreach (var id in AllBosses)
            changed |= data.BreakBossSeal(id);
        changed |= SetFlag(Rec.SealPrefix + Lich);   // 옛 규칙에서도 첫 리치 격파 = 봉인 퇴각이었다

        if (changed)
        {
            Save();
            Debug.Log("[Story] 옛 세이브 정합 — 리치 봉인 해제를 붕괴로 이관");
        }
    }

    public static void MarkLichMet()
    {
        if (SetFlag(Rec.LichMet)) Save();
    }

    /// <summary>
    /// 악몽기 리치 첫 처치 — 엔딩. 처음이면 true이고, 보스 클리어 흐름이 <see cref="ConsumePendingEnding"/>로 엔딩 연출을 튼다.
    /// 테스트 오버라이드 중엔 저장하지 않지만, 아직 엔딩 전 세이브면 연출은 매번 볼 수 있게 한다.
    /// </summary>
    public static bool MarkEnding()
    {
        if (PersistenceSuspended)
        {
            if (HasEnded) return false;
            s_endingPending = true;
            Debug.Log("[Story] 엔딩 도달 (테스트 오버라이드 — 저장 안 함)");
            return true;
        }

        if (!SetFlag(Rec.Ending)) return false;
        Save();
        s_endingPending = true;
        Debug.Log("[Story] 엔딩 도달");
        return true;
    }

    /// <summary>이번 전투에서 엔딩에 막 도달했는가 — 한 번 읽으면 내려간다.</summary>
    public static bool ConsumePendingEnding()
    {
        bool pending = s_endingPending;
        s_endingPending = false;
        return pending;
    }

    /// <summary>1회성 장면·대사를 봤다고 남긴다. 처음이면 true.</summary>
    public static bool MarkSeen(string key)
    {
        if (!SetFlag(Rec.SeenPrefix + key)) return false;
        Save();
        return true;
    }

    // ── 내부 ───────────────────────────────────────────

    private static int Get(string key) => BackendGameData.Instance?.Data?.GetRecord(key) ?? 0;

    private static bool SetFlag(string key)
    {
        if (PersistenceSuspended)
        {
            Debug.Log($"[Story] 기록 생략(테스트 오버라이드) — {key}");
            return false;
        }
        return BackendGameData.Instance?.Data?.SetRecordMax(key, 1) ?? false;
    }

    private static int Count(string prefix)
    {
        int n = 0;
        foreach (var id in AllBosses)
            if (Get(prefix + id) > 0) n++;
        return n;
    }

    private static void Save() => BackendGameData.Instance?.SaveAsync().Forget();
}
