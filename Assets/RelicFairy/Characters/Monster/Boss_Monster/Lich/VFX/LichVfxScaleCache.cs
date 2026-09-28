using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 이펙트 인스턴스 손질 표식 — <see cref="LichVfx"/>가 스폰할 때마다 붙여 둔다(인스턴스당 한 번 계산).
/// · 크기: 파티클 크기 모드를 Hierarchy로 바꿔 transform 배율이 실제 입자 크기에 반영되게 한다.
/// · 반복: <see cref="LichVfx.PlayLoop"/>는 한 번짜리 입자도 멈출 때까지 다시 뿜게 하고, 한 번 재생에선 원래 값으로 돌린다.
/// · 색: 칸의 색 입힘(tint)을 입자 시작 색에 곱한다. 풀에서 다른 칸이 같은 프리팹을 꺼내 쓸 수 있어 매번 원래 색 기준으로 다시 곱한다.
/// · 데모 스크립트: 팩 데모용 발사기(Spells Pack <c>CreateProjectile</c>)는 Start 전에 꺼 둔다 — 켜 두면 1초마다 물리 구체를 만든다.
/// </summary>
[DisallowMultipleComponent]
public sealed class LichVfxScaleCache : MonoBehaviour
{
    private ParticleSystem[]                        _systems;
    private bool[]                                  _originalLoop;
    private ParticleSystem.MinMaxGradient[]         _originalColor;
    private Color                                   _appliedTint = Color.clear;
    private bool                                    _hierarchy;

    /// <summary>스폰 직후(같은 프레임) 호출 — 데모 스크립트의 Start가 돌기 전에 끈다.</summary>
    public void Prepare(bool loop)
    {
        if (_systems == null)
        {
            _systems       = GetComponentsInChildren<ParticleSystem>(true);
            _originalLoop  = new bool[_systems.Length];
            _originalColor = new ParticleSystem.MinMaxGradient[_systems.Length];
            for (int i = 0; i < _systems.Length; i++)
            {
                var main = _systems[i].main;
                _originalLoop[i]  = main.loop;
                _originalColor[i] = main.startColor;
            }

            foreach (var demo in GetComponentsInChildren<ZakhanSpellsPack.CreateProjectile>(true))
                demo.enabled = false;
        }

        for (int i = 0; i < _systems.Length; i++)
        {
            var main = _systems[i].main;
            main.loop = loop || _originalLoop[i];
        }
    }

    /// <summary>입자 시작 색에 <paramref name="tint"/>를 곱한다. 알파 0이면 원래 색으로 돌린다. <see cref="Prepare"/> 뒤에 부른다.</summary>
    public void ApplyTint(Color tint)
    {
        if (_systems == null || tint == _appliedTint) return;
        _appliedTint = tint;

        bool restore = tint.a <= 0f;
        for (int i = 0; i < _systems.Length; i++)
        {
            var main = _systems[i].main;
            main.startColor = restore ? _originalColor[i] : Multiply(_originalColor[i], tint);
        }
    }

    public void EnsureHierarchyScaling()
    {
        if (_hierarchy) return;
        _hierarchy = true;

        foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        }
    }

    private static ParticleSystem.MinMaxGradient Multiply(ParticleSystem.MinMaxGradient g, Color tint)
    {
        switch (g.mode)
        {
            case ParticleSystemGradientMode.Color:
                return new ParticleSystem.MinMaxGradient(g.color * tint);
            case ParticleSystemGradientMode.TwoColors:
                return new ParticleSystem.MinMaxGradient(g.colorMin * tint, g.colorMax * tint);
            case ParticleSystemGradientMode.Gradient:
                return new ParticleSystem.MinMaxGradient(Multiply(g.gradient, tint));
            case ParticleSystemGradientMode.TwoGradients:
                return new ParticleSystem.MinMaxGradient(Multiply(g.gradientMin, tint), Multiply(g.gradientMax, tint));
            case ParticleSystemGradientMode.RandomColor:
                var r = new ParticleSystem.MinMaxGradient(Multiply(g.gradient, tint));
                r.mode = ParticleSystemGradientMode.RandomColor;
                return r;
            default:
                return g;
        }
    }

    private static Gradient Multiply(Gradient src, Color tint)
    {
        if (src == null) return null;
        var keys = src.colorKeys;
        for (int i = 0; i < keys.Length; i++)
        {
            var c = keys[i].color * tint;
            c.a = 1f;
            keys[i].color = c;
        }
        var alphas = src.alphaKeys;
        for (int i = 0; i < alphas.Length; i++) alphas[i].alpha *= tint.a;

        var dst = new Gradient { mode = src.mode };
        dst.SetKeys(keys, alphas);
        return dst;
    }
}
}
