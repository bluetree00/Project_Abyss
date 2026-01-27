using System.Collections.Generic;

    /// <summary>
    /// 한 번의 게임 런에서 사용되는 스테이지 전체 상태 컨텍스트
    /// - 챕터 정보
    /// - 모든 StagePointContext 관리
    /// - 현재 선택된 포인트 관리
    /// </summary>
    public class StageContext
    {
        /// <summary>
        /// 현재 챕터
        /// </summary>
        public ChapterId CurrentChapter { get; private set; }

        /// <summary>
        /// 현재 플레이어가 위치한 StagePoint ID
        /// </summary>
        public int CurrentPointId { get; private set; }

        /// <summary>
        /// 모든 스테이지 포인트 컨텍스트
        /// </summary>
        private readonly Dictionary<int, StagePointContext> _pointContexts
            = new Dictionary<int, StagePointContext>();

        public IReadOnlyDictionary<int, StagePointContext> PointContexts
            => _pointContexts;

        // -------------------------
        // 초기화
        // -------------------------

        public StageContext(ChapterId chapter)
        {
            CurrentChapter = chapter;
        }

        // -------------------------
        // Point 관리
        // -------------------------

        public void RegisterPoint(StagePointContext pointContext)
        {
            if (_pointContexts.ContainsKey(pointContext.PointId))
            {
                UnityEngine.Debug.LogWarning(
                    $"[StageContext] 중복된 PointId 등록 시도: {pointContext.PointId}");
                return;
            }

            _pointContexts.Add(pointContext.PointId, pointContext);
        }

        public StagePointContext GetPoint(int pointId)
        {
            _pointContexts.TryGetValue(pointId, out var context);
            return context;
        }

        // -------------------------
        // 진행 관리
        // -------------------------

        public void SetCurrentPoint(int pointId)
        {
            if (!_pointContexts.ContainsKey(pointId))
            {
                UnityEngine.Debug.LogError(
                    $"[StageContext] 존재하지 않는 PointId 접근: {pointId}");
                return;
            }

            CurrentPointId = pointId;
        }

        public StagePointContext GetCurrentPoint()
        {
            return GetPoint(CurrentPointId);
        }

        // -------------------------
        // 세이브 / 로드 지원
        // -------------------------

        // public StageContextSaveData CreateSaveData()
        // {
        //     return new StageContextSaveData
        //     {
        //         Chapter = CurrentChapter,
        //         CurrentPointId = CurrentPointId,
        //         PointSaveDataList = CreatePointSaveDataList()
        //     };
        // }

        // private List<StagePointContextSaveData> CreatePointSaveDataList()
        // {
        //     var list = new List<StagePointContextSaveData>();
        //     foreach (var point in _pointContexts.Values)
        //     {
        //         list.Add(point.CreateSaveData());
        //     }
        //     return list;
        // }
    }

