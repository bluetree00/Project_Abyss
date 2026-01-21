using System.Collections.Generic;


    /// <summary>
    /// 하나의 StagePoint(노드)에 대한 순수 데이터 컨텍스트
    /// - 방 타입 결정 결과 보관
    /// - 그래프 연결 정보 보관
    /// - UI, MonoBehaviour와 완전히 분리
    /// </summary>
    public class StagePointContext
    {
        // -------------------------
        // 기본 정보
        // -------------------------

        public int PointId { get; }
        public StageCategory StageCategory { get; }

        /// <summary>
        /// 다음으로 이동 가능한 포인트 ID 목록
        /// </summary>
        public IReadOnlyList<int> NextPointIds => _nextPointIds;
        private readonly List<int> _nextPointIds;

        // -------------------------
        // Normal 전용 정보
        // -------------------------

        public NormalRoomCategory NormalRoomCategory { get; }

        // -------------------------
        // 결정된 방 정보 (Resolve 결과)
        // -------------------------

        /// <summary>
        /// 실제로 선택된 방 이름 enum
        /// (Normal + Random이면 BattleRoomName / EliteRoomName 등 중 하나)
        /// </summary>
        public object ResolvedRoomName { get; private set; }

        /// <summary>
        /// 방이 결정되었는지 여부
        /// </summary>
        public bool IsResolved => ResolvedRoomName != null;

        // -------------------------
        // 생성자
        // -------------------------

        public StagePointContext(
            int pointId,
            StageCategory stageCategory,
            IEnumerable<int> nextPointIds,
            NormalRoomCategory normalRoomCategory = NormalRoomCategory.Random)
        {
            PointId = pointId;
            StageCategory = stageCategory;
            NormalRoomCategory = normalRoomCategory;
            _nextPointIds = new List<int>(nextPointIds);
        }

        // -------------------------
        // Resolve 결과 설정
        // -------------------------

        /// <summary>
        /// StagePointManager가 방을 결정한 후 호출
        /// </summary>
        public void SetResolvedRoom(object roomNameEnum)
        {
            if (IsResolved)
            {
                UnityEngine.Debug.LogWarning(
                    $"[StagePointContext] 이미 Resolve된 PointId: {PointId}");
                return;
            }

            ResolvedRoomName = roomNameEnum;
        }

        // -------------------------
        // 세이브 데이터
        // -------------------------

        // public StagePointContextSaveData CreateSaveData()
        // {
        //     return new StagePointContextSaveData
        //     {
        //         PointId = PointId,
        //         StageCategory = StageCategory,
        //         NormalRoomCategory = NormalRoomCategory,
        //         ResolvedRoomName = ResolvedRoomName?.ToString()
        //     };
        // }
    }

