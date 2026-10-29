using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 전설 룬 공용 수치(10-14 빌드 시너지 룬 재설계 R3).
/// <para>• 존 단계 성장 = 그 속성 존의 단계(0 ~ 4) 자체 — 단계마다 피해 +15%, 개수형은 단계마다 +1.</para>
/// <para>• 적중으로 터지는 것은 「그 타격 피해 × 배수」(무기 중립 · 치명 · 버프가 실린다) — 공격력 하나로 셈하면 한 방 무기에 약 3배 유리했다.</para>
/// <para>• 데이터 전환: CDN에 옛 행(발동 OnTimer · OnActivate …)이 남아 있는 동안은 옛 수치가 새 뜻과 맞지 않는다 —
/// 효과마다 새 행의 발동 이름과 같을 때만 CSV 값을 읽고, 아니면 코드 기본값을 쓴다(ITEM_DATA 업로드 뒤엔 CSV 값).</para>
/// </summary>
public static class LegendaryTuning
{
    public const float DamagePerTier = 0.20f;   // 10-24 개편 0.15 → 0.20 + 존 4단계 각성(Awakened)

    /// <summary>그 속성 존 단계(0 ~ 4).</summary>
    public static int Tier(string zone) => Mathf.Clamp(MerlinRuneBridge.Instance?.GetZoneTier(zone) ?? 0, 0, 4);

    /// <summary>그 속성 존 4단계 = 각성(10-24 개편) — 전설 규칙이 하나 더 붙는다. 화면(카드 각성 칩 · 띠)도 이것을 읽는다.</summary>
    public static bool Awakened(string zone) => Tier(zone) >= 4;

    /// <summary>존 단계 피해 배율 — 1 + 0.20 × 단계.</summary>
    public static float TierMul(string zone) => 1f + DamagePerTier * Tier(zone);

    /// <summary>지금 든 무기 기준 유효 공격력.</summary>
    public static int Atk(ItemEffectContext ctx)
        => ctx?.Stats == null ? 0 : ctx.Stats.GetEffectiveAttack(ctx.WeaponType.GetAttackStatKind());

    /// <summary>새 행이면 CSV 값(0 이하면 기본값), 옛 행이면 기본값.</summary>
    public static float Pick(bool newRow, float csv, float fallback) => newRow && csv > 0f ? csv : fallback;
}

/// <summary>
/// 전설 룬이 쌓는 자원의 화면 질의(10-24 개편) — HUD 아이콘(e2)이 key로 읽는다.
/// 1차: "heat"(불사조의 맥박 열기) · "dark"(보복의 심연 모인 어둠) · 2차: "orb" · "soul". 그 전설이 없으면 TryGet = false.
/// </summary>
public static class LegendaryHud
{
    private static readonly Dictionary<string, (int value, int max)> s_values = new();

    /// <summary>값이 바뀌었다(key).</summary>
    public static event System.Action<string> Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { s_values.Clear(); Changed = null; }

    public static bool TryGet(string key, out int value, out int max)
    {
        if (s_values.TryGetValue(key, out var v)) { value = v.value; max = v.max; return true; }
        value = 0; max = 0;
        return false;
    }

    public static void Set(string key, int value, int max)
    {
        if (s_values.TryGetValue(key, out var v) && v.value == value && v.max == max) return;
        s_values[key] = (value, max);
        Changed?.Invoke(key);
    }

    public static void Clear(string key)
    {
        if (s_values.Remove(key)) Changed?.Invoke(key);
    }
}
