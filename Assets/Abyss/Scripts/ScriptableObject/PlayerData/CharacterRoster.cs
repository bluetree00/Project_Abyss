using System;
using UnityEngine;

/// <summary>
/// 게임 준비 화면에서 선택 가능한 캐릭터 목록 SO.
/// 에디터에서 캐릭터를 추가/제거하면 PrepPanel에 자동 반영됩니다.
/// </summary>
[CreateAssetMenu(fileName = "CharacterRoster", menuName = "Characters/Character Roster")]
public class CharacterRoster : ScriptableObject
{
    public CharacterEntry[] characters;

    [Serializable]
    public class CharacterEntry
    {
        [Tooltip("캐릭터 스탯 데이터")]
        public CharacterData data;

        [Tooltip("선택 UI에 표시할 초상화")]
        public Sprite portrait;

        [Tooltip("GameRunBootstrapper에서 스폰할 Addressable 프리팹 키")]
        public string prefabKey;
    }
}
