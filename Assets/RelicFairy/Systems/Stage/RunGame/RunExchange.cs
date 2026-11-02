/// <summary>
/// 환전소(11-02 Q18 사용자 「소모 컨텐츠 방들에 환전소 — 낮은 효율로 다른 재화를 원하는 재화로」) — 런 재화 셋(골드 · 원석 · 강화재료) 사이를 손해 보고 바꾼다.
/// 골드는 상점이 포션만 팔아 런 끝에 900 ~ 1,200 남았다(10-17 예측) — 그 골드를 정제 · 재련 연료로 돌리는 출구.
/// 받는 쪽은 「얻은 양」(결과창)에 세지 않는다 — 새로 번 것이 아니라 바꾼 것이다(정제소 재료 녹이기와 같은 규약).
/// </summary>
public static class RunExchange
{
    public enum Currency { Gold, RuneOre, EnhanceMaterial }

    public readonly struct Rate
    {
        public readonly Currency From, To;
        public readonly int Give, Get;
        public Rate(Currency from, int give, Currency to, int get) { From = from; Give = give; To = to; Get = get; }
    }

    /// <summary>환율 — 골드로 사면 30 · 팔면 3(상점 원석 환전 10-22 「1 → 3」과 같은 값) · 연료끼리는 반값.</summary>
    public static readonly Rate[] Rates =
    {
        new(Currency.Gold, 30, Currency.RuneOre, 1),
        new(Currency.Gold, 30, Currency.EnhanceMaterial, 1),
        new(Currency.RuneOre, 2, Currency.EnhanceMaterial, 1),
        new(Currency.EnhanceMaterial, 2, Currency.RuneOre, 1),
        new(Currency.RuneOre, 1, Currency.Gold, ShopRoomController.OreToGold),
        new(Currency.EnhanceMaterial, 1, Currency.Gold, ShopRoomController.OreToGold),
    };

    public static int Balance(GameRunSession run, Currency c) => c switch
    {
        Currency.Gold            => run?.PlayerState?.TempGold ?? 0,
        Currency.RuneOre         => run?.FuelBank?.RuneOre ?? 0,
        Currency.EnhanceMaterial => run?.FuelBank?.EnhanceMaterial ?? 0,
        _                        => 0,
    };

    /// <summary>이 환율로 지금 몇 번 바꿀 수 있나.</summary>
    public static int MaxTimes(GameRunSession run, in Rate r) => r.Give > 0 ? Balance(run, r.From) / r.Give : 0;

    /// <summary><paramref name="times"/>번 바꾼다(모자라면 할 수 있는 만큼). 바꾼 횟수.</summary>
    public static int Trade(GameRunSession run, in Rate r, int times)
    {
        times = System.Math.Min(times, MaxTimes(run, r));
        if (times <= 0 || run?.PlayerState == null || run.FuelBank == null) return 0;
        int give = r.Give * times, get = r.Get * times;
        bool paid = r.From == Currency.Gold ? run.PlayerState.TrySpendGold(give) : run.FuelBank.Exchange(Fuel(r.From), -give);
        if (!paid) return 0;
        if (r.To == Currency.Gold) run.PlayerState.AddTempGold(get);   // 배율 없는 날 금액(환불 · 환전 규약)
        else run.FuelBank.Exchange(Fuel(r.To), get);
        return times;
    }

    public static string Name(Currency c) => c switch
    {
        Currency.Gold    => $"<color={UIPalette.GoldHex}>{Loc.T("골드")}</color>",
        Currency.RuneOre => $"<color={UIPalette.RuneOreHex}>{Loc.T("원석")}</color>",
        _                => $"<color={UIPalette.EnhanceHex}>{Loc.T("강화재료")}</color>",
    };

    private static FuelKind Fuel(Currency c) => c == Currency.RuneOre ? FuelKind.RuneOre : FuelKind.EnhanceMaterial;
}
