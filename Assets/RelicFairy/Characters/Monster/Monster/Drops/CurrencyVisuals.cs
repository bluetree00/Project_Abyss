using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 재화 월드 오브젝트 꺼내기 · 나타나기 · 사라지기(10-12) — <see cref="RunFx"/>와 같은 규약(목록 SO를 Addressables로 한 번 읽어 둔다).
/// 목록이 아직 없거나 칸이 비면 <see cref="TryCreate"/>가 null을 돌려준다 — 부르는 쪽(GoldCoinPickup · EssenceShardPickup)이 임시 도형으로 떨어뜨린다.
///
/// 나타나기 · 사라지기는 디졸브(<see cref="DissolveEffect"/>)로 한다. 반투명 재질(유리 캡슐 등)은 디졸브 재질과 맞지 않아
/// 핑크 잔상이 남으므로 알파만 낮추는 <see cref="MaterialFade"/>로 한다 — 칸마다 프리팹 재질을 한 번 보고 정해 둔다.
/// </summary>
public static class CurrencyVisuals
{
    public const string SetAddress = "Run/Drops/CurrencyVisualSet";

    private const int KindCount = 4;

    private static CurrencyVisualSetSO s_set;
    private static bool                s_loading;
    private static readonly bool[]     s_translucent = new bool[KindCount];

    public static bool IsReady => s_set != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_set     = null;
        s_loading = false;
        Array.Clear(s_translucent, 0, KindCount);
    }

    /// <summary>
    /// 목록과 디졸브 재질을 읽는다(이미 읽었거나 읽는 중이면 바로 돌아온다). 런 시작에 한 번 —
    /// 안 읽어 두면 첫 처치 드롭이 임시 도형으로 나오고, 첫 디졸브가 한 프레임 늦게 걸린다.
    /// </summary>
    public static async UniTask LoadAsync()
    {
        if (s_set != null || s_loading) return;
        s_loading = true;
        try
        {
            var am = Managers.AddressableManager;
            if (am == null) return;
            s_set = await am.TryLoadAssetAsync<CurrencyVisualSetSO>(SetAddress);
            if (s_set == null)
            {
                Debug.LogWarning($"[CurrencyVisuals] 재화 오브젝트 목록 없음 — {SetAddress} (임시 도형으로 진행)");
                return;
            }
            for (int i = 0; i < KindCount; i++)
                s_translucent[i] = HasTranslucent(s_set.Get((CurrencyVisualKind)i));
            await DissolveEffect.WarmupAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogWarning($"[CurrencyVisuals] 목록 읽기 실패(임시 도형으로 진행) — {e.Message}");
        }
        finally
        {
            s_loading = false;
        }
    }

    /// <summary>재화 모양 하나를 만든다. 목록이 없거나 칸이 비면 null(목록은 이때 읽기 시작한다).</summary>
    public static GameObject TryCreate(CurrencyVisualKind kind)
    {
        if (s_set == null)
        {
            LoadAsync().Forget();
            return null;
        }
        var prefab = s_set.Get(kind);
        return prefab != null ? UnityEngine.Object.Instantiate(prefab) : null;
    }

    /// <summary>나타나기 — 디졸브(반투명이면 알파). 임시 도형도 같은 길로 나타난다.</summary>
    public static void Appear(GameObject go, CurrencyVisualKind kind, float seconds, Color edgeColor)
    {
        if (go == null) return;
        if (IsTranslucent(kind))
        {
            var handle = MaterialFade.BeginFadeIn(go);
            MaterialFade.FadeInAsync(handle, seconds, go.GetCancellationTokenOnDestroy()).Forget();
            return;
        }
        DissolveEffect.PlayAppear(go, seconds, null, default, edgeColor);
    }

    /// <summary>사라지기 — 디졸브(반투명이면 알파) 뒤 파괴한다. 디졸브 재질을 못 읽어도 파괴는 된다.</summary>
    public static void Vanish(GameObject go, CurrencyVisualKind kind, float seconds)
    {
        if (go == null) return;
        if (IsTranslucent(kind))
        {
            FadeThenDestroyAsync(go, seconds).Forget();
            return;
        }
        DissolveEffect.PlayDisappear(go, seconds, () =>
        {
            if (go != null) UnityEngine.Object.Destroy(go);
        });
    }

    // ── Private Methods ─────────────────────────────────────

    private static bool IsTranslucent(CurrencyVisualKind kind)
    {
        int i = (int)kind;
        return s_set != null && i >= 0 && i < KindCount && s_translucent[i];
    }

    private static bool HasTranslucent(GameObject prefab)
    {
        if (prefab == null) return false;
        foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
            foreach (var m in r.sharedMaterials)
                if (m != null && m.renderQueue >= (int)RenderQueue.Transparent) return true;
        }
        return false;
    }

    private static async UniTaskVoid FadeThenDestroyAsync(GameObject go, float seconds)
    {
        try
        {
            await MaterialFade.FadeOutAsync(go, seconds, go.GetCancellationTokenOnDestroy());
        }
        catch (OperationCanceledException) { return; }
        if (go != null) UnityEngine.Object.Destroy(go);
    }
}
