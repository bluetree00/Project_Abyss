#if UNITY_EDITOR
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-23 실측 — 리치 경기장에서 부서진 칸으로 떨어졌을 때 낙사 복구가 한 번에 제자리로 돌려놓는가(ee 「바닥 뚫고 떨어져 10%씩 세 번 깎이고 사망」).
/// 플레이어 발밑 칸을 <see cref="LichPatternUtil.BreakFloor"/>(쌍둥이 주문 · 공간 도약과 같은 길)로 부수고, 6초 동안 0.25초마다 체력 · 자리를 남긴다.
/// 판정 = 「[FallProbe] 끝 — 체력 깎임 n번 · 마지막 자리」(1번 · 바닥 위면 정상). 리치 자동 패턴은 먼저 끈다(9 Force Lich Pattern/0).
/// </summary>
public static class FallRecoveryProbeEditor
{
    private const float BreakRadius = 5f;      // 쌍둥이 주문(Lich_TwinCastPattern breakRadius)과 같다
    private const float BreakWarn   = 0.2f;
    private const float BreakDown   = 4f;
    private const float WatchSeconds = 6f;
    private const float LogEvery     = 0.25f;

    private static PlayerController s_player;
    private static float s_start, s_next;
    private static int   s_lastHp, s_drops;

    [MenuItem("RelicFairy/Debug/10-23 리치 경기장 — 발밑 칸 부숴 낙사 복구 보기 (Play)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[FallProbe] 플레이 모드에서"); return; }
        s_player = GameRunBootstrapper.Instance?.Run?.Player;
        if (s_player == null || ArenaTileGrid.Active == null) { Debug.LogWarning($"[FallProbe] 준비 안 됨 — 플레이어 {s_player != null} · 경기장 {ArenaTileGrid.Active != null}"); return; }
        int n = LichPatternUtil.BreakFloor(s_player.transform.position, BreakRadius, BreakWarn, BreakDown);
        s_start  = Time.time;
        s_next   = 0f;
        s_lastHp = s_player.RuntimeStats.Hp;
        s_drops  = 0;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Debug.Log($"[FallProbe] 시작 — 발밑 {n}칸 부숨 · 체력 {s_lastHp} · 자리 {s_player.transform.position}");
    }

    private static void Tick()
    {
        if (!Application.isPlaying || s_player == null) { EditorApplication.update -= Tick; return; }
        int hp = s_player.RuntimeStats.Hp;
        if (hp < s_lastHp) s_drops++;
        s_lastHp = hp;
        float t = Time.time - s_start;
        if (t >= s_next)
        {
            s_next = t + LogEvery;
            Debug.Log($"[FallProbe] {t:0.00}초 · 체력 {hp} · 자리 {s_player.transform.position}");
        }
        if (t < WatchSeconds) return;
        EditorApplication.update -= Tick;
        Debug.Log($"[FallProbe] 끝 — 체력 깎임 {s_drops}번 · 마지막 체력 {hp} · 자리 {s_player.transform.position}");
    }
}
#endif
