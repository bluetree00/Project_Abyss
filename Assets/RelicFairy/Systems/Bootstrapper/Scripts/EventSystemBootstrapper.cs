using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// 인게임 챕터 씬(GameScene_Ch1~4, Tutorial, Logo 등)에는 EventSystem이 배치돼 있지 않아
/// 버프 호버 툴팁·상점 버튼/구매 클릭 등 모든 UI 포인터 이벤트가 동작하지 않는다.
/// (EventSystem은 BaseCamp/Lobby/테스트 씬에만 존재하며, DDOL @UIRoot에도 없음.)
///
/// 씬 로드마다 EventSystem 존재를 보장한다:
///  - 이미 있으면(씬 자체 보유 또는 DDOL) 생성하지 않는다 → 중복 방지.
///  - 없으면 생성한다. 생성물은 해당 씬에 귀속되어 다음 씬(Single) 로드 시 함께 정리된다.
///
/// 입력 모듈은 기존 씬(BaseCamp/Lobby)의 EventSystem과 동일하게 StandaloneInputModule을 쓴다
/// (ProjectSettings activeInputHandler=Both 이므로 구 입력 모듈 정상 동작).
/// </summary>
public static class EventSystemBootstrapper
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Hook()
    {
        // 첫 씬 포함 모든 씬 로드 후 보장. 중복 구독 방지.
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureEventSystem();

    private static void EnsureEventSystem()
    {
        // 씬 자체 보유 또는 DDOL 인스턴스가 이미 있으면 추가 생성하지 않는다.
        if (Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) != null)
            return;

        // 씬 귀속(DDOL 아님) → 자체 EventSystem 보유 씬과 중복되지 않도록 Single 로드 시 함께 파괴.
        new GameObject("@EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }
}
