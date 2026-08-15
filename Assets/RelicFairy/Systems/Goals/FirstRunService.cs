using UnityEngine;

/// <summary>
/// 「초행 初行」 — 아직 안 해본 조합에 표식과 보너스를 붙여 탐색을 유도한다.
///
/// <para><b>기록은 <see cref="UserGameData.records"/> 하나에 둔다.</b> 별도 저장을 만들지 않는 이유는
/// 업적과 같다 — 같은 사실을 두 곳이 기억하면 반드시 어긋난다. 키는 <c>first.gawain.bow</c> 꼴이고,
/// 값이 0이면 미기록(=초행), 1이면 이미 해봤다.</para>
///
/// <para><b>보상이 배율인 이유</b> — 고정값이면 얕게 죽어도 다 받으니 「초행으로 1층만 갔다 죽기」가
/// 최적이 된다. 배율이면 초행을 <b>깊이 끌고 가야</b> 이득이라, 새 조합을 시도하되 대충 하지 않게 된다.</para>
/// </summary>
public static class FirstRunService
{
    /// <summary>초행 런의 정수 배율. ×2 이상이면 "매번 새 조합"이 지배 전략이 되어 숙련이 안 쌓인다.</summary>
    public const float Multiplier = 1.3f;

    /// <summary>코어 파츠 초행은 배율이 아니라 고정 — 파츠는 런 깊이와 무관하게 한 번 꽂는 선택이다.</summary>
    public const int CorePartBonus = 200;

    private const string KeyPrefix = "first.";

    private static UserGameData Data => BackendGameData.Instance?.Data;

    // ── 키 ──────────────────────────────────────────────────

    /// <summary>유물 × 무기 조합 키. 둘 중 하나라도 비면 판정하지 않는다.</summary>
    public static string ComboKey(string relicId, string weaponId)
        => string.IsNullOrEmpty(relicId) || string.IsNullOrEmpty(weaponId)
           ? null
           : $"{KeyPrefix}{relicId.ToLowerInvariant()}.{weaponId.ToLowerInvariant()}";

    public static string CorePartKey(string partId)
        => string.IsNullOrEmpty(partId) ? null : $"{KeyPrefix}core.{partId.ToLowerInvariant()}";

    // ── 조회 ────────────────────────────────────────────────

    /// <summary>아직 안 해본 조합인가. 데이터가 없으면(오프라인 등) 초행으로 치지 않는다 —
    /// 잘못 켜서 배율을 남발하느니 안 주는 쪽이 안전하다.</summary>
    public static bool IsFirst(string key)
        => Data != null && !string.IsNullOrEmpty(key) && Data.GetRecord(key) <= 0;

    // ── 이번 런 ─────────────────────────────────────────────

    /// <summary>이번 런이 초행인가(로드아웃 확정 시 1회 판정해 담아둔다).</summary>
    public static bool RunIsFirst { get; private set; }

    /// <summary>이번 런에 새로 밟은 코어 파츠 초행 수 — 종료 시 고정 보너스로 환산한다.</summary>
    public static int RunCorePartFirsts { get; private set; }

    private static string _pendingComboKey;

    /// <summary>런 스코프 초기화. 런 시작에서 부른다.</summary>
    public static void BeginRun()
    {
        RunIsFirst        = false;
        RunCorePartFirsts = 0;
        _pendingComboKey  = null;
    }

    /// <summary>
    /// 로드아웃이 확정되는 순간 <b>1회</b> 판정한다. 여기서 정해두지 않고 종료 시 보면,
    /// 그 사이에 기록이 쓰여 "방금 한 것" 때문에 초행이 아니게 된다.
    /// </summary>
    public static void MarkLoadout(string relicId, string weaponId)
    {
        _pendingComboKey = ComboKey(relicId, weaponId);
        RunIsFirst       = IsFirst(_pendingComboKey);
        if (RunIsFirst) Debug.Log($"[초행] {relicId} × {weaponId} — 이번 런은 초행이다 (정수 ×{Multiplier})");
    }

    /// <summary>코어 파츠를 처음 장착했을 때. 중복 호출은 무해하다(이미 기록됐으면 세지 않는다).</summary>
    public static void MarkCorePart(string partId)
    {
        var key = CorePartKey(partId);
        if (!IsFirst(key)) return;

        RunCorePartFirsts++;
        Data?.SetRecordMax(key, 1);
        Debug.Log($"[초행] 코어 파츠 {partId} — 정수 +{CorePartBonus}");
    }

    /// <summary>
    /// 런 종료 정산. 조합 기록을 확정하고, 배율 적용분과 고정 보너스를 합쳐 돌려준다.
    /// 사망·클리어를 가리지 않는다 — 초행은 "끝까지 갔는가"가 아니라 "해봤는가"를 보상한다.
    /// </summary>
    public static int SettleRun(int baseEssence)
    {
        int bonus = 0;

        if (RunIsFirst && !string.IsNullOrEmpty(_pendingComboKey))
        {
            bonus += Mathf.RoundToInt(baseEssence * (Multiplier - 1f));
            Data?.SetRecordMax(_pendingComboKey, 1);   // 이제 해본 조합이 된다
        }

        bonus += RunCorePartFirsts * CorePartBonus;

        if (bonus > 0) Debug.Log($"[초행] 정산 — 정수 +{bonus}");

        BeginRun();   // 다음 런을 위해 비운다
        return bonus;
    }
}
