using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RelicFairy.Monster
{
/// <summary>
/// 제단 균열 흔적 — 마법이 떨어진 자리에 남는 바닥 균열(설계서 §5 「균열 데칼」, 웅장함 3축의 「흔적」).
/// 바닥에 눕힌 쿼드 + 균열 재질(<see cref="LichVfx.CrackMaterial"/>). 보라로 빛나다가(<see cref="GlowSeconds"/>)
/// 어두운 금으로 식고, 수명 끝에 옅어져 사라진다. 제단은 평평한 타일이라 투영 데칼 대신 쿼드로 충분하다(전 품질 단계 동작).
/// 동시에 <see cref="MaxCracks"/>개까지 — 넘치면 가장 오래된 것부터 거둔다.
/// (10-03) 바닥 칸에 붙는 균열(무너진 구멍 가장자리)은 따로 <see cref="MaxAttached"/>개까지.
/// </summary>
public sealed class LichCrack : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    private const int   MaxCracks    = 48;
    private const int   MaxAttached  = 128;   // (10-03) 칸에 붙는 가장자리 균열 — 오래 남아 마법 자국을 밀어내지 않게 따로 센다
    private const float GlowSeconds  = 0.6f;
    private const float FadeSeconds  = 1.5f;
    private const float FloorLift    = 0.035f;

    private static readonly int   ColorId   = Shader.PropertyToID("_Color");
    private static readonly Color GlowColor = new Color(1.3f, 0.55f, 2.2f, 1f);   // HDR 보라
    private static readonly Color RestColor = new Color(0.35f, 0.18f, 0.45f, 0.85f);

    private static readonly Queue<LichCrack> s_live = new();
    private static readonly Queue<LichCrack> s_attached = new();
    private static Mesh                  s_quad;
    private static MaterialPropertyBlock s_mpb;

    // ── Private ───────────────────────────────────────────────────
    private MeshRenderer _renderer;
    private float        _age;
    private float        _life;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_live.Clear();
        s_attached.Clear();
        s_quad = null;
        s_mpb  = null;
    }

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Update()
    {
        _age += Time.deltaTime;
        if (_age >= _life)
        {
            Destroy(gameObject);
            return;
        }

        Color c;
        if (_age < GlowSeconds)
            c = Color.Lerp(GlowColor, RestColor, _age / GlowSeconds);
        else
            c = RestColor;

        float fadeStart = _life - FadeSeconds;
        if (_age > fadeStart) c.a *= 1f - (_age - fadeStart) / FadeSeconds;
        Apply(c);
    }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>
    /// <paramref name="floorPos"/>(바닥 높이)에 지름 <paramref name="size"/> m 균열. <paramref name="seconds"/> 뒤 사라진다.
    /// 재질이 없으면 아무것도 하지 않는다.
    /// (10-03) <paramref name="parent"/>를 주면 그 밑에 붙는다 — 무너진 바닥 가장자리 흔적이 그 칸과 함께 흔들리고 떨어진다.
    /// </summary>
    public static void Spawn(Vector3 floorPos, float size, float seconds = 8f, Transform parent = null)
    {
        var mat = LichVfx.CrackMaterial;
        if (mat == null || size <= 0f) return;

        var live = parent != null ? s_attached : s_live;
        while (live.Count >= (parent != null ? MaxAttached : MaxCracks))
        {
            var old = live.Dequeue();
            if (old != null) Destroy(old.gameObject);
        }

        var go = new GameObject("LichCrack");
        go.transform.SetPositionAndRotation(floorPos + Vector3.up * (FloorLift + Random.value * 0.005f),
                                            Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
        go.transform.localScale = new Vector3(size, size, 1f);
        if (parent != null) go.transform.SetParent(parent, true);
        go.AddComponent<MeshFilter>().sharedMesh = Quad;

        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial    = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows    = false;

        var crack = go.AddComponent<LichCrack>();
        crack._renderer = mr;
        crack._life     = Mathf.Max(FadeSeconds + GlowSeconds, seconds);
        crack.Apply(GlowColor);
        live.Enqueue(crack);
    }

    /// <summary>선을 따라 균열을 늘어놓는다(구체·광선이 지나간 줄).</summary>
    public static void SpawnLine(Vector3 fromFloor, Vector3 toFloor, float size, float spacing, float seconds = 8f)
    {
        Vector3 delta = toFloor - fromFloor;
        float   len   = delta.magnitude;
        if (len < 0.01f || spacing <= 0f) { Spawn(fromFloor, size, seconds); return; }

        int count = Mathf.Min(MaxCracks / 2, Mathf.FloorToInt(len / spacing) + 1);
        for (int i = 0; i < count; i++)
            Spawn(fromFloor + delta * (i / (float)Mathf.Max(1, count - 1)), size * Random.Range(0.8f, 1.15f), seconds);
    }

    /// <summary>전투 종료 — 남은 균열을 모두 거둔다.</summary>
    public static void ClearAll()
    {
        while (s_live.Count > 0)
        {
            var c = s_live.Dequeue();
            if (c != null) Destroy(c.gameObject);
        }
        while (s_attached.Count > 0)
        {
            var c = s_attached.Dequeue();
            if (c != null) Destroy(c.gameObject);
        }
    }

    // ── Private Methods ───────────────────────────────────────────
    private void Apply(Color c)
    {
        if (_renderer == null) return;
        s_mpb ??= new MaterialPropertyBlock();
        _renderer.GetPropertyBlock(s_mpb);
        s_mpb.SetColor(ColorId, c);
        _renderer.SetPropertyBlock(s_mpb);
    }

    /// <summary>정점 색(흰색)을 가진 단위 쿼드 — 입자 셰이더가 정점 색을 곱해도 색이 살아 있게.</summary>
    private static Mesh Quad
    {
        get
        {
            if (s_quad != null) return s_quad;
            s_quad = new Mesh { name = "LichCrackQuad" };
            s_quad.SetVertices(new List<Vector3>
            {
                new(-0.5f, -0.5f, 0f), new(0.5f, -0.5f, 0f), new(-0.5f, 0.5f, 0f), new(0.5f, 0.5f, 0f),
            });
            s_quad.SetUVs(0, new List<Vector2> { new(0, 0), new(1, 0), new(0, 1), new(1, 1) });
            s_quad.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
            s_quad.SetNormals(new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            // 앞면이 -Z(내장 쿼드와 같다) — Euler(90, y, 0)으로 눕히면 위를 본다.
            s_quad.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);
            s_quad.RecalculateBounds();
            return s_quad;
        }
    }
}
}
