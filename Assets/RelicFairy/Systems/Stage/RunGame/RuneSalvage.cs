using UnityEngine;

/// <summary>
/// 룬 폐기 → <b>원석 환원</b>의 단일 판정·지급 지점.
///
/// <para><b>왜 필요한가</b> — 룬을 버리는 것이 순손실이면 판을 바꾸는 모든 판단이 손해가 된다.
/// 특히 레전더리는 자기 속성 존의 대부분(14/19칸)을 먹으므로, 그 속성을 키워놨을수록
/// <b>더 많이 철거해야 하고 더 크게 잃는다</b> — 투자한 사람이 벌받는 구조였다.</para>
///
/// <para>환원된 원석은 정제소(<see cref="RefineryService"/>)가 그대로 먹는다.
/// 그래서 폐기가 손실이 아니라 <b>다음 룬을 뽑을 기회</b>가 된다.
/// 새 재화를 만들지 않은 이유도 이것이다 — 룬은 원석을 정제한 것이니 부수면 원석으로 돌아간다.</para>
/// </summary>
public static class RuneSalvage
{
    // ── 환원 공식 ────────────────────────────────────────
    // 투자량 = 칸 수(판을 얼마나 썼나) × 등급(얼마나 귀한가).
    // 전액을 돌려주면 배치가 무료 실험이 되어 고민이 사라지므로 일부만 돌려준다.

    /// <summary>칸당 기본 환원량.</summary>
    private const int PerCell = 3;

    /// <summary>환원율. 1.0이면 전액 — 배치가 무료 실험이 되므로 낮춰 둔다.</summary>
    private const float RecoveryRate = 0.65f;

    /// <summary>
    /// 등급 배수. <b>Legendary를 후하게</b> 잡았다 —
    /// 지금 문제가 "레전더리가 나와서 손해 본다"인데 부술 때도 박하면 어느 쪽으로도 못 간다.
    /// </summary>
    private static float GradeMultiplier(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Legendary => 6f,
        ItemRarity.Epic      => 3f,
        ItemRarity.Rare      => 1.6f,
        _                    => 1f,
    };

    // ── Public Methods ───────────────────────────────────

    /// <summary>이 룬을 폐기하면 나오는 원석량. UI가 폐기 전에 미리 보여주는 용도로도 쓴다.</summary>
    public static int OreValueOf(RuntimeItemData item)
    {
        if (item == null) return 0;

        int cells = CellCountOf(item);
        float raw = cells * PerCell * GradeMultiplier(item.rarity) * RecoveryRate;
        return Mathf.Max(1, Mathf.RoundToInt(raw));   // 무엇을 버리든 최소 1은 나온다
    }

    /// <summary>
    /// 폐기분을 원석으로 지급한다. <b>인벤토리에서 빼는 것은 호출부가 한다</b> —
    /// 폐기 경로가 보관함·룬판·정제소 세 곳이라 제거 방식이 저마다 다르기 때문이다.
    /// </summary>
    /// <returns>지급한 원석량. 런이 없으면 0.</returns>
    public static int Refund(RuntimeItemData item)
    {
        var fuel = GameRunBootstrapper.Instance?.Run?.FuelBank;
        if (fuel == null || item == null) return 0;

        int ore = OreValueOf(item);
        if (ore <= 0) return 0;

        fuel.Add(FuelKind.RuneOre, ore);
        Debug.Log($"[RuneSalvage] '{item.itemId}'({item.rarity}, {CellCountOf(item)}칸) 폐기 → 원석 +{ore}");
        return ore;
    }

    // ── Private Methods ──────────────────────────────────

    /// <summary>
    /// 이 룬이 판에서 차지하는 칸 수. shape 데이터가 없으면 1칸으로 본다 —
    /// 여기서 0을 돌려주면 환원이 0이 되어 <b>폐기가 다시 순손실</b>이 된다.
    /// </summary>
    private static int CellCountOf(RuntimeItemData item)
    {
        if (item == null || item.shapeId <= 0) return 1;

        var shape = Managers.RuneData?.GetShape(item.shapeId);
        if (shape == null) return 1;

        int cells = CountOnes(shape.r1) + CountOnes(shape.r2) + CountOnes(shape.r3) + CountOnes(shape.r4);
        return Mathf.Max(1, cells);
    }

    private static int CountOnes(string row)
    {
        if (string.IsNullOrEmpty(row)) return 0;

        int n = 0;
        for (int i = 0; i < row.Length; i++)
            if (row[i] == '1') n++;
        return n;
    }
}
