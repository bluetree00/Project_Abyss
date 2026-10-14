using UnityEngine;

/// <summary>월드에 떨어지는 재화의 모양 칸. 직렬화 값이라 순서를 바꾸지 말 것.</summary>
public enum CurrencyVisualKind
{
    Gold            = 0,   // 골드 — 왕관 금화(HUD 「아이콘_골드」와 같은 얼굴)
    Essence         = 1,   // 심연의 정수 — 기억을 담은 캡슐(HUD 「아이콘_정수」)
    RuneOre         = 2,   // 원석(정제소 재료) — 지금은 월드 드롭이 없다(방 클리어 알림만). 비교 도구 · 이후 드롭용
    EnhanceMaterial = 3,   // 강화재료 — 지금은 월드 드롭이 없다(알림만). 비교 도구 · 이후 드롭용
}

/// <summary>
/// 재화 월드 오브젝트 목록(10-12 사용자 「재화의 오브젝트가 너무 안 좋아」) — 처치 · 방 클리어로 떨어지는 재화를 무엇으로 그릴지.
/// Addressables 주소 <see cref="CurrencyVisuals.SetAddress"/>로 읽는다. 프리팹은 크기 · 축 · 재질 · 피벗(가운데)을 이미 맞춘 것이고
/// 콜라이더 · 스크립트가 없다(줍기 판정은 픽업 컴포넌트가 거리로 한다). 비어 있는 칸은 임시 도형으로 떨어진다.
/// 만들기: 메뉴 RelicFairy/Setup/10-12 재화 오브젝트 만들기.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Run/Currency Visual Set", fileName = "CurrencyVisualSet")]
public sealed class CurrencyVisualSetSO : ScriptableObject
{
    [Header("재화 → 월드 프리팹")]
    [SerializeField] private GameObject gold;
    [SerializeField] private GameObject essence;
    [SerializeField] private GameObject runeOre;
    [SerializeField] private GameObject enhanceMaterial;

    public GameObject Get(CurrencyVisualKind kind) => kind switch
    {
        CurrencyVisualKind.Gold            => gold,
        CurrencyVisualKind.Essence         => essence,
        CurrencyVisualKind.RuneOre         => runeOre,
        CurrencyVisualKind.EnhanceMaterial => enhanceMaterial,
        _                                  => null,
    };

#if UNITY_EDITOR
    /// <summary>만들기 메뉴 전용.</summary>
    public void EditorSet(CurrencyVisualKind kind, GameObject prefab)
    {
        switch (kind)
        {
            case CurrencyVisualKind.Gold:            gold            = prefab; break;
            case CurrencyVisualKind.Essence:         essence         = prefab; break;
            case CurrencyVisualKind.RuneOre:         runeOre         = prefab; break;
            case CurrencyVisualKind.EnhanceMaterial: enhanceMaterial = prefab; break;
        }
    }
#endif
}
