using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Inputs
{
    public enum Command { Dodge, Skill, Heavy, Light }

    public interface IClock { float Now { get; } float Delta { get; } }
    public sealed class UnscaledClock : IClock
    {
        public float Now => Time.unscaledTime;
        public float Delta => Time.unscaledDeltaTime;
    }

    /// <summary>
    /// 입력 버퍼
    /// - 기존 기능 보존 (Push / TryConsume / TryConsumeAny / Clear / TickPrune)
    /// - 디버깅/관찰용 API 추가:
    ///   * EnableDebugLogging
    ///   * OnPushed / OnConsumed 이벤트
    ///   * GetEntries(): List&lt;EntryInfo&gt;
    ///   * DebugDump(): 버퍼 상태 로그 출력
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
                Command.Dodge => 3,
                Command.Skill => 2,
                Command.Heavy => 1,
                _ => 0 // Light
            });
        }

        // --------------------------
        // 기본 동작 (원래 API)
        // --------------------------
        public void TickPrune()
        {
            float now = _clock.Now;
            bool anyPruned = false;
            while (_count > 0)
            {
                ref var e = ref _buf[_head];
                if (now - e.t <= _windowSec) break;
                // 로그/이벤트: 오래된 항목 제거
                if (EnableDebugLogging) Debug.Log($"[InputBuffer] Prune expired: {e.cmd} age={(now - e.t):F3}s");
                PopFront();
                anyPruned = true;
            }

            if (anyPruned && EnableDebugLogging) Debug.Log($"[InputBuffer] After prune count={_count}");
        }

        public void Push(Command c)
        {
            float now = _clock.Now;
            TickPrune();

            // 연속 중복 억제
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

        // --------------------------
        // 내부 유틸
        // --------------------------
        private void PopFront()
        {
            if (_count == 0) return;
            _head = (_head + 1) % _buf.Length;
            _count--;
        }

        private void RemoveAt(int pos)
        {
            if (_count == 0) return;
            // 기록될 항목을 로그(디버그용)
            if (EnableDebugLogging)
            {
                var ev = _buf[pos];
                Debug.Log($"[InputBuffer] RemoveAt pos={pos} cmd={ev.cmd} pri={ev.pri}");
            }

            int i = pos;
            while (i != _tail)
            {
                int next = (i + 1) % _buf.Length;
                _buf[i] = _buf[next];
                i = next;
            }
            _tail = (_tail - 1 + _buf.Length) % _buf.Length;
            _count--;
        }
    }
}
