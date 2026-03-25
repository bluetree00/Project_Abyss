using System;
using System.Collections.Generic;
using UnityEngine;

namespace Abyss.Monster
{
    /// <summary>
    /// 가중치 기반 랜덤 패턴 선택기.
    ///
    /// ─ 동작 ─────────────────────────────────────────────────
    /// • 실행 가능(CanExecute == true)한 패턴들만 후보로 수집
    /// • 각 패턴의 유효 가중치 = BaseWeight × (최근 사용 여부에 따른 페널티)
    /// • 가중치 룰렛으로 1개 선택 → BTFSMActionNode 를 통해 FSM 진입
    /// • 패턴이 Running 동안 매 프레임 같은 노드를 계속 틱
    /// • 패턴 종료(Success) → _running 초기화 → 다음 틱에서 재선택
    ///
    /// ─ 페널티 ────────────────────────────────────────────────
    /// • 직전에 사용한 패턴은 penaltyDuration 초 동안 weight * penaltyMult 로 낮아짐
    /// • penaltyMult = 0 이면 해당 시간 동안 완전 차단
    /// </summary>
    public class BKWeightedPatternSelector : BTNode
    {
        // ─── 내부 엔트리 ──────────────────────────────────────
        private sealed class Entry
        {
            public readonly string                     Name;
            public readonly Func<MonsterContext, bool> CanExecute;
            public readonly BTNode                     Action;       // BTFSMActionNode
            public readonly float                      BaseWeight;
            public float                               LastUsedTime = float.MinValue;

            public Entry(string name,
                         Func<MonsterContext, bool> canExecute,
                         BTNode action,
                         float  baseWeight)
            {
                Name       = name;
                CanExecute = canExecute;
                Action     = action;
                BaseWeight = Mathf.Max(0f, baseWeight);
            }
        }

        // ─── 상태 ─────────────────────────────────────────────
        private readonly List<Entry>                 _entries        = new();
        private readonly List<(Entry e, float w)>    _candidates     = new();   // 매 선택마다 재사용
        private readonly float                       _penaltyDuration;
        private readonly float                       _penaltyMult;
        private Entry                                _running;

        // 강제 선택 (페이즈 인터럽트 등)
        private string _forcedName;
        private bool   _forceSkipCondition;

        // ─────────────────────────────────────────────────────
        public BKWeightedPatternSelector(float penaltyDuration, float penaltyMult)
        {
            _penaltyDuration = Mathf.Max(0f, penaltyDuration);
            _penaltyMult     = Mathf.Clamp01(penaltyMult);
        }

        /// <summary>패턴 등록. 등록 순서는 가중치에 영향 없음.</summary>
        public void Add(string                     name,
                        Func<MonsterContext, bool> canExecute,
                        BTNode                     action,
                        float                      weight)
            => _entries.Add(new Entry(name, canExecute, action, weight));

        /// <summary>
        /// 다음 유휴 시점에 지정 패턴을 강제 선택한다 (가중치/페널티 무시).
        /// skipCondition=true 이면 CanExecute 조건도 건너뜀 (페이즈 인터럽트용).
        /// 패턴이 현재 실행 중이면 종료 직후 다음 선택에 적용된다.
        /// </summary>
        public void ForceNext(string name, bool skipCondition = false)
        {
            _forcedName         = name;
            _forceSkipCondition = skipCondition;
        }

        // ─────────────────────────────────────────────────────
        public override BTStatus Tick(MonsterContext ctx)
        {
            // ── 패턴 실행 중: 완료까지 계속 틱 ─────────────
            if (_running != null)
            {
                var s = _running.Action.Tick(ctx);
                if (s == BTStatus.Running) return BTStatus.Running;

                _running = null;
                return BTStatus.Success;
            }

            // ── 강제 선택 처리 (페이즈 인터럽트) ────────────
            if (_forcedName != null)
            {
                string fname = _forcedName;
                bool   fskip = _forceSkipCondition;
                _forcedName         = null;
                _forceSkipCondition = false;

                foreach (var e in _entries)
                {
                    if (e.Name != fname) continue;
                    if (!fskip && !e.CanExecute(ctx)) break; // 조건 불충족 → 일반 선택으로

                    e.LastUsedTime = Time.time;
                    e.Action.Reset();
                    _running = e;

                    var fs = e.Action.Tick(ctx);
                    if (fs == BTStatus.Running) return BTStatus.Running;
                    _running = null;
                    return BTStatus.Success;
                }
                // 강제 대상 실행 불가 → fall-through 일반 가중치 선택
            }

            // ── 후보 수집 + 유효 가중치 계산 ────────────────
            _candidates.Clear();
            float totalWeight = 0f;
            float now         = Time.time;

            foreach (var e in _entries)
            {
                if (!e.CanExecute(ctx)) continue;

                float elapsed = now - e.LastUsedTime;
                float w = elapsed < _penaltyDuration
                    ? e.BaseWeight * _penaltyMult
                    : e.BaseWeight;

                if (w <= 0f) continue;

                _candidates.Add((e, w));
                totalWeight += w;
            }

            if (_candidates.Count == 0) return BTStatus.Failure;

            // ── 가중치 룰렛 선택 ─────────────────────────────
            float roll       = UnityEngine.Random.Range(0f, totalWeight);
            float cumulative = 0f;
            Entry chosen     = _candidates[0].e;

            foreach (var (e, w) in _candidates)
            {
                cumulative += w;
                if (roll <= cumulative) { chosen = e; break; }
            }

            // ── 선택된 패턴 진입 ─────────────────────────────
            chosen.LastUsedTime = now;
            chosen.Action.Reset();
            _running = chosen;

            var initial = chosen.Action.Tick(ctx);
            if (initial == BTStatus.Running) return BTStatus.Running;

            _running = null;
            return BTStatus.Success;
        }

        public override void Reset()
        {
            _running = null;
            foreach (var e in _entries)
                e.Action.Reset();
        }
    }
}
