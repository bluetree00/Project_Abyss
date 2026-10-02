using UnityEngine;

/// <summary>
/// 색 <b>갈아 끼우기</b> — 원색이 진한 단색인 팩(Hovl 지도 표지: 빨강 바탕)은 곱하기 틴트(<c>LichVfxScaleCache.ApplyTint</c>)로는
/// 빨강 채널만 남아 등급색이 안 나온다(10-02). 입자 시작 색의 <b>밝기 · 알파는 그대로</b> 두고 빛깔만 <paramref name="tint"/>로 바꾼다.
/// 원래 색은 처음 칠할 때 한 번 기억한다 — 풀에서 다시 꺼내도 매번 원래 색 기준으로 칠한다. 알파 0이면 원래 색으로 돌린다.
/// </summary>
[DisallowMultipleComponent]
public sealed class RunFxRecolor : MonoBehaviour
{
    private ParticleSystem[]                _systems;
    private ParticleSystem.MinMaxGradient[] _original;

    /// <summary>인스턴스에 붙여(없으면 붙인다) 빛깔을 갈아 끼운다.</summary>
    public static void Apply(GameObject go, Color tint)
    {
        if (go == null) return;
        if (!go.TryGetComponent<RunFxRecolor>(out var r)) r = go.AddComponent<RunFxRecolor>();
        r.Recolor(tint);
    }

    public void Recolor(Color tint)
    {
        if (_systems == null)
        {
            _systems  = GetComponentsInChildren<ParticleSystem>(true);
            _original = new ParticleSystem.MinMaxGradient[_systems.Length];
            for (int i = 0; i < _systems.Length; i++) _original[i] = _systems[i].main.startColor;
        }

        bool restore = tint.a <= 0f;
        float peak = Mathf.Max(tint.r, Mathf.Max(tint.g, tint.b));
        Color hue  = peak > 0f ? new Color(tint.r / peak, tint.g / peak, tint.b / peak, 1f) : Color.white;
        for (int i = 0; i < _systems.Length; i++)
        {
            var main = _systems[i].main;
            main.startColor = restore ? _original[i] : Swap(_original[i], hue);
        }
    }

    private static Color Swap(Color c, Color hue)
    {
        float v = Mathf.Max(c.r, Mathf.Max(c.g, c.b));   // 원래 밝기(HDR 발광 배율은 재질 쪽이라 그대로 산다)
        return new Color(hue.r * v, hue.g * v, hue.b * v, c.a);
    }

    private static Gradient Swap(Gradient g, Color hue)
    {
        var keys = g.colorKeys;
        for (int i = 0; i < keys.Length; i++) keys[i].color = Swap(keys[i].color, hue);
        var o = new Gradient { mode = g.mode };
        o.SetKeys(keys, g.alphaKeys);
        return o;
    }

    private static ParticleSystem.MinMaxGradient Swap(ParticleSystem.MinMaxGradient g, Color hue)
    {
        switch (g.mode)
        {
            case ParticleSystemGradientMode.Color:
                return new ParticleSystem.MinMaxGradient(Swap(g.color, hue));
            case ParticleSystemGradientMode.TwoColors:
                return new ParticleSystem.MinMaxGradient(Swap(g.colorMin, hue), Swap(g.colorMax, hue));
            case ParticleSystemGradientMode.Gradient:
                return new ParticleSystem.MinMaxGradient(Swap(g.gradient, hue));
            case ParticleSystemGradientMode.TwoGradients:
                return new ParticleSystem.MinMaxGradient(Swap(g.gradientMin, hue), Swap(g.gradientMax, hue));
            case ParticleSystemGradientMode.RandomColor:
                var r = new ParticleSystem.MinMaxGradient(Swap(g.gradient, hue));
                r.mode = ParticleSystemGradientMode.RandomColor;
                return r;
            default:
                return g;
        }
    }
}
