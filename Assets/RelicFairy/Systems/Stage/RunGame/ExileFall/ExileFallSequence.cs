using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using RelicFairy.UI;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 리치의 추방 — 챕터를 가로지르는 추락(10-11 · 기획 「리치추방·챕터가로지르는추락_연출설계」 권장안).
/// 봉인기 리치의 첫 붕괴에서만 돈다(봉인 의식은 봉인기에만 열린다 — 해방기 · 엔딩 뒤 · 악몽 리치는 지금 흐름 그대로).
///   ① 추방 — 붕괴 컷신 +9.0초부터(<see cref="LichSealRitual"/>): 리치가 공중에 멈춰 플레이어를 가리키고, 발밑 코어가 무너져 떨어진다.
///   ② 추락 — 같은 씬 y −3000에 런타임으로 짓는 허공(<see cref="ExileFallStage"/>). 층 넷이 스쳐 가고 봉인진이 하나씩 깨진다.
///   ③ 착지 — 흰 섬광 → 와이프. 전환(같은 런 · 같은 빌드 · 깊이 그대로 Ch1)은 <see cref="GameRunBootstrapper.EnterExileFall"/>가 맡는다.
///   ④ Ch1 첫 방 — 엎드린 채 드러나 일어나고 멀린 한 줄(<see cref="BeginLandingPose"/> · <see cref="PlayLandingAsync"/> · <see cref="LandingLine"/>).
/// 전부 실시간(unscaled) · 추락은 1초 뒤 공격 · 대시 · 상호작용(F) · 스페이스로 건너뛴다(ESC는 아니다 — 추방 내내 일시정지도 막는다, <see cref="BlocksPause"/>).
/// 전환 계기 = 보스방 클리어: 추방이 true로 돌아오면 LichMonster가 RaiseDied → RoomWaveController(3초 뒤) NotifyBossRoomCleared
/// → GameRunBootstrapper.BossClearSequenceAsync → <see cref="GameRunBootstrapper.EnterExileFall"/>가 <see cref="Claim"/>한다.
/// 그 신호가 끝내 안 오면 착지 뒤 직접 부른다(어디서 깨져도 Ch1로 — 소프트락 금지). 추방이 true로 돌아온 뒤엔 리치(꺼진다)를 참조하지 않는다.
/// 대사는 CSV 키(<see cref="DialogueKey"/>)가 먼저, 없으면 아래 폴백 줄(차트 업로드 전에도 나온다).
/// </summary>
public static class ExileFallSequence
{
    // ── Constants ─────────────────────────────────────────────────
    public const string DialogueKey = "Seal_Exile";      // 0 리치(추방) · 1 멀린(Ch3 무렵) · 2 멀린(Ch1 무렵) · 3 멀린(착지)
    private const string ShadowKey      = "Seal_Collapse";   // 3 = 그림자 — 붕괴 컷신 끝에서 추락 중으로 옮겼다
    private const int    ShadowIndex    = 3;
    private const string ShadowFallback = "그 봉인이 버틸 줄 알았어?";
    private const string LandingIllust  = "Illust_God_Default";

    private static readonly string[] FallbackLines =
    {
        "네가 묶은 것들이다. 그 밑바닥에서 다시 마주하라.",
        "봉인이… 하나씩 끊어지고 있어.",
        "붙잡아! 바닥이야!",
        "…살아 있어? 이제 저들이 깨어 있어. 다시 올라가자.",
    };
    private static readonly DialogueSpeaker[] FallbackSpeakers =
        { DialogueSpeaker.Lich, DialogueSpeaker.Merlin, DialogueSpeaker.Merlin, DialogueSpeaker.Merlin };

    // ① 추방(아레나)
    private const string PointState    = "MagicBolt";   // 가리키는 손짓 — 「낫 끝으로 가리키기」 상태가 없어 시전 손짓의 끝 자세를 빌린다(f9 권장)
    private const float  PointFromClip = 1.0f;          // MagicBolt 클립 초 — 1.27초에 가장 높이 들었다가(LichSwingDriver.Beat 실측)
    private const float  PointHoldClip = 1.8f;          //   1.8초까지 내리민다 → 여기서 멈춰 두면 손을 플레이어 쪽으로 뻗은 「가리킴」
    private const float  PointFreezeTimeout = 1.5f;
    private const float  FaceSeconds   = 0.35f;
    private const float  PointShotMove = 0.8f;
    private const float  PointHold     = 1.2f;
    private const float  CoreWarn      = 0.9f;          // 코어가 금 가며 흔들리는 시간 → 무너진다
    private const float  CoreShotMove  = 0.6f;
    private const float  DropSeconds   = 1.1f;
    private const float  DropGravity   = 20f;           // 끝 속도 약 22 m/s → 추락 무대 24 m/s로 이어진다
    private const float  DropCamHold   = 0.35f;         // 떨어지기 시작하는 동안 카메라는 위에 남아 내려다본다
    private const float  CamFollowRate = 7f;
    private const float  DipSeconds    = 0.3f;          // 무너질 바닥이 없을 때(격자 없는 아레나 · 직접 실측) 짧게 가리고 옮긴다
    public  const float  InvincibleSeconds = 60f;

