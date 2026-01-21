using System.Collections.Generic;
using UnityEngine;


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
            Debug.Log($"[GameRunManager] Start New Run - {chapter}");

            IsRunning = true;
            CurrentChapter = chapter;

            // StagePointManager 생성
            StagePointManager = new StagePointManager();
            StagePointManager.Initialize(chapter);

        }
        
    }
