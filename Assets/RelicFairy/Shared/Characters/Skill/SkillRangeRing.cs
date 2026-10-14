using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 장비 스킬 범위 고리(10-08 A10) — 범위 스킬(주변경계 · 최후의 일격 · 갈래화살)에 짧은 바닥 표시로 「어디에 맞는지」 보인다.
/// 바닥 데칼은 보스 예고와 같은 <see cref="PatternGuideHelper"/>를 쓰고, 색은 보스 예고(노랑 · 빨강)와 구별되는 흰빛 금.
/// 일반 방엔 데칼 재질이 주입되지 않으므로 런 공용 예고 재질(<see cref="RunFx.GuideCircle"/>)을 빌린다.
/// 재질이 없으면 그리지 않는다(원기둥 폴백은 바닥에 판처럼 보여 쓰지 않는다).
/// </summary>
public static class SkillRangeRing
{
    private const float DefaultLifetime = 0.25f;
    // 보스 예고 이름(Guide_*)을 쓰지 않는다 — 보스 판정 점검 도구 · 반응 시뮬이 씬의 Guide_*를 보스 공격으로 읽는다(40 요청 10-08)
    public const string DiscName   = "PlayerRange_Disc";
    public const string SectorName = "PlayerRange_Sector";
    private static readonly Color RingColor = new(1f, 0.92f, 0.65f, 0.5f);

    /// <summary>둘레 원(주변경계).</summary>
    public static void Circle(Vector3 center, float radius, float lifetime = DefaultLifetime)
    {
        if (!EnsureMaterial()) return;
        var go = PatternGuideHelper.Disc(center, radius, RingColor, lifetime);
        if (go != null) go.name = DiscName;
    }

    /// <summary>앞쪽 부채꼴(최후의 일격 반원 · 갈래화살 퍼짐).</summary>
    public static void Fan(Vector3 center, Vector3 forward, float radius, float angleDeg, float lifetime = DefaultLifetime)
    {
        if (!EnsureMaterial()) return;
        float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        var go = PatternGuideHelper.Sector(center, radius, angleDeg, yaw, RingColor, lifetime);
        if (go != null) go.name = SectorName;
    }

    private static bool EnsureMaterial()
    {
        if (PatternGuideHelper.CircleMaterial != null) return true;
        if (!RunFx.IsReady || RunFx.GuideCircle == null) { RunFx.LoadAsync().Forget(); return false; }
        PatternGuideHelper.SetMaterials(RunFx.GuideCircle, RunFx.GuideArrow);
        return true;
    }
}
