using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 원소 효과 공통 처리(VFX 스폰, 체인 데미지 등) 헬퍼.
/// 상태 없음 — 누가 호출해도 동일.
/// </summary>
public static class ElementEffectRunner
{
    private const float DefaultVfxLifetime = 3f;

    /// <summary>vfx_key 기반 Addressable 인스턴스 스폰 후 일정 시간 뒤 자동 해제. position 사용.</summary>
    public static void SpawnVFX(string vfxKey, Vector3 position, float lifetime = DefaultVfxLifetime)
    {
        if (string.IsNullOrEmpty(vfxKey)) return;
        SpawnVFXAsync(vfxKey, null, position, Vector3.up * 0.5f, lifetime).Forget();
    }

    /// <summary>parent 에 부착하여 함께 움직이는 VFX. parent가 도중에 사라지면 즉시 해제.</summary>
    public static void SpawnVFXAttached(string vfxKey, Transform parent, float lifetime = DefaultVfxLifetime, Vector3? localOffset = null)
    {
        if (string.IsNullOrEmpty(vfxKey) || parent == null) return;
        SpawnVFXAsync(vfxKey, parent, parent.position, localOffset ?? Vector3.up * 0.5f, lifetime).Forget();
    }

    private static async UniTaskVoid SpawnVFXAsync(string vfxKey, Transform parent, Vector3 fallbackPos, Vector3 offset, float lifetime)
    {
        GameObject go = null;
        try
        {
            go = await Managers.AddressableManager.InstantiateAsync(vfxKey, parent, false);
            if (go == null) return;

            if (parent != null)
                go.transform.localPosition = offset;
            else
                go.transform.position = fallbackPos + offset;

            await UniTask.Delay(TimeSpan.FromSeconds(lifetime));
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogWarning($"[ElementEffectRunner] VFX 스폰 실패: {vfxKey} ({e.Message})");
        }
        finally
        {
            if (go != null)
                Managers.AddressableManager.ReleaseInstance(go);
        }
    }

    /// <summary>Lightning chain — origin 주변 radius 이내 IDamageable에 damage 전파.</summary>
    public static void LightningChain(IElementTarget origin, float radius, float damage)
    {
        if (origin == null || radius <= 0f || damage <= 0f) return;

        var hits = Physics.OverlapSphere(origin.Transform.position, radius);
        foreach (var col in hits)
        {
            if (col == null) continue;
            if (col.gameObject == origin.GameObject) continue;
            if (col.TryGetComponent<IDamageable>(out var t))
            {
                t.TakeDamage(damage, origin.GameObject, 1f, ElementType.None, 0f);
            }
        }
    }
}
