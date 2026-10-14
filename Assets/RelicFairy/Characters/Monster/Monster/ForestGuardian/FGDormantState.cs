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

    // 등장 팬 도중 상태를 떠나면(팬이 끝나기 전 쓰러짐 등 — onPanComplete가 안 불린다) 레터박스 · HUD를 여기서 걷는다(10-14 전주기 5회차 참고)
    public void Exit(MonsterContext ctx)
    {
        if (!_cine) return;
        BossStoryScenes.EndEntrance(StoryProgress.ForestGuardian, completed: false);
        _cine = false;
    }

    private bool _cine;   // 이야기 전투 등장 — 레터박스 · HUD를 걷었다(끝에 되돌린다)

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
        // 해방기도 묶임 없이 — 봉인은 리치에게 떨어질 때 이미 깨졌다(10-13 사용자 「봉인 상태에서 시작은 레거시」)

        if (cam == null || ctx.Runtime.PlayerTarget == null)
        {
            FinishSequence(ctx);
            return;
        }
        _cine = BossStoryScenes.BeginEntrance(StoryProgress.ForestGuardian, ct);   // 레터박스 · 전투 HUD 걷기(10-14)

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
            // ② 등장 자막 + 이름 · 칭호(대각선 카메라 상태) — 반복 관람은 짧게(10-14)
            string bark = "Forest Guardian";
            var lines = Managers.DialogueData?.GetBossEncounterLines("ForestGuardian_Encounter");
            if (lines != null && lines.Length > 0) bark = lines[0].text;
            await BossStoryScenes.IntroLineAsync(fg, StoryProgress.ForestGuardian, bark, ct);

            // 해방기 — 카메라가 보스를 비추는 동안 옛 봉인 자국이 깨지며 포효(10-13 재조우)
            await BossStoryScenes.ReturnAsync(fg, StoryProgress.ForestGuardian, ct);

            // ③ 플레이어 카메라 복귀(이동2) — 내부에서 ①의 팬을 캔슬하고 이어받음
            await cam.ReturnToPlayerAsync(ctx.Runtime.PlayerTarget, 0.6f, ct);   // 짧게 블렌드 — 복귀 목표가 게임 카메라 자세로 고쳐져(10-14) 덜컥 넘어가지 않게
            if (_cine) { BossStoryScenes.EndEntrance(StoryProgress.ForestGuardian, completed: true); _cine = false; }
        }
        catch (OperationCanceledException) { return; }
        finally
        {
            if (_cine) { BossStoryScenes.EndEntrance(StoryProgress.ForestGuardian, completed: false); _cine = false; }
        }

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
