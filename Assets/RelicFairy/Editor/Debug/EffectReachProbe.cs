#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// [실측 · 플레이 중 · 공용] 10-13 「이펙트 = 판정」 — 사용자 「이펙트에 맞는 판정이 나오는지 모든 플레이어 · 몬스터 검증」.
/// 실제 재생 함수를 부르기 <b>전에</b> <see cref="Snapshot"/>으로 지금 있는 입자계를 기억하고, 부른 뒤 <see cref="ObserveAsync"/>가
/// 새로 생긴 입자계만 골라 「보이는 양」(Σ 입자 알파 × 크기²)이 가장 큰 순간의 90% 도달을 잰다(83 MonsterFxCompareEditor.Measure와 같은 셈).
/// 재는 값 세 가지 — 판정 모양에 맞춰 비교한다:
///  · 반경(원점에서 수평 거리 · 원 판정) · 앞 길이(정면 방향 투영 · 부채꼴 · 직선 · 격자) · 옆 폭(정면에 수직인 거리 절댓값 · 직선 · 격자)
/// 입자 가장자리는 크기 × 0.35를 더한다(83과 같음). 굴절층(Distortion)은 셈에서 뺀다(URP에서 회색 얼룩일 뿐 「범위」로 안 읽힌다).
/// 4b(플레이어 · 일반 몬스터) · 84(보스)가 같이 쓴다.
/// </summary>
public static class EffectReachProbe
{
    public const float ReachPct = 0.9f, EdgeK = 0.35f, Step = 0.03f;
    public const float OkLow = 0.8f, OkHigh = 1.25f;   // 판정 대비 이 범위 밖이면 어긋남

    public struct Result
    {
        public float Visible, Radius, Forward, Lateral, PeakAt;   // 정점 순간의 값
        public float RadiusMax;                                   // 구간 전체 최대 반경(퍼지는 이펙트)
        public int   Systems, Particles;
        public string Names;                                       // 새로 생긴 입자계 루트 이름(중복 뺌)
        public bool  Any => Visible > 0f;
    }

    private static ParticleSystem.Particle[] s_buf = new ParticleSystem.Particle[512];
    private static readonly List<Vector2> s_r = new(), s_f = new(), s_l = new();

