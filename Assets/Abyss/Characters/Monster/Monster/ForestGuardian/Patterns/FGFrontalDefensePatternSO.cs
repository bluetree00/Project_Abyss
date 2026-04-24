using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 공통 패턴 ② — 정면 방어.
/// 플레이어가 뒤로 물러나는 상황(거리 4m 이상)에 방어 자세를 취한다.
/// 2초간 무적. patternTag = "defense"
/// </summary>
[CreateAssetMenu(fileName = "FGFrontalDefensePatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/FrontalDefense")]
public class FGFrontalDefensePatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animStateName = "DefenseST";
    [SerializeField] private string animIdleName  = "IdleNormal";
    [SerializeField] private float  crossFade     = 0.15f;

    [Header("방어 설정")]
    [SerializeField] private float defenseDuration = 2f;

    private FGFrontalDefenseState _state;

    public override void Initialize(BossPatternContext ctx)
    {
        patternTag = "defense";
        _state = new FGFrontalDefenseState(this);
    }

    public override bool CanExecute(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;

    // ── 내부 상태 ────────────────────────────────────────────────────

    private sealed class FGFrontalDefenseState : InvincibleState<FGFrontalDefensePatternSO>
    {
        private float _timer;

        public FGFrontalDefenseState(FGFrontalDefensePatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;
            _timer = 0f;

            if (ctx.Animator != null)
            {
                string anim = string.IsNullOrEmpty(Data.animStateName)
                    ? Data.animIdleName
                    : Data.animStateName;
                ctx.Animator.CrossFade(anim, Data.crossFade);
            }
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;
            if (_timer >= Data.defenseDuration)
                ctx.Monster.ChangeState<PatrolState>();
        }

        public override void Exit(MonsterContext ctx)
        {
            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animIdleName))
                ctx.Animator.CrossFade(Data.animIdleName, Data.crossFade);
        }
    }
}
}
