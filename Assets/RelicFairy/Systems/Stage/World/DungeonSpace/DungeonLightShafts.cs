using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// S4-3 「넓은 곳」 천장 틈 빛(10-03 설계 · 10-02 구현 설계 §1-3) — 전투방 · 이벤트방 · 보스방마다 빛줄기 2~4개 + 그 안을 떠도는 먼지.
/// <list type="bullet">
/// <item>위에서 비스듬히 바닥으로 떨어지는 부드러운 빛 띠(가산 · 엇갈린 판 두 장). 해 쪽으로 25° 기운다.</item>
/// <item>자리는 방 <b>먼 쪽 가장자리</b>(화면 위쪽 띠) 바닥 칸만 — 전투 한가운데를 가리지 않게.</item>
/// <item>빛깔은 챕터(<see cref="DungeonSpaceSetSO"/>) — 숲 연초록 · 화룡 붉은 · 기사 금청 · 꼭대기 금백.</item>
/// </list>
/// 텍스처는 코드로 만든 그라데이션(LFS 증가 0). 방 루트 아래에 붙어 방과 함께 꺼지고 사라진다. 콜라이더 · 그림자 없음.
/// 돔 고리(§1-3 첫 줄)는 짓지 않는다 — S4-1 뒤 허공 비율이 전 방 0%(10-02 실측)라 더 닫을 틈이 없다.
/// </summary>
public static class DungeonLightShafts
{
    // ── Constants ─────────────────────────────────────────────────
    private const float Tilt          = 25f;    // 수직에서 해 쪽으로 기우는 각
    private const float Width         = 2.6f;
    private const float AboveWall     = 5f;     // 벽 윗변 위로 더 뻗는다(틈이 벽 너머 높은 곳에 있는 듯) — 12 m는 높은 카메라 위까지 덮어 화면이 하얗게 번졌다(10-06)
    private const float EdgeInset     = 2.5f;   // 벽에서 안쪽으로(칸)
    private const float MinSpacing    = 6f;
    private const float ShaftAlpha    = 0.22f;   // 0.30은 여러 줄이 겹치는 입구 쪽 화면에서 하얗게 번졌다(10-06)
    private const int   DustMax       = 28;
    private const int   Tries         = 24;

    private static readonly int BaseMapId   = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private static Mesh      s_mesh;
    private static Texture2D s_tex;

    // ── Public Methods ────────────────────────────────────────────
    /// <param name="quarterTurns">진행 방향 0=북(+Z) 1=동 2=남 3=서 — 카메라가 그쪽을 본다(먼 쪽 = 화면 위).</param>
    public static void Build(TileType[,] grid, Transform room, float cellSize, float baseY, float wallTop,
                             int quarterTurns, System.Random rng)
    {
        if (grid == null || room == null) return;
        if (!DungeonSpaceDirector.TryGetShaftStyle(out var mat, out var color)) return;
        rng ??= new System.Random();

        int w = grid.GetLength(0), h = grid.GetLength(1);
        float offX = (w - 1) * 0.5f * cellSize, offZ = (h - 1) * 0.5f * cellSize;
        Vector3 fwd = (quarterTurns & 3) switch { 1 => Vector3.right, 2 => Vector3.back, 3 => Vector3.left, _ => Vector3.forward };
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        float halfDepth = (Mathf.Abs(fwd.z) > 0.5f ? h : w) * 0.5f * cellSize - EdgeInset * cellSize;
        float halfWidth = (Mathf.Abs(fwd.z) > 0.5f ? w : h) * 0.5f * cellSize - EdgeInset * cellSize;
        if (halfDepth <= 1f || halfWidth <= 1f) return;

        Vector3 axis = ShaftAxis(fwd);
        float length = (wallTop + AboveWall) / Mathf.Max(0.3f, axis.y);
        int want = 2 + rng.Next(3);   // 2~4
        var picked = new List<Vector3>(want);

        for (int i = 0; i < Tries && picked.Count < want; i++)
        {
            // 먼 쪽 40% 깊이 · 양옆 바깥 35% 폭(가운데 위 금지)
            float d = Mathf.Lerp(0.2f, 1f, (float)rng.NextDouble()) * halfDepth;
            float s = (rng.Next(2) == 0 ? -1f : 1f) * Mathf.Lerp(0.3f, 1f, (float)rng.NextDouble()) * halfWidth;
            Vector3 local = fwd * d + right * s;
            int cx = Mathf.RoundToInt((local.x + offX) / cellSize);
            int cz = Mathf.RoundToInt((local.z + offZ) / cellSize);
            if (cx < 0 || cz < 0 || cx >= w || cz >= h || grid[cx, cz] != TileType.Floor) continue;
            bool near = false;
            foreach (var p in picked) if ((p - local).sqrMagnitude < MinSpacing * MinSpacing) { near = true; break; }
            if (near) continue;
            picked.Add(local);
        }

        var parent = new GameObject("@LightShafts").transform;
        parent.SetParent(room, false);
        foreach (var local in picked)
            Spawn(parent, new Vector3(local.x, baseY, local.z), axis, length, mat, color, rng);
    }

