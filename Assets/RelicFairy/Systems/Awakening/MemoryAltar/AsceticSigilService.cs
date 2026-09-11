using UnityEngine;

/// <summary>
/// 「고행자의 인장」 — 스스로 거는 <b>자발적 난이도</b>. 보상 선택지를 1개 줄이는 대신
/// 심연의 정수 수급이 1.6배가 된다. 노드 문구 그대로 「보상 −1개 · 수급 ×1.6」이다.
///
/// <para><b>해금과 착용은 다른 일이다</b> — 해금은 이 선택지를 <i>가질 수 있게</i> 할 뿐이고,
/// 실제 적용은 켜야 된다. 해금하자마자 강제로 걸리면 "돈 주고 산 페널티"가 되어
/// 열어놓고도 후회하는 노드가 된다. 제단의 그 노드에서 켜고 끈다.</para>
///
/// <para>서약(순수 파워업)과는 반대 축이다 — 이쪽이 이 게임의 첫 <b>자발적 난이도</b> 장치다.</para>
/// </summary>
public static class AsceticSigilService
{
    /// <summary>페널티가 걸린 만큼 정수를 더 준다.</summary>
    public const float EssenceMultiplier = 1.6f;

    /// <summary>보상 선택지에서 빼는 개수.</summary>
    public const int ChoicePenalty = 1;

    /// <summary>해금돼 있는가(켜고 끌 수 있는가).</summary>
    public static bool Unlocked => MemoryAltarService.IsUnlocked(MemoryAltarCatalog.SigilAscetic);

    /// <summary>지금 실제로 적용되는가 — 해금 + 착용.</summary>
    public static bool Active
    {
        get
        {
            var data = BackendGameData.Instance?.Data;
            return Unlocked && data != null && data.asceticSigilOn;
        }
    }

    /// <summary>착용을 뒤집는다. 해금 전이면 아무 일도 하지 않는다.</summary>
    /// <returns>뒤집은 뒤의 착용 상태.</returns>
    public static bool Toggle()
    {
        var data = BackendGameData.Instance?.Data;
        if (!Unlocked || data == null) return false;

        data.asceticSigilOn = !data.asceticSigilOn;
        Debug.Log($"[고행자의 인장] {(data.asceticSigilOn ? "착용" : "해제")}");
        return data.asceticSigilOn;
    }

    /// <summary>정수 획득량에 배율을 먹인다.</summary>
    public static float ApplyEssence(float amount) => Active ? amount * EssenceMultiplier : amount;

    /// <summary>
    /// 보상 선택지 수를 줄인다. <b>1개 밑으로는 내리지 않는다</b> —
    /// 0개가 되면 보상이 사라져 페널티가 아니라 버그로 읽힌다.
    /// </summary>
    public static int ApplyChoiceCount(int count) =>
        Active ? Mathf.Max(1, count - ChoicePenalty) : count;
}
