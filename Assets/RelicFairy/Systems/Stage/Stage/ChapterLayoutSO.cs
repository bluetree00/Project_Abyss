using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 방 하나의 월드 배치 데이터.
/// 위치/크기/연결/리소스 키를 담는다. 런타임 상태는 저장하지 않는다.
/// </summary>
[Serializable]
public class RoomNodeData
{
    [Header("식별")]
    [Tooltip("방 인덱스 (0~11). Inspector 확인용으로도 쓰인다.")]
    public int          roomIndex;
    public RoomCategory category;
    [Tooltip("에디터에서 방을 구분하기 위한 라벨")]
    public string       label;

    [Header("월드 배치")]
    [Tooltip("방 중심 월드 좌표. Y=0 기준. 플랫폼이 이 위치로 솟아오른다.")]
    public Vector3 worldCenter;
    [Tooltip("그리드 열 수 (grid_csv width)")]
    public int     gridWidth  = 28;
    [Tooltip("그리드 행 수 (grid_csv height)")]
    public int     gridHeight = 28;

    [Header("연결 그래프")]
    [Tooltip("이 방에서 선택 가능한 다음 방 인덱스 목록")]
    public int[] nextRoomIndices;

    [Header("리소스 키")]
    [Tooltip("Addressable 주소 — StageData SO (grid_csv, 스포너 설정 포함)")]
    public string roomDataKey;

    [Header("플랫폼 연출")]
    [Tooltip("비활성 시 배리어 아래로 가라앉는 깊이 (m). 음수일수록 깊이 잠김.")]
    public float sinkDepth = 2f;
}

/// <summary>
/// 챕터 하나의 전체 지형 레이아웃 정의.
///
/// 12개 방이 다이아몬드 레이어(1-2-3-3-2-1)로 배치된다.
/// 배리어(수면/용암/안개/허공)가 월드를 채우고,
/// 플레이어가 방을 선택하면 해당 플랫폼이 sinkDepth에서 수면 위로 솟아오른다.
///
/// Chapter 1 기본 레이아웃 (Inspector에서 직접 입력):
/// ┌──────────────────────────────────────────┐
/// │           [11: Boss  ]  X=0   Z=280      │
/// │     [9:Elite]  [10:Battle] X=±55, Z=220  │
/// │  [6:Event][7:Battle][8:Shop] X=±110/0    │ Z=160
/// │  [3:Battle][4:Shop ][5:Elite] X=±110/0   │ Z=100
/// │      [1:Battle]  [2:Battle]  X=±55, Z=50 │
/// │             [0: Start ]  X=0  Z=0        │
/// └──────────────────────────────────────────┘
/// 전체 월드 규모: 약 260m × 300m (숲 강줄기 배리어 포함)
/// </summary>
[CreateAssetMenu(fileName = "NewChapterLayout", menuName = "Stage/Chapter Layout")]
public class ChapterLayoutSO : ScriptableObject
{
    // ─────────────────────────────────────────
    // SerializeField
    // ─────────────────────────────────────────

    [Header("챕터 식별")]
    [SerializeField] private ChapterId   _chapterId;
    [SerializeField] private BarrierType _barrierType;

    [Header("배리어 볼륨")]
    [Tooltip("Addressable 주소 — 수면/용암/안개/허공 볼륨 프리팹")]
    [SerializeField] private string _barrierPrefabKey;
    [Tooltip("배리어 평면의 Y 위치. 플랫폼 표면(Y=0)보다 약간 낮게 설정한다.")]
    [SerializeField] private float  _barrierSurfaceY = -0.1f;

    [Header("플랫폼 연출")]
    [Tooltip("플랫폼이 수면 위로 솟아오르는 시간 (초)")]
    [SerializeField] private float          _platformRiseDuration = 1.2f;
    [Tooltip("솟아오르는 커브. EaseIn 계열 권장 (둔탁하게 솟아올라 착지)")]
    [SerializeField] private AnimationCurve _platformRiseCurve    = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("방 배치 (12개, Index 0~11)")]
    [SerializeField] private List<RoomNodeData> _rooms = new();

    // ─────────────────────────────────────────
    // Properties
    // ─────────────────────────────────────────

    public ChapterId      ChapterId            => _chapterId;
    public BarrierType    BarrierType          => _barrierType;
    public string         BarrierPrefabKey     => _barrierPrefabKey;
    public float          BarrierSurfaceY      => _barrierSurfaceY;
    public float          PlatformRiseDuration => _platformRiseDuration;
    public AnimationCurve PlatformRiseCurve    => _platformRiseCurve;

    public IReadOnlyList<RoomNodeData> Rooms => _rooms;

    // ─────────────────────────────────────────
    // Public Methods
    // ─────────────────────────────────────────

    public RoomNodeData GetRoom(int index)
        => (uint)index < (uint)_rooms.Count ? _rooms[index] : null;

    /// <summary>roomIndex 기준으로 탐색. 인덱스 순서와 roomIndex 값이 다를 경우 대비.</summary>
    public RoomNodeData FindRoom(int roomIndex)
    {
        foreach (var r in _rooms)
            if (r.roomIndex == roomIndex) return r;
        return null;
    }

    /// <summary>특정 방에서 선택 가능한 다음 방 목록을 반환한다.</summary>
    public IEnumerable<RoomNodeData> GetNextRooms(int fromRoomIndex)
    {
        var from = FindRoom(fromRoomIndex);
        if (from?.nextRoomIndices == null) yield break;

        foreach (var idx in from.nextRoomIndices)
        {
            var node = FindRoom(idx);
            if (node != null) yield return node;
        }
    }
}
