/// <summary>
/// 유물 리소스의 직렬화 스냅샷(이어하기용). 구현체별 의미는 다르나(게이지%/스택수 등)
/// 세이브 경로는 이 공통 구조만 본다.
/// </summary>
[System.Serializable]
public struct RelicResourceState
{
    public float fill;   // 진행도(게이지/스택 정규화 또는 원시값 — 구현체 약속)
    public int   phase;  // 구간 인덱스
    public float aux;    // 보조값(쿨다운 잔여, 축적 카운트 등)
}
