using UnityEngine;

/// <summary>
/// 서약 픽업 테스트 트리거.
/// T 키를 누르면 플레이어 앞에 WorldCovenantPickup 오브젝트를 스폰한다.
/// </summary>
public class CovenantChoiceTestTrigger : MonoBehaviour
{
    [SerializeField] private float spawnDistance = 3f;

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.T)) return;

        var run = GameRunBootstrapper.Instance?.Run;
        if (run?.Player == null) return;

        var playerT = run.Player.transform;
        Vector3 spawnPos = playerT.position + playerT.forward * spawnDistance + Vector3.up * 0.5f;

        var options = WorldCovenantPickup.PickRandomOptions(run.CovenantHandler);
        WorldCovenantPickup.SpawnAt(spawnPos, options);

        Debug.Log($"[CovenantTest] T → 서약 픽업 스폰 @ {spawnPos}");
    }
}
