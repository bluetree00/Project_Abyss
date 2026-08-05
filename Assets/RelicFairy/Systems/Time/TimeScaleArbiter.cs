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

    /// <summary>지정 우선순위 이상의 요청이 하나라도 걸려 있는지.
    /// 히트스톱이 슬로모/일시정지 구간을 덮어쓰지 않도록 판단할 때 쓴다.</summary>
    public static bool HasRequestAtOrAbove(Priority priority)
    {
        int p = (int)priority;
        foreach (var kv in _requests)
            if (kv.Value.PriorityValue >= p) return true;
        return false;
    }

    /// <summary>현재 timeScale을 잡고 있는 소유자들을 사람이 읽는 문자열로. 디버그/진단용.</summary>
    public static string DescribeHolders()
    {
        if (_requests.Count == 0) return "(none)";
        var sb = new System.Text.StringBuilder();
        foreach (var kv in _requests)
        {
            string name = kv.Key is Object uo
                ? (uo == null ? "<destroyed>" : uo.GetType().Name + ":" + uo.name)
                : kv.Key.GetType().Name;
            sb.Append($"[{name} scale={kv.Value.Scale:0.##} pri={kv.Value.PriorityValue}] ");
        }
        return sb.ToString();
    }

    // ── Private Methods ──────────────────────────────────────────────
    private static readonly List<object> s_deadScratch = new List<object>();

    /// <summary>
    /// 파괴된 Unity 객체 소유자를 정리한다. Acquire한 컴포넌트/GameObject가 Release 없이 파괴되면
    /// (예: 재스폰이 플레이어를 파괴) 그 요청이 딕셔너리에 영영 남아 timeScale이 고착된다
    /// (특히 Pause=0이면 화면이 멈춘다). Recompute마다 죽은 소유자를 걷어내 자가치유한다.
    /// </summary>
    private static void PurgeDeadOwners()
    {
        s_deadScratch.Clear();
        foreach (var kv in _requests)
            if (kv.Key is Object uo && uo == null)   // Unity의 파괴된 객체 == null (오버로드된 ==)
                s_deadScratch.Add(kv.Key);

        for (int i = 0; i < s_deadScratch.Count; i++)
        {
            _requests.Remove(s_deadScratch[i]);
            Debug.LogWarning("[TimeScaleArbiter] 파괴된 소유자의 timeScale 요청을 정리했다(누수 방지).");
        }
        s_deadScratch.Clear();
    }

    private static void Recompute()
    {
        PurgeDeadOwners();

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

        // [진단] 완전정지(0)로 떨어질 땐 누가 잡았는지 남긴다 — '멈춤' 재현 시 범인 특정용.
        if (effective <= 0.001f)
            Debug.LogWarning($"[TimeScaleArbiter] timeScale=0 (완전정지). 보유자: {DescribeHolders()}");
    }

    // 에디터 "도메인 리로드 비활성" 설정 시 정적 상태가 새 플레이세션으로 새지 않도록 초기화.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _requests.Clear();
        Time.timeScale = 1f;
    }
}
