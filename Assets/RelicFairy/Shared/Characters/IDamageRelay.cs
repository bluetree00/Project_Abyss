/// <summary>
/// 맞은 피해를 다른 대상에게 넘기는 물체(공명 성흔석 · 10-22 A1b).
/// <see cref="CombatDamage"/> · <c>CombatQuery.DealSynergyDamage</c>가
/// 대상을 <see cref="RelayTarget"/>으로 바꿔 끼워, 치명 · 아이템 · 상태 · 서약 효과가 넘겨받는 대상에게 그대로 간다.
/// </summary>
public interface IDamageRelay
{
    /// <summary>피해를 넘겨받을 대상. 넘길 수 없으면(죽음 · 사라짐) null.</summary>
    UnityEngine.GameObject RelayTarget { get; }

    /// <summary>넘기는 비율(기본 피해에 곱한다).</summary>
    float RelayRatio { get; }

    /// <summary>주 타격 한 번을 받는다 — false면 막혔다(피해 · 뒤 효과 없음).</summary>
    bool TryAcceptHit();

    /// <summary>광역 조회가 「넘겨받는 대상의 몸」으로 볼 콜라이더. 없으면 광역에 안 잡힌다.</summary>
    UnityEngine.Collider RelayBody { get; }
}

/// <summary>
/// 살아 있는 중계 물체 등록부 — <c>CombatQuery</c> 광역 조회가 훑는다(공명 성흔석은 Player 층이라 몬스터 층 겹침 질의에 안 잡힌다 · 10-22 A1b).
/// 중계 물체는 OnEnable에서 넣고 OnDisable에서 뺀다.
/// </summary>
public static class DamageRelays
{
    private static readonly System.Collections.Generic.List<IDamageRelay> s_all = new();

    public static System.Collections.Generic.IReadOnlyList<IDamageRelay> All => s_all;

    public static void Register(IDamageRelay relay)
    {
        if (relay != null && !s_all.Contains(relay)) s_all.Add(relay);
    }

    public static void Unregister(IDamageRelay relay) => s_all.Remove(relay);

    // 도메인 리로드를 끈 플레이 2회차에 지난 판 물체가 남지 않게
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_all.Clear();
}
