using UnityEngine;
using Cysharp.Threading.Tasks;

public sealed class GameRunBootstrapper : MonoBehaviour
{
    [SerializeField] private ChapterId startChapter;
    // [SerializeField] private StageMapSpawner spawner;

    // public void Bind(GameRunManager run)
    // {
    //     // spawner를 인스펙터로 못 넣는 경우도 있으니 보강
    //     if (spawner == null)
    //         spawner = FindObjectOfType<StageMapSpawner>(true);

    //     run.Spawner = spawner; // GameRunManager에 Spawner 프로퍼티 추가
    // }

    // private async void Start()
    // {
    //     // 시작 런 자동 실행이 필요하면 여기서
    //     await Managers.GameRun.StartNewRunAsync(startChapter);

    //     // Start 지점 맵 스폰까지 하고 싶으면:
    //     Managers.GameRun.SpawnCurrentPointMap(); // 이런 식의 메서드로 분리 권장
    // }
}
