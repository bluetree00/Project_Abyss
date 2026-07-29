/// <summary>
/// IRuneEffect 기본 구현. 모든 훅이 빈 가상 메서드(스켈레톤).
/// 속성 단계 효과를 구현할 때 이 클래스를 상속해 필요한 훅만 override 하고,
/// RuneEffectFactory.Create에서 해당 effect_type을 그 서브클래스로 반환하도록 교체한다.
/// 본 클래스 자체는 "연결만 된 빈 효과"로도 사용된다.
/// </summary>
public class RuneEffect : IRuneEffect
{
    public RuneSynergyEntry Entry { get; private set; }
    public string EffectType => Entry != null ? Entry.effect_type : null;

    /// <summary>생성 직후 데이터 바인딩. 팩토리에서 호출.</summary>
    public void Bind(RuneSynergyEntry entry) => Entry = entry;

    public virtual void OnActivate(PlayerController player) { }
    public virtual void OnHit(in HitInfo hit, PlayerController player) { }
    public virtual void OnCrit(in HitInfo hit, PlayerController player) { }
    public virtual void OnDamaged(in HitInfo hit, PlayerController player) { }
    public virtual void OnSkillUsed(PlayerController player) { }
    public virtual void OnKill(PlayerController player) { }
    public virtual void Tick(float dt, PlayerController player) { }
    public virtual void OnDeactivate() { }
}
