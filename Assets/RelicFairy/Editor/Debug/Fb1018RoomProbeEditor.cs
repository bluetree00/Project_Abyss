#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 10-18 사용자 피드백 ④ 「클리어 계단이 중앙 쪽에」 · ⑦ 「챕터 대기방에도 UI — 체력 포션 상황을 모른다」 실측 — 테스트 허브에서 플레이 중에 돌린다.
/// 인수 Temp/fb1018_room_args.txt 1줄 = 「챕터 [wait]」(기본 1).
///  · 기본: 그 챕터 보스 대기방 직행 → 보스방 → 강제 처치 → 기억 카드 → 계단(자리 · 광장 가운데와의 어긋남 · 포탈 자리 · 사진 3장) →
///    꼭대기 → 회랑 → 끝 문 → 다음 챕터 대기방(계단으로 온 길)에서 HUD · 샘 확인 창 · 출구에서 HUD 깜빡임.
///  · wait: 그 챕터 대기방(허브에서 처음 들어오는 길)에서 HUD · 샘 확인 창 · 출구 깜빡임만.
/// 결과 Temp/fb1018_room.txt · Temp/ui_shots/FB18_room_*.png · 로그 「[FB18room] 끝」. CHECK 줄이 전부 True면 통과. 끝나면 플레이를 멈출 것.
/// </summary>
public static class Fb1018RoomProbeEditor
{
    private const string Tag     = "[FB18room]";
    private const string ArgsTxt = "Temp/fb1018_room_args.txt";
    private const string OutTxt  = "Temp/fb1018_room.txt";
    private const string ShotDir = "Temp/ui_shots";
    private const string SkipDialogueMenu = "RelicFairy/Debug/서약 발동 실측/대사 전부 넘기기 (플레이 중)";
    private const float  TopCamHeight     = 6.5f;   // 위에서 본 사진 — 아치 꼭대기(5.8 m) 바로 위 · 천장 아래
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Regex s_tags = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly StringBuilder s_log = new();

