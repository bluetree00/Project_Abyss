using UnityEngine;

/// <summary>랭킹방 돌아가는 문(10-29 설계 §5-3) — 플레이어가 들어서면 홀에 「돌아간다」를 알린다. 트리거 콜라이더와 함께 문 자식에 붙는다.</summary>
[RequireComponent(typeof(Collider))]
public sealed class RankingHallDoor : MonoBehaviour
{
    // ── Serialized ────────────────────────────────────────────
    [SerializeField] private RankingHall _hall;

    // ── Lifecycle ─────────────────────────────────────────────
    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
        if (_hall == null) _hall = GetComponentInParent<RankingHall>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_hall != null && other.GetComponentInParent<PlayerController>() != null) _hall.RequestReturn();
    }
}
