using System;
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
    /// - 고정 크기 링 버퍼
    /// - 만료(window), 우선순위, 중복 억제(dedupe) 지원
    /// - TryConsume(특정 커맨드), TryConsumeAny(우선순위 집합) 제공
    /// </summary>
    public sealed class InputBuffer
    {
        private struct Entry { public Command cmd; public float t; public byte pri; }

        private readonly Entry[] _buf;
        private int _head = 0, _tail = 0, _count = 0;

        private readonly IClock _clock;
        private readonly float _window;   // 유효 시간(초) 0.12~0.20 추천
        private readonly float _dedupeMs; // 동일 커맨드 연타 억제(ms)
        private readonly Func<Command, byte> _priority;

        /// <param name="capacity">버퍼 용량(16 추천)</param>
        /// <param name="bufferWindowSec">입력 유효시간(초)</param>
        /// <param name="dedupeMs">동일 커맨드 연속 입력 억제(ms)</param>
        /// <param name="priority">우선순위 함수(값이 클수록 우선)</param>
        public InputBuffer(
            IClock clock,
            int capacity = 16,
            float bufferWindowSec = 0.18f,
            float dedupeMs = 0.04f,
            Func<Command, byte> priority = null)
        {
            _clock = clock ?? new UnscaledClock();
            _window = bufferWindowSec;
            _dedupeMs = dedupeMs;
            _buf = new Entry[Mathf.Max(4, capacity)];
            _priority = priority ?? (c => c switch
            {
                Command.Dodge => 3,
                Command.Skill => 2,
                Command.Heavy => 1,
                _ => 0 // Light
            });
        }

        /// <summary>프레임당 1회 호출(만료 입력 일괄 정리)</summary>
        public void TickPrune()
        {
            float now = _clock.Now;
            while (_count > 0)
            {
                ref var e = ref _buf[_head];
                if (now - e.t <= _window) break;
                PopFront();
            }
        }

        /// <summary>입력 Push(콜백에서 호출). 실행은 하지 않음.</summary>
        public void Push(Command c)
        {
            float now = _clock.Now;
            TickPrune();

            // 중복 억제: 마지막 입력과 동일 커맨드가 매우 근접하면 무시
            if (_count > 0)
            {
                int lastIdx = (_tail - 1 + _buf.Length) % _buf.Length;
                ref var last = ref _buf[lastIdx];
                if (last.cmd == c && (now - last.t) * 1000f <= _dedupeMs) return;
            }

            // 용량 초과 시 퇴거 정책
            if (_count == _buf.Length)
            {
                byte pNew = _priority(c);
                int victim = -1;
                int idx = _head;
                // 가장 앞쪽부터 "우선순위 낮은" 항목을 희생
                for (int i = 0; i < _count; i++)
                {
                    if (_buf[idx].pri < pNew) { victim = idx; break; }
                    idx = (idx + 1) % _buf.Length;
                }
                if (victim >= 0) RemoveAt(victim);
                else return; // 전부 동급 이상 → 새 입력 버림
            }

            _buf[_tail] = new Entry { cmd = c, t = now, pri = _priority(c) };
            _tail = (_tail + 1) % _buf.Length;
            _count++;
        }

        /// <summary>FIFO 우선: 맨 앞이 원하는 커맨드일 때만 소비</summary>
        public bool TryConsume(Command want)
        {
            TickPrune();
            if (_count == 0) return false;
            ref var e = ref _buf[_head];
            if (e.cmd == want) { PopFront(); return true; }
            return false;
        }

        /// <summary>우선순위 집합에서 첫 매칭을 찾아 소비(RPG 권장)</summary>
        public bool TryConsumeAny(ReadOnlySpan<Command> priorities)
        {
            TickPrune();
            if (_count == 0) return false;

            float now = _clock.Now;
            int idx = _head;
            for (int i = 0; i < _count; i++)
            {
                if (now - _buf[idx].t > _window) { idx = (idx + 1) % _buf.Length; continue; }
                foreach (var p in priorities)
                {
                    if (_buf[idx].cmd == p) { RemoveAt(idx); return true; }
                }
                idx = (idx + 1) % _buf.Length;
            }
            return false;
        }

        public void Clear()
        {
            _head = _tail = _count = 0;
        }

        // ───────── 내부 유틸 ─────────
        private void PopFront()
        {
            _head = (_head + 1) % _buf.Length;
            _count--;
        }

        private void RemoveAt(int pos)
        {
            // 링에서 pos 이후를 한 칸씩 당김 (입력이 적어 성능 충분)
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

        // 디버그용 스냅샷
        public int Count => _count;
        public void ForEach(Action<Command, float, byte> visitor)
        {
            float now = _clock.Now;
            int idx = _head;
            for (int i = 0; i < _count; i++)
            {
                var e = _buf[idx];
                visitor?.Invoke(e.cmd, Mathf.Max(0, _window - (now - e.t)), e.pri);
                idx = (idx + 1) % _buf.Length;
            }
        }
    }
}
