using UnityEngine;

/// <summary>서비스 실행 결과. 상점 UI가 피드백에 사용.</summary>
public enum ShopServiceResult
{
    Success,
    InsufficientGold,
    Unavailable,      // 조건 미충족(예: 뺄 룬이 없음, 서약이 없음)
    NotImplemented,   // 정제소 등 미구현 의존 — 진열은 하되 실행 불가
}

/// <summary>
/// 상점 정비소 서비스 실행. 성장을 파는 게 아니라 <b>빌드를 다듬는</b> 행위를 실행한다.
///
/// 설계 원칙: "없는 내용은 연결 전에 미리 객체를 만들어 둔다."
///   - 인프라 있는 것(정수/HP)은 실동작.
///   - UI/런플로우 연결이 남은 것(룬 제거·서약 재조립)은 <b>연결점(훅)만 열어둔 스텁</b>.
///   - 정제소 의존(리롤·전용룬)은 NotImplemented — 정제소 구현 시 이 switch에 채우기만 하면 된다.
/// </summary>
public static class ShopServiceRunner
{
    /// <summary>서비스의 현재 가격(누진 반영). 상점 진열가 계산에 사용.</summary>
    public static int GetPrice(ShopServiceKind kind, GameRunSession run)
    {
        var def = ShopServiceCatalog.Get(kind);
        int baseP = def?.BasePrice ?? 0;

        // 룬 제거만 누진(StS 카드제거). 나머지는 고정.
        if (kind == ShopServiceKind.RuneExtraction && run != null)
            return baseP + ShopServiceCatalog.RuneExtractPriceStep * run.RuneExtractCount;

        return baseP;
    }

    /// <summary>구매 가능 여부(골드 제외 조건). 진열 시 회색 처리 판단에 사용.</summary>
    public static bool IsAvailable(ShopServiceKind kind, GameRunSession run)
    {
        if (run == null) return false;
        var def = ShopServiceCatalog.Get(kind);
        if (def == null || !def.Implemented) return false;

        switch (kind)
        {
            case ShopServiceKind.RuneExtraction:
                return run.ItemInventory != null && run.ItemInventory.PlacedCount > 0;   // 뺄 룬이 있어야
            case ShopServiceKind.CovenantReforge:
                return run.CovenantHandler != null && run.CovenantHandler.Covenants.Count > 0; // 재조립할 서약이 있어야
            case ShopServiceKind.HpForGold:
                return run.Player?.RuntimeStats != null
                       && run.Player.RuntimeStats.Hp > ShopServiceCatalog.HpForGoldHpCost;  // HP가 소모분보다 많아야
            case ShopServiceKind.EssenceExchange:
                return true;
            default:
                return false;   // 스텁(정제소 대기)
        }
    }

    /// <summary>서비스 실행. 골드 차감은 여기서 처리한다(성공 시).</summary>
    public static ShopServiceResult Execute(ShopServiceKind kind, GameRunSession run)
    {
        if (run == null) return ShopServiceResult.Unavailable;
        var ps = run.PlayerState;
        if (ps == null) return ShopServiceResult.Unavailable;

        var def = ShopServiceCatalog.Get(kind);
        if (def == null || !def.Implemented) return ShopServiceResult.NotImplemented;
        if (!IsAvailable(kind, run)) return ShopServiceResult.Unavailable;

        int price = GetPrice(kind, run);

        switch (kind)
        {
            // ── 실동작 ──────────────────────────────────────────
            case ShopServiceKind.EssenceExchange:
            {
                if (!ps.TrySpendGold(price)) return ShopServiceResult.InsufficientGold;
                run.AddEssence(ShopServiceCatalog.EssenceExchangeYield);
                return ShopServiceResult.Success;
            }

            case ShopServiceKind.HpForGold:
            {
                var rs = run.Player.RuntimeStats;
                int hpCost = ShopServiceCatalog.HpForGoldHpCost;
                if (rs.Hp <= hpCost) return ShopServiceResult.Unavailable;   // 자살 방지
                rs.SetHp(rs.Hp - hpCost);
                ps.AddTempGold(ShopServiceCatalog.HpForGoldGoldYield);
                return ShopServiceResult.Success;
            }

            // ── 연결점 열린 스텁(코어는 있음, UI/플로우 연결이 남음) ──
            case ShopServiceKind.RuneExtraction:
            {
                // 실제 제거는 룬판 선택 UI를 거쳐야 보드/시너지 desync가 없다.
                // 여기서는 골드/누진 카운터만 확정하고, 실제 제거는 UI 훅(TryExtractRune)이 처리하도록 연다.
                if (!ps.TrySpendGold(price)) return ShopServiceResult.InsufficientGold;
                run.RuneExtractCount++;                 // 누진 가격 진행
                // TODO(연결): 상점 UI가 룬판을 '제거 모드'로 열고, 선택된 룬을 run.TryExtractPlacedRune(item)로 제거.
                Debug.Log("[ShopService] 룬 제거 결제 완료 — 룬판 제거 모드 연결 대기(TryExtractPlacedRune).");
                return ShopServiceResult.Success;
            }

            case ShopServiceKind.CovenantReforge:
            {
                if (!ps.TrySpendGold(price)) return ShopServiceResult.InsufficientGold;
                // TODO(연결): UI_CovenantAssemble 팝업을 재조립 모드로 열어 부품 교체.
                //   기존 4개 상한(MaxCovenants) 때문에 '새 서약'이 아니라 '기존 부품 교체'여야 값어치가 있다.
                Debug.Log("[ShopService] 서약 재조립 결제 완료 — UI_CovenantAssemble 연결 대기.");
                return ShopServiceResult.Success;
            }

            // ── 정제소 의존 스텁 ────────────────────────────────
            case ShopServiceKind.RuneReroll:
            case ShopServiceKind.ExclusiveRune:
            default:
                return ShopServiceResult.NotImplemented;   // 정제소 구현 후 이 자리에 로직 추가
        }
    }
}
