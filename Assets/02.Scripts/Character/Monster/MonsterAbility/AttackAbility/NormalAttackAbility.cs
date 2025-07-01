using UnityEngine;

public class NormalAttackAbility : IMonsterAbility
{
    private float damage;
    private float range;
    private AnimationClip animationClip;

    private Define.AttackStyle style;
    private Define.AttackPurpose purpose;

    private MonsterController owner;
    private BatAnimationEventReceiver eventReceiver;

    public Define.MonsterAbilityType Type => Define.MonsterAbilityType.Attack;
    public Define.AttackStyle Style => style;
    public Define.AttackPurpose Purpose => purpose;

    public NormalAttackAbility(float damage, float range, AnimationClip anim, Define.AttackStyle style, Define.AttackPurpose purpose)
    {
        this.damage = damage;
        this.range = range;
        this.animationClip = anim;
        this.style = style;
        this.purpose = purpose;
    }

    public void Init(MonsterController owner)
    {
        this.owner = owner;

        eventReceiver = owner.GetComponent<BatAnimationEventReceiver>();
        if (eventReceiver != null)
        {
            eventReceiver.OnAttackStartEvent += OnAttackStart;
            eventReceiver.OnAttackEndEvent += OnAttackEnd;
        }
    }

    public void Dispose()
    {
        if (eventReceiver != null)
        {
            eventReceiver.OnAttackStartEvent -= OnAttackStart;
            eventReceiver.OnAttackEndEvent -= OnAttackEnd;
            eventReceiver = null;
        }
    }

    public void Execute()
    {
        if (owner == null || owner.playerTarget == null)
            return;

        float distance = Vector3.Distance(owner.transform.position, owner.playerTarget.position);
        if (distance > range)
            return;

        if (animationClip != null)
        {
            owner.animator.CrossFade(animationClip.name, 0.1f);
        }
    }

    private void OnAttackStart()
    {
        Debug.Log($"NormalAttackAbility: 공격 시작 이벤트 받음, 데미지: {damage}");
        // 실제 데미지 처리 등 로직 추가
    }

    private void OnAttackEnd()
    {
        Debug.Log("NormalAttackAbility: 공격 종료 이벤트 받음");
    }
}
