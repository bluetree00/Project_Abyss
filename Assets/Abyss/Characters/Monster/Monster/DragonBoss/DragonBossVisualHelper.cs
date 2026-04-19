using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// DragonBoss 시각 효과 헬퍼.
/// 원소 종류에 따른 색상 반환 등 공통 비주얼 유틸리티를 제공한다.
/// </summary>
public static class DragonBossVisualHelper
{
    public static Color GetElementColor(DragonBossBlackboard.DragonElement element)
    {
        return element switch
        {
            DragonBossBlackboard.DragonElement.Ice     => new Color(0.4f, 0.8f, 1.0f),
            DragonBossBlackboard.DragonElement.Thunder => new Color(1.0f, 0.9f, 0.2f),
            _                                          => new Color(1.0f, 0.4f, 0.1f),
        };
    }
}
}
