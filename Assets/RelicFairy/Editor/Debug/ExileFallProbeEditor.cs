#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using OccaSoftware.Buto.Runtime;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 10-11 리치 추방 · 추락 실측 — 붕괴 → 추방 → 층 넷 → 착지 → Ch1 첫 방까지 8장 이하를 찍고 시각 · 층 · 대사 · 착지 뒤 런 상태를 적는다.
/// 길: ① 테스트 허브면 Ch4 · 보스 대기방 직행으로 띄운다 ② 보스방으로 들어가 리치 페이지를 넘기고 봉인석을 점화한다(진짜 붕괴 컷신 → 추방)
///     ③ 리치를 만나지 못하면 지금 방에서 추락만 바로 튼다(<see cref="ExileFallSequence.Editor_BeginDirect"/>).
/// 사용자 세이브를 건드리지 않게 이야기 상태는 테스트 오버라이드로 돌린다 — 봉인기(0)로 의식을 열고, 추방이 시작되면 해방기(1)로 바꿔
/// 「붕괴 저장 뒤의 시기」를 흉내 낸다(진짜 흐름은 TriggerCollapse가 BossSealService 봉인을 깨 같은 값이 된다). 끝에 원래 값으로 돌려놓는다.
/// 판정 줄(10-11 b0 · f9 후속): HUD — ch3 · ch2 · ch1 사진 시점에 HUD 루트(@HUD)가 숨어 있는가 · RaiseDied 경로 — 리치가 보스방 클리어 신호를 냈는가
/// (테스트 조우 길 _debugOverrideEncounter는 실측 동안 끈다) · 이른 저장 — 코어 붕괴 순간 「다음 = Ch1 대기방」 런 저장이 써졌는가.
/// 10-12 83 실측 후속: 리치는 12 m 감지 안에 들어와야 깨어난다 — 깨어날 때까지 8 m 앞으로 옮긴다(입구만 밟고 150초 잠든 채 끝났다).
/// 층 사진마다 화면 평균 색 · 카메라 배경 · 안개 · Buto · 화면 안 파편 개수와 크기 · 돌을 적는다. 나침반(@HUD 밖)도 HUD 판정에 넣는다.
/// 착지는 엎드림 · 일어남 두 장(가까운 카메라)과 그 순간 클립 이름. 대사창은 사진 대신 멀린 표시명을 적는다(사진 8장 상한).
/// 결과: Temp/exile_fall_probe.txt · Temp/ui_shots/Exile_*.png(8장 이하) · 로그 「[ExileProbe] 끝」. 실측 뒤 플레이를 멈출 것.
/// </summary>
public static class ExileFallProbeEditor
{
    private const string MenuPath = "RelicFairy/Debug/10-11 리치 추방 · 추락 실측 (테스트 허브, 플레이 중)";
    private const string OutPath  = "Temp/exile_fall_probe.txt";
    private const string ShotDir  = "Temp/ui_shots";

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic;

    private static bool        s_running;
    private static bool        s_raiseDied;       // 리치가 RaiseDied(보스방 클리어 신호)를 냈다
    private static bool        s_exileAtClear;    // 그 순간 추방 중이었다 = 방 클리어 징글을 내지 않는 조건
    private static LichMonster s_lich;
    private static bool        s_debugWas;        // 테스트 조우 길이 켜져 있었다(실측 동안 끄고 끝에 되돌린다)
    private static int         s_hudDefects;
    private static readonly ParticleSystem.Particle[] s_particles = new ParticleSystem.Particle[256];

