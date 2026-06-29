using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 룬 속성 효과들이 공유하는 플레이어 리소스 컨테이너(6속성 공용).
///
/// 단일 개념 "리소스" = 이름 붙은 카운터로, 아래 셋을 한 틀로 표현한다:
///  • 스택(decay) : duration>0 → 갱신 시 타이머 충전, 만료 시 전량 소멸(전기 정전기, 빛 광채).
///  • 게이지(no-decay) : duration<=0 → 비감쇠 누적, 수동 소모(어둠 게이지, 점화 발동 카운터).
///  • 임계 콜백 : RegisterThreshold로 값이 임계 도달 시 1회 발화(빛 10스택→광폭발, 어둠 100→암흑해방).
///
/// 그 외 효과 간 코디네이션용 슬롯:
///  • Register(int) / Float(float) : 1프레임 값 공유(방전 소모량, 감전 공속 기여).
///  • Target(GameObject) : 대상 공유(방전 대상 = 감전 대상).
///
/// 디스패처가 1개 보유, 매 프레임 Tick으로 스택을 감쇠한다.
/// </summary>
public sealed class RuneResourceState
{
    private sealed class Slot
    {
        public int   count;
        public int   max;
        public float remaining;   // 남은 유지 시간(초).
        public float duration;    // 갱신 시 충전값. <=0 이면 비감쇠(게이지 모드).
    }

    private sealed class ThresholdHook
    {
        public int    threshold;
        public Action onReached;
        public bool   fired;
    }

    private readonly Dictionary<string, Slot>          _slots      = new();
    private readonly Dictionary<string, int>           _registers  = new();
    private readonly Dictionary<string, float>         _floats     = new();
    private readonly Dictionary<string, GameObject>    _targets    = new();
    private readonly Dictionary<string, ThresholdHook> _thresholds = new();

    // [가이드라인 비주얼] 임계 도달 토스트 위치(플레이어). 통지 전용 — 리소스 로직엔 미사용.
    private Transform _anchor;
    public void SetAnchor(Transform anchor) => _anchor = anchor;

    // ── 스택 / 게이지 ──────────────────────────────────────

    /// <summary>amount만큼 증가(0~max 클램프). duration>0이면 유지 타이머 충전, <=0이면 비감쇠 게이지.</summary>
    public void Add(string key, int amount, int max, float duration)
    {
        if (string.IsNullOrEmpty(key) || amount == 0) return;
        if (!_slots.TryGetValue(key, out var s)) { s = new Slot(); _slots[key] = s; }
        s.max      = Mathf.Max(1, max);
        s.duration = duration;
        s.count    = Mathf.Clamp(s.count + amount, 0, s.max);
        if (duration > 0f) s.remaining = duration;
        CheckThreshold(key, s.count);
    }

    /// <summary>스택 1 증가(전기 정전기 등 적중당 1).</summary>
    public void AddStack(string key, int max, float duration) => Add(key, 1, max, duration);

    /// <summary>게이지 누적(비감쇠). 어둠 게이지 등.</summary>
    public void AddGauge(string key, int amount, int max) => Add(key, amount, max, 0f);

    public int Get(string key) => _slots.TryGetValue(key, out var s) ? s.count : 0;

    /// <summary>슬롯 상한(충전 게이지 진행% 산출용, 버프창 표시 전용). 미존재면 0.</summary>
    public int GetMax(string key) => _slots.TryGetValue(key, out var s) ? s.max : 0;

    /// <summary>전량 소모 → 소모한 수 반환. 임계 발화 플래그도 리셋(재충전 시 재발화).</summary>
    public int Consume(string key)
    {
        if (!_slots.TryGetValue(key, out var s) || s.count <= 0) return 0;
        int c = s.count;
        s.count = 0;
        s.remaining = 0f;
        if (_thresholds.TryGetValue(key, out var h)) h.fired = false;
        return c;
    }

    /// <summary>스택/게이지 슬롯 제거(단계 하강 해제 시 잔존 방지). 소유 효과의 OnDeactivate에서 호출.</summary>
    public void RemoveSlot(string key) { if (!string.IsNullOrEmpty(key)) _slots.Remove(key); }

    // 별칭(의미 명시용)
    public int GetStack(string key)   => Get(key);
    public int ConsumeStack(string key) => Consume(key);
    public int GetGauge(string key)   => Get(key);
    public int ConsumeGauge(string key) => Consume(key);

    /// <summary>임계 콜백 등록. 값이 threshold 이상 도달 시 1회 발화(소모/하강 시 재무장).</summary>
    public void RegisterThreshold(string key, int threshold, Action onReached)
    {
        if (string.IsNullOrEmpty(key) || onReached == null) return;
        _thresholds[key] = new ThresholdHook { threshold = threshold, onReached = onReached, fired = false };
    }

    /// <summary>임계 콜백 해제(상위 단계 하강 해제 시 stale 발화 방지). 등록 효과의 OnDeactivate에서 호출.</summary>
    public void RemoveThreshold(string key) { if (!string.IsNullOrEmpty(key)) _thresholds.Remove(key); }

    private void CheckThreshold(string key, int value)
    {
        if (!_thresholds.TryGetValue(key, out var h)) return;
        if (!h.fired && value >= h.threshold)
        {
            h.fired = true;
            h.onReached?.Invoke();
            if (_anchor != null)
                GuidelineVisual.Toast(_anchor.position + Vector3.up * 2.6f, key + " MAX", GuidelineVisual.ToastKind.Resource);
        }
        else if (value < h.threshold) h.fired = false;
    }

    // ── 레지스터 / float / 대상 슬롯 ───────────────────────

    public void SetRegister(string key, int value) { if (!string.IsNullOrEmpty(key)) _registers[key] = value; }
    public int  GetRegister(string key) => _registers.TryGetValue(key, out var v) ? v : 0;

    public void  SetFloat(string key, float value) { if (!string.IsNullOrEmpty(key)) _floats[key] = value; }
    public float GetFloat(string key) => _floats.TryGetValue(key, out var v) ? v : 0f;

    public void SetTarget(string key, GameObject go) { if (!string.IsNullOrEmpty(key)) _targets[key] = go; }
    public GameObject GetTarget(string key) => _targets.TryGetValue(key, out var go) ? go : null;

    // ── 수명 ──────────────────────────────────────────────

    /// <summary>유지 타이머 감쇠. 만료 시 해당 스택 전량 소멸(게이지 duration<=0은 비감쇠).</summary>
    public void Tick(float dt)
    {
        foreach (var kv in _slots)
        {
            var s = kv.Value;
            if (s.count <= 0 || s.duration <= 0f) continue;
            s.remaining -= dt;
            if (s.remaining <= 0f)
            {
                s.count = 0;
                s.remaining = 0f;
                if (_thresholds.TryGetValue(kv.Key, out var h)) h.fired = false;
            }
        }
    }

    public void Clear()
    {
        _slots.Clear();
        _registers.Clear();
        _floats.Clear();
        _targets.Clear();
        _thresholds.Clear();
    }
}
