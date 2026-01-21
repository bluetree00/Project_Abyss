using System.Collections.Generic;
using UnityEngine;


    /// <summary>
    /// 스테이지 포인트(노드)들의 방 결정을 담당
    /// - UI, MonoBehaviour 의존 없음
    /// - StagePointContext만 관리
    /// - 한 GameRun 동안만 생존
    /// </summary>
    public class StagePointManager
    {
        // =========================
        // Chapter Context
        // =========================
        public ChapterId CurrentChapter { get; private set; }

        // =========================
        // Stage Contexts
        // pointId -> StagePointContext
        // =========================
        private readonly Dictionary<int, StagePointContext> _contexts = new();

        // =========================
        // Initialize
        // =========================
        public void Initialize(ChapterId chapter)
        {
            CurrentChapter = chapter;
            _contexts.Clear();
        }

        public void Clear()
        {
            _contexts.Clear();
        }

        // =========================
        // Registration
        // =========================

        /// <summary>
        /// UI 또는 외부에서 StagePoint 정보를 Context로 등록
        /// </summary>
        public StagePointContext Register(
            int pointId,
            StageCategory stageCategory,
            IReadOnlyList<int> nextPointIds,
            NormalRoomCategory normalRoomCategory = NormalRoomCategory.Random)
        {
            if (_contexts.ContainsKey(pointId))
            {
                Debug.LogWarning($"[StagePointManager] Duplicate pointId: {pointId}");
                return _contexts[pointId];
            }

            var context = new StagePointContext(
                pointId,
                stageCategory,
                nextPointIds,
                normalRoomCategory
            );

            _contexts.Add(pointId, context);
            return context;
        }

        public StagePointContext GetContext(int pointId)
        {
            return _contexts.TryGetValue(pointId, out var context)
                ? context
                : null;
        }

        // =========================
        // Resolve
        // =========================

        public void ResolveAll()
        {
            foreach (var context in _contexts.Values)
            {
                Resolve(context);
            }
        }

        public void Resolve(StagePointContext context)
        {
            if (context == null || context.IsResolved)
                return;

            object resolvedRoom = context.StageCategory switch
            {
                StageCategory.Start => ResolveStartStage(),
                StageCategory.Normal => ResolveNormalStage(context.NormalRoomCategory),
                StageCategory.Boss => ResolveBossStage(),
                _ => null
            };

            if (resolvedRoom == null)
            {
                Debug.LogError($"[StagePointManager] Failed to resolve pointId {context.PointId}");
                return;
            }

            context.SetResolvedRoom(resolvedRoom);
        }

        // =========================
        // Resolver Methods
        // =========================

        private object ResolveStartStage()
        {
            return $"Start_{CurrentChapter}";
        }

        private object ResolveBossStage()
        {
            return $"Boss_{CurrentChapter}";
        }

        private object ResolveNormalStage(NormalRoomCategory category)
        {
            if (category == NormalRoomCategory.Random)
            {
                category = GetRandomNormalCategory();
            }

            return category switch
            {
                NormalRoomCategory.Battle => GetRandomBattleRoom(),
                NormalRoomCategory.Elite => GetRandomEliteRoom(),
                NormalRoomCategory.Special => GetRandomSpecialRoom(),
                NormalRoomCategory.Shop => GetRandomShopRoom(),
                _ => null
            };
        }

        // =========================
        // Random Helpers (임시)
        // =========================

        private NormalRoomCategory GetRandomNormalCategory()
        {
            NormalRoomCategory[] values =
            {
                NormalRoomCategory.Battle,
                NormalRoomCategory.Elite,
                NormalRoomCategory.Special,
                NormalRoomCategory.Shop
            };

            return values[Random.Range(0, values.Length)];
        }

        private BattleRoomName GetRandomBattleRoom()
        {
            BattleRoomName[] values =
            {
                BattleRoomName.SlimeForest,
                BattleRoomName.GoblinCamp,
                BattleRoomName.UndeadCrypt
            };

            return values[Random.Range(0, values.Length)];
        }

        private EliteRoomName GetRandomEliteRoom()
        {
            EliteRoomName[] values =
            {
                EliteRoomName.EliteGoblin,
                EliteRoomName.EliteKnight
            };

            return values[Random.Range(0, values.Length)];
        }

        private SpecialRoomName GetRandomSpecialRoom()
        {
            SpecialRoomName[] values =
            {
                SpecialRoomName.TreasureRoom,
                SpecialRoomName.EventRoom
            };

            return values[Random.Range(0, values.Length)];
        }

        private ShopRoomName GetRandomShopRoom()
        {
            ShopRoomName[] values =
            {
                ShopRoomName.NormalShop,
                ShopRoomName.RareShop
            };

            return values[Random.Range(0, values.Length)];
        }

        // =========================
        // Save / Load
        // =========================

        public void ApplySavedStage(int pointId, object resolvedRoom)
        {
            if (_contexts.TryGetValue(pointId, out var context))
            {
                context.SetResolvedRoom(resolvedRoom);
            }
        }
    }

