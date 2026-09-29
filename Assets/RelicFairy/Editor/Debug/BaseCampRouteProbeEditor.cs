using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 베이스캠프(「멀린의 공간」 BaseCamp_Sanctum) 동선 실측 — 에디터 전용, 플레이 중 베이스캠프 씬에서.
///   · 경로 걷기: 이름으로 찾은 지점을 차례로 실제 이동(입력 주입)으로 걷는다 — 구간마다 걸린 시간·걸은 거리·끼임(1초 넘게 거의 못 움직임)·
///     공중 프레임(접지 아님)·최저/최고 높이. 걷는 동안 1 m마다 카메라→플레이어 머리 선이 무엇에 막히는지(가림) 기록.
///     「첫 판」 = 첫 소환진 → 무형검 → 회랑 → 광장 → 공방 → 유물 → 성문 → 포탈 · 「매 판」 = 광장 소환진부터.
///   · 조작 막힘: 걷는 동안 플레이어 입력이 막힌 시간(연출 채널 · UI 팝업 · 컨트롤 정지)을 구간마다 잰다 — 막힌 동안은
///     입력을 넣지 않고(실제 플레이어처럼) 끼임 판정도 멈춘다. 사용자 규칙: 매 판 연출은 막아도 0.5초 이하.
///     구간 끝마다 카메라가 플레이어에서 얼마나 높고 먼지도 적는다 — 카메라 구역(회랑·하강)을 나온 뒤 광장 값으로 돌아오는지.
///   · 첫 화면: 광장 소환진에 세우고 카메라가 자리 잡은 뒤, 스테이션마다 화면 안/밖·가림을 잰다 + 캡처.
///   · 가장자리 밀기: 광장 가운데에서 15°마다 바깥으로 계속 걸어 — 어디서 멈추는지(연석·결계)·공중·떨어짐.
///   · 생성 연출 관찰: 처음 한 번 「멀린의 공간 생성」이 시작되길 기다렸다가 도중 화면을 캡처하고 평균 밝기를 잰다(검은 화면 뒤에서 도는지).
///   · 무형검 연출: 받침 앞에서 BaseCampFxDirector 무형검 연출만 틀어 입력 막힘 시간·카메라 복귀를 잰다(세이브는 건드리지 않음).
/// 지점은 루트(BaseCamp_Sanctum) 아래 이름으로 찾는다 — 없으면 그 지점은 건너뛴다(배치가 바뀌어도 이름만 같으면 그대로 쓴다).
/// 「@x,y,z」는 루트 로컬 좌표 지점(오브젝트 피벗이 길에서 벗어난 곳 — 성문은 피벗이 통로 옆 11 m ·
/// 유물 제단은 스테이션이 서 있어 제단 앞·가운데 통로로 · 무형검은 받침이 문 일직선 위라 받침 옆으로 — 실제 걷는 길).
/// 결과: Logs/basecamp_route/&lt;시각&gt;/report.txt + 캡처. 플레이어는 실측 동안 무적·입력 가로챔.
/// </summary>
public static class BaseCampRouteProbeEditor
{
    // ── Constants ──────────────────────────────────────────────
    private const string Root        = "RelicFairy/Debug/BaseCamp Sanctum/";
    private const string RootName    = "BaseCamp_Sanctum";
    private const float  ArriveDist  = 2.2f;    // 이 안에 들어오면 도착
    private const float  StuckSpeed  = 0.3f;    // 이보다 느리면 멈춘 것으로 친다(m/s)
    private const float  StuckTime   = 1.0f;    // 이만큼 멈춰 있으면 끼임
    private const float  HeadHeight  = 1.5f;
    private const float  MaxLockWait = 20f;    // 조작 막힘을 기다리는 상한(대사창이 입력을 기다리면 끝나지 않는다)
    private const float  EdgeStartR  = 16f;    // 가장자리 밀기 — 출발 반경(루트 로컬)
    private const float  EdgeGoalR   = 34f;    //   목표 반경(광장 바깥 허공) — 여기까지 가면 막는 것이 없다
    private static readonly Vector3 PlazaCenterLocal = new(71.4f, 9f, 0f);
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    // 경로 — (표시 이름, 찾을 오브젝트 이름 후보들). 앞의 것이 있으면 그것.
    private static readonly (string label, string[] names)[] FirstRun =
    {
        ("첫 소환진",   new[] { "Markers/FirstSpawn", "SummonRoom/SummonCircle" }),
        ("무형검 단",   new[] { "Markers/SwordSpot", "SummonRoom/SwordDais" }),
        ("무형검 옆",   new[] { "@5,0,3.5" }),
        ("회랑 1단",    new[] { "Ascent/Flight1_Top" }),
        ("회랑 2단",    new[] { "Ascent/Flight2_Top" }),
        ("회랑 3단",    new[] { "Ascent/Flight3_Top" }),
        ("문턱",        new[] { "Ascent/Threshold" }),
        ("장비 공방",   new[] { "Markers/ForgeSpot", "Stations/ForgeDais" }),
        ("파츠 공방",   new[] { "Markers/PartsSpot", "Stations/PartsDais" }),
        ("유물 제단 앞", new[] { "@82.9,9,3.51" }),
        ("유물 통로",   new[] { "@87.5,9,0" }),
        ("성문 앞",     new[] { "Markers/GateFront" }),
        ("성문 통로",   new[] { "@93.5,9,0" }),
        ("하강 층계참", new[] { "Descent/DownLanding" }),
        ("포탈",        new[] { "Markers/PortalCenter", "Descent/PortalDais" }),
    };
    private static readonly (string label, string[] names)[] ReturnRun =
    {
        ("광장 소환진", new[] { "Markers/ReturnSpawn", "Plaza/SummonCircle" }),
        ("장비 공방",   new[] { "Markers/ForgeSpot", "Stations/ForgeDais" }),
        ("파츠 공방",   new[] { "Markers/PartsSpot", "Stations/PartsDais" }),
        ("유물 제단 앞", new[] { "@82.9,9,3.51" }),
        ("유물 통로",   new[] { "@87.5,9,0" }),
        ("성문 앞",     new[] { "Markers/GateFront" }),
        ("성문 통로",   new[] { "@93.5,9,0" }),
        ("하강 층계참", new[] { "Descent/DownLanding" }),
        ("포탈",        new[] { "Markers/PortalCenter", "Descent/PortalDais" }),
    };
    // 광장 둘레 걷기 — 반경 20 m(난간 무리 r 22 안쪽) 한 바퀴: 난간·받침에 걸리는지
    private static readonly (string label, string[] names)[] RimRun =
    {
        ("둘레 180°", new[] { "@51.40,9,0.00" }),
        ("둘레 210°", new[] { "@54.08,9,-10.00" }),
        ("둘레 240°", new[] { "@61.40,9,-17.32" }),
        ("둘레 270°", new[] { "@71.40,9,-20.00" }),
        ("둘레 300°", new[] { "@81.40,9,-17.32" }),
        ("둘레 330°", new[] { "@88.72,9,-10.00" }),
        ("둘레 000°", new[] { "@91.40,9,-0.00" }),
        ("둘레 030°", new[] { "@88.72,9,10.00" }),
        ("둘레 060°", new[] { "@81.40,9,17.32" }),
        ("둘레 090°", new[] { "@71.40,9,20.00" }),
        ("둘레 120°", new[] { "@61.40,9,17.32" }),
        ("둘레 150°", new[] { "@54.08,9,10.00" }),
        ("둘레 180°", new[] { "@51.40,9,0.00" }),
    };
    // 첫 화면에 보여야 할 것(필수) · 보이면 좋은 것
    private static readonly (string label, string[] names, bool must)[] ViewTargets =
    {
        ("장비 공방",   new[] { "Markers/ForgeSpot", "Stations/ForgeDais" }, true),
        ("파츠 공방",   new[] { "Markers/PartsSpot", "Stations/PartsDais" }, true),
        ("유물 제단",   new[] { "Markers/RelicSlot_+17", "RelicSanctum/AltarSlot_+17" }, true),
        ("성문 앞",     new[] { "Markers/GateFront" }, true),    // 09-28 문 기준 재배치 — 첫 화면 정면에 성문
        ("기억의 제단", new[] { "Markers/MemoryAltarSpot", "Stations/MemoryAltar" }, false),
        ("봉인석",      new[] { "Markers/SealShrineSpot", "Stations/SealShrine" }, false),
        ("허수아비",    new[] { "Stations/TrainingDummy" }, false),
    };

    private sealed class Leg
    {
        public string From, To;
        public float Straight, Walked, Seconds, MinY = float.MaxValue, MaxY = float.MinValue, StuckAt = -1f;
        public int AirFrames, Frames;
        public float LockedSeconds;
        public float CamH, CamR;   // 도착 때 카메라 — 플레이어보다 높이 · 수평 거리
        public bool Arrived;
    }

    // ── Static ─────────────────────────────────────────────────
    private static CancellationTokenSource s_cts;
    private static CancellationTokenSource s_lockCts;   // 막힘 감시(다른 실측과 따로)
    private static CancellationTokenSource s_watchCts;  // 소환 캡처 감시
    private static string s_lastFx = "-";

    // ── Public Methods ─────────────────────────────────────────
    [MenuItem(Root + "Route Walk - First Run (Play)")]  public static void WalkFirst()  => Start(ct => WalkAsync("첫 판", FirstRun, ct));
    [MenuItem(Root + "Route Walk - Return Run (Play)")] public static void WalkReturn() => Start(ct => WalkAsync("매 판", ReturnRun, ct));
    [MenuItem(Root + "Station View From Return Spawn (Play)")] public static void View() => Start(ViewAsync);
    [MenuItem(Root + "Terrain By Floor (Play)")]               public static void Floors() => Start(FloorsAsync);
    [MenuItem(Root + "Route Walk - Plaza Rim (Play)")]         public static void WalkRim() => Start(ct => WalkAsync("광장 둘레", RimRun, ct));
    [MenuItem(Root + "Edge Push - Plaza (Play)")]              public static void EdgePush() => Start(EdgePushAsync);
    [MenuItem(Root + "Sword Receive Fx (Play)")]               public static void SwordFx() => Start(SwordFxAsync);
    [MenuItem(Root + "Watch Conjure (Play)")]                  public static void WatchConjure() => Start(WatchConjureAsync);

