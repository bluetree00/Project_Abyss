using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 베이스캠프 봉인 섬·기억 수정이 함께 쓰는 런타임 빛 입자 — 상자 안에서 자동 방출되는 Glow 입자(Hovl Glow1cg).
/// 자동 방출이라 매 프레임 코드가 돌지 않는다. <see cref="BaseCampFxDirector"/>의 성문 막 입자와 같은 방식(2차 개편 09-29).
/// </summary>
public static class BaseCampParticles
{
    /// <summary>parent 아래 상자 모양 방출기 — 속도는 부모 기준, 입자는 월드에 남는다. 만든 뒤 바로 재생한다.</summary>
    public static ParticleSystem Motes(string name, Transform parent, Vector3 localPos, Vector3 box, float rate, float lifetime,
                                       float size, Color color, Vector3 velMin, Vector3 velMax, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 160;
        main.playOnAwake = false;

        var emission = ps.emission;
        emission.rateOverTime = rate;
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = box;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(velMin.x, velMax.x);
        vel.y = new ParticleSystem.MinMaxCurve(velMin.y, velMax.y);
        vel.z = new ParticleSystem.MinMaxCurve(velMin.z, velMax.z);

        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
        return ps;
    }

    /// <summary>입자 색·양을 바꾼다(상태 전환).</summary>
    public static void Retint(ParticleSystem ps, Color color, float rate)
    {
        if (ps == null) return;
        var main = ps.main;
        main.startColor = color;
        var emission = ps.emission;
        emission.rateOverTime = rate;
    }

    /// <summary>수평 빛 고리(LineRenderer, 월드 좌표) — 봉인진. 내려다보는 게임 카메라에 가장 잘 읽히는 모양이다.</summary>
    public static LineRenderer Ring(string name, Transform parent, Vector3 center, float radius, Material material, float width, int points = 48)
    {
        var lr = Beam(name, parent, material, width);
        lr.loop = true;
        lr.positionCount = points;
        for (int i = 0; i < points; i++)
        {
            float a = i * Mathf.PI * 2f / points;
            lr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
        }
        return lr;
    }

    /// <summary>두 점을 잇는 빛 줄(LineRenderer, 월드 좌표) — 봉인 사슬·비석 줄.</summary>
    public static LineRenderer Beam(string name, Transform parent, Material material, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.widthMultiplier = width;
        lr.numCapVertices = 2;
        lr.textureMode = LineTextureMode.Stretch;
        lr.sharedMaterial = material;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }
}
