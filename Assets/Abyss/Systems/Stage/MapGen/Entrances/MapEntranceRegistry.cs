/// <summary>
/// entrance 식별자 → IMapEntrance 인스턴스 매핑.
/// 새 연출 추가 시 여기 한 줄 + 구현체 파일 하나 추가.
/// </summary>
public static class MapEntranceRegistry
{
    /// <summary>ID 매칭 실패 또는 빈 값이면 Dissolve(기본)로 폴백.</summary>
    public static IMapEntrance Resolve(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return new DissolveEntrance();

        return id.Trim() switch
        {
            "Dissolve"   => new DissolveEntrance(),
            "Scatter"    => new ScatterEntrance(),
            "TetrisDrop" => new TetrisDropEntrance(),
            "Shockwave"  => new ShockwaveEntrance(),
            _            => new DissolveEntrance(),
        };
    }
}
