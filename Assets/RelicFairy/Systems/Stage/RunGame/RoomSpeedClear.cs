using UnityEngine;

/// <summary>
/// 해방기 재미 F1 「속도 보상」(10-29 설계 §6-2 · 판정 2a / 보상 등급 · 표시 4c) — 방을 정해진 시간 안에 끝내면 보상 등급 +1.
///   · 정해진 시간 = 그 방 기준 시간(<see cref="RunTimeGrade.RoomBase"/>) × <see cref="LimitShare"/> — 챕터 시계 「A」 문턱과 같다(랭킹과 같은 방향).
///   · 잰 시간 = 그 방에서 <b>싸운 시간</b>만 — <see cref="RunCombatClock"/>이 매 프레임 <see cref="Enter"/>(방이 바뀌면 새로) · 싸우는 동안 <see cref="Tick"/>.
///     고르기 창 · 대사 · 컷신 · 웨이브 사이 빈 시간은 빠진다.
///   · 해방기 · 악몽만 · 일반 · 정예 · 보스 직전 · 정예 보스 방만(이벤트 = 도전 보상이 따로 · 보스 = 보스 보상).
/// </summary>
public static class RoomSpeedClear
{
    // ── Constants ──
    public const float LimitShare = RunTimeGrade.A;

    // ── Static ──
    private static GameRunSession s_run;
    private static int s_serial = -1;
    private static RoomPlanKind s_kind;
    private static int s_chapter;
    private static float s_roomLimit;   // 이 방 정해진 시간 — 처음 싸움이 붙을 때 드러낸다(대기방 · 재화방처럼 안 싸운 방은 끝까지 0)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { s_run = null; s_serial = -1; s_roomLimit = 0f; FightSeconds = 0f; LimitSeconds = 0f; }

    // ── Properties ──
    /// <summary>지금 방에서 싸운 시간(초).</summary>
    public static float FightSeconds { get; private set; }
    /// <summary>지금 방 정해진 시간(초) — 0이면 속도 보상이 없는 방이거나 아직 싸움이 안 붙었다.</summary>
    public static float LimitSeconds { get; private set; }
    public static bool IsActive => LimitSeconds > 0f;
    /// <summary>지금(방금 끝낸) 방을 정해진 시간 안에 끝냈다 — 보상 굴림이 본다. 싸움이 끝나면 더 늘지 않으니 클리어 뒤에도 그대로다.</summary>
    public static bool IsFast => IsActive && FightSeconds <= LimitSeconds;

    // ── Public Methods ──
    public static bool Applies(RoomPlanKind kind, StoryEra era)
        => era != StoryEra.Sealed && kind is RoomPlanKind.Normal or RoomPlanKind.Elite or RoomPlanKind.PreBoss or RoomPlanKind.MidBoss;

    /// <summary>런이나 방 순번이 바뀌면 지난 방 결과를 한 줄 남기고 새로 잰다(바뀌지 않으면 비교만).</summary>
    public static void Enter(GameRunSession run)
    {
        if (run == s_run && run.RoomEnterSerial == s_serial) return;
        if (run == s_run && IsActive)
            Debug.Log($"[SpeedClear] Ch{s_chapter} {s_kind} 싸움 {FightSeconds:0.0} / 정해진 {LimitSeconds:0.0}초 · {(IsFast ? "빠름" : "넘김")}");
        s_run     = run;
        s_serial  = run.RoomEnterSerial;
        s_kind    = run.CurrentRoomKind;
        s_chapter = (int)run.CurrentChapter;
        FightSeconds = 0f;
        LimitSeconds = 0f;
        s_roomLimit  = Applies(s_kind, run.StartEra) ? RunTimeGrade.RoomBase(s_kind, s_chapter, run.StartEra, run.AbyssDepth) * LimitShare : 0f;
    }

    public static void Tick(float dt)
    {
        if (s_roomLimit <= 0f || dt <= 0f) return;
        LimitSeconds  = s_roomLimit;
        FightSeconds += dt;
    }
}
