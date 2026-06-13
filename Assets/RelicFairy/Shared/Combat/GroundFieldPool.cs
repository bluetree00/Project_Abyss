using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 룬 장판(FD) 런타임 풀. 타입별 비활성 인스턴스를 재사용한다.
/// 프리팹/Addressables 없이 런타임 GameObject로 생성 — 의존성 0(시각 VFX는 파생/후속에서 부착).
/// 씬 전환 시 파괴된 풀 인스턴스는 Spawn에서 null 가드로 폐기.
/// </summary>
public static class GroundFieldPool
{
    private static readonly Dictionary<System.Type, Stack<GroundFieldBase>> _pools = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Clear() => _pools.Clear();

    /// <summary>풀에서 재사용하거나 새로 생성한다. 활성 상태로 반환(Initialize는 호출자 책임).</summary>
    public static T Spawn<T>() where T : GroundFieldBase
    {
        var type = typeof(T);
        if (_pools.TryGetValue(type, out var stack))
        {
            while (stack.Count > 0)
            {
                var f = stack.Pop();
                if (f != null)                 // Unity 파괴 객체 가드
                {
                    f.gameObject.SetActive(true);
                    return (T)f;
                }
            }
        }
        var go = new GameObject(type.Name);
        return go.AddComponent<T>();
    }

    /// <summary>비활성화 후 풀에 반환.</summary>
    public static void Return(GroundFieldBase field)
    {
        if (field == null) return;
        var type = field.GetType();
        if (!_pools.TryGetValue(type, out var stack))
        {
            stack = new Stack<GroundFieldBase>();
            _pools[type] = stack;
        }
        if (field.gameObject.activeSelf) field.gameObject.SetActive(false);
        stack.Push(field);
    }
}
