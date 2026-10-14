using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 장비 스킬 범위 고리(10-08 A10) — 범위 스킬(주변경계 · 최후의 일격 · 갈래화살)에 짧은 바닥 표시로 「어디에 맞는지」 보인다.
/// 바닥 데칼은 보스 예고와 같은 <see cref="PatternGuideHelper"/>를 쓰되, 플레이어 표시 규칙(<see cref="PlayerGuideColor"/>)대로
/// 시원한 흰 테두리 선만 그린다 — 적 예고는 노랑 → 빨강으로 차오르는 면이다(10-15 사용자 「누가 쓴 건지 구분하기 어렵다」 — 예전 흰빛 금 면은 노랑과 섞였다).
/// 일반 방엔 데칼 재질이 주입되지 않으므로 런 공용 예고 재질(<see cref="RunFx.GuideCircle"/>)을 빌린다.
/// 재질이 없으면 그리지 않는다(원기둥 폴백은 바닥에 판처럼 보여 쓰지 않는다).
/// </summary>
public static class SkillRangeRing
{
    private const float DefaultLifetime = 0.25f;
    // 보스 예고 이름(Guide_*)을 쓰지 않는다 — 보스 판정 점검 도구 · 반응 시뮬이 씬의 Guide_*를 보스 공격으로 읽는다(40 요청 10-08)
    public const string DiscName   = "PlayerRange_Disc";
    public const string SectorName = "PlayerRange_Sector";
    /// <summary>
    /// 플레이어 범위 표시 색 — 스킬 · 룬 · 전설 · 유물 · 무기 형 고리와 띠가 모두 이 색의 얇은 선이다(10-15).
    /// 적 예고 팔레트(노랑 · 빨강 · 보라 · 초록 · 파랑 · 청록 · 금, <see cref="PatternGuideHelper"/>)와 겹치지 않는 시원한 흰색.
    /// </summary>
    public static readonly Color PlayerGuideColor = new(0.82f, 0.94f, 1f, 0.8f);
    private static readonly Color FillColor = new(0.82f, 0.94f, 1f, 0.05f);   // 면은 거의 비운다 — 적 예고처럼 칠한 면으로 안 보이게
    private const float RimBand = 0.08f;   // 테두리 띠 두께(반경 비율)

    /// <summary>둘레 원(주변경계).</summary>
    public static void Circle(Vector3 center, float radius, float lifetime = DefaultLifetime)
    {
        if (!EnsureMaterial()) return;
        var go = PatternGuideHelper.Disc(center, radius, FillColor, lifetime);
        if (go != null) { go.name = DiscName; Rim(go); }
    }

    /// <summary>앞쪽 부채꼴(최후의 일격 반원 · 갈래화살 퍼짐).</summary>
    public static void Fan(Vector3 center, Vector3 forward, float radius, float angleDeg, float lifetime = DefaultLifetime)
    {
        if (!EnsureMaterial()) return;
        float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        var go = PatternGuideHelper.Sector(center, radius, angleDeg, yaw, FillColor, lifetime);
        if (go != null) { go.name = SectorName; Rim(go); }
    }

    // 채움 띠를 바깥 가장자리에 얇게만 — 셰이더 채움(_Duration 1 · _FlowFade 얇게)이 곧 테두리 선이 된다
    private static void Rim(GameObject go)
    {
        PatternGuideHelper.SetFlow(go, PlayerGuideColor, RimBand);
        PatternGuideHelper.SetProgress(go, 1f);
    }

    private static bool EnsureMaterial()
    {
        if (PatternGuideHelper.CircleMaterial != null) return true;
        if (!RunFx.IsReady || RunFx.GuideCircle == null) { RunFx.LoadAsync().Forget(); return false; }
        PatternGuideHelper.SetMaterials(RunFx.GuideCircle, RunFx.GuideArrow);
        return true;
    }
}
