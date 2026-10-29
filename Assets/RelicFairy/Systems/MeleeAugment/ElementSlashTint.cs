using UnityEngine;

/// <summary>
/// 속성 검(대검 R) 동안 베기 이펙트를 속성 색으로(10-26 사용자 「속성 휘두를 때 실제 검 이펙트의 색상도 신경 써야 해」).
/// 입자 시작 색 = 속성 팔레트(밝은 심지 ~ 짙은 가장자리) · 수명 색 곡선은 투명도만 남기고 색은 흰색 — 원래 파란 베기(Basic Slash Blue)의 색이 입자 색에서 나온다.
/// 풀에서 다시 쓰는 이펙트라 원래 색을 기억해 두고, 속성 검이 아닐 때 꺼내 쓰면 되돌린다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ElementSlashTint : MonoBehaviour
{
    // ── Private ───────────────────────────────────────────────────
    private ParticleSystem[] _systems;
    private ParticleSystem.MinMaxGradient[] _start, _overLife;
    private int _applied = -1;   // −1 = 원래 색 · 그 밖 = 칠한 속성

    // ── Lifecycle ─────────────────────────────────────────────────
    private void OnDisable() => Apply(null);   // 풀에 돌아간다 — 같은 풀 키를 쓰는 다른 곳(E 베기 등)이 칠한 색을 물려받지 않게(10-26 검토 I2)

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>이펙트를 속성 색으로 칠한다 — null이면 원래 색(칠한 적이 없으면 아무것도 안 한다).</summary>
    public static void Set(GameObject fx, RuneElement? element)
    {
        if (fx == null) return;
        if (!fx.TryGetComponent<ElementSlashTint>(out var tint))
        {
            if (element == null) return;
            tint = fx.AddComponent<ElementSlashTint>();
        }
        tint.Apply(element);
    }

    /// <summary>
    /// 자기 것(풀에 안 돌려주는) 이펙트를 한 번 칠한다 — 원래 색을 기억하지 않는다 · 투명도 × <paramref name="alphaMul"/>(분신 베기 · 10-26).
    /// </summary>
    public static void Paint(GameObject fx, Color bright, Color deep, float alphaMul)
    {
        if (fx == null) return;
        foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            var col  = ps.colorOverLifetime;
            float a = Alpha(main.startColor) * alphaMul;
            bright.a = a;
            deep.a   = a;
            main.startColor = new ParticleSystem.MinMaxGradient(bright, deep);
            col.color = WhiteKeepAlpha(col.color);
        }
    }

    // ── Private Methods ───────────────────────────────────────────
    private void Apply(RuneElement? element)
    {
        int want = element.HasValue ? (int)element.Value : -1;
        if (want == _applied) return;
        Cache();
        _applied = want;
        Color bright = element.HasValue ? ElementPalette.Bright(element.Value) : Color.white;
        Color deep   = element.HasValue ? ElementPalette.Deep(element.Value) : Color.white;
        for (int i = 0; i < _systems.Length; i++)
        {
            var ps = _systems[i];
            if (ps == null) continue;
            var main = ps.main;
            var col  = ps.colorOverLifetime;
            if (want < 0)
            {
                main.startColor = _start[i];
                col.color = _overLife[i];
                continue;
            }
            float a = Alpha(_start[i]);
            bright.a = a;
            deep.a   = a;
            main.startColor = new ParticleSystem.MinMaxGradient(bright, deep);
            col.color = WhiteKeepAlpha(_overLife[i]);
        }
    }

    private void Cache()
    {
        if (_systems != null) return;
        _systems  = GetComponentsInChildren<ParticleSystem>(true);
        _start    = new ParticleSystem.MinMaxGradient[_systems.Length];
        _overLife = new ParticleSystem.MinMaxGradient[_systems.Length];
        for (int i = 0; i < _systems.Length; i++)
        {
            _start[i]    = _systems[i].main.startColor;
            _overLife[i] = _systems[i].colorOverLifetime.color;
        }
    }

    private static float Alpha(ParticleSystem.MinMaxGradient g) => g.mode switch
    {
        ParticleSystemGradientMode.Color     => g.color.a,
        ParticleSystemGradientMode.TwoColors => Mathf.Max(g.colorMin.a, g.colorMax.a),
        _                                    => 1f,
    };

    /// <summary>색 열쇠는 흰색으로 · 투명도 열쇠는 그대로(페이드 모양을 지킨다).</summary>
    private static ParticleSystem.MinMaxGradient WhiteKeepAlpha(ParticleSystem.MinMaxGradient g)
    {
        switch (g.mode)
        {
            case ParticleSystemGradientMode.Gradient:
                return new ParticleSystem.MinMaxGradient(WhiteKeepAlpha(g.gradient));
            case ParticleSystemGradientMode.TwoGradients:
                return new ParticleSystem.MinMaxGradient(WhiteKeepAlpha(g.gradientMin), WhiteKeepAlpha(g.gradientMax));
            case ParticleSystemGradientMode.TwoColors:
                return new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, g.colorMin.a), new Color(1f, 1f, 1f, g.colorMax.a));
            default:
                return new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, g.color.a));
        }
    }

    private static Gradient WhiteKeepAlpha(Gradient src)
    {
        var g = new Gradient();
        if (src == null) return g;
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, src.alphaKeys);
        g.mode = src.mode;
        return g;
    }
}
