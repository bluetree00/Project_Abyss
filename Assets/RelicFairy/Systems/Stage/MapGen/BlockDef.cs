using UnityEngine;

/// <summary>
/// 블록 1종 정의. 타일 역할 + 프리팹 + 배치 규칙.
/// BlockPalette에 등록하여 사용.
/// </summary>
[CreateAssetMenu(menuName = "Map/BlockDef")]
public class BlockDef : ScriptableObject
{
    [Header("프리팹")]
    public GameObject prefab;

    [Header("타일 역할")]
    public TileType tileType = TileType.Floor;

    [Header("방향")]
    public FacingRule facingRule = FacingRule.None;

    [Header("배치 보정 (건축 프롭 등 피벗·크기가 셀과 안 맞을 때)")]
    [Tooltip("인스턴스 로컬 스케일. 기둥 등 임의 크기 메시를 셀에 맞출 때. 기본 (1,1,1).")]
    public Vector3 localScale = Vector3.one;

    [Tooltip("배치 후 로컬 위치 보정(월드 단위). 피벗이 중앙인 메시를 바닥에 앉힐 때 Y로 올림. 기본 (0,0,0).")]
    public Vector3 localOffset = Vector3.zero;

    [Header("선택 가중치 (같은 tileType 내에서)")]
    [Min(1)] public int weight = 1;
}

/// <summary>블록 배치 시 자동 회전 규칙.</summary>
public enum FacingRule
{
    None,           // 회전 안 함
    FaceCenter,     // 맵 중앙을 향함 (벽)
    FaceOutward,    // 맵 바깥을 향함 (출구)
    Random,         // 랜덤 Y 회전
}
