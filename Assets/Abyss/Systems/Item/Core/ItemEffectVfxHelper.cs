using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 아이템 효과에서 VFX를 스폰하기 위한 정적 헬퍼.
/// ObjectPooler 경유 Addressable 로드.
/// </summary>
public static class ItemEffectVfxHelper
{
    /// <summary>지정 위치에 1회성 VFX 스폰 (자동 반환).</summary>
    public static async void SpawnOneShotAt(string addressableKey, Vector3 position, float scale = 1f)
    {
        if (string.IsNullOrEmpty(addressableKey)) return;

        var obj = await Managers.ObjectPooler.SpawnAsync(
            addressableKey, ObjectPoolerManager.PoolType.Effect, position, Quaternion.identity);
        if (obj == null) return;

        if (scale != 1f)
            obj.transform.localScale = Vector3.one * scale;
    }

    /// <summary>대상 트랜스폼에 VFX를 부착하고, duration초 후 제거. 반환된 GameObject로 조기 해제 가능.</summary>
    public static async UniTask<GameObject> AttachLoopVfx(string addressableKey, Transform parent, float duration, float scale = 1f)
    {
        if (string.IsNullOrEmpty(addressableKey) || parent == null) return null;

        var prefab = await Managers.AddressableManager.LoadAssetAsync<GameObject>(addressableKey);
        if (prefab == null || parent == null) return null;

        var instance = Object.Instantiate(prefab, parent);
        instance.transform.localPosition = Vector3.zero;
        if (scale != 1f)
            instance.transform.localScale = Vector3.one * scale;

        if (duration > 0f)
            DestroyAfter(instance, duration).Forget();

        return instance;
    }

    /// <summary>아이템 효과 발동 알림을 HUD 왼쪽에 표시.</summary>
    public static void ShowNotice(string message)
    {
        var hud = Object.FindObjectOfType<HudPresenter>(true);
        hud?.ShowItemEffectNotice(message);
    }

    private static async UniTaskVoid DestroyAfter(GameObject obj, float seconds)
    {
        await UniTask.Delay((int)(seconds * 1000));
        if (obj != null)
            Object.Destroy(obj);
    }
}
