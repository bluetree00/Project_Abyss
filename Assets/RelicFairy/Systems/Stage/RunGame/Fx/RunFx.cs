using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 런 공용 이펙트 재생기 — 칸(<see cref="RunFxSlot"/>)으로 부르고 빛깔을 입힌다.
/// 재생 · 풀 · 색 입히기 · 크기 보정은 리치 재생기(<see cref="LichVfx.PlayEntry"/>)를 그대로 쓴다.
/// 목록이 아직 없거나 칸이 비어 있으면 조용히 건너뛴다 — 부르는 쪽의 판정 · 흐름은 이펙트와 무관하게 돈다.
/// </summary>
public static class RunFx
{
    public const string SetAddress = "Run/Fx/RunFxSet";

    private static RunFxSetSO s_set;
    private static bool       s_loading;

    public static bool IsReady => s_set != null;

    /// <summary>예고 장판 데칼 재질(원 · 화살표) — 목록이 없으면 null(그땐 PatternGuideHelper가 기본 도형으로 그린다).</summary>
    public static Material GuideCircle => s_set != null ? s_set.GuideCircle : null;
    public static Material GuideArrow  => s_set != null ? s_set.GuideArrow  : null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_set     = null;
        s_loading = false;
    }

    /// <summary>목록을 읽는다(이미 읽었거나 읽는 중이면 바로 돌아온다).</summary>
    public static async UniTask LoadAsync()
    {
        if (s_set != null || s_loading) return;
        s_loading = true;
        try
        {
            var am = Managers.AddressableManager;
            if (am == null) return;
            s_set = await am.TryLoadAssetAsync<RunFxSetSO>(SetAddress);
            if (s_set == null)
                Debug.LogWarning($"[RunFx] 이펙트 목록을 찾지 못했다 — {SetAddress} (이펙트 없이 진행)");
            else
                Debug.Log($"[RunFx] 이펙트 목록 로드 — {s_set.Entries.Count}칸");
        }
        finally
        {
            s_loading = false;
        }
    }

    /// <summary>칸에 프리팹이 있는가.</summary>
    public static bool Has(RunFxSlot slot) => s_set != null && s_set.TryGet(slot, out _);

    /// <summary>한 번 재생 — 수명 뒤 거둔다.</summary>
    public static GameObject Play(RunFxSlot slot, Vector3 position, float scale, Color tint)
        => Spawn(slot, position, Quaternion.identity, scale, tint, false);

    /// <summary>반복 재생 — <see cref="Stop"/>할 때까지 남는다.</summary>
    public static GameObject PlayLoop(RunFxSlot slot, Vector3 position, float scale, Color tint)
        => Spawn(slot, position, Quaternion.identity, scale, tint, true);

    /// <summary>방향을 정해 반복 재생 — 세워 두는 이펙트(포탈)용. 원래 빛깔 그대로면 <see cref="Color.clear"/>.</summary>
    public static GameObject PlayLoop(RunFxSlot slot, Vector3 position, Quaternion rotation, float scale, Color tint)
        => Spawn(slot, position, rotation, scale, tint, true);

    /// <summary>거둔다. <paramref name="fadeSeconds"/> &gt; 0이면 방출만 멈추고 남은 입자가 사라진 뒤 거둔다.</summary>
    public static void Stop(ref GameObject instance, float fadeSeconds = 0f) => LichVfx.Stop(ref instance, fadeSeconds);

    private static GameObject Spawn(RunFxSlot slot, Vector3 position, Quaternion rotation, float scale, Color tint, bool loop)
    {
        if (s_set == null || !s_set.TryGet(slot, out var e)) return null;
        var entry = new LichVfxEntry { prefab = e.prefab, scale = e.scale, offset = e.offset, lifetime = e.lifetime };
        // 재채색 칸은 곱하기 틴트를 건너뛰고(원래 색 그대로 꺼내) 빛깔만 갈아 끼운다(10-02 보상 등급 빛기둥)
        var go = LichVfx.PlayEntry(entry, position, rotation, scale, e.recolor ? Color.clear : tint, loop);
        if (go != null)
        {
            if (e.recolor) RunFxRecolor.Apply(go, tint);
            TintLights(go, tint);
            StripRefraction(go);
        }
        return go;
    }

    /// <summary>
    /// 굴절층(화면을 비틀어 보이게 하는 셰이더)을 끈다 — 기둥 · 오라 안쪽의 배경이 흙먼지 낀 유리처럼 탁해졌다(10-01 f5 톤 검토).
    /// 이 목록의 프리팹은 런 공용 풀에서만 꺼내 쓰므로(전설 아이템 이펙트는 따로 Instantiate) 꺼 둔 채 풀에 돌아가도 된다.
    /// </summary>
    private static void StripRefraction(GameObject go)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var m = r.sharedMaterial;
            if (m == null || m.shader == null) continue;
            string s = m.shader.name;
            if (s.IndexOf("Distortion", System.StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("ShockWave", System.StringComparison.OrdinalIgnoreCase) >= 0)
                r.enabled = false;
        }
    }

    /// <summary>팩 이펙트에 든 점광원도 같은 빛깔로 — 입자만 금빛이고 바닥이 파랗게 비치는 일이 없게(풀 재사용마다 다시 입힌다).</summary>
    private static void TintLights(GameObject go, Color tint)
    {
        if (tint.a <= 0f) return;
        foreach (var light in go.GetComponentsInChildren<Light>(true))
            light.color = new Color(tint.r, tint.g, tint.b, 1f);
    }
}
