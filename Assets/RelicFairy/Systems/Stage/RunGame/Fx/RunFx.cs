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

    private const float VividSaturate = 0.5f;    // 채도를 1까지 남은 거리의 절반만큼 올린다
    private const float VividValue    = 0.45f;   // 밝기 배율 — 발광 3 × 0.45 ≈ 1.35(톤매핑에서 빛깔이 남는다)
    private const float VividMinSaturation = 0.15f;   // 이보다 옅은 색은 흰빛으로 보고 손대지 않는다(f9 기준 0.05를 포함 — 0.05~0.15도 예전엔 밝기만 낮아져 회색이 됐다)

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
    /// <param name="vivid">칸의 「진한 빛깔」(vividTint)을 따를지 — 색 자체가 정해진 정보인 호출(기사 검 색 고리: 흰 검 = 흰 고리 ·
    /// 검은 검 = 어두운 바닥에서 읽히게 고른 보라)은 false로 받은 색을 그대로 쓴다(10-11 f9 요청).</param>
    public static GameObject Play(RunFxSlot slot, Vector3 position, float scale, Color tint, bool vivid = true)
        => Spawn(slot, position, Quaternion.identity, scale, tint, false, vivid);

    /// <summary>반복 재생 — <see cref="Stop"/>할 때까지 남는다.</summary>
    public static GameObject PlayLoop(RunFxSlot slot, Vector3 position, float scale, Color tint)
        => Spawn(slot, position, Quaternion.identity, scale, tint, true, true);

    /// <summary>방향을 정해 반복 재생 — 세워 두는 이펙트(포탈)용. 원래 빛깔 그대로면 <see cref="Color.clear"/>.</summary>
    public static GameObject PlayLoop(RunFxSlot slot, Vector3 position, Quaternion rotation, float scale, Color tint)
        => Spawn(slot, position, rotation, scale, tint, true, true);

    /// <summary>거둔다. <paramref name="fadeSeconds"/> &gt; 0이면 방출만 멈추고 남은 입자가 사라진 뒤 거둔다.</summary>
    public static void Stop(ref GameObject instance, float fadeSeconds = 0f) => LichVfx.Stop(ref instance, fadeSeconds);

    private static GameObject Spawn(RunFxSlot slot, Vector3 position, Quaternion rotation, float scale, Color tint, bool loop, bool vivid)
    {
        if (s_set == null || !s_set.TryGet(slot, out var e)) return null;
        if (e.vividTint && vivid) tint = Vivid(tint);
        var entry = new LichVfxEntry { prefab = e.prefab, scale = e.scale, offset = e.offset, lifetime = e.lifetime };
        // 재채색 칸은 곱하기 틴트를 건너뛰고(원래 색 그대로 꺼내) 빛깔만 갈아 끼운다(10-02 보상 등급 빛기둥)
        var go = LichVfx.PlayEntry(entry, position, rotation, scale, e.recolor ? Color.clear : tint, loop);
        if (go != null)
        {
            if (e.recolor) RunFxRecolor.Apply(go, tint);
            else if (tint.a > 0f) TrailsFollowTint(go);
            TintLights(go, tint);
            StripRefraction(go);
            if (!string.IsNullOrEmpty(e.mute)) Mute(go, e.mute);
        }
        return go;
    }

    /// <summary>
    /// 꼬리(트레일)가 입자 색을 물려받게 한다 — 곱하기 틴트는 입자 시작 색에만 들어가는데, 고리 칸(Buff white twist)의 본체 꼬리가
    /// inheritParticleColor 꺼짐이라 늘 흰빛이었다(10-12 3차 촬영: 점 입자만 물들고 고리는 셋 다 흰 청록).
    /// 풀에 돌아가도 켠 채 두어도 된다 — 원래 빛깔로 다시 꺼내면 입자 시작 색이 원본이라 꼬리도 원본 그대로다.
    /// </summary>
    private static void TrailsFollowTint(GameObject go)
    {
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var trails = ps.trails;
            if (trails.enabled && !trails.inheritParticleColor) trails.inheritParticleColor = true;
        }
    }

    /// <summary>
    /// 진한 빛깔 — 채도를 올리고 밝기를 낮춘다. 발광 배율이 큰 재질에서 옅은 색이 흰빛으로 바래지 않게(10-11 2차 촬영).
    /// 재채색 칸은 빛깔(색상 · 채도)만 쓰므로 밝기를 낮춘 것은 거기선 상관없다. 알파 0(원래 색)은 그대로.
    /// <b>흰빛 · 회색 · 흰빛에 가까운 색(채도 &lt; <see cref="VividMinSaturation"/>)은 색 · 밝기 그대로</b> — 채도 0이면 색상이 0(빨강)으로 잡혀
    /// 채도를 올리면 분홍이 되고, 밝기만 낮추면 흰 고리가 회색이 됐다(f9 확인 10-11).
    /// </summary>
    private static Color Vivid(Color c)
    {
        if (c.a <= 0f) return c;
        Color.RGBToHSV(c, out float h, out float s, out float v);
        if (s < VividMinSaturation) return c;
        s = Mathf.Lerp(s, 1f, VividSaturate);
        var o = Color.HSVToRGB(h, s, v * VividValue);
        o.a = c.a;
        return o;
    }

    /// <summary>
    /// 칸의 끌 자식(이름 목록)을 끈다 — 보상 등급 기둥의 큰 빛 번짐(Lens · Flare) · 포탈 입자 장 하나(10-11 톤 2차).
    /// 같은 프리팹은 이 칸에서만 꺼내므로 꺼 둔 채 풀에 돌아가도 된다(재사용마다 다시 꺼도 같다).
    /// </summary>
    private static void Mute(GameObject go, string names)
    {
        var all = go.GetComponentsInChildren<Transform>(true);
        foreach (var raw in names.Split(','))
        {
            string n = raw.Trim();
            if (n.Length == 0) continue;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name != n) continue;
                if (all[i] != go.transform) { all[i].gameObject.SetActive(false); continue; }
                // 뿌리 자신의 입자계(Hovl Gold dot의 금덩이처럼 뿌리 이름 = 입자계 이름) — 뿌리를 끄면 전부 꺼지니 그 렌더러만 끈다(풀 인스턴스 이름 = 프리팹 이름)
                if (go.TryGetComponent<ParticleSystemRenderer>(out var rootRenderer)) rootRenderer.enabled = false;
            }
        }
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

    /// <summary>
    /// 팩 이펙트에 든 점광원도 같은 빛깔로 — 입자만 금빛이고 바닥이 파랗게 비치는 일이 없게(풀 재사용마다 다시 입힌다).
    /// 맑은 색(알파 0)이면 원래 빛깔로 되돌린다 — 예전엔 그대로 두어 앞 사용의 색이 풀에서 남았다(10-11 톤 2차).
    /// </summary>
    private static void TintLights(GameObject go, Color tint)
    {
        if (!go.TryGetComponent<LightTint>(out var cache))
        {
            if (tint.a <= 0f) return;   // 한 번도 칠한 적 없다 — 원래 빛깔 그대로
            cache = go.AddComponent<LightTint>();
        }
        cache.Apply(tint);
    }

    /// <summary>점광원 원래 빛깔 기억(처음 칠할 때 한 번).</summary>
    [DisallowMultipleComponent]
    private sealed class LightTint : MonoBehaviour
    {
        private Light[] _lights;
        private Color[] _original;

        public void Apply(Color tint)
        {
            if (_lights == null)
            {
                _lights   = GetComponentsInChildren<Light>(true);
                _original = new Color[_lights.Length];
                for (int i = 0; i < _lights.Length; i++) _original[i] = _lights[i].color;
            }
            for (int i = 0; i < _lights.Length; i++)
                if (_lights[i] != null)
                    _lights[i].color = tint.a <= 0f ? _original[i] : new Color(tint.r, tint.g, tint.b, 1f);
        }
    }
}
