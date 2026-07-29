using System.Collections.Generic;

/// <summary>
/// 플레이어 측 지속 버프 소스(유물 메커닉 + 룬 리소스)를 버프창 수집에 연결하는 어댑터.
///
/// HudPresenter가 BindPlayer 시 1회 등록(AddSource)한다. 매 수집(Collect)마다 현재 상태를
/// pull로 읽으므로, 유물 부착/룬 디스패처 지연 생성 등 순서 의존 없이 안전하다.
///
/// - 활성 유물(RelicBehavior)이 IBuffViewSource면 기여(가웨인 정오/각인, 랜슬롯 광기/빈틈).
/// - 룬 디스패처는 지연 생성이므로 RuneEffectsOrNull로 "이미 생성된 경우에만" 기여(강제 생성 안 함).
/// </summary>
public sealed class PlayerBuffViewSource : IBuffViewSource
{
    private readonly PlayerController _player;

    public PlayerBuffViewSource(PlayerController player) => _player = player;

    public void Contribute(List<BuffViewItem> into)
    {
        if (_player == null) return;

        if (_player.RelicBehavior is IBuffViewSource relicSource)
            relicSource.Contribute(into);

        _player.RuneEffectsOrNull?.Contribute(into);
    }
}