    // ③ 착지 · 전환
    private const float LandFlashSeconds = 0.3f;
    private const float LandFlashPeak    = 0.95f;
    private const float WipeSeconds      = 0.35f;
    private const float ClaimWaitSeconds = 6f;          // 착지 뒤 보스방 클리어 신호가 전환을 맡기를 기다리는 상한(클리어는 의식 끝 3초 뒤)
    private const float RoarVolume       = 0.9f;
    private static readonly Color          WipeColor = new(0.05f, 0.04f, 0.09f, 1f);   // 층계 회랑과 같은 잉크빛
    private static readonly AnimationCurve WipeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    // ④ Ch1 착지(엎드렸다 일어남)
    private const float LandHoldSeconds  = 0.5f;
    private const float PoseBlendSeconds = 0.25f;
    private const float LandingBlockSeconds = 30f;     // Ch1 착지 동안 일시정지 막기 상한
    private const float LandShotBack   = 3.2f;           // Ch1 착지 — 몸 가까이 비춘다(10-12 83 실측: 멀리서는 엎드린 자세가 안 읽혔다)
    private const float LandShotHeight = 1.9f;
    private const float LandShotLook   = 0.35f;          //   엎드린 몸 높이를 본다

    // ── Static ────────────────────────────────────────────────────
    private static bool  s_active;            // 추방이 시작됐다 — 보스 클리어가 런 클리어 대신 Ch1로 간다
    private static bool  s_claimed;           // 부트스트래퍼가 전환을 맡았다
    private static bool  s_expectRoomClear;   // 보스방 클리어 신호가 올 길인가(직접 실측은 아니다)
    private static bool  s_landed;
    private static bool  s_landingPending;    // Ch1 부트가 한 번 읽는 「추방 착지」 표지
    private static float s_t0 = -1f;
    private static float s_landingBlockUntil; // Ch1 착지(스폰 → 일어남) 동안 일시정지를 막는 기한(실시간) — 끝나면 0. 깨져도 저절로 풀린다
    private static Vector3 s_returnAt;        // 전환 없이 끝날 때 몸을 돌려놓을 자리
    private static Animator s_lichAnim;       // 가리킴으로 멈춰 둔 리치 애니메이터 — 떨어질 때 속도를 되돌린다
    private static bool     s_landCam;        // Ch1 착지 가까운 카메라를 쥐고 있다
    private static UniTaskCompletionSource s_landedTcs;
    private static UniTask        s_build;
    private static ExileFallStage s_stage;
    private static ExileBody      s_body;
    private static ExilePose      s_landPose;
#if UNITY_EDITOR
    private static readonly List<string> s_trace = new();
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_active = s_claimed = s_expectRoomClear = s_landed = s_landingPending = false;
        s_t0       = -1f;
        s_landingBlockUntil = 0f;
        s_lichAnim = null;
        s_landCam  = false;
        s_landedTcs = null;
        s_build    = default;
        s_stage    = null;
        s_body     = null;
        s_landPose = null;
#if UNITY_EDITOR
        s_trace.Clear();
#endif
    }

    // ── Properties ────────────────────────────────────────────────
    /// <summary>추방이 시작돼 아직 Ch1로 넘어가지 않았다.</summary>
    public static bool   IsActive  => s_active;
    public static bool   HasLanded => s_landed;
    /// <summary>추방 시작 뒤 흐른 실시간(초). 시작 전이면 -1.</summary>
    public static float  Elapsed   => s_t0 < 0f ? -1f : Time.realtimeSinceStartup - s_t0;
    /// <summary>추락 무대에서 흐른 시간(초). 추락 전 · 무대 없음이면 -1.</summary>
    public static float  FallTime  => s_stage != null ? s_stage.FallTime : -1f;
    public static string LayerName => s_stage != null ? s_stage.LayerName : "-";
    /// <summary>Ch1 첫 방 착지 자세가 걸려 있다(일어나기 전).</summary>
    public static bool   IsLandingPoseActive => s_landPose != null;
    /// <summary>지금 착지 자세로 틀고 있는 클립 이름 · 클립 시각(실측). 없으면 「-」 · -1.</summary>
    public static string LandingClipName => s_landPose != null && s_landPose.Clip != null ? s_landPose.Clip.name : "-";
    public static float  LandingClipTime => s_landPose != null ? s_landPose.ClipTime : -1f;
    /// <summary>
    /// 일시정지(ESC 메뉴)를 열면 안 되는 동안 — 추방 · 추락 · 착지 와이프 · Ch1 로드 · 엎드렸다 일어날 때까지(<see cref="EscKeyListener"/>가 본다).
    /// 착지 쪽은 30초 기한이 있어 스폰이 깨져도 일시정지가 영영 막히지 않는다.
    /// </summary>
    public static bool   BlocksPause => s_active || s_landingPending || Time.realtimeSinceStartup < s_landingBlockUntil;
