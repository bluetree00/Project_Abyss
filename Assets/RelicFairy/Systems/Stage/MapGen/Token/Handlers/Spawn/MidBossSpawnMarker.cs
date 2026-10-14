using UnityEngine;

/// <summary>
/// 정예 보스 스포너 표식(10-12 「관문지기」) — <see cref="MidBossSpawnHandler"/>가 MB 토큰 자리 스포너에 붙인다.
/// <see cref="RoomWaveController"/>가 이 표식이 있는 스포너에서 <see cref="MonsterSpawner.SpawnMidBossAsync"/>를 방에서 한 번만 부른다.
/// </summary>
public sealed class MidBossSpawnMarker : MonoBehaviour
{
    [SerializeField] private string kind;

    /// <summary>주소 폴더 이름(예: BullTank) — 스포너가 챕터로 <c>{Kind}/{Kind}_Ch{n}</c>을 고른다.</summary>
    public string Kind => kind;

    public void Init(string value) => kind = value;
}