    [MenuItem(MenuPath)]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[ExileProbe] 플레이 모드(테스트 허브 · 런 중)에서"); return; }
        if (s_running) { Debug.LogWarning("[ExileProbe] 이미 도는 중"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        s_running     = true;
        s_raiseDied   = false;
        s_exileAtClear = false;
        s_lich        = null;
        s_debugWas    = false;
        s_hudDefects  = 0;
        var sb = new StringBuilder("리치 추방 · 추락 실측 (10-11)\n");
        int prevOverride = StoryProgress.DebugNightmareOverride;
        float t0 = Time.realtimeSinceStartup;
        try
        {
            SetOverride(0);
            sb.AppendLine($"이야기 오버라이드 {prevOverride} → 0(봉인기 · 이야기 기록 저장 안 함)");

            // ① 테스트 허브 — Ch4 · 보스 대기방 직행
            var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
            if (launcher != null)
            {
                launcher.SelectChapter(4);
                if (!launcher.ToggleBossApproach()) launcher.ToggleBossApproach();   // 보스 대기방 직행으로 맞춘다
                if (!await WaitAsync(launcher.TryLaunch, 30f)) { sb.AppendLine("허브 시작 실패(앱 준비 전)"); return; }
                sb.AppendLine("허브 → Ch4 보스 대기방 직행");
                if (!await WaitAsync(() => SceneManager.GetActiveScene().name == "GameScene_Ch4", 90f)) { sb.AppendLine("Ch4 진입 실패"); return; }
            }
            if (!await WaitAsync(() => RunPlayer() != null && (GameRunBootstrapper.Instance?.Run?.IsRunning ?? false), 90f))
            {
                sb.AppendLine("런 · 플레이어 없음 — 런 중에 실행할 것");
                return;
            }
            RunPlayer().SetInvincible(3600f);
            string before = DescribeRun();
            sb.AppendLine($"시작 런 — {before}");
            sb.AppendLine($"시작 씬 {SceneManager.GetActiveScene().name}");

            // ② 리치 붕괴 길 → 안 되면 ③ 직접 길
            bool full = await DriveLichAsync(sb);
            if (!full)
            {
                if (ExileFallSequence.IsActive) { sb.AppendLine("추방이 이미 도는 중 — 그대로 관측"); }
                else if (!ExileFallSequence.Editor_BeginDirect()) { sb.AppendLine("추락을 시작하지 못했다(런 없음 또는 이미 추방 중)"); return; }
                else sb.AppendLine("직접 길 — 지금 방에서 추락만(붕괴 컷신 · 보스방 클리어 없이)");
            }
            if (!await WaitAsync(() => ExileFallSequence.IsActive, 60f)) { sb.AppendLine("추방이 시작되지 않았다"); return; }
            SetOverride(1);
            sb.AppendLine("추방 시작 → 오버라이드 1(해방기)로 붕괴 저장 뒤 시기를 흉내 낸다");

            // ③ 관측 — 시각마다 한 장
            int n = 0;
            if (full)
            {
                if (await WaitAsync(() => ExileFallSequence.Elapsed >= 1.1f || ExileFallSequence.FallTime >= 0f, 10f))
                    await Shot($"Exile_{++n}_point", sb);
                if (await WaitAsync(() => ExileFallSequence.Elapsed >= 2.6f || ExileFallSequence.FallTime >= 0f, 10f))
                    await Shot($"Exile_{++n}_core", sb);
                LogEarlySave(sb);   // 코어 붕괴 순간의 이른 저장(보스방 클리어가 덮기 전)
            }
            await ShotAtFall(0.9f,  $"Exile_{++n}_ch4", sb, hud: true);
            await ShotAtFall(2.75f, $"Exile_{++n}_ch3_bound", sb, hud: false);   // 10-13 봉인된 기사 — 사슬이 끊기는 중
            await ShotAtFall(3.2f,  $"Exile_{++n}_ch3", sb, hud: true);
            await ShotAtFall(4.95f, $"Exile_{++n}_ch2_bound", sb, hud: false);   //   화룡
            await ShotAtFall(5.4f,  $"Exile_{++n}_ch2", sb, hud: true);
            await ShotAtFall(7.15f, $"Exile_{++n}_ch1_bound", sb, hud: false);   //   숲의 수호자
            await ShotAtFall(8.4f,  $"Exile_{++n}_ch1_floor", sb, hud: true);
            if (!await WaitAsync(() => ExileFallSequence.HasLanded, 30f)) { sb.AppendLine("착지 안 됨"); return; }
            float landedAt = Time.realtimeSinceStartup - t0;
            var trace = ExileFallSequence.Trace.ToArray();   // 다음 추방이 지우기 전에 떠 둔다
            sb.AppendLine($"착지 {landedAt:0.0}초(실측 시작 기준)");

            // ④ Ch1 첫 방 — 엎드린 채 드러나 일어나고 멀린 한 줄
            if (!await WaitAsync(() => SceneManager.GetActiveScene().name == "GameScene_Ch1", 60f)) { sb.AppendLine("Ch1 진입 실패"); Dump(sb, trace); return; }
            sb.AppendLine($"Ch1 진입 {Time.realtimeSinceStartup - t0:0.0}초");
            if (await WaitAsync(() => ExileFallSequence.IsLandingPoseActive, 60f))
            {
                CamDiag("자세 걸림 직후", sb);
                await UniTask.Delay(420, DelayType.Realtime);   // 와이프(0.35초)가 걷힌 뒤 — 엎드린 채(가까운 카메라)
                sb.AppendLine($"  착지 클립 {ExileFallSequence.LandingClipName} · 클립 {ExileFallSequence.LandingClipTime:0.00}초");
                CamDiag("착지 사진", sb);
                await Shot($"Exile_{++n}_land", sb);
                await UniTask.Delay(900, DelayType.Realtime);   // 0.5초 버티고 일어나는 중
                sb.AppendLine($"  일어남 클립 {ExileFallSequence.LandingClipName} · 클립 {ExileFallSequence.LandingClipTime:0.00}초");
                CamDiag("일어남 사진", sb);
                await Shot($"Exile_{++n}_getup", sb);
            }
            else sb.AppendLine("착지 자세 없음(Ch1 부트가 추방 표지를 못 읽었다?)");

            if (await WaitAsync(() => Object.FindFirstObjectByType<UI_DialoguePopup>() != null, 20f))
                sb.AppendLine($"  대기방 대사창 — 멀린 표시명 「{(StoryProgress.IsMerlinNamed ? "멀린" : "???")}」 · 리치 조우 기록 {StoryProgress.HasMetLich} " +
                              "(오버라이드 중엔 조우 기록을 저장하지 않아 「???」일 수 있다 — 진짜 흐름은 리치 등장 때 MarkLichMet로 공개)");
            else sb.AppendLine("대기방 대사창 없음");

            // 기록 — 시각 · 층 · 대사(추방 쪽 기록) + Ch1 착지 쪽 기록
            Dump(sb, trace);
            foreach (var line in ExileFallSequence.Trace.Skip(trace.Length)) sb.AppendLine("  [Ch1] " + line);

            // 착지 뒤 런 상태
            var run = GameRunBootstrapper.Instance?.Run;
            sb.AppendLine($"착지 뒤 런 — {DescribeRun()}");
            sb.AppendLine($"  같은 런: {(run != null && run.IsRunning)} · 챕터 {run?.CurrentChapter} · 깊이 {run?.AbyssDepth}");
            sb.AppendLine($"  시작 런과 같은가(유물 · 무기 · 서약 · 파츠 · 룬): {SameBuild(before, DescribeRun())}");
            sb.AppendLine($"ERA 착지 뒤 시기 = {StoryProgress.Era} · 해방기={StoryProgress.IsLiberated} (오버라이드 1 — 진짜 흐름은 BossSealService.IsSealBroken(lich)로 같은 값)");
            sb.AppendLine($"  저장된 봉인 깨짐(진짜 세이브 · 실측이 바꾸지 않음): 숲 {BossSealService.IsSealBroken(StoryProgress.ForestGuardian)} · " +
                          $"화룡 {BossSealService.IsSealBroken(StoryProgress.Dragon)} · 기사 {BossSealService.IsSealBroken(StoryProgress.DeathKnight)} · " +
                          $"리치 {BossSealService.IsSealBroken(StoryProgress.Lich)}");
            sb.AppendLine($"HUD 추락 중 숨김 유지(@HUD · 적 나침반 · ch4 · ch3 · ch2 · ch1): {(s_hudDefects == 0 ? "통과" : $"결함 {s_hudDefects}회")}");
            sb.AppendLine(full
                ? $"RaiseDied 경로 = {(s_raiseDied ? "예" : "아니오")} · 그 순간 추방 중(방 클리어 징글 생략 조건) = {(s_exileAtClear ? "예" : "아니오")}"
                : "RaiseDied 경로 = 아니오(직접 길 — 리치 없이 추락만 · 전환은 착지 뒤 직접)");
            sb.AppendLine($"사진 {n}장 · 전체 {Time.realtimeSinceStartup - t0:0.0}초");
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            RestoreLich(sb);
            SetOverride(prevOverride);
            sb.AppendLine($"이야기 오버라이드 원래대로 {prevOverride}");
            File.WriteAllText(OutPath, sb.ToString());
            Debug.Log("[ExileProbe] 끝\n" + sb);
            s_running = false;
        }
    }

    // ── 리치 붕괴 길 ─────────────────────────────────────────────────
    /// <summary>보스 대기방 → 보스방 → 리치 페이지 넘김 → 봉인석 점화 → 붕괴 컷신 → 추방 시작까지. 추방이 시작되면 true.</summary>
    private static async UniTask<bool> DriveLichAsync(StringBuilder sb)
    {
        if (Object.FindFirstObjectByType<LichMonster>() == null)
        {
            if (!await WaitAsync(() => Object.FindObjectsByType<ProcRoomGate>(FindObjectsSortMode.None).Any(g => g.IsArmed)
                                       || Object.FindFirstObjectByType<BossRoomController>() != null, 40f))
            {
                sb.AppendLine("리치 길: 열린 출구 · 보스방 없음");
                return false;
            }
            if (Object.FindFirstObjectByType<BossRoomController>() == null)
            {
                TestHubDebugMenu.EnterOpenExit();
                if (!await WaitAsync(() => Object.FindFirstObjectByType<BossRoomController>() != null, 40f))
                {
                    sb.AppendLine("리치 길: 보스방에 못 들어갔다");
                    return false;
                }
            }
            await UniTask.Delay(2500, DelayType.Realtime);   // 방 등장(디졸브) · 스포너 준비
            TestHubDebugMenu.StepIntoBossEntrance();
            await UniTask.Delay(400, DelayType.Realtime);
            TestHubDebugMenu.StepPastBossEntrance();
            if (!await WaitAsync(() => Object.FindFirstObjectByType<LichMonster>() != null, 30f))
            {
                sb.AppendLine("리치 길: 리치가 나타나지 않았다(Ch4 보스방이 아니다?)");
                return false;
            }
        }
        sb.AppendLine("리치 길: 리치 조우");
        GuardLich(Object.FindFirstObjectByType<LichMonster>(), sb);
        RunPlayer()?.SetInvincible(3600f);

        // 페이지를 넘겨 쓰러뜨린다 — 봉인석이 서면 의식. 리치는 12 m 감지 안에 들어와야 깨어난다(그 전엔 잠든 채 무적) —
        // 10-12 83 실측: 입구만 밟고 멀리 있어 150초 내내 깨어나지 않았다 → 피해가 들어갈 때까지 8 m 앞으로 옮긴다.
        // 깨어난 뒤엔 「처치」 한 번이 페이지 하나(HP가 페이지 경계에서 멈춘다) — 마지막 페이지에서 봉인 의식으로.
        float start   = Time.realtimeSinceStartup;
        bool  engaged = false;
        int   steps   = 0;
        while (Time.realtimeSinceStartup - start < 150f && !AnyStone() && !ExileFallSequence.IsActive)
        {
            var lich = Object.FindFirstObjectByType<LichMonster>();
            if (lich != null && !engaged)
            {
                engaged = lich.CurrentHp < lich.BossMaxHp;
                if (!engaged) { StepNearLich(lich); steps++; }
            }
            if (Object.FindFirstObjectByType<UI_DialoguePopup>() != null) TestHubDebugMenu.AdvanceDialogue();
            else if (lich != null) TestHubDebugMenu.KillBoss();
            await UniTask.Delay(1200, DelayType.Realtime);
        }
        sb.AppendLine($"리치 길: 깨어남 {engaged} (8 m 앞으로 {steps}번)");
        if (!AnyStone() && !ExileFallSequence.IsActive) { sb.AppendLine("리치 길: 봉인 의식에 닿지 못했다(150초)"); return false; }
        sb.AppendLine($"리치 길: 봉인 의식 {Time.realtimeSinceStartup - start:0.0}초");

        // 봉인석 점화(한 번에 한 타 · 셋씩)
        start = Time.realtimeSinceStartup;
        await UniTask.Delay(3000, DelayType.Realtime);   // 봉인석을 하나씩 세우며 비추는 동안
        while (Time.realtimeSinceStartup - start < 40f && AnyUnlitStone() && !ExileFallSequence.IsActive)
        {
            TestHubDebugMenu.HitSealStones();
            await UniTask.Delay(700, DelayType.Realtime);
        }
        sb.AppendLine($"리치 길: 봉인석 점화 끝 {Time.realtimeSinceStartup - start:0.0}초 — 붕괴 컷신 대기");

        // 붕괴 컷신(약 9초 + 완성 · 대사 3.5초) → 추방
        if (!await WaitAsync(() => ExileFallSequence.IsActive, 40f)) { sb.AppendLine("리치 길: 추방이 시작되지 않았다"); return false; }
        sb.AppendLine("리치 길: 붕괴 컷신 → 추방 시작");
        return true;
    }

    /// <summary>
    /// 리치를 지켜본다 — RaiseDied(보스방 클리어 신호)를 들으려 OnDied를 구독하고, 테스트 조우 길(_debugOverrideEncounter)이 켜져 있으면
    /// 끄고 그 길의 사망 처리(Editor_HandleDied — 3초 뒤 리치를 다시 세운다)도 떼어 낸다. 끝에 <see cref="RestoreLich"/>가 값을 되돌린다.
    /// </summary>
    private static void GuardLich(LichMonster lich, StringBuilder sb)
    {
        if (lich == null) return;
        s_lich = lich;
        lich.OnDied += OnLichDied;
        var field = typeof(LichMonster).GetField("_debugOverrideEncounter", Any);
        s_debugWas = field != null && (bool)field.GetValue(lich);
        if (s_debugWas)
        {
            field.SetValue(lich, false);
            var handler = typeof(LichMonster).GetMethod("Editor_HandleDied", Any);
            if (handler != null)
                lich.OnDied -= (System.Action<MonsterBase>)System.Delegate.CreateDelegate(typeof(System.Action<MonsterBase>), lich, handler);
        }
        sb.AppendLine($"리치 길: 테스트 조우 길 _debugOverrideEncounter={(s_debugWas ? "켜짐 → 실측 동안 끔(재시작 사망 처리 떼어 냄)" : "꺼짐")}");
    }

    private static void OnLichDied(MonsterBase _)
    {
        s_raiseDied    = true;
        s_exileAtClear = ExileFallSequence.IsActive;
    }

    private static void RestoreLich(StringBuilder sb)
    {
        if (s_lich == null) return;   // 씬이 바뀌어 리치가 사라졌다 — 인스턴스 값이라 되돌릴 것이 없다(프리팹 값 그대로)
        s_lich.OnDied -= OnLichDied;
        if (s_debugWas) typeof(LichMonster).GetField("_debugOverrideEncounter", Any)?.SetValue(s_lich, true);
        sb.AppendLine("리치 감시 해제 · 테스트 조우 길 원래대로");
        s_lich = null;
    }

    /// <summary>이른 저장 확인 — 코어 붕괴 순간 「다음 = Ch1 대기방」 런 저장이 써졌는가(챕터 1 · 대기방부터 · 전투 잠금 풀림).</summary>
    private static void LogEarlySave(StringBuilder sb)
    {
        var pm = RunProgressManager.Instance;
        int slot = pm != null ? pm.ActiveSlotIndex : -1;
        var save = pm != null && pm.HasLocalRun(slot) ? pm.LoadLocalRun(slot) : null;
        if (save == null) { sb.AppendLine("이른 저장: 로컬 런 저장 없음 — 결함"); return; }
        bool ok = save.chapter == (int)ChapterId.Chapter1 && save.chapterStartPending && !save.combatLocked;
        sb.AppendLine($"이른 저장(추방 +{ExileFallSequence.Elapsed:0.0}초): 챕터 {save.chapter} · 대기방부터 {save.chapterStartPending} · 전투 잠금 {save.combatLocked} " +
                      $"· 깊이 {save.abyssDepth} → {(ok ? "통과" : "결함")}");
    }

    /// <summary>HUD 판정 — HUD 루트(@HUD · HudPresenter)가 화면에 보이면 결함(보스방 클리어가 HUD 모드를 Combat으로 되돌려도 숨어 있어야 한다).</summary>
    private static void LogHud(string name, StringBuilder sb)
    {
        var hud     = Object.FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
        var group   = hud != null ? hud.GetComponent<CanvasGroup>() : null;
        bool shown  = hud != null && hud.gameObject.activeInHierarchy && (group == null || group.alpha > 0.01f);
        bool compass = CompassShown();
        var  run    = GameRunBootstrapper.Instance?.Run;
        if (shown)   s_hudDefects++;
        if (compass) s_hudDefects++;
        sb.AppendLine($"    HUD {name}: @HUD 보임={(shown ? "예 → 결함" : "아니오 → 통과")} · 적 나침반 보임={(compass ? "예 → 결함" : "아니오 → 통과")} · " +
                      $"HUD 모드 {run?.CurrentHudMode} · 일시정지 막힘 {ExileFallSequence.BlocksPause}");
    }

    /// <summary>리치 8 m 앞(지금 플레이어 쪽) 바닥으로 옮긴다 — 감지 반경(12 m) 안.</summary>
    private static void StepNearLich(LichMonster lich)
    {
        var player = RunPlayer();
        if (player == null || lich == null) return;
        Vector3 l   = lich.transform.position;
        Vector3 dir = player.transform.position - l;
        dir.y = 0f;
        dir   = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.back;
        Vector3 at = l + dir * 8f;
        at.y = Physics.Raycast(new Vector3(at.x, l.y + 10f, at.z), Vector3.down, out var hit, 60f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore)
            ? hit.point.y + 0.25f
            : player.transform.position.y;
        player.transform.position = at;
        if (player.TryGetComponent<Rigidbody>(out var rb) && !rb.isKinematic) { rb.position = at; rb.linearVelocity = Vector3.zero; }
    }

    /// <summary>
    /// 층 기록 — 화면 평균 색(사진에서) · 카메라 배경 · 씬 안개 · Buto(무대가 끄는지 · 지금 켜져 있는지) · 화면 안 파편 개수 · 평균 크기(px) · 보이는 돌.
    /// 층마다 화면 전체 색이 달라지는지, 파편이 읽히는 크기인지 판정한다.
    /// </summary>
    private static void LogLayer(string name, string screenColor, StringBuilder sb)
    {
        var cam   = Camera.main;
        var stage = Object.FindFirstObjectByType<ExileFallStage>();
        var buto  = VolumeManager.instance?.stack?.GetComponent<ButoVolumetricFog>();
        string fog = RenderSettings.fog
            ? $"{RenderSettings.fogMode} {(RenderSettings.fogMode == FogMode.Linear ? $"{RenderSettings.fogStartDistance:0}~{RenderSettings.fogEndDistance:0} m" : RenderSettings.fogDensity.ToString("0.000"))} #{ColorUtility.ToHtmlStringRGB(RenderSettings.fogColor)}"
            : "꺼짐";
        int inView = 0;
        float pxSum = 0f;
        if (stage != null && cam != null)
        {
            float pxPerRad = Screen.height / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            foreach (var ps in stage.Clouds)
            {
                if (ps == null) continue;
                int count = ps.GetParticles(s_particles);
                for (int i = 0; i < count; i++)
                {
                    Vector3 wp = s_particles[i].position;
                    Vector3 vp = cam.WorldToViewportPoint(wp);
                    if (vp.z <= 0.3f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) continue;
                    inView++;
                    pxSum += s_particles[i].GetCurrentSize(ps) / vp.z * pxPerRad;
                }
            }
        }
        int stones = 0;
        if (stage?.Stones != null)
            foreach (var t in stage.Stones)
                if (t != null && t.GetComponentsInChildren<Renderer>().Any(r => r.isVisible)) stones++;
        sb.AppendLine($"    층 {name}: 화면 평균 #{screenColor} · 카메라 배경 #{(cam != null ? ColorUtility.ToHtmlStringRGB(cam.backgroundColor) : "-")} · 안개 {fog} · " +
                      $"Buto 무대가 끔={(stage != null && stage.ButoSuppressed)} · 지금 켜짐={(buto != null && buto.IsActive())} · " +
                      $"파편 화면 안 {inView}개 · 평균 {(inView > 0 ? pxSum / inView : 0f):0}px · 보이는 돌 {stones}");
    }

    /// <summary>적 나침반(@HUD 밖 · 자기 캔버스)의 화살표 · 보스 배지가 보이는가.</summary>
    private static bool CompassShown()
    {
        var compass = Object.FindFirstObjectByType<EnemyCompassHud>();
        if (compass == null) return false;
        foreach (var g in compass.GetComponentsInChildren<CanvasGroup>(true))
            if ((g.name == "NearestArrow" || g.name == "BossBadge") && g.gameObject.activeInHierarchy && g.alpha > 0.01f) return true;
        return false;
    }

    private static bool AnyStone() => Object.FindObjectsByType<LichSealStone>(FindObjectsSortMode.None).Length > 0;

    private static bool AnyUnlitStone() => Object.FindObjectsByType<LichSealStone>(FindObjectsSortMode.None).Any(s => !s.IsIgnited);

    // ── 기록 ────────────────────────────────────────────────────────
    private static PlayerController RunPlayer() => GameRunBootstrapper.Instance?.Run?.Player;

    /// <summary>런 한 줄 — 유물 · 무기 · 서약 · 파츠 · 룬(놓인/대기) · 깊이 · 챕터 · 체력.</summary>
    private static string DescribeRun()
    {
        var run = GameRunBootstrapper.Instance?.Run;
        var lo  = AppBootstrapper.Instance?.Loadout;
        if (run == null) return "런 없음";
        var covenants = run.CovenantHandler?.Covenants;
        string cov    = covenants != null ? string.Join(",", covenants.Where(c => c != null).Select(c => c.CovenantId)) : "-";
        string parts  = lo?.RelicPartIds != null ? string.Join(",", lo.RelicPartIds) : "-";
        var    stats  = run.Player != null ? run.Player.RuntimeStats : null;
        return $"유물 {(lo?.Relic != null ? lo.Relic.name : "-")} · 무기 {(lo?.WeaponSlot0 != null ? lo.WeaponSlot0.name : "-")}/{(lo?.WeaponSlot1 != null ? lo.WeaponSlot1.name : "-")} · " +
               $"서약 [{cov}] · 파츠 [{parts}] · 룬 놓임 {run.ItemInventory?.PlacedCount} 대기 {run.ItemInventory?.StagingCount} | " +
               $"깊이 {run.AbyssDepth} · 챕터 {run.CurrentChapter} · 체력 {(stats != null ? $"{stats.Hp}/{stats.MaxHp}" : "-")}";
    }

    /// <summary>빌드 부분(「|」 앞)이 같은가.</summary>
    private static bool SameBuild(string a, string b)
    {
        int ia = a.IndexOf('|'), ib = b.IndexOf('|');
        return ia > 0 && ib > 0 && a.Substring(0, ia) == b.Substring(0, ib);
    }

    private static void Dump(StringBuilder sb, string[] trace)
    {
        sb.AppendLine("추방 기록(시각 · 층 · 대사):");
        foreach (var line in trace) sb.AppendLine("  " + line);
    }

    private static void SetOverride(int value)
    {
        EditorPrefs.SetInt(StoryProgress.DebugOverridePrefsKey, value);
        StoryProgress.RefreshDebugOverride();
    }

    /// <summary>
    /// 10-13 착지 카메라 진단 — 착지 사진이 가까운 카메라(높이 1.9 m · 뒤 3.2 m) 대신 높은 부감으로 찍혔다(②d).
    /// 그 순간 화면을 그리는 카메라들 · 플레이어와의 높이 차 · Brain/FreeLook · 수동 제어를 적는다.
    /// </summary>
    private static void CamDiag(string when, StringBuilder sb)
    {
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        Vector3 pp = player != null ? player.transform.position : Vector3.zero;
        var gcc = GameCameraController.Instance;
        bool panning = gcc != null && (bool)(typeof(GameCameraController).GetField("_isPanning", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(gcc) ?? false);
        var brain = gcc != null ? gcc.GetComponent<Cinemachine.CinemachineBrain>() : null;
        sb.AppendLine($"  카메라 [{when}] 씬 {SceneManager.GetActiveScene().name} · 플레이어 {pp} · 조종 카메라 {(gcc != null ? gcc.name : "없음")} · 수동 {panning} · Brain {(brain != null ? brain.enabled.ToString() : "없음")}");
        foreach (var c in Camera.allCameras.OrderByDescending(c => c.depth))
        {
            Vector3 d = c.transform.position - pp;
            sb.AppendLine($"    {c.name} · 깊이 {c.depth} · {c.transform.position} · 높이 차 {d.y:0.0} m · 수평 {new Vector2(d.x, d.z).magnitude:0.0} m · RT {(c.targetTexture != null)} · 씬 {c.gameObject.scene.name}");
        }
    }

    // ── 사진 ────────────────────────────────────────────────────────
    private static async UniTask ShotAtFall(float fallTime, string name, StringBuilder sb, bool hud)
    {
        if (!await WaitAsync(() => ExileFallSequence.FallTime >= fallTime || ExileFallSequence.HasLanded, 30f)) return;
        if (ExileFallSequence.HasLanded) { sb.AppendLine($"  {name} — 이미 착지(건너뜀)"); return; }
        string color = await Shot(name, sb);
        LogLayer(name, color, sb);
        if (hud) LogHud(name, sb);
    }

    /// <summary>한 장 찍고 화면 가운데(레터박스 · 자막 띠 밖)의 평균 색을 돌려준다.</summary>
    private static async UniTask<string> Shot(string name, StringBuilder sb)
    {
        var runner = Object.FindFirstObjectByType<Managers>();
        if (runner == null) return "-";
        await UniTask.WaitForEndOfFrame(runner);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        if (tex == null) return "-";
        Directory.CreateDirectory(ShotDir);
        File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
        string avg = AverageColor(tex);
        Object.DestroyImmediate(tex);
        sb.AppendLine($"  사진 {name} · 추방 +{ExileFallSequence.Elapsed:0.00}초 · 추락 {ExileFallSequence.FallTime:0.00}초 · 층 {ExileFallSequence.LayerName}");
        return avg;
    }

    /// <summary>화면 가운데 띠(세로 25~65% · 가로 10~90%)의 평균 색 — 레터박스 · 자막 띠를 피한다.</summary>
    private static string AverageColor(Texture2D tex)
    {
        Color sum = Color.clear;
        int n = 0;
        for (int iy = 0; iy < 16; iy++)
            for (int ix = 0; ix < 32; ix++)
            {
                int x = (int)(tex.width  * (0.10f + 0.80f * ix / 31f));
                int y = (int)(tex.height * (0.35f + 0.40f * iy / 15f));   // 텍스처 아래가 0 — 화면 위 25~65%
                sum += tex.GetPixel(x, y);
                n++;
            }
        return ColorUtility.ToHtmlStringRGB(sum / n);
    }

    private static async UniTask<bool> WaitAsync(System.Func<bool> cond, float seconds)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            try { if (cond()) return true; }
            catch (System.Exception) { }
            await UniTask.Delay(100, DelayType.Realtime);
        }
        return false;
    }
}
#endif
