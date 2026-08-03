using UnityEngine;

/// <summary>
/// <see cref="IRelicPartEffect"/> 기본 구현 — 모든 훅이 빈 가상 메서드(스켈레톤).
///
/// effect_key마다 이 클래스를 상속해 필요한 훅만 override 한다(룬의 RuneEffect와 같은 패턴).
/// 아직 구현하지 않은 key는 이 클래스 그대로 등록돼 <b>아무 일도 하지 않지만</b>,
/// 드래프트·획득·저장·해제 배관은 정상 동작한다(연결만 보장).
/// </summary>
public class RelicPartEffect : IRelicPartEffect
{
    public string EffectKey { get; }

    public RelicPartEffect(string effectKey) => EffectKey = effectKey;

    public virtual void OnAcquire(PlayerController player) { }
    public virtual void OnHit(in HitInfo hit, PlayerController player) { }
    public virtual void OnCrit(in HitInfo hit, PlayerController player) { }
    public virtual void OnDamaged(in HitInfo hit, PlayerController player) { }
    public virtual void OnSkillUsed(PlayerController player) { }
    public virtual void OnKill(GameObject deadEnemy, PlayerController player) { }
    public virtual void Tick(float dt, PlayerController player) { }
    public virtual void OnRemove(PlayerController player) { }
}
