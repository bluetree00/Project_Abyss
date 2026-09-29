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
///       → BossRoomController가 카메라를 넘기고(Ch4 아레나 = 공용 팬 생략) TriggerEntrance() 호출
///       → (BossRoomController 없을 시) AutoTriggerTimeout 후 카메라 팬 폴백 실행 후 TriggerEntrance()
///       → 등장 샷 세 개(연출·UX 시나리오 §10-4):
///          ① 제단 전경 — 리치 디졸브 인 · 안개 · 조명 전환
///          ② 리치 로우 앵글 — 책 펼침 · 이름 자막(끝까지 기다림) · 이 모드의 첫 조우면 대사창
///          ③ 플레이어 어깨 너머 → 「제단 구도」로 인계
///       → OnCombatReady(입력 복구) → ChaseState
/// </summary>
public class LichDormantState : IMonsterState
{
    private readonly float _detectionRange;  // 플레이어 감지 반경
    private float _reFireTimer;       // BossRoomController 소급 연결 대비 재발행 타이머
    private float _waitTimer;         // 감지 후 TriggerEntrance 미수신 시 자동 폴백 타이머
    private bool  _detected;          // 플레이어 감지됨 (OnEntranceRequested 발행 완료)
    private bool  _triggered;         // TriggerEntrance() 호출됨 (등장 연출 시작)
    private bool  _cameraTriggered;   // 폴백 카메라 팬 중복 방지

    // BossRoomController가 있으면 1초 내 refire로 즉시 처리됨.
    // 없는 환경(로컬 테스트 등)에서만 이 타임아웃 후 폴백 발동.
    private const float AutoTriggerTimeout = 1.5f;

    // ── 등장 샷 ─────────────────────────────────────────────
    private const float WideShotSeconds     = 1.6f;
    private const float WideBack            = 10f;    // 플레이어 뒤로
    private const float WideHeight          = 16f;
    private const float LowShotSeconds      = 1.2f;
    private const float LowShotDistance     = 4.5f;   // 리치 앞(플레이어 쪽) — 6.5 m는 리치가 화면에 작았다(09-18 실측)
    private const float LowShotHeight       = 0.6f;   // 바닥 위 — 올려다본다
    private const float LowShotLookLift     = 1.6f;
    private const float MinLowShotHold      = 1.0f;
    private const float ShoulderShotSeconds = 1.0f;
    private const float ShoulderBack        = 3.5f;
    private const float ShoulderHeight      = 2.6f;

    /// <summary>false가 되면 Exit()가 호출됐음을 의미 — LichMonster가 패턴 러너 차단 해제에 사용.</summary>
    public bool IsActive { get; private set; } = true;

    public LichDormantState(float detectionRange = 12f)
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

