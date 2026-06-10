using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Time.timeScale 단일 소유자(아비터). 여러 시스템(일시정지/사망슬로모/히트스톱/필살기)이
/// 각자 (owner, scale, priority) 요청을 Acquire/Release 하면, 우선순위가 가장 높은 요청의
/// scale 을 실제 Time.timeScale 에 반영한다(동일 우선순위는 더 느린 쪽=min 채택).
///
/// 기존엔 여러 곳이 timeScale 을 직접 set 했고 특히 히트스톱 완료 시 무조건 =1f 로 복원해
/// 일시정지/슬로모와 충돌했다(메뉴 뒤 게임 풀림, 슬로모 끊김). 이 아비터에서는 각 요청이
/// 자기 것만 Release 하므로, 다른 owner 가 남아있으면 그 값이 유지된다.
///
/// 우선순위: Pause(완전정지) > SlowMotion(연출 슬로모) > HitStop(타격 정지, 가장 짧고 양보).
/// timeScale 만 중앙화한다 — 입력 차단/UI/fixedDeltaTime/Cinemachine Brain 등 부수효과는 각 호출부 책임.
/// </summary>
public static class TimeScaleArbiter
{
    public enum Priority
    {
        HitStop    = 10,
        SlowMotion = 100,
        Pause      = 1000,
    }

    private readonly struct Request
    {
        public readonly float Scale;
        public readonly int   PriorityValue;
        public Request(float scale, int priorityValue) { Scale = scale; PriorityValue = priorityValue; }
    }

    private static readonly Dictionary<object, Request> _requests = new Dictionary<object, Request>();

    // ── Public Methods ───────────────────────────────────────────────
    /// <summary>요청 등록/갱신. 같은 owner 로 다시 부르면 덮어쓴다.</summary>
    public static void Acquire(object owner, float scale, Priority priority)
    {
        if (owner == null) return;
        _requests[owner] = new Request(Mathf.Max(0f, scale), (int)priority);
        Recompute();
    }

    /// <summary>요청 해제. 등록되지 않은 owner 면 무시(방어적 Release 안전).</summary>
    public static void Release(object owner)
    {
        if (owner == null) return;
        if (_requests.Remove(owner))
            Recompute();
    }

    /// <summary>현재 owner 가 요청을 들고 있는지.</summary>
    public static bool IsHeldBy(object owner) => owner != null && _requests.ContainsKey(owner);

    // ── Private Methods ──────────────────────────────────────────────
    private static void Recompute()
    {
        if (_requests.Count == 0)
        {
            Time.timeScale = 1f;
            return;
        }

        int   topPriority = int.MinValue;
        float effective    = 1f;
        foreach (var req in _requests.Values)
        {
            if (req.PriorityValue > topPriority)
            {
                topPriority = req.PriorityValue;
                effective   = req.Scale;
            }
            else if (req.PriorityValue == topPriority && req.Scale < effective)
            {
                effective = req.Scale; // 동일 우선순위 → 더 느린(작은) 쪽 채택
            }
        }
        Time.timeScale = effective;
    }

    // 에디터 "도메인 리로드 비활성" 설정 시 정적 상태가 새 플레이세션으로 새지 않도록 초기화.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _requests.Clear();
        Time.timeScale = 1f;
    }
}
