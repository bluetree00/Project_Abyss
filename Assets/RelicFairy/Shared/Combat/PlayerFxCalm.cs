using UnityEngine;

/// <summary>
/// 보스 예고 위를 덮는 플레이어 이펙트를 가라앉힌다(10-30 사용자 「플레이어의 이펙트가 보스의 패턴과 가이드라인을 가려서 잘 안 보인다」 · 77과 나눔).
/// 77은 예고를 정렬 <see cref="RelicFairy.Monster.PatternGuideHelper.GuideSortingOrder"/>로 올렸다 — 여기는 플레이어 이펙트 쪽:
///   ① 정렬 순서가 예고 이상인 렌더러를 통째로 그 아래로 내린다(<see cref="LowerSorting"/> · 안쪽 순서는 지킨다 · 원소 팩 10~20이 예고 위였다)
///   ② 예고가 떠 있는 동안(<see cref="Active"/>) 새로 나는 이펙트의 알파를 <see cref="DimAlpha"/>배로(<see cref="Alpha"/> · <see cref="ScaleAlpha"/>)
/// 쓰는 곳 = 플레이어 이펙트가 나는 중심 넷: 룬 · 원소(ElementVfxPlayer) · 유물 · 증강 · 형 일회성(RelicStateVfx) · 전설 룬(LegendaryRuntime) · 그림자 분신 베기(ShadowPartner).
/// 플레이어 평타 베기 · 맞음 불꽃은 내 행동 신호라 그대로 둔다.
/// </summary>
public static class PlayerFxCalm
{
    // ── Constants ─────────────────────────────────────────────────
    /// <summary>예고가 떠 있는 동안 새로 나는 플레이어 이펙트의 알파 배율.</summary>
    public const float DimAlpha = 0.5f;

    // ── Properties ────────────────────────────────────────────────
    /// <summary>플레이어 이펙트가 넘지 않을 정렬 순서(예고 바로 아래).</summary>
    public static int MaxSortingOrder => RelicFairy.Monster.PatternGuideHelper.GuideSortingOrder - 1;
    /// <summary>지금 보스 예고 · 가이드가 하나라도 떠 있나(77 · 프레임에 한 번 센다).</summary>
    public static bool Active => RelicFairy.Monster.PatternGuideHelper.AnyBossTelegraph;
    /// <summary>지금 새로 낼 플레이어 이펙트의 알파 배율(예고 중 <see cref="DimAlpha"/> · 아니면 1).</summary>
    public static float Alpha => Active ? DimAlpha : 1f;

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>렌더러 정렬 순서가 예고 이상이면 모두를 같은 만큼 내린다(인스턴스를 만들 때 한 번 · 안쪽 앞뒤는 그대로).</summary>
    public static void LowerSorting(GameObject go)
    {
        if (go == null) return;
        var rends = go.GetComponentsInChildren<Renderer>(true);
        int max = int.MinValue;
        for (int i = 0; i < rends.Length; i++) max = Mathf.Max(max, rends[i].sortingOrder);
        int over = max - MaxSortingOrder;
        if (over <= 0) return;
        for (int i = 0; i < rends.Length; i++) rends[i].sortingOrder -= over;
    }

    /// <summary>입자 시작 색 알파 × <paramref name="k"/>(만들고 버리는 인스턴스 — 풀에 돌아가는 것은 원래 색을 기억한 쪽에서).</summary>
    public static void ScaleAlpha(GameObject go, float k)
    {
        if (go == null || Mathf.Approximately(k, 1f)) return;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.startColor = ScaleAlpha(main.startColor, k);
        }
    }

    /// <summary>시작 색(색 · 두 색 · 그라디언트 · 두 그라디언트 · 무작위)의 알파 × <paramref name="k"/>.</summary>
    public static ParticleSystem.MinMaxGradient ScaleAlpha(ParticleSystem.MinMaxGradient g, float k)
    {
        switch (g.mode)
        {
            case ParticleSystemGradientMode.Color:
                return new ParticleSystem.MinMaxGradient(Fade(g.color, k));
            case ParticleSystemGradientMode.TwoColors:
                return new ParticleSystem.MinMaxGradient(Fade(g.colorMin, k), Fade(g.colorMax, k));
            case ParticleSystemGradientMode.Gradient:
                return new ParticleSystem.MinMaxGradient(Fade(g.gradient, k));
            case ParticleSystemGradientMode.TwoGradients:
                return new ParticleSystem.MinMaxGradient(Fade(g.gradientMin, k), Fade(g.gradientMax, k));
            case ParticleSystemGradientMode.RandomColor:
                return new ParticleSystem.MinMaxGradient(Fade(g.gradient, k)) { mode = ParticleSystemGradientMode.RandomColor };
            default:
                return g;
        }
    }

    // ── Private Methods ───────────────────────────────────────────
    private static Color Fade(Color c, float k) { c.a *= k; return c; }

    private static Gradient Fade(Gradient src, float k)
    {
        if (src == null) return null;
        var a = src.alphaKeys;
        for (int i = 0; i < a.Length; i++) a[i].alpha *= k;
        var g = new Gradient { mode = src.mode };
        g.SetKeys(src.colorKeys, a);
        return g;
    }
}