#if UNITY_EDITOR
    /// <summary>실측 기록(시각 · 층 · 대사) — 에디터 전용.</summary>
    public static IReadOnlyList<string> Trace => s_trace;
#endif

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>추방을 시작할 수 있는가 — 런이 돌고 있고(같은 런으로 Ch1에 떨어진다) 앱 부트가 있다(씬 전환). 아니면 예전 흐름(사라짐 · 런 클리어).</summary>
    public static bool CanExile(PlayerController player)
        => !s_active && player != null && AppBootstrapper.Instance != null
           && (GameRunBootstrapper.Instance?.Run?.IsRunning ?? false);

    /// <summary>
    /// ① 추방 — 붕괴 컷신 +9.0초에서 부른다. 리치가 가리키고(Seal_Exile 0) 코어가 무너지고 몸이 떨어진 뒤, 추락(②)을 띄우고 true.
    /// 이때부터 화면 · 몸 · HUD는 추락이 쥔다(부르는 쪽은 정리하지 않는다). 리치가 사라져 끊기면 몸 · 화면을 돌려주고 취소를 그대로 던진다.
    /// 장면 도중 예외는 삼키고 바로 추락으로 넘어간다(소프트락 금지). 추락조차 못 띄우면 false(예전 흐름으로).
    /// </summary>
    public static async UniTask<bool> ExileAsync(Transform lich, Animator lichAnim, ArenaTileGrid grid,
                                                 PlayerController player, CancellationToken ct)
    {
        if (!CanExile(player) || lich == null) return false;

        Vector3 p    = player.transform.position;
        Vector3 away = p - lich.position;
        away.y = 0f;
        away   = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.back;
        Begin(p, away, expectRoomClear: true, "붕괴 컷신");

        bool dropped = false;
        try
        {
            UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(true);   // 보스방 클리어가 HUD 모드를 바꿔도 추락 내내 숨는다
            player.SetInvincible(InvincibleSeconds);

            // +0.0 공중에 멈춰 낫 끝으로 플레이어를 가리킨다 — 「네가 묶은 것들이다. 그 밑바닥에서 다시 마주하라.」
            FaceAsync(lich, away, FaceSeconds, ct).Forget();
            PointAsync(lichAnim, ct).Forget();
            Say(0);
            Vector3 side     = Vector3.Cross(Vector3.up, away);
            Vector3 shoulder = lich.position - away * 3.2f + side * 1.6f + Vector3.up * 1.2f;   // 리치 어깨 너머로 플레이어를 내려다본다
            await LichCinematics.ShotAsync(shoulder, p + Vector3.up * 0.9f, PointShotMove, ct);
            await Wait(PointHold, ct);

            // +2.0 발밑 코어가 금 가며 무너진다 — 위에서 내려다본다(아레나 격자 붕괴를 컷신 예외로 코어까지)
            int fallen = CollapseCore(grid);
            Note($"코어 붕괴 {fallen}장");
            SaveLandingEarly();   // 이 순간부터 끄고 이어 하면 Ch1 대기방(해방기 리치방이 다시 열리지 않게)
            LichSfx.Play(LichSfxSlot.Collapse, p);
            LichCinematics.Chroma(0.25f, 0.4f);
            LichCinematics.ShotAsync(p - away * 3f + Vector3.up * 11f, p, CoreShotMove, ct).Forget();
            await Wait(CoreWarn, ct);

            // +2.9 떨어진다 — 조작 · 낙사 복구를 끄고 몸을 직접 내린다. 카메라는 위에서 내려다보다가 따라 내려간다.
            ReleasePoint();
            s_body = new ExileBody(player, FallClip);
            if (fallen > 0)
            {
                await DropAsync(s_body, away, ct);
                dropped = true;
            }
        }
        catch (OperationCanceledException)
        {
            ReleasePoint();
            Abandon();   // 리치가 사라졌다(씬 전환 등) — 몸 · 화면을 돌려주고 예전 정리로
            throw;
        }
        catch (Exception e)
        {
            ReleasePoint();
            Debug.LogError($"[Exile] 추방 장면 예외 — 바로 추락으로 넘어간다: {e}");
        }

        try
        {
            s_body ??= new ExileBody(player, FallClip);
            RunFallAsync(s_stage, s_body, dip: !dropped).Forget();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Exile] 추락을 띄우지 못했다 — 예전 흐름(런 클리어)으로: {e}");
            Abandon();
            return false;
        }
    }

    /// <summary>착지 섬광 · 와이프가 화면을 덮을 때까지 기다린다(추방이 없었으면 바로).</summary>
    public static UniTask WaitLandedAsync(CancellationToken ct)
        => s_landedTcs != null ? s_landedTcs.Task.AttachExternalCancellation(ct) : UniTask.CompletedTask;

    /// <summary>부트스트래퍼가 전환을 맡았다 — 착지 뒤 직접 부르기(대기 시간 초과)를 멈춘다.</summary>
    public static void Claim() => s_claimed = true;

    /// <summary>Ch1을 연다 — 다음 씬 부트가 <see cref="ConsumeLanding"/>로 한 번 읽는다. 추방은 여기서 끝난다.</summary>
    public static void MarkLanding()
    {
        s_landingPending = true;
        s_active = false;
        s_body   = null;
        s_stage  = null;   // 무대는 씬과 함께 사라진다
        Note("Ch1로 — 같은 런 · 같은 빌드");
    }

    /// <summary>추방으로 떨어져 온 챕터 진입인가(한 번 읽으면 꺼진다).</summary>
    public static bool ConsumeLanding()
    {
        bool v = s_landingPending;
        s_landingPending = false;
        if (v) s_landingBlockUntil = Time.realtimeSinceStartup + LandingBlockSeconds;   // 스폰 → 일어날 때까지 일시정지를 막는다
        return v;
    }

    /// <summary>
    /// 전환 없이 끝낸다(런이 없음 · 추락을 못 띄움 · 리치가 사라짐) — 무대를 지우고 화면 · 몸 · 카메라 · HUD를 돌려준다.
    /// 기다리는 쪽(<see cref="WaitLandedAsync"/>)도 풀린다.
    /// </summary>
    public static void Abandon()
    {
        var body  = s_body;
        var stage = s_stage;
        s_body   = null;
        s_stage  = null;
        s_active = false;

        if (stage != null)
        {
            stage.RestoreView();
            Object.Destroy(stage.gameObject);
        }
        body?.Restore(s_returnAt);
        ScreenFade.In(0.3f).Forget();
        LichCinematics.LetterboxOutAsync(CancellationToken.None).Forget();
        UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(false);
        if (body != null && body.Player != null)
            LichCinematics.ReturnToPlayerAsync(body.Player.transform, CancellationToken.None).Forget();
        s_landed = true;
        s_landedTcs?.TrySetResult();
        Note("전환 없이 끝 — 화면 · 몸을 돌려줬다");
    }

    /// <summary>
    /// ④ Ch1 첫 방 — 화면이 걷히기 전에 엎드린 자세를 걸고 조작을 멈춘다. 엎드림 클립(@GameRun)이 있으면 그 마지막 프레임,
    /// 없으면 큰 피격(Large_Hit) 첫 프레임에 멈춰 둔다. <see cref="PlayLandingAsync"/>가 일으켜 세운다.
    /// </summary>
    public static void BeginLandingPose(PlayerController player)
    {
        EndLandingPose();
        if (player == null) return;
        player.SetInputEnabled(false);
        player.SetControlSuspended(true);

        var boot = GameRunBootstrapper.Instance;
        var anim = player.Anim;
        var down = boot != null ? boot.ExileDownClip : null;
        s_landPose = down != null
            ? ExilePose.Play(anim, down, down.length, 0f)
            : ExilePose.Play(anim, ExilePose.FindControllerClip(anim, ExilePose.DefaultLandClip), 0f, 0f);
        Note($"착지 자세 — {(s_landPose != null ? s_landPose.Clip.name : "없음(컨트롤러 그대로)")}");
        TakeLandingCamera(player);
    }

    /// <summary>④ 일어난다 — 일어남 클립(없으면 걸어 둔 클립을 끝까지) 뒤 컨트롤러(Idle)로 섞어 돌아가고 조작을 돌려준다.</summary>
    public static async UniTask PlayLandingAsync(PlayerController player, CancellationToken ct)
    {
        try
        {
            await Wait(LandHoldSeconds, ct);
            var pose = s_landPose;
            if (pose != null)
            {
                var boot  = GameRunBootstrapper.Instance;
                var getUp = boot != null ? boot.ExileGetUpClip : null;
                if (getUp != null) pose.Swap(getUp, 0f, 1f);
                else               pose.SetSpeed(1f);
                float left = Mathf.Max(0f, pose.Length - pose.ClipTime - PoseBlendSeconds);
                Note($"일어난다 — {pose.Clip.name} {left:0.0}초");
                await Wait(left, ct);
                await pose.FadeAsync(0f, PoseBlendSeconds, ct);
            }
        }
        finally
        {
            EndLandingPose();
            s_landingBlockUntil = 0f;
            if (player != null)
            {
                player.SetControlSuspended(false);
                player.SetInputEnabled(true);
            }
            if (s_landCam)
            {
                s_landCam = false;
                if (player != null) LichCinematics.ReturnToPlayerAsync(player.transform, CancellationToken.None).Forget();   // 일어난 뒤 플레이 카메라로 물러난다
            }
        }
    }

    /// <summary>
    /// Ch1 착지 — 화면이 걷히기 전(덮인 채) 카메라를 몸 가까이 둔다. 플레이 카메라 진행 방향(출구 쪽) 뒤에서 엎드린 몸을 내려다본다.
    /// 일어난 뒤 <see cref="PlayLandingAsync"/>가 플레이 카메라로 되돌린다.
    /// </summary>
    private static void TakeLandingCamera(PlayerController player)
    {
        var cam = GameCameraController.Instance;
        if (cam == null || player == null) return;
        Vector3 dir = Quaternion.Euler(0f, cam.CurrentHeading, 0f) * Vector3.forward;
        Vector3 p   = player.transform.position;
        Vector3 pos = p - dir * LandShotBack + Vector3.up * LandShotHeight;
        cam.TakeManualControl();
        cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(p + Vector3.up * LandShotLook - pos, Vector3.up));
        s_landCam = true;
    }

    /// <summary>④ 착지 멀린 한 줄(Seal_Exile 3) — 대기방 대사창 맨 앞에 붙인다. CSV에 없으면 폴백 줄.</summary>
    public static DialogueLine LandingLine()
    {
        var lines = Managers.DialogueData?.GetLines(DialogueKey);
        var line  = lines != null && lines.Length > 3 ? lines[3] : null;
        bool csv  = line != null && !string.IsNullOrEmpty(line.text);
        if (!csv) line = new DialogueLine { speaker = DialogueSpeaker.Merlin, illustrationKey = LandingIllust, text = FallbackLines[3] };
        Note($"대사 {DialogueKey} 3 ({(csv ? "CSV" : "폴백")}) 「{line.text}」");
        return line;
    }