        (ctx.Monster as LichMonster)?.MovementController?.SetLocked(true);
    }

    /// <summary>
    /// BossRoomController(또는 폴백 팬)가 호출 — 등장 연출을 시작한다. 카메라는 이 상태가 끝까지 쥐고 돌려준다.
    /// </summary>
    public void TriggerEntrance(MonsterContext ctx)
    {
        if (_triggered) return;
        _triggered       = true;
        _cameraTriggered = true;   // 폴백 팬이 뒤늦게 끼어들지 않게

        var lich = ctx.Monster as LichMonster;
        lich?.MovementController?.RebaseToCurrentPosition();
        lich?.BeginEncounter();
        lich?.ShowPhase1Form();
        lich?.TriggerEntranceAtmosphere();
        ctx.Animator?.CrossFade("Appear", 0.1f);
        lich?.StartEncounterRecord();

        EntranceCinematicAsync(ctx).Forget();
    }

    public void Update(MonsterContext ctx)
    {
        // 등장 전까지 플레이어 방향으로 자연스럽게 회전
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

        if (_triggered) return;   // 등장 연출이 전투 진입까지 맡는다

        _waitTimer   += Time.deltaTime;
        _reFireTimer += Time.deltaTime;

        // BossRoomController가 연결된 경우 1초마다 재발행 (늦은 구독 대비)
        if (_reFireTimer >= 1f)
        {
            _reFireTimer = 0f;
            (ctx.Monster as LichMonster)?.FireEntranceRequest();
        }

        // BossRoomController 없는 환경 — 입력만 막고 등장 연출 발동(카메라는 연출이 잡는다)
        if (!_cameraTriggered && _waitTimer >= AutoTriggerTimeout)
        {
            _cameraTriggered = true;
            DoFallbackEntrance(ctx);
        }
    }

    public void Exit(MonsterContext ctx)
    {
        IsActive = false;
        (ctx.Monster as LichMonster)?.MovementController?.SetLocked(false);
    }

    // ── 등장 연출 ───────────────────────────────────────────────

    private async UniTaskVoid EntranceCinematicAsync(MonsterContext ctx)
    {
        var lich = ctx.Monster as LichMonster;
        if (lich == null)
        {
            ctx.Monster.ChangeState<ChaseState>();
            return;
        }

        var ct        = lich.ActivationToken;
        var player    = ctx.Runtime.PlayerTarget;
        var mc        = lich.MovementController;
        bool returned = false;

        LichCinematics.TakeCamera();
        try
        {
            Vector3 L     = lich.transform.position;
            Vector3 P     = player != null ? player.position : L - lich.transform.forward * 15f;
            float   floor = mc != null ? mc.FloorY : L.y;
            Vector3 dir   = P - L;
            dir.y = 0f;
            dir   = dir.sqrMagnitude > 0.01f ? dir.normalized : -lich.transform.forward;
            Vector3 floorL = new Vector3(L.x, floor, L.z);

            // ① 제단 전경 — 디졸브 인이 이 샷 동안 진행된다.
            // 등장 = 제단 바닥에서 어둠이 솟으며 몸이 맺힌다(전환 폭발과 다른 이펙트 — 09-20 사용자 지적).
            LichVfx.Play(LichVfxSlot.EntranceRise, floorL, Quaternion.identity);
            LichSfx.Play(LichSfxSlot.ZoneHum, L);
            await LichCinematics.ShotAsync(P + dir * WideBack + Vector3.up * WideHeight,
                                           Vector3.Lerp(P, floorL, 0.65f) + Vector3.up * 2f,
                                           WideShotSeconds, ct);

            // ② 리치 로우 앵글 — 이름 자막을 끝까지 보고, 첫 조우면 대사도 이 샷에서.
            lich.PulseBook(1.2f);
            LichSfx.Play(LichSfxSlot.Appear, L);
            var shot = LichCinematics.ShotAsync(floorL + dir * LowShotDistance + Vector3.up * LowShotHeight,
                                                L + Vector3.up * LowShotLookLift, LowShotSeconds, ct);
            var bark = UI_BossBark.ShowAndWaitAsync(IntroBark(lich), BossBarkType.BossIntro);
            await UniTask.WhenAll(shot, bark,
                                  UniTask.Delay(TimeSpan.FromSeconds(LowShotSeconds + MinLowShotHold),
                                                DelayType.UnscaledDeltaTime, cancellationToken: ct));

            var lines = StoryDialogue.TakeLichIntro(Managers.DialogueData, lich.LichBB?.IsNightmare ?? false);
            if (lines != null)
            {
                await Managers.UI.WaitUntilNoBlockingPopupAsync();
                var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_DialoguePopup>();
                if (popup != null) await popup.ShowAsync(lines);
            }
            if (ct.IsCancellationRequested || !IsActive) return;

            // ③ 어깨 너머 → 제단 구도로 인계
            await LichCinematics.ShotAsync(P + dir * ShoulderBack + Vector3.up * ShoulderHeight,
                                           L + Vector3.up * LowShotLookLift, ShoulderShotSeconds, ct);
            lich.PrepareArenaCamera();
            returned = true;
            await LichCinematics.ReturnToPlayerAsync(player, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            // 중간에 끊겨도 카메라가 수동 포즈에 얼어붙지 않게 돌려준다.
            if (!returned && player != null)
                LichCinematics.ReturnToPlayerAsync(player, CancellationToken.None).Forget();
        }

        if (!IsActive) return;
        lich.FireCombatReady();
        ctx.Monster.ChangeState<ChaseState>();
    }

    /// <summary>재도전 변형 대사 — 첫 조우/재도전마다 다른 대사("또 왔냐" 컨셉). 모드별 키, 없으면 보스 이름.</summary>
    private static string IntroBark(LichMonster lich)
    {
        bool nightmare = lich.LichBB?.IsNightmare ?? false;
        var  lines     = Managers.DialogueData?.GetBossEncounterLines(nightmare ? "Lich_Encounter_NM" : "Lich_Encounter");
        return lines != null && lines.Length > 0 ? lines[0].text : lich.BossName;
    }

    // ── 카메라 폴백 ──────────────────────────────────────────────

    /// <summary>
    /// BossRoomController가 없을 때의 폴백 — 입력을 막고 등장 연출을 바로 시작한다.
    /// 카메라 팬은 걸지 않는다: 등장 샷 세 개가 카메라를 직접 잡고 돌려주므로, 팬을 겹치면
    /// 팬의 홀드·복귀가 샷을 덮어 카메라가 두 곳을 오간다(09-18 실측).
    /// </summary>
    private void DoFallbackEntrance(MonsterContext ctx)
    {
        var lich   = ctx.Monster as LichMonster;
        var player = ctx.Runtime.CachedPlayer;

        if (lich != null && player != null)
        {
            player.SetInputEnabled(false);
            // 컨트롤러가 없으니 입력 복구도 여기서 — 등장 연출이 끝나 전투가 열릴 때.
            Action restore = null;
            restore = () =>
            {
                lich.OnCombatReady -= restore;
                player.SetInputEnabled(true);
            };
            lich.OnCombatReady += restore;
        }

        TriggerEntrance(ctx);
    }
}
}
