using System.Collections.Generic;

/// <summary>
/// 빌드가 적에게 거는 상태 — 서약서 이을 절 후보가 「문장 안 앞 절」뿐 아니라 이것도 소모형의 먹이로 본다(설계서 §5).
/// 지금은 유물만: 가웨인 = 화상(<c>MonsterBurnHandler</c>) · 랜슬롯 = 출혈(<c>MonsterBleed</c>) — 서약 기폭 · 수확이 실제로 먹는 채널.
/// 룬의 점화(ignite) · 정전기는 다른 채널이라 서약 소모형이 먹지 못한다 → 넣지 않는다(먹이로 치면 헛도는 카드가 된다).
/// </summary>
public static class CovenantBuildStatus
{
    public static HashSet<StatusCurrency> Collect(GameRunSession run)
    {
        var set = new HashSet<StatusCurrency>();
        var relic = run?.Player != null ? run.Player.RelicClass : null;
        if (relic == null) return set;
        switch (relic.Id)
        {
            case RelicId.Gawain:   set.Add(StatusCurrency.Burn);  break;
            case RelicId.Lancelot: set.Add(StatusCurrency.Bleed); break;
        }
        return set;
    }
}
