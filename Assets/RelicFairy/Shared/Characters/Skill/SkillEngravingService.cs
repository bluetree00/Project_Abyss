using System.Collections.Generic;

/// <summary>
/// 스킬 각인 — 스킬 단계에 오를 때 그 단계 각인 중 하나를 고른다(기획 스킬구성_재련소연결 A안, 09-25 시범: 환영베기).
/// 자동으로 열리던 단계 분기를 <b>선택</b>으로 바꿔, 재련소 방문이 매번 고르는 순간이 되게 한다.
///
/// 흐름: 재련소 화면(UI 레인)이 강화·진화 뒤 <see cref="TryGetPendingOffer"/>로 고를 거리를 묻는다 →
/// 둘 중 하나를 보여 주고 <see cref="Choose"/>로 기록 → 저장. 행동 SO는 <c>ctx.WeaponData.HasEngraving(id)</c>로 분기한다.
/// 각인 정의는 스킬 SO(<see cref="SkillSO.Engravings"/>)가 갖는다 — 비어 있는 스킬은 예전처럼 자동 분기.
/// </summary>
public static class SkillEngravingService
{
    /// <summary>
    /// 지금 단계까지 열렸는데 아직 안 고른 각인 묶음(스킬 하나 · 단계 하나). E 스킬을 먼저 본다. 없으면 false.
    /// 같은 단계 각인이 둘 이상일 때만 고를 거리다(하나뿐이면 선택이 아니다).
    /// </summary>
    public static bool TryGetPendingOffer(WeaponData w, List<SkillSO.EngravingDef> options, out SkillSO skill, out int tier)
    {
        skill = null; tier = 0;
        if (options == null) return false;
        options.Clear();
        if (w == null) return false;

        int current = SkillTierResolver.Resolve(w);
        return TryOffer(w, w.skillE, current, options, out skill, out tier)
            || TryOffer(w, w.skillQ, current, options, out skill, out tier);   // R = skillQ(필드명 레거시)
    }

    /// <summary>제안된 묶음 중 하나를 기록한다. 그 스킬의 각인이 아니거나 이미 같은 단계를 골랐으면 false.</summary>
    public static bool Choose(WeaponData w, SkillSO skill, string id)
    {
        if (w == null || skill == null || string.IsNullOrEmpty(id)) return false;
        int tier = 0;
        foreach (var def in skill.Engravings) if (def.id == id) { tier = def.tier; break; }
        if (tier == 0 || ChoseAt(w, skill, tier)) return false;
        w.AddEngraving(id);
        return true;
    }

    private static bool TryOffer(WeaponData w, SkillSO s, int current, List<SkillSO.EngravingDef> options,
                                 out SkillSO skill, out int tier)
    {
        skill = null; tier = 0;
        if (s == null) return false;
        for (int t = 2; t <= current; t++)
        {
            if (ChoseAt(w, s, t)) continue;
            options.Clear();
            foreach (var def in s.Engravings) if (def.tier == t) options.Add(def);
            if (options.Count >= 2) { skill = s; tier = t; return true; }
        }
        options.Clear();
        return false;
    }

    private static bool ChoseAt(WeaponData w, SkillSO s, int tier)
    {
        foreach (var def in s.Engravings) if (def.tier == tier && w.HasEngraving(def.id)) return true;
        return false;
    }
}
