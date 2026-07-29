using UnityEngine;

/// <summary>
/// 상점 타일 마커 컴포넌트.
///
/// 표현 방식이 "월드 매대 진열" → "상점 NPC + UI 패널"로 전환되면서, 이 컴포넌트는
/// 더 이상 월드에 상품을 진열하거나 [F] 구매를 처리하지 않는다. 현재 역할은 **마커**뿐:
///   · MapBuilder가 ShopStall 타일에서 인스턴스화하고 <see cref="SetCategory"/>로 카테고리를 주입.
///   · ShopRoomController가 이 마커들의 개수·카테고리·위치를 읽어
///     NPC 진열 슬롯 레이아웃과 NPC 스폰 위치(중심점)를 정한 뒤, 마커 오브젝트를 비활성화한다.
///
/// 과거의 월드 진열 코드(모델/VFX 진열, ApplyDisplayAsync, [F] 구매 상호작용, OnPurchaseRequested,
/// SOLD/보유/pending 상태, 등급 VFX)는 NPC+UI 흐름이 완전히 대체하여 제거했다.
/// 진열 데이터/롤/가격/구매/환불 로직은 ShopRoomController가 그대로 보유한다.
/// 복원이 필요하면 git 이력(또는 C:\tmp\shop_backup_*) 참조.
/// </summary>
public class ShopStallInteraction : MonoBehaviour
{
    // ── [SerializeField] ────────────────────────────────────
    [Header("매대 설정")]
    [Tooltip("이 매대 타일의 카테고리. MapBuilder가 TileType(ShopStallWeapon/ShopStallItem)에 따라 주입.")]
    [SerializeField] private ShopCategory category = ShopCategory.Item;

    // ── Properties ──────────────────────────────────────────
    /// <summary>이 매대 타일의 카테고리 (ShopRoomController가 슬롯 레이아웃 소스로 읽음).</summary>
    public ShopCategory Category => category;

    // ── Public Methods ──────────────────────────────────────
    /// <summary>MapBuilder가 TileType 기반으로 카테고리를 주입할 때 사용.</summary>
    public void SetCategory(ShopCategory value) => category = value;
}
