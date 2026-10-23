#if UNITY_EDITOR
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-23 실측 — 화룡 꼬리 휩쓸기(10-22 E3 D5)가 실제로 나오는가. 시뮬 봇은 늘 앞 7 m라 「등 뒤 4 m · 1.5초」 조건이 한 번도 안 맞았다.
/// 지상일 때 플레이어를 화룡 등 뒤 2.5 m에 계속 세워 두고(매 프레임 · Rigidbody 위치도), 꼬리 휩쓸기 시작(쿨다운이 켜짐)을 기다린다.
/// 판정 = 로그 「[TailProbe] 발동」 · 그 뒤 2초 체력 변화. 25초 안에 안 나오면 「미발동」.
/// </summary>
public static class DragonTailSweepProbeEditor
{
    private const float BehindDistance = 2.5f;
    private const float LimitSeconds   = 25f;
    private const float AfterSeconds   = 2f;
    private const float LogEvery       = 1f;

    private static DragonBossMonster s_dragon;
    private static PlayerController  s_player;
    private static float s_start;
    private static float s_firedAt = -1f;
    private static float s_nextLog;
    private static int   s_hpAtFire;

    [MenuItem("RelicFairy/Debug/10-23 화룡 꼬리 휩쓸기 — 등 뒤에 세우기 (Play)")]
    private static void Begin()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[TailProbe] 플레이 모드에서"); return; }
        s_dragon = Object.FindFirstObjectByType<DragonBossMonster>();
        s_player = GameRunBootstrapper.Instance?.Run?.Player;
        if (s_dragon == null || s_player == null) { Debug.LogWarning($"[TailProbe] 준비 안 됨 — 화룡 {s_dragon != null} · 플레이어 {s_player != null}"); return; }
        s_start   = Time.time;
        s_firedAt = -1f;
        s_nextLog = 0f;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Debug.Log("[TailProbe] 시작 — 지상일 때 등 뒤 2.5 m에 세운다");
    }

    private static void Tick()
    {
        if (!Application.isPlaying || s_dragon == null || s_player == null) { Stop("중단(플레이 끝 · 대상 없음)"); return; }
        var   bb = s_dragon.DragonBlackboard;
        float t  = Time.time - s_start;

        if (s_firedAt < 0f)
        {
            if (bb.TailSweepCooldown > 0f)
            {
                s_firedAt  = Time.time;
                s_hpAtFire = s_player.RuntimeStats.Hp;
                Debug.Log($"[TailProbe] 발동 — {t:0.0}초 · 체력 {s_hpAtFire}");
                return;
            }
            if (bb.BodyState == BodyState.Grounded) PlaceBehind();
            if (Time.time >= s_nextLog)
            {
                s_nextLog = Time.time + LogEvery;
                Debug.Log($"[TailProbe] {t:0.0}초 · {bb.BodyState} · 등 뒤 머묾 {bb.RearStaySeconds:0.00}초");
            }
            if (t >= LimitSeconds) Stop("미발동");
            return;
        }

        if (Time.time - s_firedAt >= AfterSeconds)
            Stop($"끝 — 발동 뒤 {AfterSeconds}초 체력 {s_hpAtFire} → {s_player.RuntimeStats.Hp}");
    }

    private static void PlaceBehind()
    {
        Vector3 back = -s_dragon.transform.forward;
        back.y = 0f;
        if (back.sqrMagnitude < 0.01f) return;
        Vector3 to = s_dragon.transform.position + back.normalized * BehindDistance;
        to.y = s_player.transform.position.y;
        s_player.transform.position = to;
        if (s_player.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.position       = to;
            rb.linearVelocity = Vector3.zero;
        }
    }

    private static void Stop(string why)
    {
        EditorApplication.update -= Tick;
        Debug.Log("[TailProbe] " + why);
        s_dragon = null;
        s_player = null;
    }
}
#endif
