/// <summary>
/// 유물이 자신의 고유 리소스(<see cref="IRelicResource"/>)를 HUD 전용 아이덴티티 바에 노출하는 선택적 계약.
/// <see cref="IBuffViewSource"/>와 동일하게 IRelicBehavior에 옵트인으로 구현한다(다른 유물은 불변).
/// HUD는 활성 유물이 이 인터페이스면 체력바 아래 아이덴티티 바에 Fill/Label/BarColor를 연결한다.
/// </summary>
public interface IRelicResourceProvider
{
    IRelicResource RelicResource { get; }
}
