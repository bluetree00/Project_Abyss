using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 폼 전환 패턴.
/// FormChangePending == true 일 때 forceExecute 엔트리에서 발동.
/// 피로 애니("TiredStart") 2초 재생 후 폼 전환 → 즉시 패턴 재개(breakOverride = 0).
/// </summary>
[CreateAssetMenu(fileName = "FGFormChangePatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/FormChange")]
public class FGFormChangePatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animTiredStart = "TiredStart";
    [SerializeField] private string animTiredStop  = "TiredStop";
    [SerializeField] private string animIdle       = "IdleNormal";
    [SerializeField] private float  crossFade      = 0.15f;

    [Header("폼 전환 설정")]
    [SerializeField] private float tiredDuration = 2f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxFormChange;

    private FGFormChangeState _state;

    public override void Initialize(BossPatternContext ctx)
    {
        breakOverride = 0f;   // 전환 직후 즉시 패턴 선택
        _state = new FGFormChangeState(this);
    }

    public override bool CanExecute(BossPatternContext ctx)
    {
        var fg = (ctx.Ctx.Monster as ForestGuardianMonster)?.FGBlackboard;
        return fg != null && fg.FormChangePending;
    }

    public override bool CanForceInterrupt(BossPatternContext ctx) => CanExecute(ctx);

    public override SpecialStateBase GetRuntimeState() => _state;

    // ── 내부 상태 ────────────────────────────────────────────────────

    private sealed class FGFormChangeState : InvincibleState<FGFormChangePatternSO>
    {
        private float                             _timer;
        private bool                              _changed;
        private ForestGuardianBlackboard.BossForm _nextForm;

        public FGFormChangeState(FGFormChangePatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;
            _timer   = 0f;
            _changed = false;

            // 다음 폼 미리 결정
            var fgBb = (ctx.Monster as ForestGuardianMonster)?.FGBlackboard;
            _nextForm = fgBb?.RollNextForm() ?? ForestGuardianBlackboard.BossForm.Liche;

            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animTiredStart))
                ctx.Animator.CrossFade(Data.animTiredStart, Data.crossFade);
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            if (!_changed && _timer >= Data.tiredDuration)
            {
                _changed = true;

                // 폼 전환 VFX
                if (Data.vfxFormChange != null)
                    BossEffectPool.SpawnOneShot(Data.vfxFormChange,
                        ctx.Transform.position, ctx.Transform.rotation);

                // 블랙보드 업데이트
                var fgBb = (ctx.Monster as ForestGuardianMonster)?.FGBlackboard;
                fgBb?.OnFormChanged(_nextForm);

                // TiredStop 애니 → 이후 자동 idle 전환 예정
                if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animTiredStop))
                    ctx.Animator.CrossFade(Data.animTiredStop, Data.crossFade);
            }

            // TiredStop 짧게 재생 후 Patrol로 복귀 (0.3초 여유)
            if (_changed && _timer >= Data.tiredDuration + 0.3f)
                ctx.Monster.ChangeState<PatrolState>();
        }

        public override void Exit(MonsterContext ctx)
        {
            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animIdle))
                ctx.Animator.CrossFade(Data.animIdle, Data.crossFade);
        }
    }
}
}
