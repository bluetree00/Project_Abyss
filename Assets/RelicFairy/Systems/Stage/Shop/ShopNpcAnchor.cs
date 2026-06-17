using UnityEngine;

/// <summary>
/// 커스텀 손맵 상점 프리팹에서 상인 NPC가 설 위치/방향을 표시하는 마커.
/// ShopRoomController가 방 계층에서 이걸 찾아 그 자리(위치+회전)에 NPC를 스폰한다.
/// 없으면 매대 중심점 → 방 중앙 순으로 폴백(절차 CSV 방 호환).
///
/// 디자이너는 카운터 뒤 등 원하는 위치에 빈 GO + 이 컴포넌트를 두고,
/// 회전(Y)으로 상인이 바라볼 방향을 정한다. 비주얼은 없다(런타임 NPC 프리팹이 채움).
/// </summary>
public class ShopNpcAnchor : MonoBehaviour
{
}
