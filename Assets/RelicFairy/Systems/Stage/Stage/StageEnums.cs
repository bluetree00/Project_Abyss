// 스테이지 시스템에서 공통으로 사용하는 열거형 정의.
// 과거 리팩토링 과정에서 소실된 타입들을 복원한 파일입니다.

public enum ChapterId
{
    Chapter1 = 1,
    Chapter2 = 2,
    Chapter3 = 3,
    Chapter4 = 4,
}

public enum RoomCategory
{
    Start,
    Battle,
    Elite,
    Boss,
    Event,
    Shop,
    Rest,
    Reward,
}

public enum BarrierType
{
    None,
    Water,
    Lava,
    Fog,
    Void,
}
