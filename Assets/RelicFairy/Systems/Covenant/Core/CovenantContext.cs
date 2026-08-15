/// <summary>
/// 서약이 런타임에 접근할 수 있는 의존성 컨테이너.
/// CovenantHandler.Initialize() 시 생성되어 각 서약에 주입된다.
/// </summary>
public sealed class CovenantContext
{
    public PlayerController    Player    { get; }
    public PlayerRuntimeStats  Stats     { get; }
    public PlayerRunState      RunState  { get; }
    public GameRunSession      Session   { get; }

    public CovenantContext(
        PlayerController    player,
        PlayerRuntimeStats  stats,
        PlayerRunState      runState,
        GameRunSession      session)
    {
        Player    = player;
        Stats     = stats;
        RunState  = runState;
        Session   = session;
    }
}
