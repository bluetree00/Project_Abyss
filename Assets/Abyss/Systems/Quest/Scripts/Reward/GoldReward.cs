using UnityEngine;

[CreateAssetMenu(menuName = "Quest/Reward/GoldReward", fileName = "GoldReward_")]
public class GoldReward : Reward
{
    [SerializeField] private int goldAmount;

    public int GoldAmount => goldAmount;

    public override void Give(Quest quest)
    {
        var run = GameRunBootstrapper.Instance?.Run;
        if (run == null)
        {
            Debug.LogWarning("[GoldReward] GameRunSession not available.");
            return;
        }
        run.AddGold(goldAmount);
    }
}
