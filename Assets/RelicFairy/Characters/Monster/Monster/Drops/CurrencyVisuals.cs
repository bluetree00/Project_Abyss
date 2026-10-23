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
    private static readonly Material[] s_outline     = new Material[KindCount];

    // 외곽선(10-22) — 몬스터 외곽선과 같은 셰이더(뒷면을 화면 px만큼 밀어 낸 껍데기). 굵기는 1080p 기준 px
    private const string OutlineShader  = "RelicFairy/MonsterOutline";
    private const float  OutlineWidthPx = 1.5f;
    private const int    OutlineQueue   = 2460;   // 불투명이 다 그려진 뒤 — 깊이를 안 써서 먼저 그리면 바닥이 덮는다

    public static bool IsReady => s_set != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_set     = null;
        s_loading = false;
        Array.Clear(s_translucent, 0, KindCount);
        Array.Clear(s_outline, 0, KindCount);
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

    /// <summary>
    /// 얇은 금빛 · 보랏빛 테두리(10-22 사용자 「골드 · 드랍 오브젝트 리소스 퀄리티 — 테두리나 작은 빛기둥」).
    /// 모양 메시를 외곽선 재질로 한 겹 더 그리는 자식 「~Outline」을 <b>꺼 둔 채</b> 만든다 — 이름이 「~」라 디졸브가 건드리지 않으므로
    /// 부르는 쪽이 내려앉으면 켜고 사라지기 전에 끈다. 셰이더를 못 찾으면 null(테두리 없이 그대로).
    /// </summary>
    public static GameObject AttachOutline(GameObject go, CurrencyVisualKind kind)
    {
        if (go == null) return null;
        var mf  = go.GetComponentInChildren<MeshFilter>(true);
        var mat = OutlineMaterial(kind);
        if (mf == null || mf.sharedMesh == null || mat == null) return null;

        var o = new GameObject("~Outline");
        o.transform.SetParent(mf.transform, false);
        o.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
        var r    = o.AddComponent<MeshRenderer>();
        var mats = new Material[Mathf.Max(1, mf.sharedMesh.subMeshCount)];
        for (int i = 0; i < mats.Length; i++) mats[i] = mat;
        r.sharedMaterials   = mats;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows    = false;
        o.SetActive(false);
        return o;
    }

    // ── Private Methods ─────────────────────────────────────

    private static Material OutlineMaterial(CurrencyVisualKind kind)
    {
        int i = (int)kind;
        if (i < 0 || i >= KindCount) return null;
        if (s_outline[i] != null) return s_outline[i];

        var shader = Shader.Find(OutlineShader);
        if (shader == null) return null;
        s_outline[i] = new Material(shader) { name = $"CurrencyOutline_{kind}", renderQueue = OutlineQueue };
        s_outline[i].SetFloat("_OutlineWidth", OutlineWidthPx);
        s_outline[i].SetColor("_OutlineColor", kind switch
        {
            CurrencyVisualKind.Essence => new Color(0.95f, 0.75f, 1.6f),   // 기억의 보랏빛(살짝 HDR — 바닥에서 테두리가 읽히게)
            _                          => new Color(1.35f, 1.05f, 0.5f),   // 금빛
        });
        return s_outline[i];
    }

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
