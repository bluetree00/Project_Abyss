using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 드래곤 보스 등장 대기 상태.
///
/// 흐름: Enter → 착지 지점 위 고공으로 이동 + 이동 잠금 + 플레이어 감지 대기
///       → 플레이어가 감지 범위(_detectionRange) 이내 진입 → OnEntranceRequested 발행
///       → BossRoomController 카메라 팬 완료 후 TriggerEntrance() 호출
///       → (BossRoomController 없을 시) AutoTriggerTimeout 후 카메라 팬 폴백 실행 후 TriggerEntrance()
///       → [1단계] 보스 룸 전체 와이드샷
///       → 멀리서 브레스를 뿜으며 비행 → 하강(Landing_Descend) 중 지붕 파괴 트리거 → 착지(Landing_Touchdown) → Idle 전환
///       → [2단계] 착지 즉시 보스 대각선 아래 클로즈업 컷 + "Dragon Boss" HUD
///       → [3단계] HUD 소멸 → 플레이어 카메라로 복귀 → ChaseState 전환 + OnCombatReady 발행
/// </summary>
public class DragonDormantState : IMonsterState
{
    private const string DescendStateName   = "Landing_Descend";
    private const string TouchdownStateName = "Landing_Touchdown";
    private const float  GroundedEpsilon     = 0.1f;

    private const float AutoTriggerTimeout = 1.5f;

    private const float FallbackPanDuration    = 2.0f;
    private const float FallbackHoldDuration   = 7.0f;
    private const float FallbackReturnDuration = 1.5f;
    private static readonly Vector3 FallbackCamOffset  = new Vector3(30f, 35f, -50f);
    private static readonly Vector3 FallbackLookOffset = new Vector3(0f, 25f, 0f);

    // UI_BossBark.Instance가 없는 씬(예: 단독 테스트 씬)에서도 동일한 페이싱을 유지하기 위한 폴백 대기 시간.
    // UI_BossBark의 BossIntro 표시 시간(fadeIn 0.15 + hold 4.0 + fadeOut 0.45)과 동일하게 맞춘다.
    private const float BossIntroFallbackDuration = 4.6f;

    private readonly float _detectionRange;

    private float _reFireTimer;
    private float _waitTimer;
    private bool  _detected;
    private bool  _triggered;
    private bool  _cameraTriggered;

    public bool IsActive { get; private set; } = true;

    public DragonDormantState(float detectionRange)
    {
        _detectionRange = detectionRange;
    }

    public void Enter(MonsterContext ctx)
    {
        _reFireTimer     = 0f;
        _waitTimer       = 0f;
        _detected        = false;
        _triggered       = false;
        _cameraTriggered = false;
        IsActive         = true;

        var dragon = ctx.Monster as DragonBossMonster;

        if (ctx.Agent != null) ctx.Agent.enabled = false;

        // 착지 지점(SpawnPosition)에서 멀리 떨어진 고공 위치에서 시작 — 이후 PlayEntranceSequenceAsync에서
        // 브레스를 뿜으며 착지 지점 위까지 날아온다.
        float   height      = dragon != null ? dragon.EntranceDescendHeight : 30f;
        Vector2 horizOffset = dragon != null ? dragon.EntranceFlyInOffset : Vector2.zero;

        Vector3 spawnPos = ctx.Runtime.SpawnPosition;
        ctx.Transform.position = spawnPos + new Vector3(horizOffset.x, height, horizOffset.y);

        Vector3 flightDir = new Vector3(-horizOffset.x, 0f, -horizOffset.y);
        if (flightDir.sqrMagnitude > 0.01f)
            ctx.Transform.rotation = Quaternion.LookRotation(flightDir.normalized, Vector3.up);

        if (ctx.Animator != null && dragon != null && !string.IsNullOrEmpty(dragon.AirChaseStateName)
            && ctx.Animator.HasState(0, Animator.StringToHash(dragon.AirChaseStateName)))
            ctx.Animator.CrossFade(dragon.AirChaseStateName, 0.2f);
    }

