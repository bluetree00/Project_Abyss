using System;

namespace RelicFairy.Monster
{
    /// <summary>
    /// 보스 등장 연출 트리거 인터페이스.
    ///
    /// 흐름: 보스가 플레이어를 감지하면 OnEntranceRequested를 발행한다.
    ///       BossRoomController가 이를 받아 카메라 팬을 수행한 뒤 TriggerEntrance()를 호출한다.
    ///       Appear 애니메이션이 끝나고 전투 진입 직전 OnCombatReady를 발행한다.
    /// </summary>
    public interface IBossEntrance
    {
        /// <summary>보스가 플레이어를 감지했을 때 발행 — BossRoomController가 구독해 카메라 팬을 시작한다.</summary>
        event Action OnEntranceRequested;

        /// <summary>Appear 연출이 끝나고 전투가 시작되기 직전 발행 — 플레이어 입력 복구 등에 사용한다.</summary>
        event Action OnCombatReady;

        /// <summary>카메라 팬 완료 후 BossRoomController가 호출 — Appear 애니메이션과 보스 이름 UI를 시작한다.</summary>
        void TriggerEntrance();
    }
}
