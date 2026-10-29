using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 아이템 효과에서 VFX를 스폰하기 위한 정적 헬퍼.
/// ObjectPooler 경유 Addressable 로드.
/// </summary>
public static class ItemEffectVfxHelper
{
    private static readonly System.Collections.Generic.Dictionary<string, float> s_lifeByKey = new();   // 키별 한 번 재생 길이(발동마다 자식 입자를 훑었다 · 10-29 풀 점검 5)

    /// <summary>지정 위치에 1회성 VFX 스폰 (자동 반환).</summary>
    public static async void SpawnOneShotAt(string addressableKey, Vector3 position, float scale = 1f)
    {
        if (string.IsNullOrEmpty(addressableKey)) return;

        var obj = await Managers.ObjectPooler.SpawnAsync(
            addressableKey, ObjectPoolerManager.PoolType.Effect, position, Quaternion.identity);
        if (obj == null) return;

        // 다 쓰면 풀에 돌려준다 — 예전엔 반환이 없어 발동마다 새 인스턴스가 @Pools 밑에 켜진 채 쌓였다(10-11 이펙트 톤 점검).
        // 크기 = 프리팹 원본 × scale(재사용 때 곱이 쌓이지 않게) · 반복은 끈다(PooledOneShotVfx).
        if (!obj.TryGetComponent<PooledOneShotVfx>(out var vfx)) vfx = obj.AddComponent<PooledOneShotVfx>();
        if (!s_lifeByKey.TryGetValue(addressableKey, out float life)) s_lifeByKey[addressableKey] = life = OneShotLifetime(obj);
        vfx.Play(life, scale);
    }

    /// <summary>한 번 재생 길이 — 입자계마다 (길이 + 최대 수명) 중 가장 긴 것. 입자계가 없으면 2초.</summary>
    private static float OneShotLifetime(GameObject go)
    {
        float life = 0f;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            life = Mathf.Max(life, main.duration + main.startLifetime.constantMax);
        }
        return life > 0.05f ? life : 2f;
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
        var hud = Object.FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
        hud?.ShowItemEffectNotice(message);
    }

    /// <summary>빌드 완성 신호(각인 단계 · 유물 진화 · 속성 존 단계)를 같은 알림 줄에 칩 · 단계 눈금과 함께(10-14 빌드 시너지 · 83 UI).</summary>
    public static void ShowBuildNotice(in BuildNotice notice)
    {
        var hud = Object.FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
        hud?.ShowBuildNotice(notice);
    }

    private static async UniTaskVoid DestroyAfter(GameObject obj, float seconds)
    {
        await UniTask.Delay((int)(seconds * 1000));
        if (obj != null)
            Object.Destroy(obj);
    }
}
