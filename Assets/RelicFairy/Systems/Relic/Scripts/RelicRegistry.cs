/// <summary>RelicId → IRelicBehavior 팩토리. 코드형 유물 로직을 여기서 생성.</summary>
public static class RelicRegistry
{
    public static IRelicBehavior Create(RelicId id)
    {
        switch (id)
        {
            case RelicId.Gawain:  return new GawainZenithRelic(); // 신규 Zenith 버전 (레거시 GawainRelic 삭제됨)
            case RelicId.Lancelot: return new LancelotMadnessRelic();
            default:              return null;
        }
    }
}
