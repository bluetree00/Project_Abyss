using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Inputs
{
    public enum Command { None, Dodge, Heavy, Light, Charge, QSkill, ESkill }

    public interface IClock { float Now { get; } float Delta { get; } }
    public sealed class UnscaledClock : IClock
    {
        public float Now => Time.unscaledTime;
        public float Delta => Time.unscaledDeltaTime;
    }

    /// <summary>
    /// 입력 버퍼
    /// - Push / TryConsume / TryConsumeAny / Clear / TickPrune 제공
    /// - 디버깅/관찰 API:
    ///   * EnableDebugLogging
    ///   * OnPushed / OnConsumed 이벤트
    ///   * GetEntries() => List<EntryInfo>
    ///   * DebugDump()
    ///   * HasRecent(Command, withinSec)
    ///   * RemoveAll(Command)
    ///   * PeekLast(out Command)
    /// </summary>
    public sealed class InputBuffer
    {
        // 내부 저장용 엔트리
        private struct Entry { public Command cmd; public float t; public byte pri; }

        // 외부에 노출할 읽기 전용 엔트리 정보
        public struct EntryInfo
        {
            public Command Command;
            public float InsertTime;    // clock.Now at insertion
            public byte Priority;
            public float Age;           // clock.Now - InsertTime

            public override string ToString() => $"[{Command}] pri={Priority} age={Age:F3}s insertedAt={InsertTime:F3}";
        }

        private readonly Entry[] _buf;
        private int _head, _tail, _count;

        private readonly IClock _clock;
        private readonly float _windowSec;   // 입력 유효시간(초)
        private readonly float _dedupeSec;   // 동일 커맨드 연속 억제(초)
        private readonly Func<Command, byte> _priority;

        // 디버그/관찰
        public bool EnableDebugLogging { get; set; } = false;
        public event Action<Command> OnPushed;
        public event Action<Command> OnConsumed;
        public int Count => _count;

        public InputBuffer(
            IClock clock,
            int capacity = 16,
            float bufferWindowSec = 0.18f,
            float dedupeSec = 0.04f,
            Func<Command, byte> priority = null)
        {
            _clock = clock ?? new UnscaledClock();
            _windowSec = bufferWindowSec;
            _dedupeSec = dedupeSec;
            _buf = new Entry[Mathf.Max(4, capacity)];
            _priority = priority ?? (c => c switch
            {
                Command.Dodge => 4,
                Command.QSkill => 3,
                Command.ESkill => 3,
                Command.Heavy => 2,
                Command.Charge => 1,   // <-- 추가: Charge는 낮은 우선순위
                _ => 0 // Light, None...
            });

        }

        // --------------------------
        // 기본 동작 (원래 API)
        // --------------------------
        /// <summary>만료된(윈도우 초과) 항목을 제거합니다.</summary>
        public void TickPrune()
        {
            float now = _clock.Now;
            bool anyPruned = false;
            while (_count > 0)
            {
                ref var e = ref _buf[_head];
                if (now - e.t <= _windowSec) break;
                if (EnableDebugLogging) Debug.Log($"[InputBuffer] Prune expired: {e.cmd} age={(now - e.t):F3}s");
                PopFront();
                anyPruned = true;
            }

            if (anyPruned && EnableDebugLogging) Debug.Log($"[InputBuffer] After prune count={_count}");
        }

        /// <summary>새 입력을 푸시합니다. 용량 초과 시 우선순위가 낮은 항목을 희생하거나 새 입력을 버립니다.</summary>
        public void Push(Command c)
        {
            float now = _clock.Now;
            TickPrune();

            // 연속 중복 억제 (가장 최근 항목과 비교)
            if (_count > 0)
            {
                int last = (_tail - 1 + _buf.Length) % _buf.Length;
                if (_buf[last].cmd == c && (now - _buf[last].t) <= _dedupeSec)
                {
                    if (EnableDebugLogging) Debug.Log($"[InputBuffer] Dedupe ignored: {c} (last age={(now - _buf[last].t):F3}s)");
                    return;
                }
            }

            // 용량 가득 → 낮은 우선순위 희생
            if (_count == _buf.Length)
            {
                byte pNew = _priority(c);
                int victim = -1;
                int idx = _head;
                for (int i = 0; i < _count; i++)
                {
                    if (_buf[idx].pri < pNew) { victim = idx; break; }
                    idx = (idx + 1) % _buf.Length;
                }
                if (victim >= 0)
                {
                    var ev = _buf[victim];
                    if (EnableDebugLogging) Debug.Log($"[InputBuffer] Evicting lower-priority: {ev.cmd} (pri={ev.pri}) for new {c} (pri={pNew})");
                    RemoveAt(victim);
                }
                else
                {
                    if (EnableDebugLogging) Debug.Log($"[InputBuffer] Buffer full and no lower priority, drop new {c}");
                    return;
                }
            }

            _buf[_tail] = new Entry { cmd = c, t = now, pri = _priority(c) };
            _tail = (_tail + 1) % _buf.Length;
            _count++;

            if (EnableDebugLogging) Debug.Log($"[InputBuffer] Push {c} (pri={_priority(c)}) now count={_count}");
            OnPushed?.Invoke(c);
        }

        /// <summary>헤드 항목이 원하는 커맨드면 소비하고 true 반환</summary>
        public bool TryConsume(Command want)
        {
            TickPrune();
            if (_count == 0) return false;
            ref var e = ref _buf[_head];
            if (e.cmd == want)
            {
                PopFront();
                if (EnableDebugLogging) Debug.Log($"[InputBuffer] TryConsume consumed {want}");
                OnConsumed?.Invoke(want);
                return true;
            }
            if (EnableDebugLogging) Debug.Log($"[InputBuffer] TryConsume did not find {want} at head (head={_buf[_head].cmd})");
            return false;
        }

        /// <summary>버퍼에서 wants 목록 중 첫 일치 항목을 찾아 제거(순서 보존)하고 true 반환</summary>
        public bool TryConsumeAny(params Command[] wants)
        {
            TickPrune();
            if (_count == 0 || wants == null || wants.Length == 0) return false;

            float now = _clock.Now;
            int idx = _head;
            for (int i = 0; i < _count; i++)
            {
                if (now - _buf[idx].t > _windowSec) { idx = (idx + 1) % _buf.Length; continue; }
                for (int w = 0; w < wants.Length; w++)
                {
                    if (_buf[idx].cmd == wants[w])
                    {
                        var consumed = _buf[idx].cmd;
                        RemoveAt(idx);
                        if (EnableDebugLogging) Debug.Log($"[InputBuffer] TryConsumeAny consumed {consumed}");
                        OnConsumed?.Invoke(consumed);
                        return true;
                    }
                }
                idx = (idx + 1) % _buf.Length;
            }
            if (EnableDebugLogging) Debug.Log("[InputBuffer] TryConsumeAny found none");
            return false;
        }

        public void Clear()
        {
            _head = _tail = _count = 0;
            if (EnableDebugLogging) Debug.Log("[InputBuffer] Cleared");
        }

        // --------------------------
        // 디버그/관찰 API
        // --------------------------

        /// <summary>현재 버퍼의 스냅샷을 반환합니다 (읽기 전용 리스트)</summary>
        public List<EntryInfo> GetEntries()
        {
            var list = new List<EntryInfo>(_count);
            float now = _clock.Now;
            int idx = _head;
            for (int i = 0; i < _count; i++)
            {
                var e = _buf[idx];
                list.Add(new EntryInfo
                {
                    Command = e.cmd,
                    InsertTime = e.t,
                    Priority = e.pri,
                    Age = now - e.t
                });
                idx = (idx + 1) % _buf.Length;
            }
            return list;
        }

        /// <summary>버퍼 내용을 Debug.Log로 출력합니다.</summary>
        public void DebugDump(string title = "InputBuffer Dump")
        {
            var entries = GetEntries();
            Debug.Log($"--- {title} (count={entries.Count}) ---");
            for (int i = 0; i < entries.Count; i++)
            {
                Debug.Log($"[{i}] {entries[i]}");
            }
            Debug.Log($"--- end dump ---");
        }

        /// <summary>버퍼에 특정 커맨드가 포함되어 있는지 검사</summary>
        public bool Contains(Command c)
        {
            int idx = _head;
            for (int i = 0; i < _count; i++)
            {
                if (_buf[idx].cmd == c) return true;
                idx = (idx + 1) % _buf.Length;
            }
            return false;
        }

        /// <summary>맨 앞 항목을 확인(소비하지 않음). false면 out값 무시.</summary>
        public bool Peek(out Command cmd)
        {
            if (_count == 0) { cmd = default; return false; }
            cmd = _buf[_head].cmd;
            return true;
        }

        /// <summary>가장 최근(마지막) 항목 확인(소비하지 않음)</summary>
        public bool PeekLast(out Command cmd)
        {
            if (_count == 0) { cmd = default; return false; }
            int last = (_tail - 1 + _buf.Length) % _buf.Length;
            cmd = _buf[last].cmd;
            return true;
        }

        /// <summary>
        /// 최근에 해당 커맨드가 삽입되었는지 검사.
        /// withinSec: 검색할 시간 범위(초). 기본은 dedupeSec.
        /// 뒤에서(가장 최근 항목부터) 검사하므로 빠릅니다.
        /// </summary>
        public bool HasRecent(Command c, float withinSec = -1f)
        {
            if (_count == 0) return false;
            if (withinSec <= 0f) withinSec = _dedupeSec;

            float now = _clock.Now;
            int idx = (_tail - 1 + _buf.Length) % _buf.Length;
            for (int i = 0; i < _count; i++)
            {
                var e = _buf[idx];
                if (now - e.t <= withinSec)
                {
                    if (e.cmd == c) return true;
                }
                else
                {
                    // 더 오래된 항목이면 뒤로 갈 필요 없음 (시간순 삽입 가정)
                    break;
                }
                idx = (idx - 1 + _buf.Length) % _buf.Length;
            }
            return false;
        }

        /// <summary>
        /// 버퍼에 있는 특정 커맨드를 모두 제거합니다.
        /// 안전하게 재구성하여 순서를 보존합니다.
        /// </summary>
        public void RemoveAll(Command c)
        {
            if (_count == 0) return;

            var temp = new List<Entry>(_count);
            int idx = _head;
            for (int i = 0; i < _count; i++)
            {
                var e = _buf[idx];
                if (e.cmd != c) temp.Add(e);
                idx = (idx + 1) % _buf.Length;
            }

            // 재구성
            int newCap = _buf.Length;
            Array.Clear(_buf, 0, _buf.Length);
            for (int i = 0; i < temp.Count; i++)
            {
                _buf[i] = temp[i];
            }
            _head = 0;
            _count = temp.Count;
            _tail = _count % _buf.Length;

            if (EnableDebugLogging) Debug.Log($"[InputBuffer] RemoveAll {c} done, newCount={_count}");
        }

        // --------------------------
        // 내부 유틸
        // --------------------------
        private void PopFront()
        {
            if (_count == 0) return;
            // NOTE: PopFront does not raise OnConsumed because Prune is not a deliberate consumption.
            _head = (_head + 1) % _buf.Length;
            _count--;
        }

        /// <summary>pos 위치 항목을 제거(순서 보존). pos는 내부 인덱스(버퍼 배열 인덱스)</summary>
        private void RemoveAt(int pos)
        {
            if (_count == 0) return;

            if (EnableDebugLogging)
            {
                var ev = _buf[pos];
                Debug.Log($"[InputBuffer] RemoveAt pos={pos} cmd={ev.cmd} pri={ev.pri}");
            }

            int i = pos;
            while (i != _tail)
            {
                int next = (i + 1) % _buf.Length;
                // next might equal tail (past-last) — in that case loop will assign tail slot to current pos,
                // but that's intended because we'll move tail back afterwards.
                _buf[i] = _buf[next];
                i = next;
            }
            // move tail backward
            _tail = (_tail - 1 + _buf.Length) % _buf.Length;
            _count--;
        }
    }
}
