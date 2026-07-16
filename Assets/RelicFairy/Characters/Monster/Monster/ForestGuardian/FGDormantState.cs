using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// ForestGuardian 보스 등장 대기 상태.
///
/// 흐름: Enter() 즉시 시작
///   → 입력 차단 + NavMesh 비활성 + AttackReady 애니 + Voice SFX
///   → 플레이어 시점 → 대각선 클로즈업(이동1)
///   → 보스 이름 HUD 표시/소멸
///   → 플레이어 복귀(이동2) → NavMesh 복구 + 입력 복구 + OnCombatReady 발행
///
/// OnEntranceRequested 를 발행하지 않으므로 BossRoomController 카메라 팬이 개입하지 않는다.
/// </summary>
public class FGDormantState : IMonsterState
{
    private const float BossIntroFallbackDuration = 4.6f;

    public bool IsActive { get; private set; } = true;

    public void Enter(MonsterContext ctx)
    {
        IsActive = true;
        if (ctx.Agent != null) ctx.Agent.enabled = false;

        // BRC 없는 씬 대비 — BRC 가 있으면 이미 차단 상태이므로 중복 무해
        ctx.Runtime.CachedPlayer?.SetInputEnabled(false);

        PlayAttackReadyAnim(ctx);
        (ctx.Monster as ForestGuardianMonster)?.PlayEntranceVoice();

        RunSequenceAsync(ctx).Forget();
    }

    public void Update(MonsterContext ctx) { }

    public void Exit(MonsterContext ctx) { }

    // BRC 가 호출할 수 있으나, 이 보스는 Enter 에서 직접 시작하므로 무시
    public void TriggerEntrance(MonsterContext ctx) { }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 카메라 시퀀스
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private async UniTaskVoid RunSequenceAsync(MonsterContext ctx)
    {
        var fg  = ctx.Monster as ForestGuardianMonster;
        if (fg == null) { FinishSequence(ctx); return; }

        var cam = GameCameraController.Instance;
        var ct  = fg.destroyCancellationToken;

        if (cam == null || ctx.Runtime.PlayerTarget == null)
        {
            FinishSequence(ctx);
            return;
        }

        // ① 플레이어 시점 → 대각선(이동1)
        // onPanComplete 에서 HUD+복귀 시퀀스 시작. ReturnToPlayerAsync 가 이 팬을 내부 캔슬함.
        try
        {
            await cam.PanToZoneAndReturnAsync(
                ctx.Transform.position,
                fg.EntranceCamMoveDuration,
                99f,                        // HUD 시퀀스가 캔슬할 것이므로 dummy 값
                fg.EntranceCamReturnDuration,
                ctx.Runtime.PlayerTarget,
                ct,
                fg.EntranceCamOffset,
                onPanComplete: () => HudAndReturnAsync(ctx, fg, cam).Forget(),
                customLookOffset: fg.EntranceCamLookOffset);
        }
        catch (OperationCanceledException) { }
        // FinishSequence 는 HudAndReturnAsync 에서 호출
    }

    private async UniTaskVoid HudAndReturnAsync(MonsterContext ctx, ForestGuardianMonster fg, GameCameraController cam)
    {
        var ct = fg.destroyCancellationToken;

        try
        {
            // ② 보스 이름 HUD (대각선 카메라 상태에서 표시)
            if (UI_BossBark.Instance != null)
            {
                string bark = "Forest Guardian";
                var lines = Managers.DialogueData?.GetVisitLines("ForestGuardian_Encounter");
                if (lines != null && lines.Length > 0) bark = lines[0].text;
                await UI_BossBark.ShowAndWaitAsync(bark, BossBarkType.BossIntro);
            }
            else
                await UniTask.Delay(TimeSpan.FromSeconds(BossIntroFallbackDuration), cancellationToken: ct);

            // ③ 플레이어 카메라 복귀(이동2) — 내부에서 ①의 팬을 캔슬하고 이어받음
            await cam.ReturnToPlayerAsync(ctx.Runtime.PlayerTarget, fg.EntranceCamReturnDuration, ct);
        }
        catch (OperationCanceledException) { return; }

        FinishSequence(ctx);
    }

    private void FinishSequence(MonsterContext ctx)
    {
        if (ctx.Agent != null && !ctx.Agent.enabled)
        {
            ctx.Agent.enabled = true;
            ctx.Agent.Warp(ctx.Transform.position);
        }

        ctx.Runtime.CachedPlayer?.SetInputEnabled(true);
        IsActive = false;
        (ctx.Monster as ForestGuardianMonster)?.FireCombatReady();
        ctx.Monster.ChangeState<ChaseState>();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 헬퍼
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    private static void PlayAttackReadyAnim(MonsterContext ctx)
    {
        if (ctx.Animator == null) return;
        string stateName = ctx.Animation?.attackReadyStateName;
        if (string.IsNullOrEmpty(stateName)) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(stateName))) return;
        ctx.Animator.CrossFade(stateName, 0.2f);
    }
}
}
