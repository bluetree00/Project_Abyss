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
/// 흐름: Enter(=BossTrigger로 활성화된 직후) → 착지 지점 위 고공으로 이동 + 이동 잠금
///       → 즉시 OnEntranceRequested 발행
///       → BossRoomController 카메라 팬 완료 후 TriggerEntrance() 호출
///       → (BossRoomController 없을 시) AutoTriggerTimeout 후 카메라 팬 폴백 실행 후 TriggerEntrance()
///       → [1단계] 보스 룸 전체 와이드샷
///       → 멀리서 브레스를 뿜으며 비행 → 진입로 바위 파괴 → 착지(Landing_Touchdown) → Idle 전환
///       → [2단계] 착지 즉시 보스 대각선 아래 클로즈업 컷 + "Dragon Boss" HUD
///       → [3단계] HUD 소멸 → 플레이어 카메라로 복귀 + 입력 복구
///       → [4단계] 플레이어가 보스 인식 범위(_detectionRange) 진입 → ChaseState 전환 + OnCombatReady 발행
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
    private bool  _triggered;
    private bool  _cameraTriggered;
    private bool  _landed;
    private bool  _combatTriggered;

    public bool IsActive { get; private set; } = true;

    public DragonDormantState(float detectionRange)
    {
        _detectionRange = detectionRange;
    }

    public void Enter(MonsterContext ctx)
    {
        _reFireTimer     = 0f;
        _waitTimer       = 0f;
        _triggered       = false;
        _cameraTriggered = false;
        _landed          = false;
        _combatTriggered = false;
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

        // BossTrigger 진입으로 이미 활성화된 상태 — 즉시 등장 연출 요청
        dragon?.FireEntranceRequest();
    }

    /// <summary>
    /// BossRoomController → 카메라 팬 완료 후 호출.
    /// 하강/착지 연출, 진입로 바위 파괴, 카메라 클로즈업, 보스 이름 UI를 순차 진행한다.
    /// </summary>
    public void TriggerEntrance(MonsterContext ctx)
    {
        if (_triggered) return;
        _triggered = true;

        PlayEntranceSequenceAsync(ctx).Forget();
    }

    public void Update(MonsterContext ctx)
    {
        if (_landed)
        {
            if (_combatTriggered) return;

            float distToPlayer = ctx.Runtime.PlayerTarget != null
                ? Vector3.Distance(ctx.Transform.position, ctx.Runtime.PlayerTarget.position)
                : float.MaxValue;

            if (distToPlayer <= _detectionRange)
            {
                _combatTriggered = true;
                var dragon = ctx.Monster as DragonBossMonster;
                dragon?.FireCombatReady();
                ctx.Monster.ChangeState<ChaseState>();
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

            // 브레스를 뿜으며 착지 지점 위까지 비행
            await FlyInWithBreathAsync(ctx, dragon, cam, ct);

            // FlyInWithBreathAsync 완료 시점에 카메라가 이미 하강 추적 위치에 도달 → 별도 아크 불필요

            // 착지 → Idle 전환
            await DescendAndLandAsync(ctx, dragon, cam, ct);

            // 2단계: 보스 대각선 아래에서 올려다보는 클로즈업으로 서서히 전환 + 보스 이름 HUD (Idle 전환과 동시)
            dragon.PlayNormalSfx();
            if (cam != null)
                await LerpCameraCloseUpAsync(cam, ctx.Transform, dragon.EntranceCameraOffset, dragon.EntranceCameraLookOffset, dragon.EntranceCameraCloseUpDuration, ct);

            GameObject windVfx = dragon.SpawnEntranceWindVfx();
            try
            {
                if (UI_BossBark.Instance != null)
                {
                    // 인카운터 대사(방문 변형) — 미로드 시 "Dragon Boss" 폴백
                    string bark = "Dragon Boss";
                    var encounterLines = Managers.DialogueData?.GetBossEncounterLines("Dragon_Encounter");
                    if (encounterLines != null && encounterLines.Length > 0) bark = encounterLines[0].text;
                    await UI_BossBark.ShowAndWaitAsync(bark, BossBarkType.BossIntro);
                }
                else
                    await UniTask.Delay(TimeSpan.FromSeconds(BossIntroFallbackDuration), cancellationToken: ct);
            }
            finally
            {
                if (windVfx != null) BossEffectPool.Release(windVfx);
            }

            // 3단계: HUD 소멸 → 플레이어 카메라로 복귀
            if (cam != null)
                await cam.ReturnToPlayerAsync(ctx.Runtime.PlayerTarget, dragon.EntranceCameraMoveDuration, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        DragonPatternFloorUtils.EnsureAgentOnNavMesh(ctx);
        if (dragon.DragonBlackboard != null)
            dragon.DragonBlackboard.BodyState = BodyState.Grounded;

        // 착지 완료 — 입력 복구. 전투는 플레이어가 보스 인식 범위에 들어와야 시작된다.
        ctx.Runtime.CachedPlayer?.SetInputEnabled(true);
        _landed = true;
    }

    private static async UniTask FlyInWithBreathAsync(MonsterContext ctx, DragonBossMonster dragon, GameCameraController cam, CancellationToken ct)
    {
        Vector3 targetPos = ctx.Runtime.SpawnPosition + Vector3.up * dragon.EntranceDescendHeight;
        float   speed     = dragon.EntranceFlyInSpeed;

        // VFX보다 사운드를 먼저 재생 — 체감상 브레스 타이밍이 더 빠르게 느껴지도록
        dragon.PlayEntranceBreathSfx();
        if (dragon.EntranceBreathSfxLeadTime > 0f)
            await UniTask.Delay(TimeSpan.FromSeconds(dragon.EntranceBreathSfxLeadTime), cancellationToken: ct);

        GameObject breathVfx = dragon.SpawnEntranceBreathVfx(ctx.Transform.forward);

        // 비행 방향 및 U자 아크 사전 계산
        Vector3 flightHoriz = targetPos - ctx.Transform.position;
        flightHoriz.y = 0f;
        Vector3 horizFlight  = flightHoriz.sqrMagnitude > 0.01f ? flightHoriz.normalized : Vector3.back;
        Vector3 rightDir     = Vector3.Cross(horizFlight, Vector3.up).normalized; // 비행방향 기준 우측
        Quaternion dragonEndRot = Quaternion.LookRotation(horizFlight, Vector3.up);

        // 카메라 시작: 플레이어 위치 기준 우측
        // 카메라 끝:   착지 위치 기준 하강 추적 카메라 위치
        float   totalDist = Vector3.Distance(ctx.Transform.position, targetPos);
        Vector3 playerPos = ctx.Runtime.PlayerTarget != null
            ? ctx.Runtime.PlayerTarget.position
            : ctx.Runtime.SpawnPosition;

        Vector3 camStart = playerPos
            + rightDir   * dragon.EntranceBreathCamRightShift
            + Vector3.up * dragon.EntranceBreathCamHeight;
        Vector3 camEnd   = targetPos + dragonEndRot * dragon.EntranceDescentCamOffset;
        Vector3 lookEnd  = targetPos + dragonEndRot * dragon.EntranceDescentCamLookOffset;
        // 컨트롤 포인트: 드래곤 로컬 오프셋 → 월드 변환 (음수 X = 좌측 스윙 → U자)
        Vector3 ctrlPos  = Vector3.Lerp(camStart, camEnd, 0.5f) + dragonEndRot * dragon.EntranceArcCtrlOffset;

        if (cam != null)
        {
            // 카메라를 시작 위치로 즉시 배치
            Vector3 initLook = ctx.Transform.position + Vector3.up * dragon.EntranceBreathCamLookElevation;
            Vector3 initDir  = initLook - camStart;
            cam.transform.position = camStart;
            if (initDir.sqrMagnitude > 0.01f)
                cam.transform.rotation = Quaternion.LookRotation(initDir, Vector3.up);
        }

        // 비행 루프 — 진행도에 맞춰 카메라를 U자 베지어 위에서 실시간 이동
        while (Vector3.Distance(ctx.Transform.position, targetPos) > 0.05f)
        {
            ct.ThrowIfCancellationRequested();
            ctx.Transform.position = Vector3.MoveTowards(ctx.Transform.position, targetPos, speed * Time.deltaTime);

            if (cam != null && totalDist > 0.01f)
            {
                float remaining = Vector3.Distance(ctx.Transform.position, targetPos);
                float raw = Mathf.Clamp01(1f - remaining / totalDist);
                float s   = raw * raw * (3f - 2f * raw); // smoothstep
                float it  = 1f - s;

                // 2차 베지어 위치
                Vector3 camPos = it*it*camStart + 2f*it*s*ctrlPos + s*s*camEnd;
                // 시선: 드래곤 현재 위치 추적 → 끝에서 하강 시선으로 전환
                Vector3 lookAt = Vector3.Lerp(
                    ctx.Transform.position + Vector3.up * dragon.EntranceBreathCamLookElevation,
                    lookEnd, s);

                cam.transform.position = camPos;
                Vector3 dir = lookAt - camPos;
                if (dir.sqrMagnitude > 0.01f)
                    cam.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }

            await UniTask.Yield(ct);
        }

        ctx.Transform.position = targetPos;

        // 착지 위치 도달 즉시 하강 자세로 전환하고 브레스 해제 — 정지 없이 바로 착지 시작
        PlayAnim(ctx, DescendStateName, 0.2f);

        if (breathVfx != null)
            BossEffectPool.Release(breathVfx);
        dragon.StopEntranceBreathSfx();
    }

    private static async UniTask DescendAndLandAsync(MonsterContext ctx, DragonBossMonster dragon, GameCameraController cam, CancellationToken ct)
    {
        float targetY     = DragonPatternFloorUtils.GetFloorY(ctx.Transform.position, ctx.Runtime.SpawnPosition.y);
        float fastSpeed   = dragon.EntranceDescendFastSpeed;
        float slowSpeed   = dragon.EntranceDescendSpeed;
        float totalHeight = ctx.Transform.position.y - targetY;

        // Landing_Touchdown 클립 길이 × 착지 속도 = 착지 애니 전환 높이
        // 클립이 재생되는 동안 slowSpeed로 이동하면 바닥에 정확히 도달하도록 역산
        float clipLen       = GetAnimClipLength(ctx, TouchdownStateName);
        float triggerHeight = clipLen > 0f ? clipLen * slowSpeed : 3f;

        bool touchdownTriggered = false;

        // [하강 추적] U-아크가 이미 추적 위치에 도달했으므로 블렌드 없이 즉시 추적 시작
        using var descentTrackCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (cam != null)
            TrackCameraAsync(cam, ctx.Transform, dragon.EntranceDescentCamOffset, dragon.EntranceDescentCamLookOffset, 0f, descentTrackCts.Token)
                .SuppressCancellationThrow().Forget();

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            Vector3 pos         = ctx.Transform.position;
            float   distToGround = pos.y - targetY;

            if (distToGround <= GroundedEpsilon)
            {
                pos.y = targetY;
                ctx.Transform.position = pos;
                break;
            }

            // 착지 애니 전환 시점 — 하강 추적 종료
            if (!touchdownTriggered && distToGround <= triggerHeight)
            {
                touchdownTriggered = true;
                descentTrackCts.Cancel();
                PlayAnim(ctx, TouchdownStateName, 0.1f);
            }

            // 속도 곡선: 트리거 구간 위에서 fastSpeed → slowSpeed로 sqrt 감속
            // triggerHeight 이하(Landing_Touchdown 재생 중)는 slowSpeed 고정
            float easeRange = totalHeight - triggerHeight;
            float t = easeRange > 0.01f
                ? Mathf.Clamp01((distToGround - triggerHeight) / easeRange)
                : 0f;
            float speed = Mathf.Lerp(slowSpeed, fastSpeed, Mathf.Sqrt(t));

            pos.y = Mathf.MoveTowards(pos.y, targetY, speed * Time.deltaTime);
            ctx.Transform.position = pos;

            await UniTask.Yield(ct);
        }

        // 착지 거리가 triggerHeight보다 짧은 경우 안전망
        if (!touchdownTriggered)
        {
            descentTrackCts.Cancel();
            PlayAnim(ctx, TouchdownStateName, 0.1f);
        }

        await WaitForAnimNearEndAsync(ctx, TouchdownStateName, ct);

        // 착지 즉시 Idle 전환 — 2단계 카메라 컷/HUD와 동시에 보여진다
        PlayAnim(ctx, ctx.Animation.idleStateName, ctx.Animation.crossFadeDuration);
    }

    /// <summary>
    /// 현재 카메라 위치에서 endPos까지 2차 Bezier 아크를 따라 이동한다.
    /// 컨트롤 포인트는 (start+end 중점 + arcCtrlOffset). 부드러운 U형 호를 만들려면 Y를 높인다.
    /// </summary>
    private static async UniTask BezierArcToCameraAsync(
        GameCameraController cam,
        Vector3 endPos, Vector3 endLookAt,
        Vector3 arcCtrlOffset, float duration, CancellationToken ct)
    {
        Vector3    startPos    = cam.transform.position;
        Vector3    startLookAt = startPos + cam.transform.forward * 20f;
        Vector3    ctrlPos     = Vector3.Lerp(startPos, endPos, 0.5f) + arcCtrlOffset;
        Vector3    ctrlLookAt  = Vector3.Lerp(startLookAt, endLookAt, 0.5f);
        float      dur         = Mathf.Max(0.01f, duration);

        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            ct.ThrowIfCancellationRequested();
            float s  = Mathf.Clamp01(t / dur);
            float sm = s * s * (3f - 2f * s); // smoothstep
            float it = 1f - sm;

            Vector3 pos    = it*it*startPos    + 2f*it*sm*ctrlPos    + sm*sm*endPos;
            Vector3 lookAt = it*it*startLookAt + 2f*it*sm*ctrlLookAt + sm*sm*endLookAt;
            Vector3 dir    = lookAt - pos;
            cam.transform.position = pos;
            if (dir.sqrMagnitude > 0.01f)
                cam.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            await UniTask.Yield(ct);
        }

        cam.transform.position = endPos;
        Vector3 finalDir = endLookAt - endPos;
        if (finalDir.sqrMagnitude > 0.01f)
            cam.transform.rotation = Quaternion.LookRotation(finalDir, Vector3.up);
    }

    /// <summary>보간 진입 후 ct가 취소될 때까지 드래곤 위치 + 로컬 오프셋을 매 프레임 추적한다.</summary>
    private static async UniTask TrackCameraAsync(GameCameraController cam, Transform target, Vector3 localOffset, Vector3 localLookOffset, float blendDuration, CancellationToken ct)
    {
        Vector3    fromPos = cam.transform.position;
        Quaternion fromRot = cam.transform.rotation;

        // 진입 보간
        for (float t = 0f; t < blendDuration; t += Time.deltaTime)
        {
            ct.ThrowIfCancellationRequested();
            float      k      = Mathf.Clamp01(t / blendDuration);
            Vector3    toPos   = target.position + target.rotation * localOffset;
            Vector3    lookAt  = target.position + target.rotation * localLookOffset;
            Vector3    lookDir = lookAt - toPos;
            Quaternion toRot   = lookDir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(lookDir, Vector3.up) : fromRot;
            cam.transform.position = Vector3.Lerp(fromPos, toPos, k);
            cam.transform.rotation = Quaternion.Slerp(fromRot, toRot, k);
            await UniTask.Yield(ct);
        }

        // 실시간 추적 (ct 취소 = 착지 애니 시작 시점까지)
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            Vector3 trackPos = target.position + target.rotation * localOffset;
            Vector3 lookAt   = target.position + target.rotation * localLookOffset;
            Vector3 lookDir  = lookAt - trackPos;
            cam.transform.position = trackPos;
            if (lookDir.sqrMagnitude > 0.01f)
                cam.transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
            await UniTask.Yield(ct);
        }
    }

    private static float GetAnimClipLength(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator?.runtimeAnimatorController == null) return 0f;
        foreach (var clip in ctx.Animator.runtimeAnimatorController.animationClips)
        {
            if (clip != null && clip.name == stateName)
                return clip.length;
        }
        return 0f;
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

    private static async UniTask LerpCameraCloseUpAsync(GameCameraController cam, Transform target, Vector3 offset, Vector3 lookOffset, float duration, CancellationToken ct)
    {
        Vector3 toPos  = target.position + offset;
        Vector3 lookAt = target.position + lookOffset;
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
