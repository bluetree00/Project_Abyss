/// <summary>
/// 원소가 자신의 효과를 알고 있는 Strategy 베이스.
/// 새 원소 추가 시 이 클래스를 상속하고 ElementRegistry에 등록.
/// </summary>
public abstract class ElementBehavior
{
    public abstract ElementType Element { get; }

    /// <summary>임계값 도달 → 효과 시작 시 1회 호출.</summary>
    public abstract void Apply(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry);

    /// <summary>tick_interval 마다 호출. 기본은 무동작.</summary>
    public virtual void Tick(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry) { }

    /// <summary>지속시간 만료 또는 덮어쓰기 시 호출. 상태 원복.</summary>
    public abstract void Clear(IElementTarget target, ElementBuildup buildup, ElementEffectEntry entry);
}
