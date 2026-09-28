using System;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 아레나 실측 — 플레이어를 아레나 안 여러 지점으로 옮기며 <b>실제 게임 카메라</b> 화면을 저장하고,
/// 카메라→플레이어 시선이 벽(Wall 레이어)에 막히는지 수치로 남긴다.
///
/// ⚠️ MCP manage_camera 스크린샷은 호출마다 강제 임포트 + 도메인 리로드를 일으켜 그 뒤 런이 죽는다(09-22 실측).
///    여기서는 게임 안에서 ScreenCapture로 받아 Temp/(Assets 밖)에 쓰므로 임포트가 없다 → 한 런에 여러 장.
/// 런 카메라(FreeLook)는 마우스 회전 입력이 없어 방 진행 방향에 고정이다 — 아레나에서 카메라는 늘 플레이어 뒤(남쪽).
/// 그래서 남쪽(카메라 쪽) 가장자리를 먼저 보고, 입구 트리거(로컬 z −8)를 넘어 보스를 깨울 수 있는 지점은 뒤에 둔다.
/// </summary>
public static class ArenaViewCaptureEditor
{
    // ── Constants ──────────────────────────────────────────────
    private const string MenuPath       = "RelicFairy/Boss/Arena View Capture (Play)";
    private const string InvincibleMenu = "RelicFairy/Test Run/Player Invincible 1h (Play)";
    private const int    SettleMs       = 1400;   // 순간이동 뒤 카메라가 자리 잡을 시간(비스케일)
    private const int    MaxAttempts    = 20;     // 게임 뷰 백버퍼가 검게 읽히면 다음 프레임을 다시 읽는다
    private const float  PlayerEyeY     = 1.4f;

    // 아레나 로컬(x, z) — BossZoneCenter의 부모(아레나 루트) 기준이라 방이 돌아가 지어져도 따라간다.
    private static readonly (string name, Vector2 local, bool teleport)[] Spots =
    {
        ("01_spawn",      new Vector2(  0f,  -12f),  false),   // 스폰 그대로
        ("02_south_edge", new Vector2(  0f,  -13.3f), true),
        ("03_sw",         new Vector2( -9f,   -9.5f), true),
        ("04_se",         new Vector2(  9f,   -9.5f), true),
        ("05_center_s",   new Vector2(  0f,   -3f),   true),
        ("06_west",       new Vector2(-12.8f,  0f),   true),
        ("07_east",       new Vector2( 12.8f,  0f),   true),
        ("08_north",      new Vector2(  0f,    8f),   true),
    };

    // ── Static ─────────────────────────────────────────────────
    private static CancellationTokenSource s_cts;

    // ── Public Methods ─────────────────────────────────────────
    [MenuItem(MenuPath)]
    public static void Capture()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[ArenaView] 플레이 모드에서만 동작한다."); return; }

        s_cts?.Cancel();
        s_cts?.Dispose();
        s_cts = new CancellationTokenSource();
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        RunAsync(s_cts.Token).Forget();
    }

    // ── Private Methods ────────────────────────────────────────
    private static async UniTaskVoid RunAsync(CancellationToken ct)
    {
        try
        {
            var runner = UnityEngine.Object.FindFirstObjectByType<Managers>();
            var player = GameRunBootstrapper.Instance?.Run?.Player;
            var zone   = GameObject.Find("BossZoneCenter");
            var cam    = Camera.main;
            if (runner == null || player == null || zone == null || zone.transform.parent == null || cam == null)
            {
                Debug.LogWarning($"[ArenaView] 준비 안 됨 — runner={runner != null} player={player != null} zone={zone != null} cam={cam != null}");
                return;
            }

            Transform arena = zone.transform.parent;
            int   wallMask  = LayerMask.GetMask("Wall");
            float baseY     = zone.transform.position.y - 1f;   // BossZoneCenter는 바닥 위 1 m

            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "boss_arena_view",
                                      $"{arena.name}_{DateTime.Now:MMdd_HHmmss}");
            Directory.CreateDirectory(dir);

            EditorApplication.ExecuteMenuItem(InvincibleMenu);

            var report = new StringBuilder();
            report.AppendLine($"arena={arena.name} heading={GameCameraController.Instance?.CurrentHeading:F0}");

            foreach (var (name, local, teleport) in Spots)
            {
                ct.ThrowIfCancellationRequested();

                if (teleport)
                {
                    Vector3 pos = arena.TransformPoint(new Vector3(local.x, 0f, local.y));
                    pos.y = baseY + 0.3f;
                    MovePlayer(player, pos);
                    var camCtrl = GameCameraController.Instance;
                    if (camCtrl != null) camCtrl.SetHeadingImmediate(camCtrl.CurrentHeading);   // 보간 없이 새 자리로
                }
                await UniTask.Delay(SettleMs, ignoreTimeScale: true, cancellationToken: ct);

                Vector3 eye    = player.transform.position + Vector3.up * PlayerEyeY;
                Vector3 camPos = cam.transform.position;
                bool    hidden = Physics.Linecast(camPos, eye, out var hit, wallMask, QueryTriggerInteraction.Ignore);
                Vector3 camLocal = arena.InverseTransformPoint(camPos);
                string  line = $"{name}  카메라 로컬=({camLocal.x:F1},{camLocal.y:F1},{camLocal.z:F1})  " +
                               (hidden ? $"가림 있음 — {hit.collider.name} @ {Vector3.Distance(camPos, hit.point):F1} m" : "가림 없음");
                report.AppendLine(line);
                Debug.Log($"[ArenaView] {line}");

                await SaveShotAsync(runner, Path.Combine(dir, name + ".png"), ct);
            }

            File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString(), Encoding.UTF8);
            Debug.Log($"[ArenaView] 완료 — {dir}");
        }
        catch (OperationCanceledException)
        {
            Debug.Log("[ArenaView] 취소 — 플레이가 끝났다.");
        }
    }

    private static void MovePlayer(PlayerController player, Vector3 pos)
    {
        player.transform.position = pos;
        if (player.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.position = pos;
            if (!rb.isKinematic)
            {
                rb.linearVelocity  = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }

    private static async UniTask SaveShotAsync(MonoBehaviour runner, string path, CancellationToken ct)
    {
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            await UniTask.WaitForEndOfFrame(runner, ct);
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (tex == null) continue;

            bool black = true;
            for (int y = 0; y < tex.height && black; y += Mathf.Max(1, tex.height / 12))
                for (int x = 0; x < tex.width; x += Mathf.Max(1, tex.width / 16))
                    if (tex.GetPixel(x, y).maxColorComponent > 0.02f) { black = false; break; }

            if (!black || attempt == MaxAttempts - 1)
            {
                File.WriteAllBytes(black ? path.Replace(".png", "_black.png") : path, tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
                return;
            }
            UnityEngine.Object.Destroy(tex);
        }
    }

    // ── Event Handlers ─────────────────────────────────────────
    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingPlayMode) return;
        s_cts?.Cancel();
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
    }
}
