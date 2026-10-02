#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 대기방 카메라 인계 실측 — 「던전 대기방에 들어설 때 카메라가 가끔 문이 아닌 쪽을 본다」(10-01 사용자) 원인 가르기.
/// 켜 두면 던전 씬에서 플레이어가 막 생긴 순간(무기 매니저가 아직 없는 로드 구간)에 플레이어를 옆으로 돌려 놓는다
/// — 로드 중 이동·클릭으로 몸이 도는 상황을 흉내 낸다. 3초 뒤 카메라 헤딩을 로그로 남긴다.
///   · 헤딩이 「돌린 각」을 따라가면 = 인계가 「지금 플레이어가 보는 각」을 물려받는다(결함).
///   · 헤딩이 「스폰 때 각」(출구 방향)이면 = 인계가 출구 방향을 쓴다(정상).
/// 세이브·씬은 건드리지 않는다. 메뉴를 다시 누르면 꺼진다(플레이를 멈춰도 꺼진다).
/// </summary>
[InitializeOnLoad]
public static class WaitingRoomCameraProbeEditor
{
    private const string MenuPath = "RelicFairy/Debug/대기방 카메라 인계 실측 — 스폰 직후 플레이어 돌리기 (켜기·끄기)";
    private const float  SpinDeg  = 135f;

    private static bool   s_on;
    private static bool   s_requested;
    private static PlayerController s_player;
    private static int    s_spunId;
    private static int    s_skipId;
    private static float  s_spawnYaw, s_reportAt = -1f;

    static WaitingRoomCameraProbeEditor()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += _ => { s_on = false; s_spunId = 0; s_reportAt = -1f; };
    }

    [MenuItem(MenuPath)]
    private static void Toggle()
    {
        s_on = !s_on;
        var existing = Application.isPlaying ? Object.FindFirstObjectByType<PlayerController>() : null;
        s_skipId = existing != null ? existing.GetInstanceID() : 0;
        s_spunId = 0;
        s_reportAt = -1f;
        Debug.Log($"[WaitCamProbe] {(s_on ? "켬 — 다음에 생기는 던전 플레이어를 스폰 직후 옆으로 돌린다" : "끔")}");
    }

    private static void Tick()
    {
        if (!s_on || !Application.isPlaying) return;

        if (s_reportAt > 0f && Time.unscaledTime >= s_reportAt)
        {
            s_reportAt = -1f;
            var cam = GameCameraController.Instance;
            var p = Object.FindFirstObjectByType<PlayerController>();
            float heading = cam != null ? Mathf.Repeat(cam.CurrentHeading, 360f) : -1f;
            float spun = Mathf.Repeat(s_spawnYaw + SpinDeg, 360f);
            bool followedSpin = Mathf.Abs(Mathf.DeltaAngle(heading, spun)) < 20f;
            bool keptSpawn = Mathf.Abs(Mathf.DeltaAngle(heading, s_spawnYaw)) < 20f;
            Debug.Log($"[WaitCamProbe] 결과 — 스폰 yaw {s_spawnYaw:0}° · 돌린 yaw {spun:0}° · 3초 뒤 카메라 헤딩 {heading:0}° · 플레이어 yaw {(p != null ? p.transform.eulerAngles.y : -1f):0}° → " +
                      (followedSpin ? "카메라가 돌린 각을 따라갔다(인계가 플레이어 현재 각을 물려받음)" : keptSpawn ? "카메라가 스폰(출구) 방향을 지켰다" : "둘 다 아님"));
            return;
        }

        // 초기화가 끝나면(무기 매니저 생김) facing 시스템에도 같은 각을 한 번 요청한다 — 안 그러면 유휴 분기가 스폰 각으로 되돌린다
        if (s_spunId != 0)
        {
            if (!s_requested && s_player != null && s_player.WeaponManager != null)
            {
                s_requested = true;
                s_player.RequestFacing(Quaternion.Euler(0f, s_spawnYaw + SpinDeg, 0f));
            }
            return;
        }
        // 던전 씬(GameRunBootstrapper가 있는 씬)에서 새로 생긴 플레이어 — 켤 때 있던 거점 플레이어는 건너뛴다
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (GameRunBootstrapper.Instance == null) return;
        var player = Object.FindFirstObjectByType<PlayerController>();
        if (player == null || player.GetInstanceID() == s_skipId) return;

        s_spunId = player.GetInstanceID();
        s_player = player;
        s_requested = false;
        s_spawnYaw = Mathf.Repeat(player.transform.eulerAngles.y, 360f);
        var rot = Quaternion.Euler(0f, s_spawnYaw + SpinDeg, 0f);
        player.transform.rotation = rot;
        if (player.TryGetComponent<Rigidbody>(out var rb)) rb.rotation = rot;
        s_reportAt = Time.unscaledTime + 6f;   // 무기 로드 + 인계 보간(1.2초)이 끝난 뒤
        Debug.Log($"[WaitCamProbe] 스폰 직후 돌림 — 씬 {scene} · 스폰 yaw {s_spawnYaw:0}° → {Mathf.Repeat(s_spawnYaw + SpinDeg, 360f):0}°");
    }
}
#endif