    /// <summary>지금 씬에 있는 입자계 — 재생 함수를 부르기 직전에 잡는다.</summary>
    public static HashSet<int> Snapshot()
    {
        var set = new HashSet<int>();
        // 살아 있는 것만 기억한다 — 풀(RunFx · ElementVfxPlayer)이 꺼 둔 인스턴스를 다시 꺼내 쓰면 새 이펙트로 잡아야 한다(10-13 84 지적: 가시 0 m)
        foreach (var ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            if (ps.gameObject.activeInHierarchy && ps.IsAlive(false)) set.Add(ps.GetInstanceID());
        return set;
    }

    /// <summary>
    /// <paramref name="before"/>에 없던 입자계를 <paramref name="window"/>초(실시간) 동안 지켜본다.
    /// <paramref name="forward"/>는 판정 정면(수평). 원 판정이면 아무 방향이나 된다.
    /// </summary>
    public static async UniTask<Result> ObserveAsync(HashSet<int> before, Vector3 origin, Vector3 forward, float window, CancellationToken ct)
    {
        forward.y = 0f;
        forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        var best = new Result();
        var weights = new Dictionary<string, float>();
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < window)
        {
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);
            s_r.Clear(); s_f.Clear(); s_l.Clear(); weights.Clear();
            float total = 0f; int systems = 0, particles = 0;
            foreach (var ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                if (before.Contains(ps.GetInstanceID()) || !ps.gameObject.activeInHierarchy) continue;
                if (!ps.TryGetComponent(out ParticleSystemRenderer pr) || !pr.enabled || IsRefraction(pr)) continue;
                int n = ps.particleCount;
                if (n == 0) continue;
                systems++;
                string root = RootName(ps.transform);
                if (s_buf.Length < n) s_buf = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(n)];
                n = ps.GetParticles(s_buf, n);
                particles += n;
                bool local = ps.main.simulationSpace == ParticleSystemSimulationSpace.Local;
                float sc = Mathf.Abs(ps.transform.lossyScale.x);
                for (int k = 0; k < n; k++)
                {
                    Vector3 p = local ? ps.transform.TransformPoint(s_buf[k].position) : s_buf[k].position;
                    float size = s_buf[k].GetCurrentSize(ps) * sc;
                    float a = s_buf[k].GetCurrentColor(ps).a / 255f;
                    float w = a * size * size;
                    if (w <= 0f) continue;
                    total += w;
                    weights[root] = (weights.TryGetValue(root, out var acc) ? acc : 0f) + w;
                    Vector3 d = p - origin; d.y = 0f;
                    float edge = size * EdgeK;
                    s_r.Add(new Vector2(d.magnitude + edge, w));
                    s_f.Add(new Vector2(Mathf.Max(0f, Vector3.Dot(d, forward)) + edge, w));
                    s_l.Add(new Vector2(Mathf.Abs(Vector3.Dot(d, right)) + edge, w));
                }
            }
            if (total <= 0f) continue;
            float radius = Pct(s_r, total);
            best.RadiusMax = Mathf.Max(best.RadiusMax, radius);
            if (total > best.Visible)
            {
                best.Visible = total;
                best.Radius = radius;
                best.Forward = Pct(s_f, total);
                best.Lateral = Pct(s_l, total);
                best.PeakAt = Time.realtimeSinceStartup - t0;
                best.Systems = systems;
                best.Particles = particles;
                // 정점 프레임에서 보이는 양이 큰 루트 3개(몫 %) — 무엇이 크기를 키웠는지
                var top = new List<KeyValuePair<string, float>>(weights);
                top.Sort((x, y) => y.Value.CompareTo(x.Value));
                var parts = new List<string>();
                for (int i = 0; i < top.Count && i < 3; i++) parts.Add($"{top[i].Key} {top[i].Value / total:P0}");
                best.Names = string.Join(", ", parts);
            }
        }
        return best;
    }

    /// <summary>이름 — 풀 그릇(@Pools · EffectPool 등) 밑이면 그 그릇 바로 밑(실제 이펙트 인스턴스) 이름, 아니면 맨 위 이름.</summary>
    private static string RootName(Transform t)
    {
        for (var x = t; x != null; x = x.parent)
            if (x.parent == null || IsPoolBox(x.parent.name)) return x.name;
        return t.root.name;
    }

    private static bool IsPoolBox(string n) => n.StartsWith("@") || n.EndsWith("Pool") || n.EndsWith("Pools");

    /// <summary>판정 대비 비율 판정 — ok는 0.8 ~ 1.25배. 이펙트가 없으면 거짓.</summary>
    public static bool Ok(float measured, float hit) => hit > 0f && measured >= hit * OkLow && measured <= hit * OkHigh;

    public static string Ratio(float measured, float hit) => hit > 0f ? $"{measured / hit:0.00}배" : "—";

    private static float Pct(List<Vector2> list, float total)
    {
        list.Sort((a, b) => a.x.CompareTo(b.x));
        float acc = 0f, goal = total * ReachPct;
        for (int i = 0; i < list.Count; i++)
        {
            acc += list[i].y;
            if (acc >= goal) return list[i].x;
        }
        return list.Count > 0 ? list[list.Count - 1].x : 0f;
    }

    private static bool IsRefraction(Renderer r)
    {
        var mat = r.sharedMaterial;
        if (mat == null) return false;
        string sh = mat.shader != null ? mat.shader.name : string.Empty;
        return sh.IndexOf("Distortion", StringComparison.OrdinalIgnoreCase) >= 0
            || sh.IndexOf("ShockWave", StringComparison.OrdinalIgnoreCase) >= 0
            || mat.name.IndexOf("Distortion", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
#endif
