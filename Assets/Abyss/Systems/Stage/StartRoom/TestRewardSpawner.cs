using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 테스트 전용: 게임 세션 준비 후 기존 EndEffect2 + ClearRewardTrigger를 이 위치에 스폰.
/// GameScene에 배치해 아이템 획득 UI 흐름을 빠르게 검증하는 데 사용.
/// </summary>
public class TestRewardSpawner : MonoBehaviour
{
    [SerializeField, Tooltip("RoomClearGate와 동일한 EndEffect2 프리팹. 비워두면 빈 오브젝트 사용.")]
    private GameObject endEffect2Prefab;

    [SerializeField, Tooltip("테스트에 사용할 아이템 SO. 비워두면 Common 랜덤.")]
    private ItemSO testItemSO;

    [SerializeField, Tooltip("GameRunSession 준비 후 추가 대기 시간(초)."), Min(0f)]
    private float spawnDelay = 1f;

    private void Start() => SpawnAsync().Forget();

    private async UniTaskVoid SpawnAsync()
    {
        var ct = this.GetCancellationTokenOnDestroy();

        try
        {
            // ItemInventory가 초기화될 때까지 대기 (GameRunBootstrapper.BindPlayer 이후)
            await UniTask.WaitUntil(
                () => GameRunBootstrapper.Instance?.Run?.ItemInventory != null,
                cancellationToken: ct);

            await UniTask.Delay(TimeSpan.FromSeconds(spawnDelay), cancellationToken: ct);
        }
        catch (OperationCanceledException) { return; }

        var run = GameRunBootstrapper.Instance?.Run;
        if (run == null)
        {
            Debug.LogWarning("[TestRewardSpawner] GameRunSession이 null — 스폰 취소");
            return;
        }

        var so = ResolveItemSO();
        if (so == null)
        {
            Debug.LogWarning("[TestRewardSpawner] 사용할 ItemSO 없음 — ItemSORegistry 확인 필요");
            return;
        }

        var data = RuntimeItemData.FromSO(so);
        if (data == null)
        {
            Debug.LogWarning($"[TestRewardSpawner] RuntimeItemData 변환 실패 — itemId={so.itemId}");
            return;
        }

        // RoomClearGate.SpawnRewardObject와 동일한 방식 — 기존 이펙트 프리팹 재사용
        var rewardGO = endEffect2Prefab != null
            ? Instantiate(endEffect2Prefab, transform.position, Quaternion.identity)
            : new GameObject("TestClearReward");
        rewardGO.transform.position = transform.position;

        var trigger = rewardGO.AddComponent<ClearRewardTrigger>();
        var rewards = new List<(RuntimeItemData, ItemSO)> { (data, so) };
        trigger.Initialize(run, rewards, isBossRoom: false);

        Debug.Log($"[TestRewardSpawner] 보상 트리거 스폰 완료 — {so.itemId} @ {transform.position}");
    }

    private ItemSO ResolveItemSO()
    {
        if (testItemSO != null) return testItemSO;

        // SO 미할당 시 레지스트리에서 Common 아이템 랜덤 선택
        var pool = ItemSORegistry.GetByRarity(ItemRarity.Common);
        if (pool != null && pool.Count > 0)
            return pool[UnityEngine.Random.Range(0, pool.Count)];

        pool = ItemSORegistry.GetByRarity(ItemRarity.Rare);
        if (pool != null && pool.Count > 0)
            return pool[UnityEngine.Random.Range(0, pool.Count)];

        return null;
    }
}
