/// <summary>
/// 몬스터·플레이어 공격에 적용되는 원소 속성.
/// None = 무속성 (기존 동작 유지).
/// </summary>
public enum ElementType
{
    None      = -1,
    Lightning =  0,   // 번개
    Water     =  1,   // 물
    Fire      =  2,   // 불
    Grass     =  3,   // 풀
    Earth     =  4,   // 땅
}

/// <summary>원소 인덱스 상수 및 유틸리티.</summary>
public static class ElementTypeUtil
{
    public const int Count = 5;

    /// <summary>ElementType → 배열 인덱스. None은 -1 반환.</summary>
    public static int ToIndex(this ElementType e) => (int)e;

    /// <summary>유효한 원소 타입인지 (None 제외).</summary>
    public static bool IsValid(this ElementType e) => e != ElementType.None;
}