#if UNITY_EDITOR
    /// <summary>
    /// 실측 전용 — 지금 방에서 추락만 바로 튼다(붕괴 컷신 · 보스방 클리어 없이). 짧게 가리고 무대로 옮긴다.
    /// 착지 뒤 전환은 기다리지 않고 바로 <see cref="GameRunBootstrapper.EnterExileFall"/>가 맡는다(같은 런 · 깊이 그대로 Ch1).
    /// </summary>
    public static bool Editor_BeginDirect()
    {
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (!CanExile(player)) return false;

        Vector3 away = player.transform.forward;
        away.y = 0f;
        away   = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
        Begin(player.transform.position, away, expectRoomClear: false, "직접 실측");
        UIRootBootstrapper.Instance?.SetHudStartRoomSuppressed(true);
        UI_BossBark.Dismiss(0f);
        LichCinematics.LetterboxInAsync(CancellationToken.None).Forget();
        LichCinematics.TakeCamera();
        s_body = new ExileBody(player, FallClip);
        RunFallAsync(s_stage, s_body, dip: true).Forget();
        return true;
    }
#endif

    // ── Internal (무대가 부른다) ────────────────────────────────
    /// <summary>추방 대사 한 줄 — CSV 먼저(대기열을 비우고 바로), 없으면 폴백. 0은 리치(바크), 나머지는 멀린(내레이션).</summary>
    internal static void Say(int index)
    {
        var  type = index == 0 ? BossBarkType.Bark : BossBarkType.MerlinNarration;
        bool csv  = UI_BossBark.ShowDialogueLine(DialogueKey, index, type, interrupt: true);
        if (!csv) UI_BossBark.ShowLatest(FallbackLines[index], type, FallbackSpeakers[index]);
        Note($"대사 {DialogueKey} {index} ({(csv ? "CSV" : "폴백")}) 「{LineText(DialogueKey, index, FallbackLines[index])}」");
    }

    /// <summary>그림자 한 줄(옛 붕괴 컷신 끝 · Seal_Collapse 3) — 추락 첫머리로 옮겼다.</summary>
    internal static void SayShadow()
    {
        bool csv = UI_BossBark.ShowDialogueLine(ShadowKey, ShadowIndex, BossBarkType.MerlinNarration, interrupt: true);
        if (!csv) UI_BossBark.ShowLatest(ShadowFallback, BossBarkType.MerlinNarration, DialogueSpeaker.Shadow);
        Note($"대사 {ShadowKey} {ShadowIndex} ({(csv ? "CSV" : "폴백")}) 「{LineText(ShadowKey, ShadowIndex, ShadowFallback)}」");
    }

    /// <summary>층 보스 포효 — @GameRun 칸([0] 기사 · [1] 화룡 · [2] 숲). 빈 칸은 리치 결계 깨짐 소리로 대신한다.</summary>
    internal static void Roar(int index, Vector3 at)
    {
        if (index < 0) return;
        var boot = GameRunBootstrapper.Instance;
        var clip = boot != null ? boot.ExileRoar(index) : null;
        if (clip != null && Managers.Sound != null)
        {
            Managers.Sound.PlayEffect(clip, RoarVolume);
            Note($"포효 {index} — {clip.name}");
            return;
        }
        LichSfx.Play(LichSfxSlot.WardBreak, at);
        Note($"포효 {index} — 칸 비어 결계 깨짐 소리");
    }

    /// <summary>
    /// 추락 건너뛰기 키 — 공격(왼쪽 클릭) · 대시(오른쪽 클릭) · 상호작용(F) · 스페이스만. ESC · 일시정지 키는 세지 않는다(10-11 b0).
    /// 플레이어 입력 액션은 추락 동안 꺼져 있어 장치 키를 직접 본다(바인딩: PlayerInputActions Attack · Dodge · Jump, 상호작용 F).
    /// </summary>
    internal static bool SkipPressed()
        => Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)
        || Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.Space);

    /// <summary>실측 기록 한 줄(에디터) + 콘솔. 사건마다 한 번만 부른다(매 프레임 금지).</summary>
    internal static void Note(string message)
    {
        string line = $"+{Mathf.Max(0f, Elapsed):0.00}초 {message}";
#if UNITY_EDITOR
        s_trace.Add(line);
#endif
        Debug.Log($"[Exile] {line}");
    }

    // ── Private Methods ───────────────────────────────────────────
    private static AnimationClip FallClip
    {
        get
        {
            var boot = GameRunBootstrapper.Instance;
            return boot != null ? boot.ExileFallClip : null;
        }
    }

    private static Transform CameraTf()
    {
        var cam = GameCameraController.Instance;
        return cam != null ? cam.transform : null;
    }

    private static void Begin(Vector3 at, Vector3 away, bool expectRoomClear, string from)
    {
        s_active          = true;
        s_claimed         = false;
        s_landed          = false;
        s_expectRoomClear = expectRoomClear;
        s_returnAt        = at;
        s_landedTcs       = new UniTaskCompletionSource();
        s_t0              = Time.realtimeSinceStartup;
#if UNITY_EDITOR
        s_trace.Clear();
#endif
        Note($"추방 시작({from}) · 시기 {StoryProgress.Era} · 챕터 {GameRunBootstrapper.Instance?.Run?.CurrentChapter}");

        // 무대는 가리키는 동안 미리 짓는다(y −3000 — 화면에 안 든다) — 떨어지는 순간 읽기를 기다리지 않게.
        s_stage = ExileFallStage.Create(at, away);
        s_build = s_stage.BuildAsync(s_stage.destroyCancellationToken).Preserve();
    }

    /// <summary>
    /// 가리킴 — MagicBolt를 든 자세(클립 1.0초)부터 틀어 내리미는 끝(1.8초)에서 멈춰 둔다. 처음부터 끝까지 틀면 시전처럼 보인다(f9 권장).
    /// 리치 Update가 멈춘 사망 · 의식 상태라 박자 구동기(LichSwingDriver) 대신 애니메이터 속도를 직접 0으로. 떨어질 때 <see cref="ReleasePoint"/>.
    /// </summary>
    private static async UniTaskVoid PointAsync(Animator anim, CancellationToken ct)
    {
        if (anim == null) return;
        int hash = Animator.StringToHash(PointState);
        if (!anim.HasState(0, hash)) { Note($"리치 애니 상태 없음 「{PointState}」 — 가리킴 생략"); return; }
        s_lichAnim = anim;
        anim.speed = 1f;
        anim.CrossFadeInFixedTime(hash, 0.15f, 0, PointFromClip);
        try
        {
            float until = Time.realtimeSinceStartup + PointFreezeTimeout;
            while (Time.realtimeSinceStartup < until)
            {
                if (anim == null || s_lichAnim != anim) return;   // 이미 놓았다
                var info = anim.GetCurrentAnimatorStateInfo(0);
                if (info.shortNameHash == hash && info.normalizedTime * info.length >= PointHoldClip) break;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            if (anim != null && s_lichAnim == anim)
            {
                anim.speed = 0f;
                Note("리치 가리킴 — MagicBolt 끝 자세에서 멈춤");
            }
        }
        catch (OperationCanceledException) { ReleasePoint(); }
    }

    /// <summary>가리킴으로 멈춘 리치 애니메이터 속도를 되돌린다(떨어질 때 · 끊길 때). 리치가 꺼지기 전에 부른다.</summary>
    private static void ReleasePoint()
    {
        if (s_lichAnim != null) s_lichAnim.speed = 1f;
        s_lichAnim = null;
    }

    /// <summary>
    /// 코어가 무너지는 순간 「다음 = Ch1 대기방」 런 저장(10-11 b0 — 붕괴 ~ 추락 3초 사이에 끄면 해방기 리치방이 다시 열리던 빈틈).
    /// 의식이 끝났으니 전투 잠금도 이 저장으로 풀린다. 보스방 클리어(추락 3초)가 오면 처치 수 · 정수까지 담은 저장으로 다시 덮는다 —
    /// 그 사이에 끄면 리치 처치 1 · 무결 챕터 1 · 리치 정수 조각만 빠진다(리치방이 다시 열리는 것보다 작다).
    /// </summary>
    private static void SaveLandingEarly()
    {
        var run  = GameRunBootstrapper.Instance?.Run;
        var flow = RunFlowController.Active;
        if (run == null || flow == null) return;
        try
        {
            run.SaveAsExileLanding(flow.SaveChapterAdvance);
            Note("저장 — 다음 = Ch1 대기방(코어 붕괴 시점 · 보스방 클리어가 다시 덮는다)");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Exile] 이른 저장 예외(건너뜀 — 보스방 클리어 저장이 맡는다): {e.Message}");
        }
    }

    /// <summary>남은 링 전부(코어 포함)를 무너뜨린다 — 컷신 전용 예외(<see cref="ArenaTileGrid.CollapseRing"/> includeCore).</summary>
    private static int CollapseCore(ArenaTileGrid grid)
    {
        if (grid == null) return 0;
        int fallen = 0;
        for (int ring = grid.OuterRing; ring >= 0; ring--)
            fallen += grid.CollapseRing(ring, CoreWarn, ignoreCap: true, includeCore: true);
        return fallen;
    }

    /// <summary>아레나에서 떨어진다 — 가속 낙하. 카메라는 잠깐 위에 남아 내려다보다 몸을 따라 내려간다.</summary>
    private static async UniTask DropAsync(ExileBody body, Vector3 away, CancellationToken ct)
    {
        var     cam   = CameraTf();
        Vector3 start = body.Position;
        for (float t = 0f; t < DropSeconds; t += Time.unscaledDeltaTime)
        {
            Vector3 pos = start + Vector3.down * (0.5f * DropGravity * t * t);
            body.MoveTo(pos);
            body.Spin(ExileFallStage.SpinDegPerSec * Time.unscaledDeltaTime);
            if (cam != null)
            {
                if (t >= DropCamHold)
                    cam.position = Vector3.Lerp(cam.position, ExileFallStage.CamTarget(pos, away, 0f),
                                                1f - Mathf.Exp(-CamFollowRate * Time.unscaledDeltaTime));
                cam.rotation = Quaternion.LookRotation(ExileFallStage.LookPoint(pos) - cam.position, Vector3.up);
            }
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
    }

    /// <summary>② 추락 — 무대로 옮기고(바닥이 없었으면 짧게 가린 뒤) 층을 지나 착지까지. 예외는 삼키고 착지로 넘어간다.</summary>
    private static async UniTaskVoid RunFallAsync(ExileFallStage stage, ExileBody body, bool dip)
    {
        var ct = stage.destroyCancellationToken;
        try
        {
            if (dip) await ScreenFade.Out(DipSeconds, ct);
            await s_build;

            var     cam   = CameraTf();
            Vector3 delta = stage.Top - body.Position;
            body.MoveTo(stage.Top);
            if (cam != null) cam.position += delta;   // 몸과 같이 옮긴다 — 아레나 낙하에서 끊김 없이 이어진다
            stage.TakeView(cam);
            if (dip) ScreenFade.In(DipSeconds).Forget();
            Note("추락 시작 — 층 넷");
            await stage.PlayAsync(body, snapCamera: dip, ct);
        }
        catch (OperationCanceledException)
        {
            return;   // 씬이 바뀌었다 — 다음 씬 부트가 화면을 연다
        }
        catch (Exception e)
        {
            Debug.LogError($"[Exile] 추락 예외 — 착지로 넘어간다: {e}");
        }
        await LandAsync(stage, body);
    }

    /// <summary>③ 착지 — 흰 섬광(0.3초) → 와이프로 덮는다. 덮인 뒤 자세 · 화면 설정 · 레터박스 · 자막을 걷고 전환을 넘긴다.</summary>
    private static async UniTask LandAsync(ExileFallStage stage, ExileBody body)
    {
        try
        {
            LichCinematics.Flash(Color.white, LandFlashSeconds, LandFlashPeak);
            await UniTask.Delay(TimeSpan.FromSeconds(LandFlashSeconds * 0.5f), DelayType.UnscaledDeltaTime);
            await ScreenFade.CoverAsync(Vector2.down, WipeColor, WipeSeconds, WipeCurve);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Exile] 착지 와이프 예외 — 암전으로 덮는다: {e.Message}");
            ScreenFade.Out(0f).Forget();
        }
        finally
        {
            body?.ReleasePose();
            if (stage != null) stage.RestoreView();
            LichCinematics.LetterboxOutAsync(CancellationToken.None).Forget();
            UI_BossBark.Dismiss(0f);
            s_landed = true;
            Note("착지 — 화면을 덮었다");
            s_landedTcs?.TrySetResult();
        }
        await HandOffAsync();
    }

    /// <summary>
    /// 전환 넘기기 — 보스방 클리어(의식 끝 3초 뒤)가 이미 맡았으면 끝. 직접 실측이거나 신호가 끝내 안 오면 여기서 부른다(덮인 화면에 갇히지 않게).
    /// </summary>
    private static async UniTask HandOffAsync()
    {
        float until = Time.realtimeSinceStartup + (s_expectRoomClear ? ClaimWaitSeconds : 0f);
        while (!s_claimed && Time.realtimeSinceStartup < until)
            await UniTask.Yield(PlayerLoopTiming.Update);
        if (s_claimed) return;

        var boot = GameRunBootstrapper.Instance;
        Note(boot != null ? "보스방 클리어가 전환을 맡지 않았다 — 직접 Ch1로" : "부트스트래퍼 없음 — 화면을 돌려준다");
        if (boot != null) boot.EnterExileFall();
        else              Abandon();
    }

    private static async UniTaskVoid FaceAsync(Transform tf, Vector3 dir, float seconds, CancellationToken ct)
    {
        if (tf == null || dir.sqrMagnitude < 0.0001f) return;
        Quaternion from = tf.rotation;
        Quaternion to   = Quaternion.LookRotation(dir, Vector3.up);
        try
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                if (tf == null) return;
                tf.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            if (tf != null) tf.rotation = to;
        }
        catch (OperationCanceledException) { }
    }

    private static void EndLandingPose()
    {
        s_landPose?.Destroy();
        s_landPose = null;
    }

    private static string LineText(string key, int index, string fallback)
    {
        var lines = Managers.DialogueData?.GetLines(key);
        return lines != null && index < lines.Length && lines[index] != null ? lines[index].text : fallback;
    }

    private static UniTask Wait(float seconds, CancellationToken ct)
        => UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0f, seconds)), DelayType.UnscaledDeltaTime, cancellationToken: ct);
}

