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

    public sealed class InputBuffer
    {
        private struct Entry { public Command cmd; public float t; public byte pri; }

        private readonly Entry[] _buf;
        private int _head, _tail, _count;

        private readonly IClock _clock;
        private readonly float _windowSec;   // 입력 유효시간(초)
        private readonly float _dedupeSec;   // 동일 커맨드 연속 억제(초)
        private readonly Func<Command, byte> _priority;

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

        public void TickPrune()
        {
            float now = _clock.Now;
            while (_count > 0)
            {
                ref var e = ref _buf[_head];
                if (now - e.t <= _windowSec) break;
                PopFront();
            }
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
                    return;
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
                if (victim >= 0) RemoveAt(victim);
                else return;
            }

            _buf[_tail] = new Entry { cmd = c, t = now, pri = _priority(c) };
            _tail = (_tail + 1) % _buf.Length;
            _count++;
        }

        public bool TryConsume(Command want)
        {
            TickPrune();
            if (_count == 0) return false;
            ref var e = ref _buf[_head];
            if (e.cmd == want) { PopFront(); return true; }
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
                    if (_buf[idx].cmd == wants[w]) { RemoveAt(idx); return true; }
                }
                idx = (idx + 1) % _buf.Length;
            }
            return false;
        }

        public void Clear() => _head = _tail = _count = 0;

        private void PopFront() { _head = (_head + 1) % _buf.Length; _count--; }

        private void RemoveAt(int pos)
        {
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