    // ── Private Methods ───────────────────────────────────────────
    /// <summary>
    /// 빛줄기 축(바닥 → 위) — 진행 방향(<paramref name="away"/> = 카메라에서 먼 쪽)으로 <see cref="Tilt"/>° 기운다.
    /// 예전엔 해 쪽으로 기울여, 해가 카메라 쪽이면 위끝이 카메라 위를 덮어 전투방 입구 화면이 하얗게 번졌다(10-06 실측).
    /// </summary>
    private static Vector3 ShaftAxis(Vector3 away)
    {
        Vector3 hz = new Vector3(away.x, 0f, away.z);
        if (hz.sqrMagnitude < 0.001f) hz = Vector3.forward;
        hz.Normalize();
        float t = Tilt * Mathf.Deg2Rad;
        return (Vector3.up * Mathf.Cos(t) + hz * Mathf.Sin(t)).normalized;
    }

    private static void Spawn(Transform parent, Vector3 floorLocal, Vector3 axis, float length, Material mat, Color color,
                              System.Random rng)
    {
        var go = new GameObject("Shaft", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = floorLocal;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis)
                                   * Quaternion.Euler(0f, (float)rng.NextDouble() * 90f, 0f);
        go.transform.localScale    = new Vector3(Width, length, Width);
        go.GetComponent<MeshFilter>().sharedMesh = CrossMesh();
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial    = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows    = false;
        var mpb = new MaterialPropertyBlock();
        mpb.SetTexture(BaseMapId, GradientTexture());
        mpb.SetColor(BaseColorId, new Color(color.r, color.g, color.b, ShaftAlpha));
        r.SetPropertyBlock(mpb);

        AddDust(parent, floorLocal, axis, length, mat, color);
    }

    /// <summary>빛줄기 아래쪽 절반을 떠도는 먼지 — 느리게 오르내린다.</summary>
    private static void AddDust(Transform parent, Vector3 floorLocal, Vector3 axis, float length, Material mat, Color color)
    {
        var go = new GameObject("Dust");
        go.transform.SetParent(parent, false);
        float span = Mathf.Min(length * 0.5f, 9f);
        go.transform.localPosition = floorLocal + axis * (span * 0.5f + 0.3f);
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration        = 5f;
        main.loop            = true;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(4f, 7f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(0.02f, 0.10f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
        main.startColor      = new Color(Mathf.Lerp(color.r, 1f, 0.4f), Mathf.Lerp(color.g, 1f, 0.4f), Mathf.Lerp(color.b, 1f, 0.4f), 0.55f);
        main.maxParticles    = DustMax;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.prewarm         = true;
        main.playOnAwake     = true;

        var emission = ps.emission;
        emission.rateOverTime = DustMax / 6f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale     = new Vector3(Width * 0.6f, span, Width * 0.6f);

        var noise = ps.noise;
        noise.enabled   = true;
        noise.strength  = 0.15f;
        noise.frequency = 0.3f;

        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;

        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial    = mat;
        pr.shadowCastingMode = ShadowCastingMode.Off;
        pr.receiveShadows    = false;
        ps.Play(true);
    }

    /// <summary>엇갈린 판 두 장(바닥 y=0 → 위 y=1). 재질이 양면(Cull Off)이라 뒷면을 따로 안 만든다.</summary>
    private static Mesh CrossMesh()
    {
        if (s_mesh != null) return s_mesh;
        var v = new[]
        {
            new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f),
            new Vector3(0f, 0f, -0.5f), new Vector3(0f, 0f, 0.5f), new Vector3(0f, 1f, -0.5f), new Vector3(0f, 1f, 0.5f),
        };
        var uv = new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f),
        };
        s_mesh = new Mesh { name = "LightShaft_Cross", vertices = v, uv = uv, triangles = new[] { 0, 2, 1, 1, 2, 3, 4, 6, 5, 5, 6, 7 } };
        s_mesh.RecalculateNormals();
        s_mesh.RecalculateBounds();
        return s_mesh;
    }

    /// <summary>빛 띠 그라데이션 — 가로는 가운데가 밝고 가장자리로 사라지며, 세로는 위(틈)가 밝고 바닥 쪽으로 옅어진다.</summary>
    private static Texture2D GradientTexture()
    {
        if (s_tex != null) return s_tex;
        const int W = 32, H = 128;
        s_tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "LightShaft_Gradient", wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        {
            float v = y / (H - 1f);
            float vert = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.25f, v))      // 바닥에서 솟아오름
                       * Mathf.Lerp(0.45f, 1f, v)                                          // 위가 밝다
                       * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.85f, 1f, v))); // 맨 위 끝은 흐리게
            for (int x = 0; x < W; x++)
            {
                float u = x / (W - 1f);
                float horiz = Mathf.Pow(Mathf.Sin(u * Mathf.PI), 2f);
                byte a = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(vert * horiz));
                px[y * W + x] = new Color32(255, 255, 255, a);
            }
        }
        s_tex.SetPixels32(px);
        s_tex.Apply(false, true);
        return s_tex;
    }
}
