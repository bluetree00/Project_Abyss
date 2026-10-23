using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 데스나이트 보스 등장 대기 상태.
///
/// 흐름: Enter → NavMesh 비활성 + OnEntranceRequested 발행
///       → BossRoomController 카메라 팬 완료 후 TriggerEntrance() 호출
///       → 보스 클로즈업 + 보스 이름 자막(Idle 재생) + 바람 이펙트
///       → 플레이어 카메라 복귀 + 입력 복구 + OnCombatReady 발행
/// </summary>
public class DKDormantState : IMonsterState
{
    private const float AutoTriggerTimeout        = 1.5f;
    private const float BossIntroFallbackDuration = 4.6f;

    private const float FallbackPanDuration    = 1.5f;
    private const float FallbackHoldDuration   = 2.0f;
    private const float FallbackReturnDuration = 1.0f;
    private static readonly Vector3 FallbackCamOffset  = new Vector3(0f, -1f, -7f);
    private static readonly Vector3 FallbackLookOffset = new Vector3(0f, 2f,  0f);

    // 입장 연출 — 프롭 파괴
    private const float AttackHitDelay    = 0.7f; // Attack1 hitTime (슬래시 사운드 타이밍)
    private const float StageGapDelay     = 0.4f; // 슬래시 → 이펙트+프롭 사이 간격
    private const float PropsFlyViewDelay = 1.5f; // 프롭 날아가는 모습 오버뷰 카메라로 보여주는 시간
    private const float RepeatPropsFlyViewDelay = 0.6f;   // 반복 관람 — 베기 · 소품만 짧게(10-14)

    private float _reFireTimer;
    private float _waitTimer;
    private bool  _triggered;
    private bool  _cameraTriggered;
    private BossRoomController _room;     // 씬에 있으면 폴백 비활성 · 등장 이름(EntranceTitle)을 읽는다

    public bool IsActive { get; private set; } = true;

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // IMonsterState
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public void Enter(MonsterContext ctx)
    {
        _reFireTimer          = 0f;
        _waitTimer            = 0f;
        _triggered            = false;
        _cameraTriggered      = false;
        IsActive              = true;
        _room                 = UnityEngine.Object.FindFirstObjectByType<BossRoomController>();

        if (ctx.Agent != null) ctx.Agent.enabled = false;

        (ctx.Monster as DeathKnightBossMonster)?.FireEntranceRequest();
    }

    public void Update(MonsterContext ctx)
    {
        if (_triggered) return;

        float dt = Time.deltaTime;
        _waitTimer   += dt;
        _reFireTimer += dt;

        if (_reFireTimer >= 1f)
        {
            _reFireTimer = 0f;
            (ctx.Monster as DeathKnightBossMonster)?.FireEntranceRequest();
        }

        // BossRoomController가 씬에 있으면 폴백 발동 안 함 — 트리거로만 시작
        if (!_cameraTriggered && _room == null && _waitTimer >= AutoTriggerTimeout)
        {
            _cameraTriggered = true;
            DoFallbackCameraAsync(ctx).Forget();
        }
    }