/// <summary>
/// 추방 동안의 플레이어 몸 — 입력 · 조작 정지 · 무적 · 키네마틱 · 낙사 복구 끔 · 추락 자세(<see cref="ExilePose"/>).
/// 전환(Ch1 로드)이면 몸이 씬과 함께 사라지므로 자세만 걷는다. 전환 없이 끝날 때만 <see cref="Restore"/>로 되돌린다.
/// </summary>
internal sealed class ExileBody
{
    private const float PoseFadeIn = 0.15f;

    public readonly PlayerController Player;
    public readonly Transform        Tf;
    private readonly Rigidbody              _rb;
    private readonly FallRecoveryController _recovery;
    private readonly bool                   _wasKinematic;
    private ExilePose _pose;
    private float     _yaw;

    public Vector3 Position => Tf != null ? Tf.position : Vector3.zero;

    public ExileBody(PlayerController player, AnimationClip fallClip)
    {
        Player = player;
        Tf     = player.transform;
        _rb    = player.Rigid;
        _yaw   = Tf.eulerAngles.y;

        player.SetInputEnabled(false);
        player.SetControlSuspended(true);   // 상태머신 · 입력 틱 정지 — 공중 판정 · 착지 반응이 돌지 않게
        player.SetInvincible(ExileFallSequence.InvincibleSeconds);
        if (player.TryGetComponent(out _recovery)) _recovery.enabled = false;   // 아레나 낙사 깊이(28 m)에서 되돌려 놓지 않게
        if (_rb != null)
        {
            _wasKinematic = _rb.isKinematic;
            if (!_rb.isKinematic) _rb.linearVelocity = Vector3.zero;
            _rb.isKinematic = true;
        }

        var anim = player.Anim;
        var clip = fallClip != null ? fallClip : ExilePose.FindControllerClip(anim, ExilePose.DefaultFallClip);
        _pose = ExilePose.Play(anim, clip, 0f, 1f, 0f);
        if (_pose != null) _pose.FadeAsync(1f, PoseFadeIn, player.destroyCancellationToken).Forget();
        ExileFallSequence.Note($"추락 자세 — {(clip != null ? clip.name : "없음(컨트롤러 그대로)")}");
    }

    public void MoveTo(Vector3 pos)
    {
        if (Tf == null) return;
        Tf.position = pos;
        if (_rb != null) _rb.position = pos;
    }

    /// <summary>몸이 천천히 돈다(수직축).</summary>
    public void Spin(float degrees)
    {
        if (Tf == null) return;
        _yaw += degrees;
        var rot = Quaternion.Euler(0f, _yaw, 0f);
        Tf.rotation = rot;
        if (_rb != null) _rb.rotation = rot;
    }

    public void ReleasePose()
    {
        _pose?.Destroy();
        _pose = null;
    }

    /// <summary>전환 없이 끝날 때 — 몸을 <paramref name="at"/>에 돌려놓고 조작 · 물리 · 낙사 복구를 되살린다.</summary>
    public void Restore(Vector3 at)
    {
        ReleasePose();
        if (Player == null) return;
        MoveTo(at);
        if (_rb != null) _rb.isKinematic = _wasKinematic;
        if (_recovery != null) _recovery.enabled = true;
        Player.SetControlSuspended(false);
        Player.SetInputEnabled(true);
    }
}
