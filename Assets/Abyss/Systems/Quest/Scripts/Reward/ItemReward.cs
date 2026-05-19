using UnityEngine;

[CreateAssetMenu(menuName = "Quest/Reward/ItemReward", fileName = "ItemReward_")]
public class ItemReward : Reward
{
    [SerializeField] private ItemSO itemSO;

    public ItemSO Item => itemSO;

    public override void Give(Quest quest)
    {
        if (itemSO == null)
        {
            Debug.LogWarning("[ItemReward] itemSO is not assigned.");
            return;
        }

        var run = GameRunBootstrapper.Instance?.Run;
        if (run == null)
        {
            Debug.LogWarning("[ItemReward] GameRunSession not available.");
            return;
        }

        var data = RuntimeItemData.FromSO(itemSO);
        if (data == null)
        {
            Debug.LogWarning($"[ItemReward] RuntimeItemData.FromSO failed for {itemSO.name}.");
            return;
        }

        run.ItemInventory?.AddToStaging(data);
    }
}
