using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 2페이지 무대 변화 — 영구 위험 구역(09-28 설계 확정 §2 · §6 「영구 · 상한」).
///   · <see cref="CreateEdgeBand"/> : 아레나(사각) 가장자리 띠 — 숲 가시 뿌리 · 화룡 흑염.
///   · <see cref="CreateRect"/>     : 한 사각 구역 — 기사 가운데 3×3 붕괴.
/// 들어가 있으면 <see cref="tickSeconds"/>마다 피해(보스 공격력 × 배율, 약한 피격). 예고색 바닥 띠(가이드 데칼) +
/// 선택 이펙트 프리팹을 띠를 따라 늘어놓는다. <paramref name="growSeconds"/> 동안 안쪽으로 자라며 드러나고, 보스가 죽으면 사라진다.
/// 띠 폭은 <see cref="Widen"/>으로 한 번 더 넓힐 수 있다(숲 간판 실패).
/// </summary>
public sealed class BossStageHazard : MonoBehaviour
{
    // ── Constants ──────────────────────────────────────────────
    private const float VfxSpacing  = 3.5f;
    private const int   MaxVfx      = 56;
    private const float FloorLift   = 0.2f;    // 가이드와 같은 높이 — 바닥 타일 위(09-28 R4)
    private const float RebuildStep = 0.25f;   // 자라는 동안 이만큼 넓어질 때마다 다시 깐다

    private static Material s_bandMat;

    // ── Private ────────────────────────────────────────────────
    private enum Shape { EdgeBand, Rect }

    private Shape       _shape;
    private Bounds      _arena;          // EdgeBand: 아레나 XZ 경계 / Rect: 구역
    private float       _band;           // EdgeBand 현재 목표 폭
    private float       _shownBand;      // 자라는 중인 폭
    private float       _builtBand = -1f;
    private float       _growSpeed;
    private float       _floorY;
    private Color       _color;
    private GameObject  _vfxPrefab;
    private float       _vfxScale = 1f;
    private MonsterBase _boss;
    private float       _damageMult;
    private float       tickSeconds = 0.5f;
    private float       _tick;
    private readonly List<GameObject> _strips = new();
    private readonly List<GameObject> _vfx    = new();

    // ── Properties ─────────────────────────────────────────────
    /// <summary>EdgeBand의 안쪽 안전 경계(XZ) — 패턴이 무언가를 둘 자리를 고를 때.</summary>
    public Bounds SafeInner
    {
        get
        {
            if (_shape != Shape.EdgeBand) return _arena;
            var b = _arena;
            b.Expand(new Vector3(-_band * 2f, 0f, -_band * 2f));
            return b;
        }
    }

    // ── Public Methods ─────────────────────────────────────────

    /// <summary>아레나 가장자리 띠. <paramref name="arenaXZ"/> = 아레나 바닥 XZ 경계(y는 무시).</summary>
    public static BossStageHazard CreateEdgeBand(MonsterBase boss, Bounds arenaXZ, float floorY, float bandWidth, float growSeconds,
                                                 Color color, GameObject vfxPrefab, float vfxScale, float damageMultPerTick)
    {
        var h = Create("~BossStageHazard_Edge", boss, color, vfxPrefab, vfxScale, damageMultPerTick);
        h._shape     = Shape.EdgeBand;
        h._arena     = arenaXZ;
        h._floorY    = floorY;
        h._band      = Mathf.Max(0.5f, bandWidth);
        h._shownBand = 0f;
        h._growSpeed = h._band / Mathf.Max(0.1f, growSeconds);
        h.RebuildVisuals();
        return h;
    }

    /// <summary>사각 구역(축 정렬). <paramref name="rect"/> = 구역 XZ 경계.</summary>
    public static BossStageHazard CreateRect(MonsterBase boss, Bounds rect, float floorY, float growSeconds,
                                             Color color, GameObject vfxPrefab, float vfxScale, float damageMultPerTick)
    {
        var h = Create("~BossStageHazard_Rect", boss, color, vfxPrefab, vfxScale, damageMultPerTick);
        h._shape     = Shape.Rect;
        h._arena     = rect;
        h._floorY    = floorY;
        h._band      = 1f;
        h._shownBand = 0f;
        h._growSpeed = 1f / Mathf.Max(0.1f, growSeconds);
        h.RebuildVisuals();
        return h;
    }