    /// <summary>
    /// BossRoomController → 카메라 팬 완료 후 호출.
    /// 하강/착지 연출, 지붕 파괴, 카메라 클로즈업, 보스 이름 UI를 순차 진행한다.
    /// </summary>
    public void TriggerEntrance(MonsterContext ctx)
    {
        if (_triggered) return;
        _triggered = true;

        PlayEntranceSequenceAsync(ctx).Forget();
    }

    public void Update(MonsterContext ctx)
    {
        if (!_detected)
        {
            // Enter()에서 +EntranceDescendHeight 만큼 고공으로 이동한 현재 위치(transform.position) 기준의
            // DistToPlayer가 아니라, 착지 지점(SpawnPosition) 기준으로 플레이어 접근을 감지한다.
            // 그래야 공중에 떠 있는 동안에도 플레이어가 보스방에 입장하면 감지될 수 있다.
            float distToSpawn = ctx.Runtime.PlayerTarget != null
                ? Vector3.Distance(ctx.Runtime.SpawnPosition, ctx.Runtime.PlayerTarget.position)
                : float.MaxValue;

            if (distToSpawn <= _detectionRange)
            {
                _detected    = true;
                _reFireTimer = 0f;
                (ctx.Monster as DragonBossMonster)?.FireEntranceRequest();
            }
            return;
        }

        if (_triggered) return;

        _waitTimer   += Time.deltaTime;
        _reFireTimer += Time.deltaTime;

        if (_reFireTimer >= 1f)
        {
            _reFireTimer = 0f;
            (ctx.Monster as DragonBossMonster)?.FireEntranceRequest();
        }

        if (!_cameraTriggered && _waitTimer >= AutoTriggerTimeout)
        {
            _cameraTriggered = true;
            DoFallbackCameraAsync(ctx).Forget();
        }
    }

    public void Exit(MonsterContext ctx)
    {
        IsActive = false;
    }

    // ── 등장 연출 시퀀스 ─────────────────────────────────────────

    private async UniTaskVoid PlayEntranceSequenceAsync(MonsterContext ctx)
    {
        var dragon = ctx.Monster as DragonBossMonster;
        if (dragon == null) return;

        var ct = dragon.destroyCancellationToken;

        try
        {
            var cam = GameCameraController.Instance;

            // 고공 와이드샷(지붕이 살아있는 건물 전체 + 멀리서 날아오는 드래곤)은
            // BossRoomController의 1단계 팬(bossCloseUpOffset/bossCloseUpLookOffset)이 이미 잡아준 상태 —
            // 여기서 추가로 전환하지 않고 그대로 유지한다.

            // 브레스를 뿜으며 착지 지점 위까지 비행 → 도착 시 지붕 파괴
            await FlyInWithBreathAsync(ctx, dragon, ct);

            // 착지 → Idle 전환
            await DescendAndLandAsync(ctx, dragon, ct);

            // 2단계: 즉시 컷 — 보스 대각선 아래에서 올려다보는 클로즈업 + 보스 이름 HUD (Idle 전환과 동시)
            if (cam != null)
                await LerpCameraCloseUpAsync(cam, ctx.Transform, dragon.EntranceCameraOffset, 0f, ct);

            if (UI_BossBark.Instance != null)
                await UI_BossBark.ShowAndWaitAsync("Dragon Boss", BossBarkType.BossIntro);
            else
                await UniTask.Delay(TimeSpan.FromSeconds(BossIntroFallbackDuration), cancellationToken: ct);

            // 3단계: HUD 소멸 → 플레이어 카메라로 복귀
            if (cam != null)
                await cam.ReturnToPlayerAsync(ctx.Runtime.PlayerTarget, dragon.EntranceCameraMoveDuration, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ctx.Agent != null && !ctx.Agent.enabled)
        {
            ctx.Agent.enabled = true;
            ctx.Agent.Warp(ctx.Transform.position);
        }
        if (dragon.DragonBlackboard != null)
            dragon.DragonBlackboard.BodyState = BodyState.Grounded;

        dragon.FireCombatReady();
        ctx.Monster.ChangeState<ChaseState>();
    }

    private static async UniTask FlyInWithBreathAsync(MonsterContext ctx, DragonBossMonster dragon, CancellationToken ct)
    {
        Vector3 targetPos = ctx.Runtime.SpawnPosition + Vector3.up * dragon.EntranceDescendHeight;
        float   speed     = dragon.EntranceFlyInSpeed;

        GameObject breathVfx = dragon.SpawnEntranceBreathVfx();

        while (Vector3.Distance(ctx.Transform.position, targetPos) > 0.05f)
        {
            ct.ThrowIfCancellationRequested();
            ctx.Transform.position = Vector3.MoveTowards(ctx.Transform.position, targetPos, speed * Time.deltaTime);
            await UniTask.Yield(ct);
        }

        ctx.Transform.position = targetPos;

        if (breathVfx != null)
            UnityEngine.Object.Destroy(breathVfx);

        dragon.TriggerRoofDestruction();
    }

    private static async UniTask DescendAndLandAsync(MonsterContext ctx, DragonBossMonster dragon, CancellationToken ct)
    {
        float targetY = ctx.Runtime.SpawnPosition.y;
        float descentSpeed = dragon.EntranceDescendSpeed;

        PlayAnim(ctx, DescendStateName, 0.15f);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            Vector3 pos = ctx.Transform.position;
            pos.y = Mathf.MoveTowards(pos.y, targetY, descentSpeed * Time.deltaTime);
            ctx.Transform.position = pos;

            if (pos.y - targetY <= GroundedEpsilon)
            {
                pos.y = targetY;
                ctx.Transform.position = pos;
                break;
            }

            await UniTask.Yield(ct);
        }

        PlayAnim(ctx, TouchdownStateName, 0.1f);
        await WaitForAnimNearEndAsync(ctx, TouchdownStateName, ct);

        // 착지 순간 — 남아있는 지붕 타일 전부 파괴
        dragon.TriggerRoofCollapse();

        // 착지 즉시 Idle 전환 — 2단계 카메라 컷/HUD와 동시에 보여진다
        PlayAnim(ctx, ctx.Animation.idleStateName, ctx.Animation.crossFadeDuration);
    }

