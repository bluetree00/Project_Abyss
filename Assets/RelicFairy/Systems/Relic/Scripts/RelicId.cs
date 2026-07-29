/// <summary>
/// 유물 클래스 식별자. RelicClassSO ↔ 코드형 패시브/스킬 팩토리 매핑 키.
/// 신규 유물 = Gawain(Zenith) / Lancelot. (Galahad/Knight/Mage/Berserker 폐기)
/// 값은 직렬화 호환을 위해 유지(1은 결번).
/// </summary>
public enum RelicId
{
    None = 0,
    Gawain = 2,
    Lancelot = 3,
}
