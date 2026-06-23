using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 보스 등장 대기 상태.
///
/// 흐름: Enter → 이동 잠금 + 플레이어 감지 대기
///       → 플레이어가 감지 범위(_detectionRange) 이내 진입 → OnEntranceRequested 발행
///       → BossRoomController 카메라 팬 완료 후 TriggerEntrance() 호출
///       → (BossRoomController 없을 시) AutoTriggerTimeout 후 카메라 팬 폴백 실행 후 TriggerEntrance()
///       → Appear 애니메이션 + 보스 이름 표시 + 조우 기록
///       → introDuration 경과 → ChaseState 전환
/// </summary>
public class LichDormantState : IMonsterState
{
    private readonly float _duration;        // Appear 후 전투 진입까지 대기 시간
    private readonly float _detectionRange;  // 플레이어 감지 반경
    private float _elapsed;
    private float _reFireTimer;       // BossRoomController 소급 연결 대비 재발행 타이머
    private float _waitTimer;         // 감지 후 TriggerEntrance 미수신 시 자동 폴백 타이머
    private bool  _detected;          // 플레이어 감지됨 (OnEntranceRequested 발행 완료)
    private bool  _triggered;         // TriggerEntrance() 호출됨 (Appear 시작)
    private bool  _cameraTriggered;   // 폴백 카메라 팬 중복 방지

    // BossRoomController가 있으면 1초 내 refire로 즉시 처리됨.
    // 없는 환경(로컬 테스트 등)에서만 이 타임아웃 후 폴백 카메라 발동.
    private const float AutoTriggerTimeout = 1.5f;

    // 카메라 폴백 파라미터 (BossRoomController와 동일한 기본값)
    private const float FallbackPanDuration    = 1.5f;
    private const float FallbackHoldDuration   = 2.0f;
    private const float FallbackReturnDuration = 1.0f;
    private static readonly Vector3 FallbackCamOffset = new Vector3(0f, 3f, -7f);

    /// <summary>false가 되면 Exit()가 호출됐음을 의미 — LichMonster가 패턴 러너 차단 해제에 사용.</summary>
    public bool IsActive { get; private set; } = true;

    public LichDormantState(float duration = 2.5f, float detectionRange = 12f)
    {
        _duration       = duration;
        _detectionRange = detectionRange;
    }

    public void Enter(MonsterContext ctx)
    {
        _elapsed         = 0f;
        _reFireTimer     = 0f;
        _waitTimer       = 0f;
        _detected        = false;
        _triggered       = false;
        _cameraTriggered = false;
        IsActive         = true;

        (ctx.Monster as LichMonster)?.MovementController?.SetLocked(true);
    }

    /// <summary>
    /// BossRoomController → 카메라 팬 완료 후 호출.
    /// Appear 애니메이션과 보스 이름 UI를 시작한다.
    /// </summary>
    public void TriggerEntrance(MonsterContext ctx)
    {
        if (_triggered) return;
        _triggered = true;
        _elapsed   = 0f; // 트리거 시점부터 _duration 카운트

        (ctx.Monster as LichMonster)?.ShowPhase1Form();
        (ctx.Monster as LichMonster)?.TriggerEntranceAtmosphere();
        ctx.Animator?.CrossFade("Appear", 0.1f);

        // 보스 재도전 변형 대사 — 첫 조우/재도전마다 다른 대사("또 왔냐" 컨셉). 미로드 시 "리치" 폴백.
        string introBark = "리치";
        var encounterLines = Managers.DialogueData?.GetVisitLines("Lich_Encounter");
        if (encounterLines != null && encounterLines.Length > 0)
            introBark = encounterLines[0].text;
        UI_BossBark.Show(introBark, BossBarkType.BossIntro);

        (ctx.Monster as LichMonster)?.StartEncounterRecord();
    }

    public void Update(MonsterContext ctx)
    {
        // Appear 전까지 플레이어 방향으로 자연스럽게 회전
        if (!_triggered && ctx.Runtime.PlayerTarget != null)
            (ctx.Monster as LichMonster)?.MovementController?.FaceTowards(
                ctx.Runtime.PlayerTarget.position, Time.deltaTime);

        if (!_detected)
        {
            // 보스 중심 원형 감지 — 전방향
            if (ctx.Runtime.DistToPlayer <= _detectionRange)
            {
                _detected    = true;
                _reFireTimer = 0f;
                (ctx.Monster as LichMonster)?.FireEntranceRequest();
            }
            return;
        }

        if (!_triggered)
        {
            _waitTimer   += Time.deltaTime;
            _reFireTimer += Time.deltaTime;

            // BossRoomController가 연결된 경우 1초마다 재발행 (늦은 구독 대비)
            if (_reFireTimer >= 1f)
            {
                _reFireTimer = 0f;
                (ctx.Monster as LichMonster)?.FireEntranceRequest();
            }

            // BossRoomController 없는 환경 — 카메라 팬 폴백 후 등장 연출 발동
            if (!_cameraTriggered && _waitTimer >= AutoTriggerTimeout)
            {
                _cameraTriggered = true;
                DoFallbackCameraAsync(ctx).Forget();
            }

            return;
        }

        _elapsed += Time.deltaTime;
        if (_elapsed >= _duration)
        {
            (ctx.Monster as LichMonster)?.FireCombatReady();
            ctx.Monster.ChangeState<ChaseState>();
        }
    }

    public void Exit(MonsterContext ctx)
    {
        IsActive = false;
        (ctx.Monster as LichMonster)?.MovementController?.SetLocked(false);
    }

    // ── 카메라 폴백 ──────────────────────────────────────────────

    /// <summary>
    /// BossRoomController가 없을 때 실행되는 카메라 팬 폴백.
    /// 플레이어 입력 차단 → 카메라 팬 → TriggerEntrance → 입력 복구.
    /// </summary>
    private async UniTaskVoid DoFallbackCameraAsync(MonsterContext ctx)
    {
        var lich   = ctx.Monster as LichMonster;
        var player = ctx.Runtime.CachedPlayer;
        var cam    = GameCameraController.Instance;

        if (cam == null || lich == null)
        {
            TriggerEntrance(ctx);
            return;
        }

        player?.SetInputEnabled(false);

        var ct = lich.destroyCancellationToken;
        try
        {
            await cam.PanToZoneAndReturnAsync(
                lich.transform.position,
                FallbackPanDuration,
                FallbackHoldDuration,
                FallbackReturnDuration,
                ctx.Runtime.PlayerTarget,
                ct,
                FallbackCamOffset,
                onPanComplete: () => TriggerEntrance(ctx));
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
