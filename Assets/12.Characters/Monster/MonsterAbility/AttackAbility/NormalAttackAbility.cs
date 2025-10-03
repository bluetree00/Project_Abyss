using UnityEngine;

public class NormalAttackAbility : IAttackAbility, IAnimClipProvider
{
    private float damage;
    private float range;
    private AnimationClip animationClip;

    private Define.AttackStyle style;
    private Define.AttackPurpose purpose;

    private MonsterController owner;
    private MonsterAnimationEventReceiver eventReceiver;

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

        eventReceiver = owner.GetComponent<MonsterAnimationEventReceiver>();
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

        // float distance = Vector3.Distance(owner.transform.position, owner.playerTarget.position);
        // if (distance > range)
        //     return;

        if (animationClip != null)
        {
            owner.animator.CrossFade(animationClip.name, 0.1f);
        }
    }

    public AnimationClip GetAttackAnimationClip() => animationClip;

    private void OnAttackStart()
    {
        if (owner == null) return;

        // 현재 진행 중인 Purpose와 이 Ability의 Purpose가 다르면 무시 (중복 구독 방지용 필터)
        if (owner.currentAttackPurpose != purpose)
            return;

        // 이펙트를 생성할 위치 (몬스터 앞 방향으로 약간 떨어진 곳)
        Vector3 spawnOffset = owner.transform.forward * 1.0f; // 1.0f는 거리, 필요에 따라 조정
        Vector3 spawnPosition = owner.transform.position + spawnOffset;

        Quaternion spawnRotation = Quaternion.LookRotation(owner.transform.forward); // 방향 유지

        //CHECKLIST:임시 로그 비활성화
        //GameObject effect = Managers.ObjectPooler.SpawnFromPool("ShinySlash", spawnPosition, spawnRotation);

        //CHECKLIST:임시 로그 비활성화
        Debug.Log($"NormalAttackAbility: 공격 시작 이벤트 받음, 데미지: {damage}");
        // 실제 데미지 처리 로직 추가
    }

    private void OnAttackEnd()
    {
        if (owner == null) return;

        if (owner.currentAttackPurpose != purpose)
            return;
        owner.SetAttackReadyTime(owner.MyStat.attack_cooldown);
        owner.SetAttack(false);
        //CHECKLIST:임시 로그 비활성화
        //Debug.Log("NormalAttackAbility: 공격 종료 이벤트 받음");
    }
}
