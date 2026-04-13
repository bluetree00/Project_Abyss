using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 스테이지 노드 카테고리별 아이콘 Addressable 키 매핑.
/// StagePointUI가 자신의 카테고리에 맞는 아이콘을 비동기 로드하는 데 사용한다.
/// </summary>
[CreateAssetMenu(menuName = "Abyss/Stage/NodeIconMap", fileName = "StageNodeIconMap")]
public class StageNodeIconMap : ScriptableObject
{
    [Header("StageCategory 기본 아이콘")]
    [SerializeField] private string startIconKey = "flag_1";
    [SerializeField] private string bossIconKey = "dragon";

    [Header("NormalRoomCategory 아이콘")]
    [SerializeField] private string battleIconKey = "sword_1";
    [SerializeField] private string eliteIconKey = "skeleton_1";
    [SerializeField] private string eventIconKey = "symbol_1";
    [SerializeField] private string shopIconKey = "chest_1";

    [Header("기본값 (매핑 실패 시)")]
    [SerializeField] private string defaultIconKey = "flag_1";

    /// <summary>StageCategory + NormalRoomCategory 조합으로 Addressable 키 반환.</summary>
    public string GetIconKey(StageCategory stage, NormalRoomCategory normal)
    {
        switch (stage)
        {
            case StageCategory.Start:
                return !string.IsNullOrEmpty(startIconKey) ? startIconKey : defaultIconKey;

            case StageCategory.Boss:
                return !string.IsNullOrEmpty(bossIconKey) ? bossIconKey : defaultIconKey;

            case StageCategory.Normal:
                return GetNormalIconKey(normal);

            default:
                return defaultIconKey;
        }
    }

    private string GetNormalIconKey(NormalRoomCategory normal)
    {
        var key = normal switch
        {
            NormalRoomCategory.Battle => battleIconKey,
            NormalRoomCategory.Elite  => eliteIconKey,
            NormalRoomCategory.Event  => eventIconKey,
            NormalRoomCategory.Shop   => shopIconKey,
            NormalRoomCategory.Random => battleIconKey,
            _ => null,
        };

        return !string.IsNullOrEmpty(key) ? key : defaultIconKey;
    }
}
