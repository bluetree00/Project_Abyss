using System.Collections.Generic;
using UnityEngine;
using System.Linq;


    /// <summary>
    /// 하나의 게임 Run(인게임 세션)을 관리하는 매니저
    /// - 챕터 / 스테이지 진행
    /// - StagePointManager, StageContext 생명주기 관리
    /// - 전역 Managers 아래의 "하위 런 매니저"
    /// </summary>
    public class GameRunManager
    {
        // -------------------------
        // 상태
        // -------------------------
        public bool IsRunning { get; private set; }

        public ChapterId CurrentChapter { get; private set; }

         // -------------------------
        // 캐시된 Stage Data
        // -------------------------
        private Dictionary<int, StageData> stageDataCache;
        // -------------------------
        // 하위 매니저
        // -------------------------

        public StagePointManager StagePointManager { get; private set; }
        public StagePointContext CurrentStagePoint { get; private set; }

        // -------------------------
        // 생성 / 종료
        // -------------------------

        /// <summary>
        /// 새 게임 시작 (New Run)
        /// </summary>
        public void StartNewRun(ChapterId chapter)
        {

            IsRunning = true;
            CurrentChapter = chapter;

            LoadStageData();

            StagePointManager = new StagePointManager();
            StagePointManager.Initialize(chapter);
        
            var points = Object.FindObjectsOfType<StagePointUI>();
            foreach (var ui in points)
            {
                ui.Register(StagePointManager);
            }

            StagePointManager.ResolveAll();

            CurrentStagePoint = StagePointManager.GetStartPoint();

            if (CurrentStagePoint == null)
            {
                Debug.LogError("[GameRun] Start point not found");
            }
            else
            {
                Debug.Log($"[GameRun] Start at Point {CurrentStagePoint.PointId}");
            }


        }


        public bool TryMoveToStage(int targetPointId)
        {
            // 1. 현재 위치 기준으로만 판단
            if (!CurrentStagePoint.NextPointIds.Contains(targetPointId))
                return false;

            // 2. 딱 필요한 것만 조회
            var next = StagePointManager.GetContext(targetPointId);
            if (next == null)
                return false;

            // 3. 이동
            CurrentStagePoint = next;
            return true;
        }


        private void LoadStageData()
        {
            var textAsset = Resources.Load<TextAsset>("Data/STAGEDATA");

            if (textAsset == null)
            {
                Debug.LogError("[GameRun] STAGEDATA.json not found");
                return;
            }

            var root = JsonUtility.FromJson<StageDataRoot>(textAsset.text);

            stageDataCache = root.stages.ToDictionary(s => s.stageId, s => s);

            Debug.Log($"[GameRun] StageData Loaded: {stageDataCache.Count}");
        }



        
    }
