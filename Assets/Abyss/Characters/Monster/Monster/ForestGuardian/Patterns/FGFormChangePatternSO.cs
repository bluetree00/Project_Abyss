using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 페이즈 전환 패턴.
/// HP ≤ 50%로 PhaseChangePending == true 가 되면 forceExecute 엔트리에서 발동.
/// TiredStart 애니 3초 재생 → VFX → Phase2 전환 완료 → TiredStop.
/// 전환 중 무적(InvincibleState), 전환 직후 breakOverride = 0 (즉시 패턴 재개).
/// </summary>
[CreateAssetMenu(fileName = "FGFormChangePatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/PhaseTransition")]
public class FGPhaseTransitionPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animTiredStart = "TiredStart";
    [SerializeField] private string animTiredStop  = "TiredStop";
    [SerializeField] private string animIdle       = "IdleNormal";
    [SerializeField] private float  crossFade      = 0.15f;

    [Header("전환 설정")]
    [SerializeField] private float transitionDuration = 3f;   // 기획서: 임시 3초

    [Header("VFX")]
    [SerializeField] private GameObject vfxPhaseChange;

    private FGPhaseTransitionState _state;

    public override void Initialize(BossPatternContext ctx)
    {
        breakOverride = 0f;   // 전환 직후 즉시 패턴 선택
        _state = new FGPhaseTransitionState(this);
    }

    public override bool CanExecute(BossPatternContext ctx)
    {
        var fg = (ctx.Ctx.Monster as ForestGuardianMonster)?.FGBlackboard;
        return fg != null && fg.PhaseChangePending;
    }

    public override bool CanForceInterrupt(BossPatternContext ctx) => CanExecute(ctx);

    public override SpecialStateBase GetRuntimeState() => _state;

    // ── 내부 상태 ───────────────────────────────────────────────────────

    private sealed class FGPhaseTransitionState : InvincibleState<FGPhaseTransitionPatternSO>
    {
        private float _timer;
        private bool  _transitioned;

        public FGPhaseTransitionState(FGPhaseTransitionPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;
            _timer       = 0f;
            _transitioned = false;

            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animTiredStart))
                ctx.Animator.CrossFade(Data.animTiredStart, Data.crossFade);
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            if (!_transitioned && _timer >= Data.transitionDuration)
            {
                _transitioned = true;

                if (Data.vfxPhaseChange != null)
                    BossEffectPool.SpawnOneShot(Data.vfxPhaseChange,
                        ctx.Transform.position, ctx.Transform.rotation);

                var fg = (ctx.Monster as ForestGuardianMonster)?.FGBlackboard;
                fg?.OnPhaseTransitionComplete();

                if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animTiredStop))
                    ctx.Animator.CrossFade(Data.animTiredStop, Data.crossFade);
            }

            if (_transitioned && _timer >= Data.transitionDuration + 0.3f)
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
