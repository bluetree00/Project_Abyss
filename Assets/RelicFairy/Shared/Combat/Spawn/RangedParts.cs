using UnityEngine;

/// <summary>
/// 원거리 파츠 → 발사 요청 적용점. <see cref="CombatSpawner"/>의 ②단계에서만 호출된다.
///
/// <b>스코프가 핵심</b> — 파츠는 무기에 귀속되므로 그 무기로 쏜 것에만 걸려야 한다.
/// 근접으로 전환한 상태의 공격에 원거리 파츠가 묻으면 안 되므로 <c>sourceSlot</c>으로 게이트한다.
/// 파츠 종류가 늘어도 스폰 코드는 그대로다 — 요청 필드만 더 채우면 된다.
/// </summary>
public static class RangedParts
{
    /// <summary>원거리 무기가 꽂히는 슬롯(1번 키=근접 슬롯0 / 2번 키=원거리 슬롯1).</summary>
    public const int RangedSlot = 1;

    /// <summary>장착된 파츠 효과를 요청에 누적한다. 원거리 슬롯이 아니면 아무 것도 하지 않는다.</summary>
    public static void Apply(ref ProjectileRequest req)
    {
        if (req.sourceSlot != RangedSlot) return;

        var state = RangedPartsState.Current;
        if (state == null || !state.HasAny) return;

        state.Apply(ref req);
    }
}
