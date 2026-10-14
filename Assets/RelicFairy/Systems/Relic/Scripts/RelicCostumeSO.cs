using UnityEngine;

/// <summary>
/// 유물(기사)별 의상 — 견습 기사 옷 위에 유물의 기억이 선명해질수록 그 기사의 갑옷을 덧입는다(10-08 · 기획 플레이어의상_유물별덧입기).
/// 단계와 입는 부위는 <see cref="PlayerCostume"/>가 정한다. 여기에는 유물마다 다른 재질만 둔다(정적 데이터).
///   1단계(유물 적용) = 견습 옷 + 망토 · 2단계(선명 조각) = + 흉갑 · 팔 · 어깨 · 3단계(찬란 조각) = + 갑옷 치마 + 망토 테두리 빛
/// </summary>
[CreateAssetMenu(menuName = "Game/Relic/RelicCostume")]
public class RelicCostumeSO : ScriptableObject
{
    [Header("견습 기사 옷 (1단계부터)")]
    [SerializeField, Tooltip("견습 기사 옷을 유물 색으로 칠한 재질 사본")]
    private Material squireMaterial;

    [Header("갑옷 (2단계 흉갑 · 팔 · 어깨 → 3단계 치마)")]
    [SerializeField, Tooltip("갑옷 부위 재질(팩 색 변형 Shield_Sword_Cloth_0N)")]
    private Material armorMaterial;

    [Header("머리 (비우면 그대로)")]
    [SerializeField] private Material hairMaterial;

    public Material SquireMaterial      => squireMaterial;
    public Material ArmorMaterial       => armorMaterial;
    public Material HairMaterial        => hairMaterial;
}
