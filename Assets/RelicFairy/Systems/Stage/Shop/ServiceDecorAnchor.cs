using UnityEngine;

/// <summary>
/// 서비스 방(상점·재련소·정제소) 소품이 놓일 자리를 표시하는 마커.
///
/// <b>왜 필요한가</b> — 예전엔 소품 위치를 <see cref="ServiceRoomDecorPlacer"/>가 런타임에
/// "NPC 뒤 부채꼴 ±70°, 반경 3.5~4.7m 난수 + 벽 OverlapSphere 검사"로 찾았다.
/// 결과가 물리 탐색에 좌우돼 방마다 달라지고 재현되지 않아, <b>기획이 무대를 정할 수 없었다.</b>
/// 이 마커가 있으면 그 자리를 그대로 쓰고, 없으면 기존 탐색이 폴백으로 남는다(회귀 0).
///
/// 배치는 grid_csv 토큰으로 한다 — <c>NC</c>(카운터/작업대) · <c>NP</c>(소품).
/// <see cref="ShopNpcAnchor"/>(NPC 자리, 토큰 <c>NS</c>)와 짝을 이룬다.
/// </summary>
public class ServiceDecorAnchor : MonoBehaviour
{
    /// <summary>소품 종류. 카운터는 1개만 쓰이고 나머지는 Prop 순서대로 채운다.</summary>
    public enum Slot
    {
        /// <summary>판매대·작업대 — decorPrefabs[0]이 놓인다.</summary>
        Counter,
        /// <summary>배경 소품 — decorPrefabs[1] 이후가 배치 순서대로 놓인다.</summary>
        Prop,
    }

    [SerializeField] private Slot slot = Slot.Prop;

    /// <summary>이 앵커가 받을 소품 종류.</summary>
    public Slot Kind => slot;

    /// <summary>토큰 핸들러가 스폰 직후 종류를 지정한다.</summary>
    public void SetKind(Slot kind) => slot = kind;
}
