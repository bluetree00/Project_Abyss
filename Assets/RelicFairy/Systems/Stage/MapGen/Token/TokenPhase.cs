/// <summary>
/// 토큰 핸들러 실행 페이즈.
/// PreBuild: MapBuilder.Build 직후, NavMesh 빌드 전에 실행 (스포너 등 NavMesh 무관 오브젝트).
/// PostBuild: NavMesh 빌드 후에 실행 (장식 등 Read/Write OFF 메시 포함 가능한 오브젝트).
/// </summary>
public enum TokenPhase
{
    PreBuild,
    PostBuild,
}
