using System;
using UnityEngine;

/// <summary>
/// 격리형 절차 진행의 출구 게이트. 방 클리어 후 출구 문 위치마다 생성된다.
/// 플레이어가 접촉하면 바인딩된 DoorPlan으로 onChosen 콜백을 발화한다.
/// (물리 연결 없음 — 통과 = 다음 방 전환 트리거)
/// </summary>
[RequireComponent(typeof(Collider))]
public class ProcRoomGate : MonoBehaviour
{
    private DoorPlan              _plan;
    private Action<ProcRoomGate>  _onChosen;
    private bool                  _armed;

    public DoorPlan     Plan => _plan;
    public RoomPlanKind Kind => _plan.kind;

    public void Initialize(DoorPlan plan, Action<ProcRoomGate> onChosen)
    {
        _plan     = plan;
        _onChosen = onChosen;
        _armed    = true;

        if (TryGetComponent<Collider>(out var col))
            col.isTrigger = true;
    }

    /// <summary>플레이어가 다른 문을 선택했을 때, 나머지 게이트를 비활성화하기 위해 호출.</summary>
    public void Disarm() => _armed = false;

    private void OnTriggerEnter(Collider other)
    {
        if (!_armed) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;

        _armed = false;
        _onChosen?.Invoke(this);
    }
}
