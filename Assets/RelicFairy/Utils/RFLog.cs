using UnityEngine;

/// <summary>
/// 핫패스(맵 빌드/룬 배치 등) 전용 조건부 로그.
/// UNITY_EDITOR / DEVELOPMENT_BUILD 빌드에서만 컴파일에 포함되며,
/// 릴리즈 빌드에서는 호출문 전체(문자열 보간 인자 포함)가 제거되어 GC 할당이 사라진다.
///
/// ⚠ 에러/경고는 릴리즈에서도 노출돼야 하므로 이 래퍼를 쓰지 말고
///   Debug.LogError / Debug.LogWarning 을 직접 사용한다.
/// </summary>
public static class RFLog
{
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void D(string message) => Debug.Log(message);

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void D(string message, Object context) => Debug.Log(message, context);
}
