using System.Collections.Generic;

/// <summary>
/// 서약의 "발동/지속 상태"를 버프창 수집에 연결하는 어댑터(옵트인).
///
/// HudPresenter가 BindCovenant 시 1회 등록한다. 매 수집마다 <see cref="CovenantHandler"/>를 pull로 읽는다.
/// 보유 서약 상시 목록은 전용 <see cref="CovenantPanelView"/>가 표시하므로, 여기서는 일시 발동/지속 상태를
/// 노출하기로 한 서약(CovenantBase.TryGetBuffView override)만 기여한다. 동작/밸런스 무변경(읽기 전용 수집).
/// </summary>
public sealed class CovenantBuffViewSource : IBuffViewSource
{
    private readonly CovenantHandler _covenants;

    public CovenantBuffViewSource(CovenantHandler covenants) => _covenants = covenants;

    public void Contribute(List<BuffViewItem> into) => _covenants?.CollectBuffViews(into);
}
