using System;
using UnityEngine;

/// <summary>
/// 게임 준비 화면에서 선택 가능한 무기 목록 SO.
/// 에디터에서 무기를 추가/제거하면 PrepPanel에 자동 반영됩니다.
/// </summary>
[CreateAssetMenu(fileName = "WeaponRoster", menuName = "Weapons/Weapon Roster")]
public class WeaponRoster : ScriptableObject
{
    public WeaponEntry[] weapons;

    [Serializable]
    public class WeaponEntry
    {
        [Tooltip("무기 SO 데이터")]
        public WeaponSO data;

        [Tooltip("선택 UI에 표시할 아이콘 (비어있으면 data.icon 사용)")]
        public Sprite icon;
    }
}