    public void Exit(MonsterContext ctx) { }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 등장 트리거
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    public void TriggerEntrance(MonsterContext ctx)
    {
        if (_triggered) return;
        _triggered = true;
        PlayEntranceSequenceAsync(ctx).Forget();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 등장 연출 시퀀스
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private async UniTaskVoid PlayEntranceSequenceAsync(MonsterContext ctx)
    {
        var dk = ctx.Monster as DeathKnightBossMonster;
        if (dk == null) return;

        var ct  = dk.destroyCancellationToken;
        var cam = GameCameraController.Instance;
        // 해방기도 묶임 없이 — 봉인은 리치에게 떨어질 때 이미 깨졌다(10-13 사용자 「봉인 상태에서 시작은 레거시」)
        // 방이 정한 이름(인트로)은 체력바에도 — 옛 이름이 「타락한 모르드레드」 위에 떴다(10-09 전주기 시뮬 · 10-13 칭호 통일)
        if (_room != null && !string.IsNullOrEmpty(_room.EntranceTitle)) dk.SetDisplayName(_room.EntranceTitle);
        bool cine  = BossStoryScenes.BeginEntrance(StoryProgress.DeathKnight, ct);   // 레터박스 · 전투 HUD 걷기 — 이야기 전투만(인트로는 그대로, 10-14)
        bool first = BossStoryScenes.IsFirstEntrance(StoryProgress.DeathKnight);

        try
        {
            // ① 오버뷰 카메라 상태: 스윙과 함께 검 등장 → hitTime 후 슬래시 VFX + 프롭 날리기
            dk.ShowSwordVisual();
            PlayAnim(ctx, "Attack1");
            await UniTask.Delay(TimeSpan.FromSeconds(AttackHitDelay), cancellationToken: ct);
            dk.PlayEntranceSlashSfx();
            await UniTask.Delay(TimeSpan.FromSeconds(StageGapDelay), cancellationToken: ct);
            dk.SpawnEntranceRadialVfx();
            SwapAndFlyProps(dk); // 물리는 백그라운드에서 계속 날아감

            // 오버뷰 카메라로 프롭 날아가는 모습 노출 후 클로즈업 전환
            await UniTask.Delay(TimeSpan.FromSeconds(first ? PropsFlyViewDelay : RepeatPropsFlyViewDelay), cancellationToken: ct);

            // ② 클로즈업으로 전환
            if (cam != null)
                await LerpCameraCloseUpAsync(cam, ctx.Transform,
                    dk.EntranceCameraOffset, dk.EntranceCameraLookOffset,
                    dk.EntranceCameraCloseUpDuration, ct);

            // 보스 이름 HUD 직전: Idle 애니메이션 재생 — 검은 든 채 궤적만 끈다(10-22 A4 「검은 들고 있게」 · 예전엔 비무장으로 지웠다)
            PlayIdleAnim(ctx);
            dk.EndSwordSwing();

            // 보스 이름 HUD + 바람 이펙트
            GameObject windVfx = dk.SpawnEntranceWindVfx();
            try
            {
                // 방이 정한 이름(인트로 「타락한 모르드레드」) > 인카운터 대사(방문 변형) > 「타락한 모르드레드」(10-13 칭호 통일)
                // + 이름 · 칭호 · 반복 관람은 짧게(10-14 — 인트로는 예전 그대로)
                string bark = _room != null ? _room.EntranceTitle : null;
                if (string.IsNullOrEmpty(bark))
                {
                    bark = "타락한 모르드레드";
                    var encounterLines = Managers.DialogueData?.GetBossEncounterLines("DeathKnight_Encounter");
                    if (encounterLines != null && encounterLines.Length > 0) bark = encounterLines[0].text;
                }
                await BossStoryScenes.IntroLineAsync(dk, StoryProgress.DeathKnight, bark, ct);
            }
            finally
            {
                if (windVfx != null) BossEffectPool.Release(windVfx);
            }

            // 해방기 — 클로즈업 동안 옛 봉인 자국을 검으로 베어 떨쳐내며 포효(10-13 재조우)
            if (StoryProgress.IsLiberated) { dk.ShowSwordVisual(); dk.BeginSwordSwing(); }
            await BossStoryScenes.ReturnAsync(dk, StoryProgress.DeathKnight, ct);
            if (StoryProgress.IsLiberated) dk.EndSwordSwing();

            // 플레이어 카메라로 복귀
            if (cam != null)
                await cam.ReturnToPlayerAsync(ctx.Runtime.PlayerTarget, 1.0f, ct);   // 블렌드 — 복귀 목표가 게임 카메라 자세로 고쳐져(10-14) 덜컥 넘어가지 않게 · 유리벽 너머라 약 18 m를 오니 0.6초는 첫 0.1초에 7 m가 휙(실측)
            if (cine) { BossStoryScenes.EndEntrance(StoryProgress.DeathKnight, completed: true); cine = false; }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (cine) BossStoryScenes.EndEntrance(StoryProgress.DeathKnight, completed: false);
        }

        // NavMesh 복구
        if (ctx.Agent != null && !ctx.Agent.enabled)
        {
            ctx.Agent.enabled = true;
            ctx.Agent.Warp(ctx.Transform.position);
        }

        // 입력 복구 + 전투 준비
        ctx.Runtime.CachedPlayer?.SetInputEnabled(true);
        IsActive = false;
        dk.FireCombatReady();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 카메라 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private static async UniTask LerpCameraCloseUpAsync(
        GameCameraController cam, Transform target,
        Vector3 offset, Vector3 lookOffset, float duration, CancellationToken ct)
    {
        Vector3    toPos   = target.position + offset;
        Vector3    lookAt  = target.position + lookOffset;
        Vector3    fromPos = cam.transform.position;
        Quaternion fromRot = cam.transform.rotation;
        Vector3    lookDir = lookAt - toPos;
        Quaternion toRot   = lookDir.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(lookDir, Vector3.up) : fromRot;

        if (duration <= 0f)
        {
            cam.transform.SetPositionAndRotation(toPos, toRot);
            return;
        }

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            ct.ThrowIfCancellationRequested();
            float k = Mathf.Clamp01(t / duration);
            cam.transform.SetPositionAndRotation(
                Vector3.Lerp(fromPos, toPos, k),
                Quaternion.Slerp(fromRot, toRot, k));
            await UniTask.Yield(ct);
        }
        cam.transform.SetPositionAndRotation(toPos, toRot);
    }

    // BossRoomController 없이 씬에 단독 배치 시 사용하는 폴백 카메라 흐름
    private async UniTaskVoid DoFallbackCameraAsync(MonsterContext ctx)
    {
        var dk     = ctx.Monster as DeathKnightBossMonster;
        var player = ctx.Runtime.CachedPlayer;
        var cam    = GameCameraController.Instance;

        if (cam == null || dk == null)
        {
            TriggerEntrance(ctx);
            return;
        }

        player?.SetInputEnabled(false);
        var ct = dk.destroyCancellationToken;
        try
        {
            await cam.PanToZoneAndReturnAsync(
                ctx.Runtime.SpawnPosition,
                FallbackPanDuration, FallbackHoldDuration, FallbackReturnDuration,
                ctx.Runtime.PlayerTarget, ct,
                FallbackCamOffset,
                onPanComplete: () => TriggerEntrance(ctx),
                customLookOffset: FallbackLookOffset);
        }
        catch (OperationCanceledException)
        {
            player?.SetInputEnabled(true);
            return;
        }

        player?.SetInputEnabled(true);
    }

    private static void PlayIdleAnim(MonsterContext ctx)
    {
        if (ctx.Animator == null) return;
        if (ctx.Animator.HasState(0, Animator.StringToHash("Idle")))
            ctx.Animator.CrossFade("Idle", 0.2f);
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        if (ctx.Animator.HasState(0, Animator.StringToHash(stateName)))
            ctx.Animator.CrossFade(stateName, 0.05f, 0, 0f);
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 씬 프롭 교체 + 물리 날려버리기
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private static void SwapAndFlyProps(DeathKnightBossMonster dk)
    {
        Vector3 bossPos = dk.transform.position;
        var props = UnityEngine.Object.FindObjectsByType<EntrancePropDestructible>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var prop in props)
            prop.SwapAndFly(bossPos);
    }
}
}