    /// <summary>띠를 <paramref name="extra"/> m 넓힌다(영구 · 한 번에 자라남).</summary>
    public void Widen(float extra)
    {
        if (_shape != Shape.EdgeBand || extra <= 0f) return;
        _band += extra;
    }

    /// <summary>이 점이 위험 구역 안인가(XZ).</summary>
    public bool Contains(Vector3 p)
    {
        if (_shape == Shape.Rect)
            return _shownBand >= 0.99f && p.x >= _arena.min.x && p.x <= _arena.max.x && p.z >= _arena.min.z && p.z <= _arena.max.z;
        float b = _shownBand;
        if (b <= 0.01f) return false;
        bool insideArena = p.x >= _arena.min.x && p.x <= _arena.max.x && p.z >= _arena.min.z && p.z <= _arena.max.z;
        bool insideInner = p.x >= _arena.min.x + b && p.x <= _arena.max.x - b && p.z >= _arena.min.z + b && p.z <= _arena.max.z - b;
        return insideArena && !insideInner;
    }

    // ── Lifecycle ──────────────────────────────────────────────
    private void Update()
    {
        if (_boss == null || _boss.IsDead) { Destroy(gameObject); return; }

        float target = _shape == Shape.EdgeBand ? _band : 1f;
        if (_shownBand < target)
        {
            _shownBand = Mathf.MoveTowards(_shownBand, target, _growSpeed * Time.deltaTime);
            if (Mathf.Abs(_shownBand - _builtBand) >= RebuildStep * (_shape == Shape.Rect ? 0.2f : 1f) || _shownBand >= target)
                RebuildVisuals();
        }

        _tick -= Time.deltaTime;
        if (_tick > 0f) return;
        _tick = tickSeconds;
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player == null || !Contains(player.transform.position)) return;
        float atk = _boss.EffectiveAttackPower;
        int dmg = Mathf.Max(1, Mathf.RoundToInt((atk > 0f ? atk : 10f) * _damageMult));
        player.TakeDamage(dmg, _boss.gameObject, false, HitWeight.Light);
    }

    private void OnDestroy()
    {
        foreach (var s in _strips) if (s != null) Destroy(s);
        foreach (var v in _vfx) if (v != null) Destroy(v);
    }

    // ── Private Methods ────────────────────────────────────────
    private static BossStageHazard Create(string name, MonsterBase boss, Color color, GameObject vfxPrefab, float vfxScale, float damageMult)
    {
        var go = new GameObject(name);
        var h  = go.AddComponent<BossStageHazard>();
        h._boss       = boss;
        h._color      = color;
        h._vfxPrefab  = vfxPrefab;
        h._vfxScale   = vfxScale;
        h._damageMult = damageMult;
        return h;
    }

    /// <summary>바닥 띠(반투명 사각 판)와 이펙트를 지금 폭에 맞춰 다시 깐다.</summary>
    private void RebuildVisuals()
    {
        _builtBand = _shownBand;
        foreach (var st in _strips) if (st != null) Destroy(st);
        _strips.Clear();

        if (_shape == Shape.Rect)
        {
            float t = Mathf.Clamp01(_shownBand);
            if (t <= 0.01f) return;
            AddStrip(_arena.center.x, _arena.center.z, _arena.size.x * t, _arena.size.z * t);
            if (t >= 0.99f && _vfx.Count == 0) SpawnVfxGrid(_arena);
            return;
        }

        float b = _shownBand;
        if (b <= 0.05f) return;
        var a = _arena;
        float w = a.size.x, d = a.size.z;
        // 네 변 — 북 · 남은 전체 폭, 동 · 서는 북남 띠 사이만.
        AddStrip(a.center.x, a.max.z - b * 0.5f, w, b);
        AddStrip(a.center.x, a.min.z + b * 0.5f, w, b);
        AddStrip(a.min.x + b * 0.5f, a.center.z, b, Mathf.Max(0.1f, d - b * 2f));
        AddStrip(a.max.x - b * 0.5f, a.center.z, b, Mathf.Max(0.1f, d - b * 2f));

        if (_vfxPrefab != null && b >= _band - 0.01f) RespawnEdgeVfx(b);
    }

    /// <summary>반투명 바닥 판 하나(축 정렬 XZ 사각).</summary>
    private void AddStrip(float cx, float cz, float sizeX, float sizeZ)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = "HazardStrip";
        if (q.TryGetComponent<Collider>(out var col)) Destroy(col);
        q.transform.SetParent(transform, false);
        q.transform.position   = new Vector3(cx, _floorY + FloorLift, cz);
        q.transform.rotation   = Quaternion.Euler(90f, 0f, 0f);
        q.transform.localScale = new Vector3(sizeX, sizeZ, 1f);
        var mr = q.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;
        mr.sharedMaterial    = BandMaterial;
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", _color);
        mr.SetPropertyBlock(mpb);
        _strips.Add(q);
    }

    /// <summary>URP Unlit 반투명 공유 재질(색은 MPB).</summary>
    private static Material BandMaterial
    {
        get
        {
            if (s_bandMat != null) return s_bandMat;
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            s_bandMat = new Material(sh) { name = "BossStageHazardBand" };
            s_bandMat.SetFloat("_Surface", 1f);   // Transparent
            s_bandMat.SetFloat("_Blend", 0f);     // Alpha
            s_bandMat.SetOverrideTag("RenderType", "Transparent");
            s_bandMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            s_bandMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            s_bandMat.SetInt("_ZWrite", 0);
            s_bandMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            s_bandMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return s_bandMat;
        }
    }

    private void RespawnEdgeVfx(float b)
    {
        foreach (var v in _vfx) if (v != null) Destroy(v);
        _vfx.Clear();
        var a = _arena;
        float inset = b * 0.5f;
        var pts = new List<Vector3>();
        for (float x = a.min.x + inset; x <= a.max.x - inset && pts.Count < MaxVfx; x += VfxSpacing)
        {
            pts.Add(new Vector3(x, _floorY, a.max.z - inset));
            pts.Add(new Vector3(x, _floorY, a.min.z + inset));
        }
        for (float z = a.min.z + b + VfxSpacing * 0.5f; z <= a.max.z - b && pts.Count < MaxVfx; z += VfxSpacing)
        {
            pts.Add(new Vector3(a.min.x + inset, _floorY, z));
            pts.Add(new Vector3(a.max.x - inset, _floorY, z));
        }
        foreach (var p in pts) SpawnOneVfx(p);
    }

    private void SpawnVfxGrid(Bounds r)
    {
        if (_vfxPrefab == null) return;
        for (float x = r.min.x + 1f; x <= r.max.x - 0.5f && _vfx.Count < MaxVfx; x += VfxSpacing * 0.6f)
            for (float z = r.min.z + 1f; z <= r.max.z - 0.5f && _vfx.Count < MaxVfx; z += VfxSpacing * 0.6f)
                SpawnOneVfx(new Vector3(x, _floorY, z));
    }

    private void SpawnOneVfx(Vector3 p)
    {
        var fx = Instantiate(_vfxPrefab, p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), transform);
        fx.transform.localScale = Vector3.one * _vfxScale;
        foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>())
        {
            var main = ps.main;
            main.loop = true;   // 영구 무대 — 한 번 터지고 끝나는 이펙트도 계속 돌게
            if (!ps.isPlaying) ps.Play();
        }
        foreach (var col in fx.GetComponentsInChildren<Collider>()) col.enabled = false;   // 시각 전용
        // 메시 이펙트(숲 가시 spike_mesh — 원래 흰 원뿔)는 띠 색으로 칠한다. 파티클은 그대로.
        var meshes = fx.GetComponentsInChildren<MeshRenderer>();
        if (meshes.Length > 0)
        {
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", new Color(_color.r, _color.g, _color.b, 1f));
            foreach (var mr in meshes) mr.SetPropertyBlock(mpb);
        }
        _vfx.Add(fx);
    }
}
}
