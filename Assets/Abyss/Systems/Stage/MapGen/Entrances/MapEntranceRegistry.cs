public static class MapEntranceRegistry
{
    public static IMapEntrance Resolve(string _) => new DissolveEntrance();
}
