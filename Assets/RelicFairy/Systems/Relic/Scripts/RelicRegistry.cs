/// <summary>RelicId → IRelicBehavior 팩토리. 코드형 유물 로직을 여기서 생성.</summary>
public static class RelicRegistry
{
    public static IRelicBehavior Create(RelicId id)
    {
        switch (id)
        {
            case RelicId.Galahad: return new GalahadRelic();
            case RelicId.Gawain:  return new GawainZenithRelic(); // v1 차세대(레거시 GawainRelic 보존·미등록)
            default:              return null;
        }
    }
}