    private static async UniTask LerpCameraToAsync(GameCameraController cam, Vector3 toPos, Vector3 lookAt, float duration, CancellationToken ct)
    {
        Vector3    fromPos = cam.transform.position;
        Quaternion fromRot = cam.transform.rotation;
        Vector3    lookDir = lookAt - toPos;
        Quaternion toRot   = lookDir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(lookDir, Vector3.up) : fromRot;

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

    private static async UniTask LerpCameraCloseUpAsync(GameCameraController cam, Transform target, Vector3 offset, float duration, CancellationToken ct)
    {
        Vector3 toPos  = target.position + offset;
        Vector3 lookAt = target.position + Vector3.up * 1.5f;
        await LerpCameraToAsync(cam, toPos, lookAt, duration, ct);
    }

    private static void PlayAnim(MonsterContext ctx, string stateName, float fadeDuration)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        ctx.Animator.CrossFade(stateName, fadeDuration, 0, 0f);
    }

    private static async UniTask WaitForAnimNearEndAsync(MonsterContext ctx, string stateName, CancellationToken ct)
    {
        if (ctx.Animator == null) return;
        int hash = Animator.StringToHash(stateName);
        if (!ctx.Animator.HasState(0, hash)) return;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (!ctx.Animator.IsInTransition(0))
            {
                var info = ctx.Animator.GetCurrentAnimatorStateInfo(0);
                if (info.shortNameHash != hash || info.normalizedTime >= 0.9f) return;
            }
            await UniTask.Yield(ct);
        }
    }

    // ── 카메라 폴백 ──────────────────────────────────────────────

    private async UniTaskVoid DoFallbackCameraAsync(MonsterContext ctx)
    {
        var dragon = ctx.Monster as DragonBossMonster;
        var player = ctx.Runtime.CachedPlayer;
        var cam    = GameCameraController.Instance;

        if (cam == null || dragon == null)
        {
            TriggerEntrance(ctx);
            return;
        }

        player?.SetInputEnabled(false);

        var ct = dragon.destroyCancellationToken;
        try
        {
            await cam.PanToZoneAndReturnAsync(
                ctx.Runtime.SpawnPosition,
                FallbackPanDuration,
                FallbackHoldDuration,
                FallbackReturnDuration,
                ctx.Runtime.PlayerTarget,
                ct,
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
}
}
