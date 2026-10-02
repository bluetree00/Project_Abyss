using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 이펙트 재생기 — 칸(<see cref="LichVfxSlot"/>)으로 부르고 <see cref="BossEffectPool"/>로 재사용한다.
///
/// 목록(<see cref="LichVfxSetSO"/>)은 리치가 초기화될 때 Addressables 주소 <see cref="SetAddress"/>에서 한 번 읽는다.
/// 목록이 아직 없거나 칸이 비어 있으면 조용히 건너뛴다 — 패턴의 판정·예고는 이펙트와 무관하게 돈다.
///
/// 풀에 넣지 않는 프리팹: 팩 스크립트가 스스로를 파괴하는 것(SSEP <c>NewMaterialChange</c>·<c>DestroyObject</c>, PixPlays 드라이버).
/// 재사용하면 몇 번째부터 몸체가 사라지므로 매번 새로 만들고 수명 뒤 파괴한다.
/// </summary>
public static class LichVfx
{
    public const string SetAddress = "Lich/VFX/LichVfxSet";

    private static LichVfxSetSO s_set;
    private static bool         s_loading;
    private static readonly Dictionary<int, bool> s_freshOnly = new();

    public static bool IsReady => s_set != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_set     = null;
        s_loading = false;
        s_freshOnly.Clear();
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
            s_set = await am.TryLoadAssetAsync<LichVfxSetSO>(SetAddress);
            if (s_set == null)
                Debug.LogWarning($"[LichVfx] 이펙트 목록을 찾지 못했다 — {SetAddress} (이펙트 없이 진행)");
            else
                Debug.Log($"[LichVfx] 이펙트 목록 로드 — {s_set.Entries.Count}칸");
        }
        finally
        {
            s_loading = false;
        }
    }

    /// <summary>칸에 프리팹이 있는가.</summary>
    public static bool Has(LichVfxSlot slot) => s_set != null && s_set.TryGet(slot, out _);

    /// <summary>사슬 줄 재질(<see cref="LichChainLine"/>). 목록이 없으면 null.</summary>
    public static Material ChainMaterial => s_set != null ? s_set.ChainMaterial : null;

    /// <summary>바닥 균열 재질(<see cref="LichCrack"/>). 목록이 없으면 null.</summary>
    public static Material CrackMaterial => s_set != null ? s_set.CrackMaterial : null;

    /// <summary>한 번 재생 — 수명(목록 값 또는 파티클 길이) 뒤 거둔다.</summary>
    public static GameObject Play(LichVfxSlot slot, Vector3 position, Quaternion rotation,
                                  float scale = 1f, Transform parent = null)
    {
        var go = Spawn(slot, position, rotation, scale, parent, false, null, out var entry);
        if (go == null) return null;

        float life = entry.lifetime > 0f ? entry.lifetime : BossEffectPool.CalculateLifetime(entry.prefab, 2f);
        ReleaseAfter(go, life);
        return go;
    }

    /// <summary>한 번 재생 — 수명을 호출부가 정한다(판정 시간에 맞출 때).</summary>
    public static GameObject Play(LichVfxSlot slot, Vector3 position, Quaternion rotation,
                                  float scale, float lifetime, Transform parent = null)
    {
        var go = Spawn(slot, position, rotation, scale, parent, false, null, out _);
        if (go != null) ReleaseAfter(go, Mathf.Max(0.05f, lifetime));
        return go;
    }

    /// <summary>한 번 재생 — 칸의 색 대신 <paramref name="tint"/>를 입힌다(같은 칸을 다른 뜻으로 쓸 때: 흰 잔상 등).</summary>
    public static GameObject PlayTinted(LichVfxSlot slot, Vector3 position, Quaternion rotation, float scale, Color tint)
    {
        var go = Spawn(slot, position, rotation, scale, null, false, tint, out var entry);
        if (go == null) return null;

        float life = entry.lifetime > 0f ? entry.lifetime : BossEffectPool.CalculateLifetime(entry.prefab, 2f);
        ReleaseAfter(go, life);
        return go;
    }

    /// <summary>
    /// 모델 칸(봉인석 메시 등)을 새로 만든다 — 재질 값을 바꿔 쓰는 물체라 풀에 넣지 않는다. 칸이 비면 null(호출부가 대체 모양을 쓴다).
    /// </summary>
    public static GameObject InstantiateModel(LichVfxSlot slot, Vector3 position, Quaternion rotation, Transform parent = null)
    {
        if (s_set == null || !s_set.TryGet(slot, out var entry)) return null;
        var go = Object.Instantiate(entry.prefab, position + rotation * entry.offset, rotation, parent);
        float s = entry.scale > 0f ? entry.scale : 1f;
        go.transform.localScale = entry.prefab.transform.localScale * s;
        return go;
    }

    /// <summary>
    /// 화면 전체 오버레이(카메라에 붙는 Hovl 스크린 이펙트)를 <paramref name="seconds"/> 동안 띄운다.
    /// 같은 칸이 이미 떠 있으면 시간만 늘린다. 드래곤 화면 효과와 같은 경로.
    /// </summary>
    public static void PlayScreen(LichVfxSlot slot, float seconds)
    {
        if (s_set == null || !s_set.TryGet(slot, out var entry) || seconds <= 0f) return;
        PlayerStatusEffectVisuals.ApplyScreenEffectTimed(entry.prefab, entry.scale > 0f ? entry.scale : 1f, seconds, ScreenMarker(slot));
    }

    private static string ScreenMarker(LichVfxSlot slot) => slot switch
    {
        LichVfxSlot.ScreenDebuff    => "LichScreen_Debuff",
        LichVfxSlot.ScreenMagicFlow => "LichScreen_MagicFlow",
        LichVfxSlot.ScreenSpace     => "LichScreen_Space",
        LichVfxSlot.ScreenWind      => "LichScreen_Wind",
        _                           => "LichScreen_Other",
    };

    /// <summary>
    /// 광선 한 번 — <paramref name="from"/>에서 <paramref name="to"/>까지. 선 렌더러가 있으면 두 끝점을 옮기고,
    /// 없으면 목록의 beamLength(배율 1일 때 +Z 길이)로 Z축만 늘인다. 굵기는 <paramref name="widthScale"/>배.
    /// </summary>
    public static GameObject PlayBeam(LichVfxSlot slot, Vector3 from, Vector3 to, float widthScale, float lifetime)
    {
        Vector3 delta = to - from;
        float   len   = delta.magnitude;
        if (len < 0.01f) return null;

        var go = Spawn(slot, from, Quaternion.LookRotation(delta / len), widthScale, null, false, null, out var entry);
        if (go == null) return null;

        // 팩 광선 드라이버(PixPlays) — 몸통 길이·끝 폭발을 스스로 맞추고 수명 뒤 스스로 파괴한다.
        if (go.TryGetComponent<PixPlays.ElementalVFX.BaseVfx>(out var driver))
        {
            go.transform.localScale = entry.prefab.transform.localScale;
            driver.Play(new PixPlays.ElementalVFX.VfxData(from, to, Mathf.Max(0.05f, lifetime), widthScale));
            return go;
        }

        bool hasLine = false;
        foreach (var lr in go.GetComponentsInChildren<LineRenderer>())
        {
            hasLine          = true;
            lr.positionCount = 2;
            lr.SetPosition(0, lr.useWorldSpace ? from : lr.transform.InverseTransformPoint(from));
            lr.SetPosition(1, lr.useWorldSpace ? to   : lr.transform.InverseTransformPoint(to));
        }
        if (!hasLine && entry.beamLength > 0f)
        {
            var s = go.transform.localScale;
            s.z = entry.prefab.transform.localScale.z * len / entry.beamLength;
            go.transform.localScale = s;
        }

        ReleaseAfter(go, Mathf.Max(0.05f, lifetime));
        return go;
    }

    /// <summary>
    /// 움직이는 광선 — 두 끝을 <paramref name="from"/> · <paramref name="to"/> 트랜스폼이 매 프레임 정한다(쓸고 도는 격류).
    /// 팩 광선 드라이버(PixPlays)만 따라간다. 없으면 지금 두 점으로 한 번 그린다.
    /// </summary>
    public static GameObject PlayBeamTracked(LichVfxSlot slot, Transform from, Transform to, float widthScale, float lifetime)
    {
        if (from == null || to == null) return null;
        Vector3 delta = to.position - from.position;
        if (delta.sqrMagnitude < 0.0001f) delta = Vector3.forward;

        var go = Spawn(slot, from.position, Quaternion.LookRotation(delta.normalized), widthScale, null, false, null, out var entry);
        if (go == null) return null;

        if (go.TryGetComponent<PixPlays.ElementalVFX.BaseVfx>(out var driver))
        {
            go.transform.localScale = entry.prefab.transform.localScale;
            driver.Play(new PixPlays.ElementalVFX.VfxData(from, to, Mathf.Max(0.05f, lifetime), widthScale));
            return go;
        }

        ReleaseAfter(go, Mathf.Max(0.05f, lifetime));
        return go;
    }

    /// <summary>반복 재생 — 한 번짜리 입자도 <see cref="Stop"/>할 때까지 다시 뿜는다.</summary>
    public static GameObject PlayLoop(LichVfxSlot slot, Vector3 position, Quaternion rotation,
                                      float scale = 1f, Transform parent = null)
        => Spawn(slot, position, rotation, scale, parent, true, null, out _);

    /// <summary>반복 재생 — 칸의 색 대신 <paramref name="tint"/>.</summary>
    public static GameObject PlayLoopTinted(LichVfxSlot slot, Vector3 position, Quaternion rotation, float scale, Color tint)
        => Spawn(slot, position, rotation, scale, null, true, tint, out _);

    /// <summary>
    /// 목록 밖 칸을 직접 재생 — 런 공용 목록(RunFx)처럼 다른 목록이 같은 재생기(풀 · 색 입히기 · 크기 보정)를 쓴다.
    /// 한 번 재생이면 수명 뒤 거두고, 반복이면 <see cref="Stop"/>할 때까지 남는다.
    /// </summary>
    public static GameObject PlayEntry(in LichVfxEntry entry, Vector3 position, Quaternion rotation,
                                       float scale, Color? tint, bool loop, Transform parent = null)
    {
        if (entry.prefab == null) return null;
        var go = SpawnEntry(entry, position, rotation, scale, parent, loop, tint);
        if (go == null || loop) return go;

        float life = entry.lifetime > 0f ? entry.lifetime : BossEffectPool.CalculateLifetime(entry.prefab, 2f);
        ReleaseAfter(go, life);
        return go;
    }

    /// <summary>
    /// 거둔다. <paramref name="fadeSeconds"/> &gt; 0이면 방출만 멈추고 남은 입자가 사라진 뒤 거둔다.
    /// </summary>
    public static void Stop(ref GameObject instance, float fadeSeconds = 0f)
    {
        if (instance == null) return;

        if (fadeSeconds > 0f && instance.activeInHierarchy)
        {
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>())
                ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            ReleaseAfter(instance, fadeSeconds);
        }
        else
        {
            BossEffectPool.Release(instance);   // 풀 밖 인스턴스는 여기서 파괴된다
        }
        instance = null;
    }

    private static void ReleaseAfter(GameObject go, float seconds)
    {
        if (go.TryGetComponent<BossPooledEffect>(out _))
        {
            BossEffectPool.ScheduleRelease(go, seconds);
        }
        else
        {
            // 부모(손 등)에 붙인 채 부모가 먼저 사라져도 남지 않게 월드로 떼지 않는다 — 부모와 함께 파괴된다.
            Object.Destroy(go, seconds);
        }
    }

    private static GameObject Spawn(LichVfxSlot slot, Vector3 position, Quaternion rotation,
                                    float scale, Transform parent, bool loop, Color? tint, out LichVfxEntry entry)
    {
        entry = default;
        if (s_set == null || !s_set.TryGet(slot, out entry)) return null;
        return SpawnEntry(entry, position, rotation, scale, parent, loop, tint);
    }

    private static GameObject SpawnEntry(in LichVfxEntry entry, Vector3 position, Quaternion rotation,
                                         float scale, Transform parent, bool loop, Color? tint)
    {
        Vector3 pos = position + rotation * entry.offset;
        var go = IsFreshOnly(entry.prefab)
            ? Object.Instantiate(entry.prefab, pos, rotation, parent)
            : BossEffectPool.Spawn(entry.prefab, pos, rotation, parent);
        if (go == null) return null;

        if (!go.TryGetComponent<LichVfxScaleCache>(out var cache))
            cache = go.AddComponent<LichVfxScaleCache>();
        cache.Prepare(loop);
        cache.ApplyTint(tint ?? entry.tint);

        float s =(entry.scale > 0f ? entry.scale : 1f) * scale;
        go.transform.localScale = entry.prefab.transform.localScale * s;
        // 팩마다 파티클 크기 모드가 Local이라 transform 크기를 무시하는 경우가 있다 — 한 번만 Hierarchy로 맞춘다.
        if (!Mathf.Approximately(s, 1f)) cache.EnsureHierarchyScaling();
        return go;
    }

    /// <summary>스스로를 파괴하는 팩 스크립트가 있으면 풀에 넣지 않는다(프리팹당 한 번 검사).</summary>
    private static bool IsFreshOnly(GameObject prefab)
    {
        int key = prefab.GetInstanceID();
        if (!s_freshOnly.TryGetValue(key, out bool fresh))
        {
            fresh = prefab.GetComponentInChildren<global::NewMaterialChange>(true) != null
                 || prefab.GetComponentInChildren<global::DestroyObject>(true) != null
                 || prefab.GetComponentInChildren<PixPlays.ElementalVFX.BaseVfx>(true) != null;
            s_freshOnly[key] = fresh;
        }
        return fresh;
    }
}
}
