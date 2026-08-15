using UnityEngine;

/// <summary>
/// 심연의 정수 보상. 업적 수령의 실제 지급자.
///
/// <para>⚠️ <see cref="GoldReward"/>를 복제하면 안 된다 — 그쪽은 <c>GameRunBootstrapper.Instance?.Run</c>을
/// 보는데, 업적 수령은 <b>거점 제단</b>에서 일어나 런이 없다. 그대로 베끼면 경고만 찍히고 정수가 증발한다.
/// 정수는 계정 영구 재화이므로 <see cref="UserGameData"/>를 직접 본다.</para>
/// </summary>
[CreateAssetMenu(menuName = "Quest/Reward/EssenceReward", fileName = "EssenceReward_")]
public class EssenceReward : Reward
{
    [SerializeField] private int essenceAmount;

    public int EssenceAmount => essenceAmount;

    public override void Give(Quest quest)
    {
        if (essenceAmount <= 0) return;

        var data = BackendGameData.Instance?.Data;
        if (data == null)
        {
            Debug.LogWarning($"[EssenceReward] UserGameData 없음 — '{quest?.CodeName}' 보상 {essenceAmount} 미지급.");
            return;
        }

        data.abyssEssence += essenceAmount;
    }
}