    [MenuItem("RelicFairy/Debug/10-18 클리어 계단 · 대기방 HUD 촬영 (테스트 허브, 플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning($"{Tag} 플레이 중(테스트 허브)에만 동작한다."); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new StringBuilder($"10-18 클리어 계단 · 대기방 HUD 실측 · 시기 {StoryProgress.Era}\n");
        Directory.CreateDirectory(ShotDir);
        s_log.Clear();
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        try
        {
            int ch = 1;
            bool waitOnly = false;
            if (File.Exists(ArgsTxt))
            {
                var parts = File.ReadAllText(ArgsTxt).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && int.TryParse(parts[0], out var a)) ch = Mathf.Clamp(a, 1, 4);
                waitOnly = parts.Length > 1 && parts[1].Trim().ToLowerInvariant() == "wait";
            }
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher == null) { sb.AppendLine("테스트 허브가 아니다"); return; }
            typeof(TestHubLauncher).GetField("_chapter", Any)?.SetValue(launcher, ch);
            typeof(TestHubLauncher).GetField("_bossApproach", Any)?.SetValue(launcher, !waitOnly);
            if (!launcher.TryLaunch()) { sb.AppendLine("런 시작 실패"); return; }
            if (!await WaitAsync(() => GameRunBootstrapper.Instance?.Run?.Player != null, 60f)) { sb.AppendLine("런 대기 초과"); return; }

            if (waitOnly) { await WaitingRoomAsync(ch, sb); return; }
            if (await BossStairAsync(ch, sb)) await WaitingRoomAsync(ch + 1, sb);
        }
        catch (Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            Application.logMessageReceived -= OnLog;
            sb.AppendLine("[로그 — 계단 자리 · 출구 카메라]").Append(s_log);
            File.WriteAllText(OutTxt, sb.ToString());
            Debug.Log($"{Tag} 끝\n{sb}");
        }
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (msg.StartsWith("[ChapterStairway] 광장 가운데") || msg.StartsWith("[BossClear] 출구 완료") || msg.StartsWith("[ChapterStairway] 오름 방향"))
            s_log.Append("  ").AppendLine(msg);
    }

    // ── ④ 보스 클리어 계단 ───────────────────────────────────
    private static async UniTask<bool> BossStairAsync(int ch, StringBuilder sb)
    {
        sb.AppendLine($"[④ Ch{ch} 보스 클리어 계단]");
        await SkipAsync(10);
        TestHubDebugMenu.EnterOpenExit();
        await UniTask.Delay(5000, ignoreTimeScale: true);
        await SkipAsync(4);
        TestHubDebugMenu.StepIntoBossEntrance();
        await UniTask.Delay(400, ignoreTimeScale: true);
        TestHubDebugMenu.StepPastBossEntrance();

        var spawner = Object.FindFirstObjectByType<BossSpawner>();
        await WaitAsync(() => { EditorApplication.ExecuteMenuItem(SkipDialogueMenu); spawner ??= Object.FindFirstObjectByType<BossSpawner>(); return spawner != null && spawner.SpawnedBoss != null; }, 30f, 1000);
        var boss = spawner != null ? spawner.SpawnedBoss : null;
        if (boss == null) { sb.AppendLine("  보스 없음"); return false; }
        // 화룡은 70 · 40 · 10%에서 새끼 용이 다 죽을 때까지 무적이다 — 새끼(보스 외 몬스터)도 같이 쓰러뜨린다(10-18 Ch2에서 안 쓰러졌다)
        await WaitAsync(() =>
        {
            EditorApplication.ExecuteMenuItem(SkipDialogueMenu);
            foreach (var m in Object.FindObjectsByType<RelicFairy.Monster.MonsterBase>(FindObjectsSortMode.None))
                if (m != null && m != boss && !m.IsDead) m.TakeSynergyDamage(m.CurrentHp * 10f + 10000f, null, 1f);
            // 새끼 용은 MonsterBase가 아니다(IDamageable) — 위 줄이 못 쳐서 무적이 안 풀렸다(10-18 2차). 77 보스 시뮬 KillAdds와 같은 길(점화형이라 3번)
            var who = GameRunBootstrapper.Instance?.Run?.Player != null ? GameRunBootstrapper.Instance.Run.Player.gameObject : null;
            foreach (var w in Object.FindObjectsByType<RelicFairy.Monster.DragonMiniDragon>(FindObjectsSortMode.None))
                if (w != null && w.isActiveAndEnabled)
                    for (int i = 0; i < 3; i++) ((IDamageable)w).TakeDamage(100000f, who, 0f);
            // 화룡은 70 · 40 · 10%에서 소환 패턴이 실제로 돌기 전까지 체력이 그 아래로 안 내려간다(SummonFloorHp) — AI가 소환을 고르길 기다리면
            // 한 단계에 100초 가까이 걸렸다(10-18 3차). 계단 자리만 재는 실측이라 소환을 이미 한 셈 치고 쓰러뜨린다.
            if (boss is RelicFairy.Monster.DragonBossMonster dragon && dragon.DragonBlackboard != null)
            {
                var bb = dragon.DragonBlackboard;
                bb.HasSummonedAt70 = bb.HasSummonedAt40 = bb.HasSummonedAt10 = true;
                bb.SetSummonGated(false);
            }
            TestHubDebugMenu.ForceKillBoss();
            return boss.IsDead;
        }, 120f, 2000);
        if (!boss.IsDead) { sb.AppendLine("  보스가 안 쓰러짐"); return false; }

        // 기억 카드 고르기 → 계단
        await WaitAsync(() =>
        {
            var popup = Object.FindFirstObjectByType<UI_RelicPartDraftPopup>();
            if (popup == null || !popup.gameObject.activeInHierarchy) return false;
            typeof(UI_RelicPartDraftPopup).GetField("_selected", Any)?.SetValue(popup, 0);
            typeof(UI_RelicPartDraftPopup).GetMethod("OnConfirmClicked", Any)?.Invoke(popup, null);
            return true;
        }, 40f, 500);

        ChapterStairway stair = null;
        await WaitAsync(() => (stair = Object.FindFirstObjectByType<ChapterStairway>()) != null, 20f, 300);
        var run = GameRunBootstrapper.Instance?.Run;
        if (stair == null)
        {
            sb.AppendLine($"  계단 없음 — 게이트 {GameObject.Find("@ChapterGate") != null} · 다음 챕터 있음 {run?.HasNextChapter()}(마지막 챕터면 계단 대신 귀환 · 심연)");
            return false;
        }
        MeasurePlacement(stair, sb);

        await UniTask.Delay(3000, ignoreTimeScale: true);
        await ShotAsync($"FB18_room_4_ch{ch}_rising");
        await UniTask.Delay(3200, ignoreTimeScale: true);
        await ShotAsync($"FB18_room_4_ch{ch}_built");
        RenderTop(stair, ch, sb);
        MeasureBuilt(stair, sb);

        // 층계참 위에 서 본다 — 2 m 위에 머무르는가
        float arenaY = run.Player.transform.position.y;
        var landing = stair.transform.Find("@StairEntry/Landing");
        if (landing != null)
        {
            Teleport(run.Player, landing.position + Vector3.up * 2.3f);
            await UniTask.Delay(900, ignoreTimeScale: true);
            float dy = run.Player.transform.position.y - arenaY;
            sb.AppendLine($"  CHECK 층계참 위 높이 2 m 근처: {Mathf.Abs(dy - 2f) < 0.35f} ({dy:0.00} m)");
            await ShotAsync($"FB18_room_4_ch{ch}_onLanding");
        }

        // 꼭대기(포탈) → 회랑 → 끝 문 → 다음 챕터
        var top = stair.transform.Find("TopTrigger");
        if (top != null) Teleport(run.Player, top.position + Vector3.down * 0.8f);
        bool inCorridor = await WaitAsync(() => run.Player != null && run.Player.transform.position.y > arenaY + 100f, 10f, 200);
        sb.AppendLine($"  CHECK 포탈로 회랑 도착: {inCorridor}");
        if (!inCorridor) return false;
        await UniTask.Delay(1200, ignoreTimeScale: true);
        var door = GameObject.Find("@StairCorridor")?.transform.Find("DoorTrigger");
        if (door != null) Teleport(run.Player, door.position + Vector3.down * 0.6f);
        var next = (ChapterId)(ch + 1);
        bool arrived = await WaitAsync(() => GameRunBootstrapper.Instance != null && GameRunBootstrapper.Instance.Run?.CurrentChapter == next
                                             && GameRunBootstrapper.Instance.Run.Player != null, 40f, 200);
        sb.AppendLine($"  다음 챕터 {(arrived ? "도착" : "대기 초과")}");
        return arrived;
    }

    /// <summary>계단 자리 — 광장 가운데 규칙을 탔나 · 바닥 가운데에서 옆으로 어긋남 · 앞으로 거리 · 예전 출구 자리.</summary>
    private static void MeasurePlacement(ChapterStairway stair, StringBuilder sb)
    {
        var boot  = GameRunBootstrapper.Instance;
        var arena = typeof(GameRunBootstrapper).GetField("_currentArena", Any)?.GetValue(boot) as Transform;
        bool centered = typeof(ChapterStairway).GetField("_centered", Any)?.GetValue(stair) is true;
        var rise  = typeof(ChapterStairway).GetField("_rise", Any)?.GetValue(stair) is Vector3 r ? r : Vector3.forward;
        var p     = stair.transform.position;
        sb.AppendLine($"  아레나 {(arena != null ? arena.name : "없음")} · 계단 {p} · 오름 {rise}");
        sb.AppendLine($"  CHECK 광장 가운데 규칙으로 섰다: {centered}");
        if (arena == null) return;

        object[] args = { arena, null };
        bool hasFloor = typeof(ChapterStairway).GetMethod("TryArenaFloorBounds", Any)?.Invoke(null, args) is true;
        if (hasFloor && args[1] is Bounds floor)
        {
            var d = p - floor.center; d.y = 0f;
            float ahead = Vector3.Dot(d, rise);
            float side  = Vector3.Dot(d, new Vector3(rise.z, 0f, -rise.x));
            sb.AppendLine($"  바닥 가운데 {floor.center} · 크기 {floor.size.x:0.0}×{floor.size.z:0.0} m");
            sb.AppendLine($"  CHECK 옆 어긋남 0 근처: {Mathf.Abs(side) < 0.3f} (옆 {side:0.00} m · 앞 {ahead:0.00} m — 4 m면 규칙대로)");
        }
        var old = BossExitPath.ResolveExit(arena.position, arena, out _);
        var moved = p - old; moved.y = 0f;
        sb.AppendLine($"  예전 출구 자리 {old} → 옮긴 거리 {moved.magnitude:0.0} m");
    }

    /// <summary>다 솟은 뒤 — 단 · 층계참 · 아치 · 포탈이 계단 꼭대기에 붙어 있는가 · 디딤판이 카메라 쪽인가.</summary>
    private static void MeasureBuilt(ChapterStairway stair, StringBuilder sb)
    {
        var entry = stair.transform.Find("@StairEntry");
        int steps = 0, risen = 0;
        bool landingUp = false;
        if (entry != null)
            foreach (Transform c in entry)
            {
                bool up = Mathf.Abs(c.localPosition.y) < 0.01f;
                if (c.name.StartsWith("Step_")) { steps++; if (up) risen++; }
                else if (c.name == "Landing") landingUp = up;
            }
        bool arch = false;
        foreach (Transform c in stair.transform) if (c.name.Contains("StoneArch")) arch = true;
        var portal = typeof(ChapterStairway).GetField("_doorFx", Any)?.GetValue(stair) as GameObject;
        var top    = stair.transform.Find("TopTrigger");
        sb.AppendLine($"  CHECK 돌계단 다 섰다: {steps == 8 && risen == 8 && landingUp && arch && portal != null && top != null}" +
                      $" (단 {risen}/{steps} · 층계참 {landingUp} · 아치 {arch} · 포탈 {portal != null} · 꼭대기 문 {top != null})");

        var rise = typeof(ChapterStairway).GetField("_rise", Any)?.GetValue(stair) is Vector3 r ? r : Vector3.forward;
        var p    = stair.transform.position;
        if (portal != null)
        {
            var d = portal.transform.position - p;
            float ahead = Vector3.Dot(new Vector3(d.x, 0f, d.z), rise);
            sb.AppendLine($"  CHECK 포탈 = 층계참 위 아치 안: {d.y > 2f && Mathf.Abs(ahead - 0.9f) < 0.3f} (층계참 가운데에서 높이 {d.y:0.00} m · 앞 {ahead:0.00} m)");
        }
        var s0 = entry != null ? entry.Find("Step_0") : null;
        var ld = entry != null ? entry.Find("Landing") : null;
        var cam = Camera.main;
        if (s0 != null && ld != null && cam != null)
        {
            var fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            float near = Vector3.Dot(s0.position - ld.position, fwd);
            sb.AppendLine($"  CHECK 디딤판이 카메라 쪽(보임): {near < 0f} ({near:0.0} m)");
        }
    }

    /// <summary>위에서 본 사진 — 바닥 가운데 위 직교 카메라(오르는 쪽이 사진 위). 광장 안 계단 자리를 한눈에.</summary>
    private static void RenderTop(ChapterStairway stair, int ch, StringBuilder sb)
    {
        var arena = typeof(GameRunBootstrapper).GetField("_currentArena", Any)?.GetValue(GameRunBootstrapper.Instance) as Transform;
        var rise  = typeof(ChapterStairway).GetField("_rise", Any)?.GetValue(stair) is Vector3 r ? r : Vector3.forward;
        Vector3 center = stair.transform.position;
        float half = 12f;
        object[] args = { arena, null };
        if (arena != null && typeof(ChapterStairway).GetMethod("TryArenaFloorBounds", Any)?.Invoke(null, args) is true && args[1] is Bounds floor)
        {
            center = new Vector3(floor.center.x, stair.transform.position.y, floor.center.z);
            half   = Mathf.Min(18f, Mathf.Max(floor.extents.x, floor.extents.z));
        }
        const int W = 1280, H = 720;
        var go = new GameObject("~FB18TopCam");
        var cam = go.AddComponent<Camera>();
        var rt  = new RenderTexture(W, H, 24);
        try
        {
            go.transform.SetPositionAndRotation(center + Vector3.up * TopCamHeight, Quaternion.LookRotation(Vector3.down, rise));
            cam.orthographic     = true;
            cam.orthographicSize = half;
            cam.aspect           = (float)W / H;
            cam.nearClipPlane    = 0.1f;
            cam.farClipPlane     = 30f;
            cam.cullingMask      = ~LayerMask.GetMask("UI");
            cam.clearFlags       = CameraClearFlags.SolidColor;
            cam.backgroundColor  = Color.black;
            cam.targetTexture    = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(ShotDir, $"FB18_room_4_ch{ch}_top.png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            sb.AppendLine($"  위에서 본 사진 FB18_room_4_ch{ch}_top — 사진 가운데 = 바닥 가운데 · 위 = 오르는 쪽 · 세로 반폭 {half:0} m");
        }
        catch (Exception e) { sb.AppendLine("  위에서 본 사진 실패: " + e.Message); }
        finally
        {
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
        }
    }

    // ── ⑦ 챕터 대기방 HUD · 샘 확인 창 ──────────────────────
    private static async UniTask WaitingRoomAsync(int ch, StringBuilder sb)
    {
        sb.AppendLine($"[⑦ Ch{ch} 대기방]");
        // 도착 대사를 넘기며 HUD가 켜지기를 기다린다(대사 뒤 RevealHudAsync 0.6초)
        float t0 = Time.realtimeSinceStartup;
        bool shown = await WaitAsync(() =>
        {
            var dlg = Object.FindFirstObjectByType<UI_DialoguePopup>();
            if (dlg != null && dlg.gameObject.activeInHierarchy) TestHubDebugMenu.AdvanceDialogue();
            var s = HudState();
            return Object.FindFirstObjectByType<ChapterSpring>() != null && s.active && !s.suppressed && s.fade >= 0.99f && !Managers.UI.IsGameplayBlocked;
        }, 45f, 400);
        await UniTask.Delay(800, ignoreTimeScale: true);
        var hs = HudState();
        sb.AppendLine($"  CHECK 대기방 HUD 보임: {shown && hs.active && !hs.suppressed} (켜짐 {hs.active} · 숨김 {hs.suppressed} · 알파 {hs.fade:0.00}×{hs.dim:0.00}" +
                      $" · 모드 {hs.mode} · 전투 칸(체력 · 포션) {hs.combat} · {Time.realtimeSinceStartup - t0:0.0}초)");
        await ShotAsync($"FB18_room_7_ch{ch}_hud");

        var spring = Object.FindFirstObjectByType<ChapterSpring>();
        var boot   = GameRunBootstrapper.Instance;
        var stats  = boot?.Run?.Player?.RuntimeStats;
        var state  = boot?.Run?.PlayerState;
        if (spring == null || stats == null || state == null) { sb.AppendLine("  샘 · 플레이어 없음"); return; }
        sb.AppendLine($"  악몽 {NightmareRules.IsActive}");

        // 가득일 때(도착 그대로일 수 있다) — 알림 창
        if (stats.Hp >= stats.MaxHp && state.PotionCount >= state.PotionCapacity)
        {
            typeof(ChapterSpring).GetMethod("OpenPrompt", Any)?.Invoke(spring, null);
            await UniTask.Delay(600, ignoreTimeScale: true);
            sb.AppendLine($"  가득일 때 창: 「{DialogBody()}」");
            await ShotAsync($"FB18_room_7_ch{ch}_spring_full");
            UI_ConfirmDialog.CloseIfOpen();
            await UniTask.Delay(400, ignoreTimeScale: true);
        }

        // 다친 상태 — 체력 55% · 포션 하나 씀 → HUD에 보이는가 · [F] 창 한 줄
        stats.SetHp(Mathf.RoundToInt(stats.MaxHp * 0.55f));
        state.TryConsumePotion();
        await UniTask.Delay(900, ignoreTimeScale: true);
        await ShotAsync($"FB18_room_7_ch{ch}_hud_hurt");
        int hp = stats.Hp, max = stats.MaxHp, pot = state.PotionCount, cap = state.PotionCapacity;
        typeof(ChapterSpring).GetMethod("OpenPrompt", Any)?.Invoke(spring, null);
        await UniTask.Delay(600, ignoreTimeScale: true);
        string body  = DialogBody();
        string first = body.Split('\n')[0];
        bool oneLine = first.Contains($"지금 체력 {hp}/{max}") && first.Contains($"포션 {pot}/{cap}") && first.Contains("→") && first.Contains("샘 뒤");
        sb.AppendLine($"  CHECK 샘 창 첫 줄 = 지금 → 샘 뒤 한 줄: {oneLine} 「{first}」");
        sb.AppendLine($"  창 전체: 「{body.Replace("\n", " / ")}」");
        await ShotAsync($"FB18_room_7_ch{ch}_spring");
        UI_ConfirmDialog.CloseIfOpen();
        await UniTask.Delay(400, ignoreTimeScale: true);

        await ExitBlinkAsync(sb);
    }

    /// <summary>대기방 출구를 지나 첫 방까지 — HUD 페이드 알파가 0으로 떨어지는 순간(깜빡임)이 있었나.</summary>
    private static async UniTask ExitBlinkAsync(StringBuilder sb)
    {
        StartRoomGate gate = null;
        foreach (var g in Object.FindObjectsByType<StartRoomGate>(FindObjectsSortMode.None))
            if ((int)(typeof(StartRoomGate).GetField("_fromZoneIndex", Any)?.GetValue(g) ?? 0) == -1) gate = g;
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (gate == null || player == null || !gate.TryGetComponent<Collider>(out var col)) { sb.AppendLine("  출구 문 없음 — 깜빡임 검사 건너뜀"); return; }
        typeof(StartRoomGate).GetMethod("HandleCovenantAssembled", Any)?.Invoke(gate, null);   // 서약 제단을 건너뛰고 문을 연다
        await UniTask.Delay(500, ignoreTimeScale: true);
        var b = col.bounds;
        Teleport(player, new Vector3(b.center.x, b.min.y + 0.3f, b.center.z));

        float min = 1f, t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 15f)
        {
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
            var s = HudState();
            if (s.active) min = Mathf.Min(min, s.fade);
        }
        sb.AppendLine($"  CHECK 출구 → 첫 방 HUD 깜빡임 없음: {min >= 0.99f} (페이드 알파 최저 {min:0.00} · 15초)");
    }

    // ── 부품 ────────────────────────────────────────────────
    private static (bool active, bool suppressed, float fade, float dim, string mode, bool combat) HudState()
    {
        var hb = Object.FindFirstObjectByType<HudBootstrapper>(FindObjectsInactive.Include);
        if (hb == null) return (false, false, 0f, 0f, "?", false);
        var presenter = typeof(HudBootstrapper).GetField("presenter", Any)?.GetValue(hb) as HudPresenter;
        var root      = typeof(HudBootstrapper).GetField("hudVisualRoot", Any)?.GetValue(hb) as Transform;
        if (root == null && presenter != null) root = presenter.transform;
        bool suppressed = typeof(HudBootstrapper).GetField("_startRoomSuppressed", Any)?.GetValue(hb) is true;
        float fade = typeof(HudBootstrapper).GetField("_fadeAlpha", Any)?.GetValue(hb) is float f ? f : -1f;
        float dim  = typeof(HudBootstrapper).GetField("_popupDim", Any)?.GetValue(hb) is float d ? d : -1f;
        string mode = presenter != null ? typeof(HudPresenter).GetField("_currentMode", Any)?.GetValue(presenter)?.ToString() : "?";
        var view    = presenter != null ? typeof(HudPresenter).GetField("view", Any)?.GetValue(presenter) as HudView : null;
        bool combat = view != null && view.CombatPanel != null && view.CombatPanel.gameObject.activeInHierarchy;
        return (root != null && root.gameObject.activeInHierarchy, suppressed, fade, dim, mode, combat);
    }

    /// <summary>열린 확인 창 본문(태그 걷은 글).</summary>
    private static string DialogBody()
    {
        var open = typeof(UI_ConfirmDialog).GetField("s_open", Any)?.GetValue(null) as UI_ConfirmDialog;
        var body = open != null ? typeof(UI_ConfirmDialog).GetField("_body", Any)?.GetValue(open) as TMP_Text : null;
        return body != null ? s_tags.Replace(body.text, "") : "(창 없음)";
    }

    private static async UniTask<bool> WaitAsync(Func<bool> cond, float seconds, int stepMs = 500)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            try { if (cond()) return true; } catch { }
            await UniTask.Delay(stepMs, ignoreTimeScale: true);
        }
        return false;
    }

    private static async UniTask SkipAsync(int times)
    {
        for (int i = 0; i < times; i++)
        {
            if (Object.FindFirstObjectByType<UI_DialoguePopup>() != null) TestHubDebugMenu.AdvanceDialogue();
            await UniTask.Delay(400, ignoreTimeScale: true);
        }
    }

    private static void Teleport(PlayerController p, Vector3 pos)
    {
        if (p == null) return;
        p.transform.position = pos;
        if (p.TryGetComponent<Rigidbody>(out var rb)) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
    }

    private static async UniTask ShotAsync(string name)
    {
        var runner = Object.FindFirstObjectByType<Managers>();
        if (runner == null) return;
        await UniTask.WaitForEndOfFrame(runner);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        if (tex == null) return;
        File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
        Object.Destroy(tex);
    }
}
#endif
