using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 2페이지 무대 변화 — 영구 위험 구역(09-28 설계 확정 §2 · §6 「영구 · 상한」).
///   · <see cref="CreateEdgeBand"/> : 아레나(사각) 가장자리 띠 — 숲 가시 뿌리 · 화룡 흑염.
///   · <see cref="CreateRect"/>     : 한 사각 구역 — 기사 가운데 3×3 붕괴.
///   · <see cref="CreateLine"/>     : 바닥 줄(방향 있는 사각 · 수명 있음) — 숲 악몽 특성 「흔적」(돌진이 지나간 길).
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
    // 흔적 줄(Line)
    private const float LineGrowIn     = 0.25f;  // 시작 → 끝으로 자라나는 시간(알파도 함께 차오름)
    private const float LineFadeOut    = 0.4f;   // 수명 끝에 옅어지는 시간 — 이 동안은 피해 없음
    private const float LineVfxSpacing = 2f;
    private const int   MaxLineVfx     = 16;
    private const float LineVfxPop     = 0.5f;   // 자라는 줄 끝이 이만큼 지나가면 가시가 다 돋는다 (m)

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static Material s_bandMat;

    // ── Private ────────────────────────────────────────────────
    private enum Shape { EdgeBand, Rect, Line }

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

    // Line(흔적 줄) — 시작점 · 방향(XZ 단위) · 길이 · 드러난 길이
    private Vector3     _lineFrom;
    private Vector3     _lineDir;
    private float       _lineLength;
    private float       _lineShown;
    private float       _lineHalfWidth;
    private float       _lineVfxStep;
    private float       _lifetime;
    private float       _age;
    private bool        _lineFading;
    private Transform   _lineStrip;
    private MeshRenderer _lineRenderer;
    private MaterialPropertyBlock _lineMpb;
    private readonly List<ParticleSystem> _lineParticles = new();

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

    /// <summary>
    /// 바닥 줄(방향 있는 사각) — <paramref name="from"/> → <paramref name="to"/>(XZ)를 따라 폭 <paramref name="width"/>.
    /// <see cref="LineGrowIn"/> 동안 시작 → 끝으로 자라나고, <paramref name="lifetime"/> 끝 <see cref="LineFadeOut"/> 동안 옅어진 뒤 스스로 사라진다.
    /// 피해 판정은 다 드러난 뒤부터 옅어지기 전까지(띠와 같은 0.5초 틱).
    /// </summary>
    public static BossStageHazard CreateLine(MonsterBase boss, Vector3 from, Vector3 to, float floorY, float width, float lifetime,
                                             Color color, GameObject vfxPrefab, float vfxScale, float damageMultPerTick)
    {
        var h = Create("~BossStageHazard_Line", boss, color, vfxPrefab, vfxScale, damageMultPerTick);
        Vector3 d = to - from;
        d.y = 0f;
        h._shape         = Shape.Line;
        h._floorY        = floorY;
        h._lineFrom      = new Vector3(from.x, floorY, from.z);
        h._lineLength    = d.magnitude;
        h._lineDir       = h._lineLength > 0.001f ? d / h._lineLength : Vector3.forward;
        h._lineHalfWidth = Mathf.Max(0.05f, width * 0.5f);
        h._lifetime      = Mathf.Max(LineGrowIn + LineFadeOut, lifetime);
        h._shownBand     = 1f;           // 띠 · 구역의 자라기 경로는 건너뛴다 — 줄은 TickLine이 키운다
        h._tick          = LineGrowIn;   // 첫 피해 판정은 다 드러난 뒤
        h.BuildLine();
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
        if (_shape == Shape.Line) return LineContains(p);
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
        if (_shape == Shape.Line && !TickLine()) return;   // 흔적 줄 — 수명이 다해 치웠다

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

    /// <summary>흔적 줄 — 바닥 판 하나(길이 · 알파는 <see cref="PlaceLine"/>가 바꾼다) + 줄을 따라 가시(처음엔 크기 0, 줄이 닿으면 돋는다).</summary>
    private void BuildLine()
    {
        AddStrip(_lineFrom.x, _lineFrom.z, _lineHalfWidth * 2f, 0.01f);
        _lineStrip = _strips[_strips.Count - 1].transform;
        _lineStrip.rotation = Quaternion.Euler(90f, Mathf.Atan2(_lineDir.x, _lineDir.z) * Mathf.Rad2Deg, 0f);   // 판의 세로(로컬 Y) = 줄 방향
        _lineRenderer = _lineStrip.GetComponent<MeshRenderer>();
        _lineMpb      = new MaterialPropertyBlock();

        if (_vfxPrefab != null)
        {
            int n = Mathf.Clamp(Mathf.FloorToInt(_lineLength / LineVfxSpacing), 1, MaxLineVfx);
            _lineVfxStep = _lineLength / n;
            for (int i = 0; i < n; i++)
            {
                SpawnOneVfx(_lineFrom + _lineDir * (_lineVfxStep * (i + 0.5f)));
                foreach (var ps in _vfx[_vfx.Count - 1].GetComponentsInChildren<ParticleSystem>()) _lineParticles.Add(ps);
            }
        }
        PlaceLine(0f, 0f, 1f);
    }

    /// <summary>흔적 줄 한 프레임 — 자라남 → 머묾 → 옅어짐(가시는 오그라들고 파티클은 방출을 멈춘다). 수명이 다해 치웠으면 false.</summary>
    private bool TickLine()
    {
        _age += Time.deltaTime;
        if (_age >= _lifetime) { Destroy(gameObject); return false; }

        float grow = Mathf.Clamp01(_age / LineGrowIn);
        float fade = Mathf.Clamp01((_lifetime - _age) / LineFadeOut);
        if (grow >= 1f && fade >= 1f && _lineShown >= _lineLength) return true;   // 다 드러나 머무는 동안 — 바꿀 것 없음

        if (fade < 1f && !_lineFading)
        {
            _lineFading = true;
            foreach (var ps in _lineParticles)
                if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        PlaceLine(grow, Mathf.Min(grow, fade), fade);
        return true;
    }

    /// <summary>드러난 길이(<paramref name="grow"/>) · 바닥 알파 · 가시 크기(돋음 × <paramref name="vfxFade"/>)를 지금 값으로.</summary>
    private void PlaceLine(float grow, float alpha, float vfxFade)
    {
        _lineShown = _lineLength * grow;
        float len = Mathf.Max(0.01f, _lineShown);
        _lineStrip.position   = _lineFrom + _lineDir * (len * 0.5f) + Vector3.up * FloorLift;
        _lineStrip.localScale = new Vector3(_lineHalfWidth * 2f, len, 1f);
        _lineMpb.SetColor(BaseColorId, new Color(_color.r, _color.g, _color.b, _color.a * alpha));
        _lineRenderer.SetPropertyBlock(_lineMpb);

        for (int i = 0; i < _vfx.Count; i++)
        {
            if (_vfx[i] == null) continue;
            float pop = grow >= 1f ? 1f : Mathf.Clamp01((_lineShown - _lineVfxStep * (i + 0.5f)) / LineVfxPop);
            _vfx[i].transform.localScale = Vector3.one * (_vfxScale * pop * vfxFade);
        }
    }

    /// <summary>흔적 줄 안인가(XZ) — 시작점에서 드러난 길이까지 · 줄 축에서 반 폭 안. 옅어지는 동안은 아니다.</summary>
    private bool LineContains(Vector3 p)
    {
        if (_lineFading || _lineShown <= 0.01f) return false;
        float dx = p.x - _lineFrom.x, dz = p.z - _lineFrom.z;
        float along = dx * _lineDir.x + dz * _lineDir.z;              // 줄 방향 성분
        if (along < 0f || along > _lineShown) return false;
        return Mathf.Abs(dx * _lineDir.z - dz * _lineDir.x) <= _lineHalfWidth;   // 줄 축에서 옆으로 떨어진 거리
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
