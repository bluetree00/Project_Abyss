/// <summary>
/// effect_type 문자열 → IRuneEffect 인스턴스 생성.
///
/// 현재는 모든 effect_type에 빈 RuneEffect를 반환(연결만 보장).
/// 각 속성 단계 효과를 구현할 때, 해당 effect_type을 전용 서브클래스로 분기하도록 교체한다.
/// 예) "FireEmber" => new FireEmberEffect()
/// </summary>
public static class RuneEffectFactory
{
    public static IRuneEffect Create(RuneSynergyEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.effect_type))
            return null;

        // TODO: effect_type 별 전용 효과 클래스로 분기 (불/얼음/전기/풀/빛/어둠 24종)
        var fx = new RuneEffect();
        fx.Bind(entry);
        return fx;
    }
}