    /// <summary>
    /// 조작 막힘·시간 배율 감시(백그라운드, 최대 10분) — 막힘이 풀릴 때마다 길이와 채널, 직전 [BaseCampFx] 로그를 남긴다.
    /// 설계서 3장: 매 판 연출은 조작을 막지 않고 시간을 멈추거나 느리게 하지 않는다. 다른 실측 메뉴와 따로 돈다(서로 취소하지 않음).
    /// </summary>
    [MenuItem(Root + "Lock Monitor - Start (Play)")]
    public static void LockMonitorStart()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[RouteProbe] 플레이 모드에서만."); return; }
        s_lockCts?.Cancel(); s_lockCts?.Dispose();
        s_lockCts = new CancellationTokenSource();
        LockMonitorAsync(s_lockCts.Token).Forget();
    }

    [MenuItem(Root + "Lock Monitor - Stop (Play)")]
    public static void LockMonitorStop() { s_lockCts?.Cancel(); }

    private static async UniTaskVoid LockMonitorAsync(CancellationToken ct)
    {
        var fields = new[] { "_inputDisabledExternally", "_inputBlockedByUI", "_controlSuspended" }
                     .Select(n => (n, f: typeof(PlayerController).GetField(n, Inst))).Where(x => x.f != null).ToArray();
        void OnLog(string msg, string st, LogType lt) { if (msg.StartsWith("[BaseCampFx]")) s_lastFx = msg.Length > 60 ? msg.Substring(0, 60) : msg; }
        Application.logMessageReceived += OnLog;
        Debug.Log("[RouteProbe] 막힘 감시 시작");
        float t0 = Time.realtimeSinceStartup, lockStart = -1f, slowStart = -1f;
        string lockWhy = "", slowFx = "";
        float slowScale = 1f;
        int count = 0;
        try
        {
            while (!ct.IsCancellationRequested && Time.realtimeSinceStartup - t0 < 600f)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                var player = FindPlayer();
                float now = Time.realtimeSinceStartup;
                var on = player != null ? fields.Where(x => (bool)x.f.GetValue(player)).Select(x => x.n.TrimStart('_')).ToArray() : Array.Empty<string>();
                if (on.Length > 0 && lockStart < 0f) { lockStart = now; lockWhy = string.Join("+", on); }
                else if (on.Length == 0 && lockStart >= 0f)
                {
                    count++;
                    Debug.Log($"[RouteProbe] 조작 막힘 {now - lockStart:0.00}초 · {lockWhy} · 직전 연출 「{s_lastFx}」");
                    lockStart = -1f;
                }
                if (Time.timeScale < 0.99f && slowStart < 0f) { slowStart = now; slowScale = Time.timeScale; slowFx = s_lastFx; }
                else if (Time.timeScale < 0.99f) slowScale = Mathf.Min(slowScale, Time.timeScale);
                else if (slowStart >= 0f)
                {
                    Debug.Log($"[RouteProbe] 시간 배율 {slowScale:0.00} {now - slowStart:0.00}초 · 직전 연출 「{slowFx}」");
                    slowStart = -1f;
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            Application.logMessageReceived -= OnLog;
            Debug.Log($"[RouteProbe] 막힘 감시 끝 — 막힘 {count}회");
        }
    }

    // ── 4단계 연출 실측(설계서 3장) ─────────────────────────────

    /// <summary>「[BaseCampFx] 소환 시작」 로그가 날 때마다 0.5초·1.2초 뒤 화면을 캡처(밝기 포함) — 최대 4회·10분. 베이스캠프 로드 전에 걸어 둔다.</summary>
    [MenuItem(Root + "Watch Summon (Play)")]
    public static void WatchSummon()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[RouteProbe] 플레이 모드에서만."); return; }
        s_watchCts?.Cancel(); s_watchCts?.Dispose();
        s_watchCts = new CancellationTokenSource();
        WatchSummonAsync(s_watchCts.Token).Forget();
    }

    private static async UniTaskVoid WatchSummonAsync(CancellationToken ct)
    {
        string pending = null;
        void OnLog(string msg, string st, LogType lt) { if (msg.StartsWith("[BaseCampFx] 소환 시작")) pending = msg.Substring("[BaseCampFx] 소환 시작".Length).Trim(' ', '—'); }
        Application.logMessageReceived += OnLog;
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "basecamp_route", $"{DateTime.Now:MMdd_HHmmss}_summon");
        Directory.CreateDirectory(dir);
        int n = 0;
        float t0 = Time.realtimeSinceStartup;
        try
        {
            while (n < 4 && Time.realtimeSinceStartup - t0 < 600f)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                if (pending == null) continue;
                string kind = pending; pending = null; n++;
                float s0 = Time.realtimeSinceStartup;
                var parts = new List<string>();
                foreach (float m in new[] { 0.25f, 0.7f, 1.4f, 1.8f, 2.4f })   // 빛이 먼저(0.45초) → 그 안에서 몸(0.9초) — 09-28 사용자 결정 · 처음(First)은 몸 완성 2.4초
                {
                    while (Time.realtimeSinceStartup - s0 < m) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    float l = await CaptureLumAsync(Path.Combine(dir, $"{n}_{Clean(kind)}_{m:0.00}s.png"), ct);
                    parts.Add($"{m:0.00}초 밝기 {l:0.000}");
                }
                Debug.Log($"[RouteProbe] 소환 캡처 {kind} — {string.Join(" · ", parts)} — {dir}");
            }
        }
        catch (OperationCanceledException) { }
        finally { Application.logMessageReceived -= OnLog; }
    }

    /// <summary>광장 가운데에서 성문 쪽(yaw 90)을 보고 준비 룬 3개 · 성문 · 포탈이 화면 어디에 있는지(HUD 띠 y&lt;0.25와 겹침) + 캡처.</summary>
    [MenuItem(Root + "Gate Runes View (Play)")] public static void GateRunesView() => Start(GateRunesViewAsync);

    private static async UniTask GateRunesViewAsync(CancellationToken ct)
    {
        var player = FindPlayer();
        var root = GameObject.Find(RootName)?.transform;
        if (player == null || root == null) { Debug.LogWarning("[RouteProbe] 준비 안 됨"); return; }
        try
        {
            Teleport(player, Ground(root.TransformPoint(PlazaCenterLocal)) + Vector3.up * 0.1f);
            player.RequestFacing(Quaternion.Euler(0f, 90f, 0f));
            GameCameraController.Instance?.RotateHeadingTo(90f, 0f);
            await UniTask.Delay(1800, ignoreTimeScale: true, cancellationToken: ct);
            var cam = Camera.main;
            var sb = new StringBuilder();
            string Where(Vector3 w)
            {
                Vector3 v = cam.WorldToViewportPoint(w);
                bool inView = v.z > 0f && v.x > 0f && v.x < 1f && v.y > 0f && v.y < 1f;
                return inView ? $"({v.x:0.00},{v.y:0.00}){(v.y < 0.25f ? " ⚠️HUD 띠" : "")}" : "화면 밖";
            }
            var fx = BaseCampFxDirector.Instance;
            var on = typeof(BaseCampFxDirector).GetField("_runeOn", Inst)?.GetValue(fx) as bool[];
            var gate = ResolveTransform(root, new[] { "Markers/GateFront" });
            sb.AppendLine($"성문 쪽 시점 — 카메라 yaw {cam.transform.eulerAngles.y:0}° · 성문 앞 {(gate != null ? Where(gate.position + Vector3.up * 3f) : "-")}");
            sb.AppendLine($"  준비 상태(원거리/파츠/유물): {(on != null ? string.Join("/", on.Select(b => b ? "켜짐" : "꺼짐")) : "-")}");
            foreach (var line in RuneLines(cam)) sb.AppendLine(line);
            var gateComp = UnityEngine.Object.FindFirstObjectByType<BaseCampDungeonGate>();
            var portal = gateComp != null ? typeof(BaseCampDungeonGate).GetField("portalActive", Inst)?.GetValue(gateComp) as GameObject : null;
            sb.AppendLine($"  포탈 비주얼 {(portal != null ? (portal.activeInHierarchy ? "켜짐" : "꺼짐") : "-")}");
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "basecamp_route", $"{DateTime.Now:MMdd_HHmmss}_gate");
            Directory.CreateDirectory(dir);
            float lum = await CaptureLumAsync(Path.Combine(dir, "gate.png"), ct);
            sb.AppendLine($"  화면 밝기 {lum:0.000}");
            File.WriteAllText(Path.Combine(dir, "report.txt"), sb.ToString(), new UTF8Encoding(true));
            Debug.Log($"[RouteProbe] 성문 룬 시점 — {sb.ToString().Replace("\n", " ").Replace("\r", "")} — {dir}");
        }
        catch (OperationCanceledException) { Debug.Log("[RouteProbe] 취소"); }
    }

    /// <summary>성문 앞 약 5 m(루트 로컬 x 86)에서 성문 쪽(yaw 90)을 보고 캡처 — 마력 막(닫힘/열림)·아치 등불을 가까이서.</summary>
    [MenuItem(Root + "Gate Close View (Play)")] public static void GateCloseView() => Start(GateCloseViewAsync);

    private static async UniTask GateCloseViewAsync(CancellationToken ct)
    {
        var player = FindPlayer();
        var root = GameObject.Find(RootName)?.transform;
        if (player == null || root == null) { Debug.LogWarning("[RouteProbe] 준비 안 됨"); return; }
        try
        {
            Teleport(player, Ground(root.TransformPoint(new Vector3(86f, 9f, 0f))) + Vector3.up * 0.1f);
            player.RequestFacing(Quaternion.Euler(0f, 90f, 0f));
            GameCameraController.Instance?.RotateHeadingTo(90f, 0f);
            await UniTask.Delay(1500, ignoreTimeScale: true, cancellationToken: ct);
            var on = typeof(BaseCampFxDirector).GetField("_runeOn", Inst)?.GetValue(BaseCampFxDirector.Instance) as bool[];
            string state = on != null && on.All(b => b) ? "open" : "closed";
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "basecamp_route", $"{DateTime.Now:MMdd_HHmmss}_gateclose_{state}");
            Directory.CreateDirectory(dir);
            float lum = await CaptureLumAsync(Path.Combine(dir, "gate_close.png"), ct);
            var sb = new StringBuilder();
            sb.AppendLine($"성문 가까이 — 준비 {(on != null ? string.Join("/", on.Select(b => b ? "켜짐" : "꺼짐")) : "-")} · 화면 밝기 {lum:0.000}");
            foreach (var line in RuneLines(Camera.main)) sb.AppendLine(line);
            File.WriteAllText(Path.Combine(dir, "report.txt"), sb.ToString(), new UTF8Encoding(true));
            Debug.Log($"[RouteProbe] 성문 가까이 — {sb.ToString().Replace("\r", "").Replace("\n", " ")} — {dir}");
        }
        catch (OperationCanceledException) { Debug.Log("[RouteProbe] 취소"); }
    }

    /// <summary>
    /// 스테이션 세 곳을 실제 경로로 채운다(창 없이): 원거리 = 장비 공방 EquipChoiceAsync(활) · 파츠 = 첫 파츠 id + 파츠 얻기 연출(StartPartStation과 같은 두 줄) ·
    /// 유물 = 첫 유물 제단 Claim(로드아웃·연출·재스폰). 각 얻기 연출 중 0.4·1.0·1.6초 캡처. 플레이어는 광장 소환진에서 첫 화면 방향.
    /// </summary>
    [MenuItem(Root + "Acquire All - Station Paths (Play)")] public static void AcquireAll() => Start(AcquireAllAsync);

    private static async UniTask AcquireAllAsync(CancellationToken ct)
    {
        var player = FindPlayer();
        var root = GameObject.Find(RootName)?.transform;
        var lo = AppBootstrapper.Instance?.Loadout;
        var fx = BaseCampFxDirector.Instance;
        var forge = UnityEngine.Object.FindFirstObjectByType<WeaponForgeAltar>();
        var partSt = UnityEngine.Object.FindFirstObjectByType<StartPartStation>();
        var altar = UnityEngine.Object.FindObjectsByType<RelicAltar>(FindObjectsSortMode.None)
                    .FirstOrDefault(a => typeof(RelicAltar).GetField("relicClass", Inst)?.GetValue(a) != null);
        if (player == null || root == null || lo == null || fx == null || forge == null || partSt == null || altar == null)
        { Debug.LogWarning($"[RouteProbe] 준비 안 됨 — player={player != null} lo={lo != null} fx={fx != null} forge={forge != null} part={partSt != null} relic={altar != null}"); return; }

        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "basecamp_route", $"{DateTime.Now:MMdd_HHmmss}_acquire");
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder();
        // 연출 로그가 찍힌 시각 — F(받기 호출) → 「무형검 받기 시작」 간격 · 원거리 → 「파츠 작업대 깨어남」 시간(09-28 D안)
        var marks = new List<(float t, string msg)>();
        void OnLog(string msg, string stack, LogType type)
        {
            if (msg.StartsWith("[BaseCampFx]") || msg.StartsWith("[WorldSwordAwakening]")) marks.Add((Time.realtimeSinceStartup, msg));
        }
        float Since(float t0, string key)
        {
            foreach (var m in marks) if (m.t >= t0 && m.msg.Contains(key)) return m.t - t0;
            return -1f;
        }
        var orbital = typeof(BaseCampFxDirector).GetField("partsOrbital", Inst)?.GetValue(fx) as GameObject;
        Application.logMessageReceived += OnLog;
        async UniTask Shots(string tag)
        {
            float s0 = Time.realtimeSinceStartup;
            foreach (float m in new[] { 0.4f, 1.0f, 1.6f })
            {
                while (Time.realtimeSinceStartup - s0 < m) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                float lum = await CaptureLumAsync(Path.Combine(dir, $"{tag}_{m:0.0}s.png"), ct);
                sb.AppendLine($"  {tag} {m:0.0}s 밝기 {lum:0.000}");
            }
        }
        try
        {
            // ⓪ 무형검 — 슬롯이 아직 검을 안 받았으면(새 세이브) 소환의 방 받침에서 실제 경로(AwakenAsync)로 받는다 — 무형검 받기 연출도 진짜 받침으로 잰다
            var swordAltar = UnityEngine.Object.FindFirstObjectByType<WorldSwordAwakening>();
            if (lo.WeaponSlot0 == null && swordAltar != null)
            {
                var swordSpot = Resolve(root, new[] { "Markers/SwordSpot" });
                if (swordSpot != null) Teleport(player, Ground(swordSpot.Value) + Vector3.up * 0.1f);
                await UniTask.Delay(1200, ignoreTimeScale: true, cancellationToken: ct);
                typeof(WorldSwordAwakening).GetField("_player", Inst)?.SetValue(swordAltar, player);
                float f0 = Time.realtimeSinceStartup;   // F를 누른 순간(Update의 F 분기가 부르는 것과 같은 메서드)
                typeof(WorldSwordAwakening).GetMethod("AwakenAsync", Inst)?.Invoke(swordAltar, new object[] { ct });
                await Shots("0_sword");
                float w0 = Time.realtimeSinceStartup;
                while (lo.WeaponSlot0 == null && Time.realtimeSinceStartup - w0 < 5f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                while (Since(f0, "무형검 각성") < 0f && Time.realtimeSinceStartup - f0 < 8f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
                sb.AppendLine($"무형검(받침): {(lo.WeaponSlot0 != null ? lo.WeaponSlot0.name : "⚠️ 못 받음")}");
                sb.AppendLine($"  F → 「무형검 받기 시작」 {Since(f0, "무형검 받기 시작"):0.00}초 · F → 손에 쥠(각성 로그) {Since(f0, "무형검 각성"):0.00}초 (-1 = 안 찍힘)");
                await UniTask.Delay(800, ignoreTimeScale: true, cancellationToken: ct);
            }
            else sb.AppendLine($"무형검: {(lo.WeaponSlot0 != null ? lo.WeaponSlot0.name + "(이미 있음)" : "없음 · 받침도 없음")}");

            var spawnT = ResolveTransform(root, new[] { "Markers/ReturnSpawn" });
            player = FindPlayer();
            Teleport(player, Ground(spawnT.position) + Vector3.up * 0.1f);
            player.RequestFacing(Quaternion.Euler(0f, spawnT.eulerAngles.y, 0f));
            GameCameraController.Instance?.RotateHeadingTo(spawnT.eulerAngles.y, 0f);
            await UniTask.Delay(1500, ignoreTimeScale: true, cancellationToken: ct);

            // ① 원거리
            var bow = typeof(WeaponForgeAltar).GetField("bowOption", Inst)?.GetValue(forge) as WeaponSO;
            typeof(WeaponForgeAltar).GetField("_player", Inst)?.SetValue(forge, player);
            bool orbBefore = orbital != null && orbital.activeInHierarchy;
            float r0 = Time.realtimeSinceStartup;
            var equip = (UniTask)typeof(WeaponForgeAltar).GetMethod("EquipChoiceAsync", Inst).Invoke(forge, new object[] { lo, bow, ct });
            await Shots("1_ranged");
            await equip;
            sb.AppendLine($"원거리: {(lo.WeaponSlot1 != null ? lo.WeaponSlot1.name : "null")}");
            // 원거리를 얻으면 공방 → 파츠 작업대로 빛이 건너가 궤도 구슬이 켜진다
            while (Since(r0, "파츠 작업대 깨어남") < 0f && Time.realtimeSinceStartup - r0 < 8f) await UniTask.Yield(PlayerLoopTiming.Update, ct);
            await UniTask.Delay(300, ignoreTimeScale: true, cancellationToken: ct);
            float wakeLum = await CaptureLumAsync(Path.Combine(dir, "1w_parts_awake.png"), ct);
            sb.AppendLine($"  파츠 작업대: 궤도 구슬 {(orbital == null ? "필드 없음" : $"{(orbBefore ? "켜짐" : "꺼짐")} → {(orbital.activeInHierarchy ? "켜짐" : "꺼짐")}")}" +
                          $" · 원거리 획득 → 「파츠 작업대 깨어남」 {Since(r0, "파츠 작업대 깨어남"):0.00}초 · 캡처 밝기 {wakeLum:0.000}");

            // ② 파츠
            string partId = Managers.WeaponParts?.All?.Count > 0 ? Managers.WeaponParts.All[0].part_id : null;
            lo.SetStartPart(partId);
            fx.PlayAcquire(partSt.transform.position + Vector3.up * 1.3f, BaseCampFxDirector.AcquireKind.Part);
            await Shots("2_part");
            sb.AppendLine($"파츠: {partId ?? "null"}");

            // ③ 유물 — Claim은 플레이어를 다시 세운다(재스폰)
            typeof(RelicAltar).GetField("_player", Inst)?.SetValue(altar, player);
            typeof(RelicAltar).GetMethod("Claim", Inst).Invoke(altar, null);
            await Shots("3_relic");
            await UniTask.Delay(1500, ignoreTimeScale: true, cancellationToken: ct);
            sb.AppendLine($"유물: {(lo.Relic != null ? lo.Relic.name : "null")} · 무형검 {(lo.WeaponSlot0 != null ? lo.WeaponSlot0.name : "null")}");
            var on = typeof(BaseCampFxDirector).GetField("_runeOn", Inst)?.GetValue(fx) as bool[];
            sb.AppendLine($"준비 룬: {(on != null ? string.Join("/", on.Select(b => b ? "켜짐" : "꺼짐")) : "-")}");
            File.WriteAllText(Path.Combine(dir, "report.txt"), sb.ToString(), new UTF8Encoding(true));
            Debug.Log($"[RouteProbe] 얻기 세 곳 완료 — {sb.ToString().Replace("\r", "").Replace("\n", " · ")} — {dir}");
        }
        catch (OperationCanceledException) { Debug.Log("[RouteProbe] 취소"); }
        finally { Application.logMessageReceived -= OnLog; }
    }

    /// <summary>
    /// 첫 진입(빈 슬롯 — SanctumDebug 10번 뒤) 무형검 받침: F(= Update의 F 분기가 부르는 AwakenAsync)를 누른 순간부터
    /// 「무형검 받기 시작」 · 손에 쥠(각성 로그) 간격, 입력 막힘 길이, F 전 · 0.4 · 1.0 · 1.6초 캡처. 받침은 검을 받은 적 없는 슬롯에만 있다.
    /// </summary>
    [MenuItem(Root + "First Flow - Sword Pedestal F (Play)")] public static void SwordPedestal() => Start(SwordPedestalAsync);

    private static async UniTask SwordPedestalAsync(CancellationToken ct)
    {
        var player = FindPlayer();
        var root = GameObject.Find(RootName)?.transform;
        var lo = AppBootstrapper.Instance?.Loadout;
        var altar = UnityEngine.Object.FindFirstObjectByType<WorldSwordAwakening>();
        if (player == null || root == null || lo == null || altar == null)
        { Debug.LogWarning($"[RouteProbe] 준비 안 됨 — player={player != null} root={root != null} lo={lo != null} 받침={altar != null}(검을 받은 슬롯이면 받침이 없다)"); return; }

        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "basecamp_route", $"{DateTime.Now:MMdd_HHmmss}_sword_pedestal");
        Directory.CreateDirectory(dir);
        float fAt = -1f, startAt = -1f, gripAt = -1f;
        void OnLog(string msg, string stack, LogType type)
        {
            float now = Time.realtimeSinceStartup;
            if (startAt < 0f && msg.StartsWith("[BaseCampFx] 무형검 받기 시작")) startAt = now;
            else if (gripAt < 0f && msg.StartsWith("[WorldSwordAwakening] 무형검 각성")) gripAt = now;
        }
        var lockFields = new[] { "_inputDisabledExternally", "_inputBlockedByUI", "_controlSuspended" }
                         .Select(n => typeof(PlayerController).GetField(n, Inst)).Where(f => f != null).ToArray();
        bool Locked() => lockFields.Any(f => (bool)f.GetValue(player));
        Application.logMessageReceived += OnLog;
        try
        {
            var spot = Resolve(root, new[] { "Markers/SwordSpot" });
            if (spot != null) Teleport(player, Ground(spot.Value) + Vector3.up * 0.1f);
            await UniTask.Delay(1500, ignoreTimeScale: true, cancellationToken: ct);
            var sb = new StringBuilder();
            sb.AppendLine($"F 전 밝기 {await CaptureLumAsync(Path.Combine(dir, "0_before_F.png"), ct):0.000}");

            typeof(WorldSwordAwakening).GetField("_player", Inst)?.SetValue(altar, player);
            fAt = Time.realtimeSinceStartup;
            typeof(WorldSwordAwakening).GetMethod("AwakenAsync", Inst)?.Invoke(altar, new object[] { ct });
            float locked = 0f, last = fAt;
            foreach (float m in new[] { 0.4f, 1.0f, 1.6f })
            {
                while (Time.realtimeSinceStartup - fAt < m)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    float now = Time.realtimeSinceStartup;
                    if (Locked()) locked += now - last;
                    last = now;
                }
                sb.AppendLine($"F 뒤 {m:0.0}초 밝기 {await CaptureLumAsync(Path.Combine(dir, $"{m:0.0}s.png"), ct):0.000}");
                last = Time.realtimeSinceStartup;
            }
            while ((gripAt < 0f || Locked()) && Time.realtimeSinceStartup - fAt < 8f)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                float now = Time.realtimeSinceStartup;
                if (Locked()) locked += now - last;
                last = now;
            }
            string line = $"F → 「무형검 받기 시작」 {(startAt < 0f ? -1f : startAt - fAt):0.00}초 · F → 손에 쥠 {(gripAt < 0f ? -1f : gripAt - fAt):0.00}초 · " +
                          $"입력 막힘 {locked:0.00}초 · 슬롯0 {(lo.WeaponSlot0 != null ? lo.WeaponSlot0.name : "없음")} (-1 = 안 찍힘)";
            sb.AppendLine(line);
            File.WriteAllText(Path.Combine(dir, "report.txt"), sb.ToString(), new UTF8Encoding(true));
            Debug.Log($"[RouteProbe] 무형검 받침 F — {line} — {dir}");
        }
        catch (OperationCanceledException) { Debug.Log("[RouteProbe] 취소"); }
        finally { Application.logMessageReceived -= OnLog; }
    }

    /// <summary>유물 제단마다 트리거 안(광장 쪽)에 서서 「[F] 유물 선택」이 켜진 순간 캡처 — 이름표 · [F]가 빛기둥 옆(labelOffset)에 보이는지. 유물을 안 받은 슬롯에서.</summary>
    [MenuItem(Root + "First Flow - Relic Prompts (Play)")] public static void RelicPrompts() => Start(RelicPromptsAsync);

    private static async UniTask RelicPromptsAsync(CancellationToken ct)
    {
        var player = FindPlayer();
        var root = GameObject.Find(RootName)?.transform;
        var altars = UnityEngine.Object.FindObjectsByType<RelicAltar>(FindObjectsSortMode.None).Where(a => a.RelicClass != null).ToArray();
        if (player == null || root == null || altars.Length == 0) { Debug.LogWarning($"[RouteProbe] 준비 안 됨 — player={player != null} root={root != null} 제단 {altars.Length}"); return; }

        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "basecamp_route", $"{DateTime.Now:MMdd_HHmmss}_relic_prompt");
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder();
        Vector3 center = root.TransformPoint(PlazaCenterLocal);
        try
        {
            foreach (var a in altars)
            {
                Vector3 toCenter = Flat(center - a.transform.position).normalized;
                float reach = a.TryGetComponent<Collider>(out var col) ? Mathf.Min(col.bounds.extents.x, col.bounds.extents.z) * 0.6f : 1.5f;
                Teleport(player, Ground(a.transform.position + toCenter * Mathf.Max(0.8f, reach)) + Vector3.up * 0.1f);
                var face = Quaternion.LookRotation(-toCenter);
                player.RequestFacing(face);
                GameCameraController.Instance?.RotateHeadingTo(face.eulerAngles.y, 0f);
                await UniTask.Delay(1300, ignoreTimeScale: true, cancellationToken: ct);
                var prompt = typeof(RelicAltar).GetField("_promptGo", Inst)?.GetValue(a) as GameObject;
                string name = Clean(a.RelicClass.name);
                float lum = await CaptureLumAsync(Path.Combine(dir, $"relic_{name}.png"), ct);
                string where = "-";
                var cam = Camera.main;
                if (prompt != null && cam != null)
                {
                    Vector3 v = cam.WorldToViewportPoint(prompt.transform.position);
                    where = $"화면 ({v.x:0.00},{v.y:0.00}) · 제단에서 {Flat(prompt.transform.position - a.transform.position).magnitude:0.0} m 옆 · 높이 {prompt.transform.position.y - a.transform.position.y:0.0} m";
                }
                sb.AppendLine($"{name}: [F] {(prompt != null && prompt.activeInHierarchy ? "켜짐" : "꺼짐")} · {where} · 밝기 {lum:0.000}");
            }
            File.WriteAllText(Path.Combine(dir, "report.txt"), sb.ToString(), new UTF8Encoding(true));
            Debug.Log($"[RouteProbe] 유물 제단 [F] — {sb.ToString().Replace("\r", "").Replace("\n", " · ")} — {dir}");
        }
        catch (OperationCanceledException) { Debug.Log("[RouteProbe] 취소"); }
    }

    /// <summary>성문 → 하강 층계참 → 포탈로 걸으며 도착마다 화면 밝기(어두워지는지)를 재고, 포탈에서 「심연 진입」 로그가 나면 멈춘다(씬이 바뀐다).</summary>
    [MenuItem(Root + "Abyss Walk (Play)")] public static void AbyssWalk() => Start(AbyssWalkAsync);

    private static async UniTask AbyssWalkAsync(CancellationToken ct)
    {
        var player = FindPlayer();
        var root = GameObject.Find(RootName)?.transform;
        if (player == null || root == null) { Debug.LogWarning("[RouteProbe] 준비 안 됨"); return; }
        bool entered = false;
        void OnLog(string msg, string st, LogType lt) { if (msg.StartsWith("[BaseCampFx] 심연 진입")) entered = true; }
        Application.logMessageReceived += OnLog;
        var inputField = typeof(PlayerController).GetField("_inputActions", Inst);
        var moveField  = typeof(PlayerController).GetField("_moveDirection", Inst);
        object savedInput = inputField?.GetValue(player);
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "basecamp_route", $"{DateTime.Now:MMdd_HHmmss}_abyss");
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder();
        var pts = new (string label, string[] names)[]
        {
            ("성문 앞", new[] { "Markers/GateFront" }), ("성문 통로", new[] { "@93.5,9,0" }),
            ("하강 층계참", new[] { "Descent/DownLanding" }), ("포탈", new[] { "Markers/PortalCenter" }),
        };
        try
        {
            inputField?.SetValue(player, null);
            int k = 0;
            foreach (var pt in pts)
            {
                var target = Resolve(root, pt.names);
                if (target == null) continue;
                float t0 = Time.realtimeSinceStartup;
                while (!entered && Time.realtimeSinceStartup - t0 < 12f)
                {
                    if (player == null) break;
                    Vector3 to = Flat(target.Value - player.transform.position);
                    if (to.magnitude <= ArriveDist && pt.label != "포탈") break;   // 포탈은 진입 트리거까지 계속 민다
                    moveField?.SetValue(player, to.normalized);
                    await UniTask.Yield(PlayerLoopTiming.FixedUpdate, ct);
                }
                if (entered || player == null) break;
                moveField?.SetValue(player, Vector3.zero);
                float lum = await CaptureLumAsync(Path.Combine(dir, $"{k++}_{Clean(pt.label)}.png"), ct);
                sb.AppendLine($"  {pt.label} 도착 · 높이 {root.InverseTransformPoint(player.transform.position).y:0.0} · 화면 밝기 {lum:0.000}");
            }
            sb.AppendLine(entered ? "포탈 진입 — 「심연 진입」 로그 확인(잉크 와이프 → 던전)" : "⚠️ 포탈 진입 로그 없음(준비 미달이거나 트리거에 안 닿음)");
            File.WriteAllText(Path.Combine(dir, "report.txt"), sb.ToString(), new UTF8Encoding(true));
            Debug.Log($"[RouteProbe] 심연 걷기 — {sb.ToString().Replace("\r", "").Replace("\n", " · ")} — {dir}");
        }
        catch (OperationCanceledException) { Debug.Log("[RouteProbe] 취소"); }
        finally
        {
            Application.logMessageReceived -= OnLog;
            if (player != null)
            {
                moveField?.SetValue(player, Vector3.zero);
                inputField?.SetValue(player, savedInput);
            }
        }
    }

    /// <summary>
    /// 보스 대기방(커스텀 방 Road_Boss_ChN) — 스폰에서 Exit 쪽으로 실제 이동하며 1.5초마다 캡처(밝기), Exit 4 m 앞에서 한 장,
    /// 그대로 Exit로 들어가 0.5·1.5·3·5초 뒤 캡처(전환 연출). 캡처마다 그 순간 켜진 파티클 수와 가장 가까운 큰 파티클을 적는다.
    /// </summary>
    [MenuItem("RelicFairy/Boss/Waiting Room Walk Captures (Play)")] public static void WaitingRoomWalk() => Start(WaitingRoomWalkAsync);

    private static async UniTask WaitingRoomWalkAsync(CancellationToken ct)
    {
        var player = FindPlayer();
        var room = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                   .FirstOrDefault(t => t.name.StartsWith("Road_Boss_Ch") && t.gameObject.activeInHierarchy);
        var exit = room != null ? room.Find("Exit") : null;
        if (player == null || exit == null) { Debug.LogWarning($"[RouteProbe] 대기방 준비 안 됨 — player={player != null} room={(room != null ? room.name : "null")}"); return; }
        var inputField = typeof(PlayerController).GetField("_inputActions", Inst);
        var moveField  = typeof(PlayerController).GetField("_moveDirection", Inst);
        var invField   = typeof(PlayerController).GetField("debugInvincible", Inst);
        object savedInput = inputField?.GetValue(player);
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "boss_waitroom", $"{room.name}_{DateTime.Now:MMdd_HHmmss}");
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder();
        Vector3 exitPos = exit.position;   // 방이 전환되면 Exit가 파괴된다 — 위치만 들고 간다
        string roomName = room.name;
        sb.AppendLine($"대기방 {roomName} · 스폰 {player.transform.position} · Exit {exitPos} · 거리 {Flat(exitPos - player.transform.position).magnitude:0.0} m");
        int k = 0;
        async UniTask Shot(string tag)
        {
            var cam = Camera.main;
            var ps = UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Where(p => p != null && p.isPlaying && p.particleCount > 0).ToList();
            var near = cam != null ? ps.OrderBy(p => Vector3.Distance(cam.transform.position, p.transform.position)).FirstOrDefault() : null;
            float lum = await CaptureLumAsync(Path.Combine(dir, $"{k++:00}_{Clean(tag)}.png"), ct);
            sb.AppendLine($"  {tag,-14} 씬 {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} · 밝기 {lum:0.000} · 파티클 켜짐 {ps.Count}개 · 입자 {ps.Sum(p => p.particleCount)} · 카메라에 가장 가까운 {(near != null ? $"{near.name}({near.transform.root.name}) {Vector3.Distance(cam.transform.position, near.transform.position):0.0} m" : "-")}");
        }
        try
        {
            inputField?.SetValue(player, null);
            invField?.SetValue(player, true);
            await Shot("스폰");
            float t0 = Time.realtimeSinceStartup, lastShot = t0;
            while (Time.realtimeSinceStartup - t0 < 30f)
            {
                Vector3 to = Flat(exitPos - player.transform.position);
                if (to.magnitude < 4f) break;
                moveField?.SetValue(player, to.normalized);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                if (Time.realtimeSinceStartup - lastShot >= 1.5f) { moveField?.SetValue(player, Vector3.zero); await Shot($"걷기 {Time.realtimeSinceStartup - t0:0.0}s"); lastShot = Time.realtimeSinceStartup; }
            }
            moveField?.SetValue(player, Vector3.zero);
            await Shot("Exit 4m 앞");
            float e0 = Time.realtimeSinceStartup;
            foreach (float m in new[] { 0.5f, 1.0f, 1.5f, 2.5f, 3.5f, 5f, 7f })
            {
                while (Time.realtimeSinceStartup - e0 < m)
                {
                    if (player != null) moveField?.SetValue(player, Flat(exitPos - player.transform.position).normalized);
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                await Shot($"Exit 진입 {m:0.0}s");
            }
            File.WriteAllText(Path.Combine(dir, "report.txt"), sb.ToString(), new UTF8Encoding(true));
            Debug.Log($"[RouteProbe] 대기방 걷기 완료 — {dir}");
        }
        catch (OperationCanceledException) { Debug.Log("[RouteProbe] 취소"); }
        finally
        {
            if (player != null)
            {
                moveField?.SetValue(player, Vector3.zero);
                inputField?.SetValue(player, savedInput);
                invField?.SetValue(player, false);
            }
        }
    }

    /// <summary>지금 화면을 캡처하고 평균 밝기를 남긴다(던전 대기방 도착 뒤 잉크가 걷혔는지 등).</summary>
    [MenuItem(Root + "Capture Screen (Play)")] public static void CaptureScreen() => Start(CaptureScreenAsync);

    private static async UniTask CaptureScreenAsync(CancellationToken ct)
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "basecamp_route", $"{DateTime.Now:MMdd_HHmmss}_screen");
        Directory.CreateDirectory(dir);
        try
        {
            float lum = await CaptureLumAsync(Path.Combine(dir, "screen.png"), ct);
            Debug.Log($"[RouteProbe] 화면 캡처 — 씬 {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} · 밝기 {lum:0.000}{(lum < 0.03f ? " ⚠️거의 검정/덮임" : "")} · 입력 {(FindPlayer() is PlayerController p ? (p.enabled ? "플레이어 있음" : "플레이어 꺼짐") : "플레이어 없음")} — {dir}");
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>구역 이름판(ZoneSign) 실제 상태 — 카메라 참조·이름판 회전 대 카메라 회전·렌더러(자식 SubMesh 포함)·알파.</summary>
    [MenuItem(Root + "Dump Zone Labels (Play)")]
    public static void DumpZoneLabels()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[RouteProbe] 플레이 모드에서만."); return; }
        var cam = Camera.main;
        var gcc = GameCameraController.Instance;
        var sb = new StringBuilder();
        sb.AppendLine($"[RouteProbe] 이름판 상태 — Camera.main={(cam != null ? cam.name : "null")} rot {(cam != null ? cam.transform.eulerAngles.ToString("F0") : "-")} · GCC={(gcc != null ? gcc.name : "null")} rot {(gcc != null ? gcc.transform.eulerAngles.ToString("F0") : "-")} · LabelsHidden={ZoneSign.LabelsHidden}");
        var t = typeof(ZoneSign);
        foreach (var z in UnityEngine.Object.FindObjectsByType<ZoneSign>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var camT  = t.GetField("_camTransform", Inst)?.GetValue(z) as Transform;
            var label = t.GetField("_label", Inst)?.GetValue(z) as Transform;
            var mr    = t.GetField("_labelRenderer", Inst)?.GetValue(z) as MeshRenderer;
            var text  = t.GetField("_text", Inst)?.GetValue(z) as TMPro.TMP_Text;
            string kids = label != null
                ? string.Join(",", label.GetComponentsInChildren<Renderer>(true).Where(r => r.transform != label).Select(r => $"{r.name}:{(r.enabled ? "on" : "off")}"))
                : "-";
            sb.AppendLine($"  {z.name} enabled={z.enabled} active={z.gameObject.activeInHierarchy} cam={(camT == null ? "null" : camT.name)} · label rot {(label != null ? label.eulerAngles.ToString("F0") : "null")} · renderer {(mr == null ? "null" : mr.enabled ? "on" : "off")} · alpha {(text != null ? text.alpha.ToString("0.00") : "-")} · 자식 렌더러 [{kids}] · 이름판 수 {z.GetComponentsInChildren<TMPro.TextMeshPro>(true).Length}");
        }
        Debug.Log(sb.ToString());
    }

    // 층마다 서서 지형 점검(ArenaTerrainInspector 루트 모드) — 기준 바닥 = 그 층 발밑 Ground
    private static readonly (string label, string[] names)[] FloorPoints =
    {
        ("소환의 방 y0",  new[] { "Markers/FirstSpawn" }),
        ("회랑 y3",       new[] { "Ascent/Flight1_Top" }),
        ("회랑 y6",       new[] { "Ascent/Flight2_Top" }),
        ("광장 y9",       new[] { "Markers/ReturnSpawn" }),
        ("하강 층계참 y6", new[] { "Descent/DownLanding" }),
        ("포탈 y3",       new[] { "Markers/PortalCenter" }),
    };

    // ── Private Methods ────────────────────────────────────────
    private static void Start(Func<CancellationToken, UniTask> run)
    {
        if (!Application.isPlaying) { Debug.LogWarning("[RouteProbe] 플레이 모드에서만."); return; }
        s_cts?.Cancel(); s_cts?.Dispose();
        s_cts = new CancellationTokenSource();
        run(s_cts.Token).Forget();
    }

    private static async UniTask WalkAsync(string title, (string label, string[] names)[] route, CancellationToken ct)
    {
        var player = FindPlayer();
        var root = GameObject.Find(RootName)?.transform;
        if (player == null || root == null) { Debug.LogWarning($"[RouteProbe] 준비 안 됨 — player={player != null} root={root != null}"); return; }

        var points = route.Select(r => (r.label, t: Resolve(root, r.names))).ToList();
        var missing = points.Where(p => p.t == null).Select(p => p.label).ToList();
        var pts = points.Where(p => p.t != null).Select(p => (p.label, pos: p.t.Value)).ToList();
        if (pts.Count < 2) { Debug.LogWarning("[RouteProbe] 지점이 둘 미만"); return; }

        var inputField = typeof(PlayerController).GetField("_inputActions", Inst);
        var moveField  = typeof(PlayerController).GetField("_moveDirection", Inst);
        var invField   = typeof(PlayerController).GetField("debugInvincible", Inst);
        var lockFields = new[] { "_inputDisabledExternally", "_inputBlockedByUI", "_controlSuspended" }
                         .Select(n => typeof(PlayerController).GetField(n, Inst)).Where(f => f != null).ToArray();
        bool Locked() => lockFields.Any(f => (bool)f.GetValue(player));
        object savedInput = inputField?.GetValue(player);
        bool savedInv = invField != null && (bool)invField.GetValue(player);
        var legs = new List<Leg>();
        var blocks = new List<string>();
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "basecamp_route", $"{DateTime.Now:MMdd_HHmmss}_{(route == FirstRun ? "first" : route == RimRun ? "rim" : "return")}");
        Directory.CreateDirectory(dir);
        try
        {
            inputField?.SetValue(player, null);
            invField?.SetValue(player, true);
            Teleport(player, Ground(pts[0].pos) + Vector3.up * 0.1f);
            await UniTask.Delay(1200, ignoreTimeScale: true, cancellationToken: ct);   // 카메라 자리 잡기

            var cam = Camera.main;
            int wallMask = ~LayerMask.GetMask("Player", "Monster", "MonsterHit", "Ignore Raycast", "UI", "Ground");
            for (int i = 1; i < pts.Count; i++)
            {
                var leg = new Leg { From = pts[i - 1].label, To = pts[i].label };
                Vector3 target = pts[i].pos;
                Vector3 p0 = player.transform.position;
                leg.Straight = Flat(target - p0).magnitude;
                float limit = Mathf.Max(8f, leg.Straight / 2f + 5f);
                float t0 = Time.realtimeSinceStartup, stillSince = -1f, sinceBlockCheck = 0f;
                Vector3 prev = p0;
                while (Time.realtimeSinceStartup - t0 - leg.LockedSeconds < limit)
                {
                    Vector3 to = Flat(target - player.transform.position);
                    if (to.magnitude <= ArriveDist) { leg.Arrived = true; break; }
                    if (Locked())   // 연출·팝업이 조작을 막는 중 — 입력을 넣지 않고 기다리며 잰다
                    {
                        float lt = Time.realtimeSinceStartup;
                        moveField?.SetValue(player, Vector3.zero);
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                        leg.LockedSeconds += Time.realtimeSinceStartup - lt;
                        if (leg.LockedSeconds > MaxLockWait)   // 대사창 등이 입력을 기다리며 계속 막는 중 — 이 구간은 여기서 끊는다
                        {
                            blocks.Add($"조작 막힘 {MaxLockWait:0}초 넘음 {leg.From}→{leg.To} @ {Local(root, player.transform.position)} (대사·팝업 대기?)");
                            break;
                        }
                        stillSince = -1f;
                        prev = player.transform.position;
                        continue;
                    }
                    moveField?.SetValue(player, to.normalized);
                    await UniTask.Yield(PlayerLoopTiming.FixedUpdate, ct);

                    Vector3 p = player.transform.position;
                    float step = Flat(p - prev).magnitude;
                    leg.Walked += step; leg.Frames++;
                    if (!player.IsGrounded()) leg.AirFrames++;
                    leg.MinY = Mathf.Min(leg.MinY, p.y); leg.MaxY = Mathf.Max(leg.MaxY, p.y);
                    float speed = step / Mathf.Max(1e-4f, Time.fixedDeltaTime);
                    if (speed < StuckSpeed) { if (stillSince < 0f) stillSince = Time.realtimeSinceStartup; }
                    else stillSince = -1f;
                    if (stillSince > 0f && Time.realtimeSinceStartup - stillSince > StuckTime && leg.StuckAt < 0f)
                    {
                        leg.StuckAt = Time.realtimeSinceStartup - t0;
                        blocks.Add($"끼임 {leg.From}→{leg.To} @ {Local(root, p)}");
                        break;
                    }
                    // 1 m마다 카메라 가림
                    sinceBlockCheck += step;
                    if (cam != null && sinceBlockCheck >= 1f)
                    {
                        sinceBlockCheck = 0f;
                        Vector3 head = p + Vector3.up * HeadHeight;
                        if (Physics.Linecast(cam.transform.position, head, out var hit, wallMask, QueryTriggerInteraction.Ignore))
                            blocks.Add($"가림 {leg.From}→{leg.To} @ {Local(root, p)} — {hit.collider.name} (층 {LayerMask.LayerToName(hit.collider.gameObject.layer)})");
                    }
                    prev = p;
                }
                moveField?.SetValue(player, Vector3.zero);
                leg.Seconds = Time.realtimeSinceStartup - t0;
                if (cam != null)
                {
                    Vector3 d = cam.transform.position - player.transform.position;
                    leg.CamH = d.y; leg.CamR = Flat(d).magnitude;
                }
                legs.Add(leg);
                Debug.Log($"[RouteProbe] {leg.From}→{leg.To} {(leg.Arrived ? "도착" : "⚠️ 못 감")} {leg.Seconds:0.0}s · 직선 {leg.Straight:0.0} m · 걸음 {leg.Walked:0.0} m · 공중 {leg.AirFrames}/{leg.Frames}" +
                          $" · 카메라 높이 {leg.CamH:0.0} 거리 {leg.CamR:0.0}" +
                          (leg.LockedSeconds > 0.05f ? $" · 조작 막힘 {leg.LockedSeconds:0.0}s" : ""));
                await CaptureAsync(Path.Combine(dir, $"{i:00}_{Clean(leg.To)}.png"), ct);
                if (!leg.Arrived)   // 못 간 지점 — 다음 구간을 위해 그 지점으로 옮긴다
                    Teleport(player, Ground(target) + Vector3.up * 0.1f);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"베이스캠프 경로 걷기 — {title} · 루트 {root.name} {root.position}");
            if (missing.Count > 0) sb.AppendLine($"없어서 건너뛴 지점: {string.Join(", ", missing)}");
            sb.AppendLine($"조작 막힘 합계: {legs.Sum(l => l.LockedSeconds):0.0}초 (구간별은 아래 「막힘」)");
            sb.AppendLine($"합계: {legs.Sum(l => l.Seconds):0.0}초 · 걸음 {legs.Sum(l => l.Walked):0.0} m · 직선 합 {legs.Sum(l => l.Straight):0.0} m · 못 간 구간 {legs.Count(l => !l.Arrived)}");
            sb.AppendLine();
            foreach (var l in legs)
                sb.AppendLine($"{l.From,-8} → {l.To,-8} {(l.Arrived ? "도착" : "⚠️못감")} {l.Seconds,5:0.0}s  직선 {l.Straight,5:0.0}  걸음 {l.Walked,5:0.0}  공중 {l.AirFrames}/{l.Frames}  높이 {l.MinY - root.position.y:0.0}~{l.MaxY - root.position.y:0.0}  카메라 {l.CamH:0.0}/{l.CamR:0.0}" +
                              (l.StuckAt >= 0f ? $"  끼임 {l.StuckAt:0.0}s" : "") + (l.LockedSeconds > 0.05f ? $"  막힘 {l.LockedSeconds:0.0}s" : ""));
            sb.AppendLine();
            sb.AppendLine($"끼임·가림 {blocks.Count}건");
            foreach (var b in blocks.Take(200)) sb.AppendLine("  " + b);
            File.WriteAllText(Path.Combine(dir, "report.txt"), sb.ToString(), new UTF8Encoding(true));
            Debug.Log($"[RouteProbe] 완료 — {title} {legs.Sum(l => l.Seconds):0.0}초 · 못 간 구간 {legs.Count(l => !l.Arrived)} · 끼임·가림 {blocks.Count} — {dir}");
        }
        catch (OperationCanceledException) { Debug.Log("[RouteProbe] 취소"); }
        finally
        {
            moveField?.SetValue(player, Vector3.zero);
            inputField?.SetValue(player, savedInput);
            invField?.SetValue(player, savedInv);
        }
    }

    private static async UniTask FloorsAsync(CancellationToken ct)
    {
        var player = FindPlayer();
        var root = GameObject.Find(RootName)?.transform;
        if (player == null || root == null) { Debug.LogWarning($"[RouteProbe] 준비 안 됨 — player={player != null} root={root != null}"); return; }
        try
        {
            foreach (var f in FloorPoints)
            {
                var at = Resolve(root, f.names);
                if (at == null) { Debug.LogWarning($"[RouteProbe] 층 {f.label} 지점 없음"); continue; }
                Teleport(player, Ground(at.Value) + Vector3.up * 0.1f);
                await UniTask.Delay(1000, ignoreTimeScale: true, cancellationToken: ct);
                Debug.Log($"[RouteProbe] 층 점검 — {f.label}");
                await ArenaTerrainInspectorEditor.RunAsync(ct, root);
            }
            Debug.Log("[RouteProbe] 층 점검 완료");
        }
        catch (OperationCanceledException) { Debug.Log("[RouteProbe] 취소"); }
    }

    private static async UniTask ViewAsync(CancellationToken ct)
    {
        var player = FindPlayer();
        var root = GameObject.Find(RootName)?.transform;
        var spawn = root != null ? Resolve(root, new[] { "Markers/ReturnSpawn", "Plaza/SummonCircle" }) : null;   // Vector3?
        if (player == null || spawn == null) { Debug.LogWarning($"[RouteProbe] 준비 안 됨 — player={player != null} spawn={spawn != null}"); return; }
        try
        {
            // 이미 복귀 자리에 서 있으면(베이스캠프에 막 들어온 직후) 실제 스폰·카메라 상태를 그대로 잰다.
            // 아니면 순간이동 + 게임이 복귀 때 하는 카메라 스냅(마커 yaw)을 흉내 낸다 — 보고서에 「모의」로 적는다.
            bool real = Flat(player.transform.position - spawn.Value).magnitude <= 3f;
            if (!real)
            {
                Teleport(player, Ground(spawn.Value) + Vector3.up * 0.1f);
                var spawnT = ResolveTransform(root, new[] { "Markers/ReturnSpawn" });
                if (spawnT != null)
                {
                    player.RequestFacing(Quaternion.Euler(0f, spawnT.eulerAngles.y, 0f));
                    UnityEngine.Object.FindFirstObjectByType<GameCameraController>()?.RotateHeadingTo(spawnT.eulerAngles.y, 0f);
                }
            }
            await UniTask.Delay(2000, ignoreTimeScale: true, cancellationToken: ct);   // 카메라 자리 잡기
            var cam = Camera.main;
            var sb = new StringBuilder();
            sb.AppendLine($"첫 화면 — 광장 소환진({(real ? "실제 스폰 상태" : "모의: 순간이동 + 카메라 스냅")}) · 카메라 {Local(root, cam.transform.position)} · 시선 yaw {cam.transform.eulerAngles.y:0}° pitch {cam.transform.eulerAngles.x:0}° · 플레이어 yaw {player.transform.eulerAngles.y:0}°");
            int mustOk = 0, mustAll = 0;
            foreach (var v in ViewTargets)
            {
                var tp = Resolve(root, v.names);
                if (tp == null) { sb.AppendLine($"  {v.label}: (없음)"); continue; }
                Transform t = ResolveTransform(root, v.names);
                Vector3 p = tp.Value + Vector3.up * 1f;
                Vector3 vp = cam.WorldToViewportPoint(p);
                bool inView = vp.z > 0f && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f;
                string block = null;
                if (inView && Physics.Linecast(cam.transform.position, p, out var hit, ~LayerMask.GetMask("Player", "UI", "Ignore Raycast"), QueryTriggerInteraction.Ignore)
                    && (t == null || !hit.collider.transform.IsChildOf(t)) && Flat(hit.point - p).sqrMagnitude > 3f * 3f)   // 3 m 안은 스테이션 자기 몸(작업대·선돌)
                    block = hit.collider.name;
                bool ok = inView && block == null;
                if (v.must) { mustAll++; if (ok) mustOk++; }
                sb.AppendLine($"  {v.label}{(v.must ? "(필수)" : "")}: {(inView ? $"화면 안 ({vp.x:0.00},{vp.y:0.00})" : "화면 밖")}{(block != null ? $" · 가림 {block}" : "")} · 거리 {Flat(tp.Value - player.transform.position).magnitude:0.0} m · {Bearing(cam, tp.Value)}");
            }
            // 마커가 스테이션을 따라 옮겨졌는지 — 실물(컴포넌트) 위치와 비교
            void Real(string label, Component c, string marker)
            {
                if (c == null) { sb.AppendLine($"  실물 {label}: (없음)"); return; }
                var m = Resolve(root, new[] { marker });
                float gap = m != null ? Flat(m.Value - c.transform.position).magnitude : -1f;
                Vector3 v2 = cam.WorldToViewportPoint(c.transform.position + Vector3.up * 1f);
                bool iv = v2.z > 0f && v2.x > 0f && v2.x < 1f && v2.y > 0f && v2.y < 1f;
                sb.AppendLine($"  실물 {label}: {(iv ? $"화면 안 ({v2.x:0.00},{v2.y:0.00})" : "화면 밖")} · {Bearing(cam, c.transform.position)} · 마커와 {(gap < 0f ? "마커 없음" : $"{gap:0.0} m{(gap > 3f ? " ⚠️마커가 안 따라옴" : "")}")}");
            }
            Real("장비 공방", UnityEngine.Object.FindFirstObjectByType<WeaponForgeAltar>(), "Markers/ForgeSpot");
            Real("파츠 공방", UnityEngine.Object.FindFirstObjectByType<StartPartStation>(), "Markers/PartsSpot");
            sb.AppendLine($"필수 스테이션 보임 {mustOk}/{mustAll}");
            var fxDir = BaseCampFxDirector.Instance;
            if (fxDir != null)   // 처음 판엔 멀린의 공간 생성 뒤 — 공방이 다 서 있어야 상호작용할 수 있다
            {
                var st = typeof(BaseCampFxDirector).GetField("conjureStations", Inst)?.GetValue(fxDir) as GameObject[];
                sb.AppendLine($"멀린이 만드는 스테이션 활성 {st?.Count(g => g != null && g.activeInHierarchy) ?? 0}/{st?.Length ?? 0} · 생성 연출 남음 {fxDir.ConjurePending}");
            }
            foreach (var line in RuneLines(cam)) sb.AppendLine(line);
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "basecamp_route", $"{DateTime.Now:MMdd_HHmmss}_view");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "report.txt"), sb.ToString(), new UTF8Encoding(true));
            await CaptureAsync(Path.Combine(dir, "view.png"), ct);
            Debug.Log($"[RouteProbe] 첫 화면 — 필수 {mustOk}/{mustAll} — {dir}");
        }
        catch (OperationCanceledException) { Debug.Log("[RouteProbe] 취소"); }
    }

    /// <summary>광장 가운데에서 15°마다 바깥으로 걸어 본다 — 어디서 멈추는지(연석·결계·스테이션), 공중 프레임, 떨어짐.</summary>
    private static async UniTask EdgePushAsync(CancellationToken ct)
    {
        var player = FindPlayer();
        var root = GameObject.Find(RootName)?.transform;
        if (player == null || root == null) { Debug.LogWarning($"[RouteProbe] 준비 안 됨 — player={player != null} root={root != null}"); return; }

        var inputField = typeof(PlayerController).GetField("_inputActions", Inst);
        var moveField  = typeof(PlayerController).GetField("_moveDirection", Inst);
        var invField   = typeof(PlayerController).GetField("debugInvincible", Inst);
        object savedInput = inputField?.GetValue(player);
        bool savedInv = invField != null && (bool)invField.GetValue(player);
        int wallMask = ~LayerMask.GetMask("Player", "Monster", "MonsterHit", "Ignore Raycast", "UI", "Ground");
        var sb = new StringBuilder();
        sb.AppendLine($"광장 가장자리 밀기 — 가운데 로컬 {PlazaCenterLocal} · r {EdgeStartR}→{EdgeGoalR} (루트 로컬) · 0°=+x(성문 쪽)");
        int falls = 0, open = 0;
        try
        {
            inputField?.SetValue(player, null);
            invField?.SetValue(player, true);
            for (int a = 0; a < 360; a += 15)
            {
                Vector3 dirL = new(Mathf.Cos(a * Mathf.Deg2Rad), 0f, Mathf.Sin(a * Mathf.Deg2Rad));
                Vector3 goal = root.TransformPoint(PlazaCenterLocal + dirL * EdgeGoalR);
                Teleport(player, Ground(root.TransformPoint(PlazaCenterLocal + dirL * EdgeStartR)) + Vector3.up * 0.1f);
                await UniTask.Delay(400, ignoreTimeScale: true, cancellationToken: ct);

                int air = 0, frames = 0;
                float maxR = 0f, minY = float.MaxValue, t0 = Time.realtimeSinceStartup, stillSince = -1f;
                bool fell = false;
                Vector3 prev = player.transform.position;
                while (Time.realtimeSinceStartup - t0 < 5f)
                {
                    Vector3 to = Flat(goal - player.transform.position);
                    if (to.magnitude < 0.5f) break;
                    moveField?.SetValue(player, to.normalized);
                    await UniTask.Yield(PlayerLoopTiming.FixedUpdate, ct);
                    Vector3 p = player.transform.position, l = root.InverseTransformPoint(p);
                    frames++;
                    if (!player.IsGrounded()) air++;
                    minY = Mathf.Min(minY, l.y);
                    maxR = Mathf.Max(maxR, Flat(l - PlazaCenterLocal).magnitude);
                    if (l.y < PlazaCenterLocal.y - 2f) { fell = true; break; }
                    if (Flat(p - prev).magnitude / Mathf.Max(1e-4f, Time.fixedDeltaTime) < StuckSpeed)
                    { if (stillSince < 0f) stillSince = Time.realtimeSinceStartup; else if (Time.realtimeSinceStartup - stillSince > 0.6f) break; }
                    else stillSince = -1f;
                    prev = p;
                }
                moveField?.SetValue(player, Vector3.zero);
                Vector3 dirW = Flat(goal - player.transform.position).normalized;
                string ahead = Physics.Raycast(player.transform.position + Vector3.up * 1f, dirW, out var hit, 1.5f, wallMask, QueryTriggerInteraction.Ignore)
                    ? $"{hit.collider.name} (층 {LayerMask.LayerToName(hit.collider.gameObject.layer)})" : "-";
                if (fell) falls++;
                if (!fell && maxR > EdgeGoalR - 1f) open++;
                sb.AppendLine($"{a,3}°  멈춘 반경 {maxR,5:0.0}  공중 {air}/{frames}  최저 높이 {minY - PlazaCenterLocal.y:+0.00;-0.00}  앞 {ahead}" +
                              (fell ? "  ⚠️떨어짐" : maxR > EdgeGoalR - 1f ? "  (끝까지 감 — 통로?)" : ""));
                if (fell) Teleport(player, Ground(root.TransformPoint(PlazaCenterLocal)) + Vector3.up * 0.1f);
            }
            sb.Insert(0, $"떨어짐 {falls} · 끝까지 감 {open}\n");
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "basecamp_route", $"{DateTime.Now:MMdd_HHmmss}_edge");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "report.txt"), sb.ToString(), new UTF8Encoding(true));
            Debug.Log($"[RouteProbe] 가장자리 밀기 완료 — 떨어짐 {falls} · 끝까지 감 {open} — {dir}");
        }
        catch (OperationCanceledException) { Debug.Log("[RouteProbe] 취소"); }
        finally
        {
            moveField?.SetValue(player, Vector3.zero);
            inputField?.SetValue(player, savedInput);
            invField?.SetValue(player, savedInv);
        }
    }

    /// <summary>무형검 받기 연출만 틀어 잰다 — 입력 막힘(기준 ≤ 1.5초)·카메라가 원래 자리로 돌아오는지. 무기 지급·세이브 기록은 하지 않는다.</summary>
    private static async UniTask SwordFxAsync(CancellationToken ct)
    {
        var player = FindPlayer();
        var root = GameObject.Find(RootName)?.transform;
        var fx = BaseCampFxDirector.Instance;
        var spot = root != null ? Resolve(root, new[] { "Markers/SwordSpot" }) : null;
        if (player == null || fx == null || spot == null) { Debug.LogWarning($"[RouteProbe] 준비 안 됨 — player={player != null} fx={fx != null} spot={spot != null}"); return; }

        var lockFields = new[] { "_inputDisabledExternally", "_inputBlockedByUI", "_controlSuspended" }
                         .Select(n => typeof(PlayerController).GetField(n, Inst)).Where(f => f != null).ToArray();
        var altar = UnityEngine.Object.FindFirstObjectByType<WorldSwordAwakening>();
        Transform pedestal = altar != null ? altar.transform : root.Find("Markers/SwordSpot");
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "basecamp_route", $"{DateTime.Now:MMdd_HHmmss}_sword");
        Directory.CreateDirectory(dir);
        try
        {
            Teleport(player, Ground(spot.Value) + Vector3.up * 0.1f);
            await UniTask.Delay(1500, ignoreTimeScale: true, cancellationToken: ct);
            var cam = Camera.main;
            Vector3 before = cam.transform.position - player.transform.position;
            float yawBefore = cam.transform.eulerAngles.y;
            await CaptureAsync(Path.Combine(dir, "0_before.png"), ct);

            float t0 = Time.realtimeSinceStartup, locked = 0f;
            bool shot1 = false, shot2 = false;
            var task = fx.PlaySwordReceiveAsync(pedestal, player, ct).Preserve();
            while (task.Status == UniTaskStatus.Pending && Time.realtimeSinceStartup - t0 < 10f)
            {
                float f0 = Time.realtimeSinceStartup;
                bool isLocked = lockFields.Any(f => (bool)f.GetValue(player));
                float el = f0 - t0;
                if (!shot1 && el >= 0.5f) { shot1 = true; await CaptureAsync(Path.Combine(dir, "1_0.5s.png"), ct); }
                else if (!shot2 && el >= 1.1f) { shot2 = true; await CaptureAsync(Path.Combine(dir, "2_1.1s.png"), ct); }
                else await UniTask.Yield(PlayerLoopTiming.Update, ct);
                if (isLocked) locked += Time.realtimeSinceStartup - f0;
            }
            await task;
            float total = Time.realtimeSinceStartup - t0;
            await UniTask.Delay(1000, ignoreTimeScale: true, cancellationToken: ct);
            Vector3 after = cam.transform.position - player.transform.position;
            await CaptureAsync(Path.Combine(dir, "3_after.png"), ct);

            string line = $"무형검 연출 — 연출 {total:0.00}초 · 입력 막힘 {locked:0.00}초(기준 ≤ 1.5) · 받침 {(altar != null ? "제단" : "마커(제단 없음 — 이미 받은 슬롯)")}\n" +
                          $"  카메라 전 높이 {before.y:0.00} 거리 {Flat(before).magnitude:0.00} yaw {yawBefore:0}° → 1초 뒤 높이 {after.y:0.00} 거리 {Flat(after).magnitude:0.00} yaw {cam.transform.eulerAngles.y:0}°";
            File.WriteAllText(Path.Combine(dir, "report.txt"), line, new UTF8Encoding(true));
            Debug.Log($"[RouteProbe] {line.Replace("\n", " ·")} — {dir}");
        }
        catch (OperationCanceledException) { Debug.Log("[RouteProbe] 취소"); }
    }

    /// <summary>처음 한 번 생성 연출을 기다렸다가 도중 화면(캡처 + 평균 밝기)·입력 막힘·켜진 스테이션 수를 잰다.</summary>
    private static async UniTask WatchConjureAsync(CancellationToken ct)
    {
        var fx = BaseCampFxDirector.Instance;
        var conjuringField = typeof(BaseCampFxDirector).GetField("_conjuring", Inst);
        if (fx == null || conjuringField == null || !fx.ConjurePending) { Debug.LogWarning($"[RouteProbe] 생성 연출 관찰 — 대기 중인 연출 없음 (fx={fx != null} pending={fx != null && fx.ConjurePending})"); return; }
        var stations = typeof(BaseCampFxDirector).GetField("conjureStations", Inst)?.GetValue(fx) as GameObject[];
        var lockFields = new[] { "_inputDisabledExternally", "_inputBlockedByUI", "_controlSuspended" }
                         .Select(n => typeof(PlayerController).GetField(n, Inst)).Where(f => f != null).ToArray();
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "basecamp_route", $"{DateTime.Now:MMdd_HHmmss}_conjure");
        Directory.CreateDirectory(dir);
        try
        {
            Debug.Log("[RouteProbe] 생성 연출 관찰 — 시작 기다림");
            float w0 = Time.realtimeSinceStartup;
            while (!(bool)conjuringField.GetValue(fx))
            {
                if (Time.realtimeSinceStartup - w0 > 120f || !fx.ConjurePending) { Debug.LogWarning("[RouteProbe] 생성 연출 관찰 — 120초 안에 시작 안 함"); return; }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            float t0 = Time.realtimeSinceStartup;
            var sb = new StringBuilder();
            sb.AppendLine($"멀린의 공간 생성 — 관찰 (시작까지 기다린 시간 {t0 - w0:0.0}초)");
            float[] marks = { 0.3f, 1.5f, 3f, 4.5f, 6f };
            int k = 0;
            while ((bool)conjuringField.GetValue(fx) && Time.realtimeSinceStartup - t0 < 20f)
            {
                float el = Time.realtimeSinceStartup - t0;
                if (k < marks.Length && el >= marks[k])
                {
                    var player = FindPlayer();
                    bool locked = player != null && lockFields.Any(f => (bool)f.GetValue(player));
                    int on = stations?.Count(g => g != null && g.activeInHierarchy) ?? 0;
                    float lum = await CaptureLumAsync(Path.Combine(dir, $"{k}_{marks[k]:0.0}s.png"), ct);
                    sb.AppendLine($"  {el,4:0.0}s  화면 밝기 {lum:0.000}{(lum < 0.03f ? " ⚠️거의 검정" : "")}  입력 막힘 {locked}  스테이션 {on}/{stations?.Length ?? 0}");
                    k++;
                    continue;
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            sb.AppendLine($"연출 길이 {Time.realtimeSinceStartup - t0:0.00}초");
            await UniTask.Delay(1500, ignoreTimeScale: true, cancellationToken: ct);
            float after = await CaptureLumAsync(Path.Combine(dir, "9_after.png"), ct);
            var cam = Camera.main;
            sb.AppendLine($"끝나고 1.5초 — 화면 밝기 {after:0.000} · 카메라 yaw {(cam != null ? cam.transform.eulerAngles.y : 0f):0}° · 스테이션 {stations?.Count(g => g != null && g.activeInHierarchy) ?? 0}/{stations?.Length ?? 0}");
            File.WriteAllText(Path.Combine(dir, "report.txt"), sb.ToString(), new UTF8Encoding(true));
            Debug.Log($"[RouteProbe] 생성 연출 관찰 끝 — {dir}");
        }
        catch (OperationCanceledException) { Debug.Log("[RouteProbe] 취소"); }
    }

    /// <summary>캡처해서 저장하고 평균 밝기(0~1, 16픽셀 간격 표본)를 돌려준다.</summary>
    private static async UniTask<float> CaptureLumAsync(string path, CancellationToken ct)
    {
        var runner = UnityEngine.Object.FindFirstObjectByType<Managers>();
        if (runner == null) return -1f;
        await UniTask.WaitForEndOfFrame(runner, ct);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        var px = tex.GetPixels32();
        double sum = 0; int n = 0;
        for (int i = 0; i < px.Length; i += 16) { sum += (0.2126 * px[i].r + 0.7152 * px[i].g + 0.0722 * px[i].b) / 255.0; n++; }
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.Destroy(tex);
        return n > 0 ? (float)(sum / n) : 0f;
    }

    /// <summary>켜져 있는 룬 연출 오브젝트(이름이 「~」로 시작하고 Rune이 들어간 것) 전부 — 화면 위치와 HUD 띠(y&lt;0.25) 겹침.</summary>
    private static IEnumerable<string> RuneLines(Camera cam)
    {
        var runes = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                    .Where(t => t.gameObject.activeInHierarchy && t.name.StartsWith("~") && t.name.Contains("Rune"))
                    .OrderBy(t => t.name).ToList();
        if (runes.Count == 0) { yield return "  룬 오브젝트 없음(~…Rune…)"; yield break; }
        foreach (var r in runes)
        {
            Vector3 v = cam.WorldToViewportPoint(r.position);
            bool inView = v.z > 0f && v.x > 0f && v.x < 1f && v.y > 0f && v.y < 1f;
            yield return $"  룬 {r.name} · {(inView ? $"화면 ({v.x:0.00},{v.y:0.00}){(v.y < 0.25f ? " ⚠️HUD 띠" : "")}" : "화면 밖")} · 거리 {Vector3.Distance(cam.transform.position, r.position):0.0} m";
        }
    }

    /// <summary>카메라 시선(수평)에서 대상까지 좌우 각도 — 「왼쪽 40°」/「오른쪽 40°」.</summary>
    private static string Bearing(Camera cam, Vector3 target)
    {
        float a = Vector3.SignedAngle(Flat(cam.transform.forward), Flat(target - cam.transform.position), Vector3.up);
        return Mathf.Abs(a) < 1f ? "정면" : $"{(a < 0f ? "왼쪽" : "오른쪽")} {Mathf.Abs(a):0}°";
    }

    private static PlayerController FindPlayer()
        => GameRunBootstrapper.Instance?.Run?.Player ?? UnityEngine.Object.FindFirstObjectByType<PlayerController>();

    /// <summary>이름이면 그 오브젝트 위치, 「@x,y,z」면 루트 로컬 좌표를 월드로. 없으면 null.</summary>
    private static Vector3? Resolve(Transform root, string[] names)
    {
        foreach (var n in names)
        {
            if (n.StartsWith("@"))
            {
                var v = n.Substring(1).Split(',').Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                return root.TransformPoint(new Vector3(v[0], v[1], v[2]));
            }
            var t = root.Find(n);
            if (t != null) return t.position;
        }
        return null;
    }

    private static Transform ResolveTransform(Transform root, string[] names)
    {
        foreach (var n in names)
            if (!n.StartsWith("@") && root.Find(n) is Transform t && t != null) return t;
        return null;
    }

    private static Vector3 Ground(Vector3 p)
        => Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 10f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore) ? hit.point : p;

    private static void Teleport(PlayerController player, Vector3 pos)
    {
        player.transform.position = pos;
        if (player.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.position = pos;
            if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }
    }

    private static async UniTask CaptureAsync(string path, CancellationToken ct)
    {
        var runner = UnityEngine.Object.FindFirstObjectByType<Managers>();
        if (runner == null) return;
        await UniTask.WaitForEndOfFrame(runner, ct);
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.Destroy(tex);
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    private static string Local(Transform root, Vector3 p)
    {
        Vector3 l = root.InverseTransformPoint(p);
        return $"({l.x:0.0}, {l.y:0.0}, {l.z:0.0})";
    }

    private static string Clean(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }
}
