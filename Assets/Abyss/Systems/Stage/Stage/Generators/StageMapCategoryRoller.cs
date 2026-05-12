using UnityEngine;

/// <summary>
/// 진행도(층 인덱스) 기반 방 카테고리 랜덤 선택.
/// Pyramid/Refined 스타일이 공통으로 사용.
/// </summary>
internal static class StageMapCategoryRoller
{
    public static NormalRoomCategory Pick(int layerIdx, int totalMiddleLayers)
    {
        float progress = totalMiddleLayers > 1 ? (float)layerIdx / (totalMiddleLayers - 1) : 0.5f;
        float roll = Random.value;

        if (progress < 0.3f)
            return roll < 0.7f ? NormalRoomCategory.Battle : NormalRoomCategory.Event;
        if (progress < 0.6f)
            return roll < 0.4f ? NormalRoomCategory.Battle :
                   roll < 0.7f ? NormalRoomCategory.Event :
                   roll < 0.85f ? NormalRoomCategory.Shop : NormalRoomCategory.Elite;
        return roll < 0.5f ? NormalRoomCategory.Battle :
               roll < 0.75f ? NormalRoomCategory.Elite : NormalRoomCategory.Event;
    }
}
