using System.Collections.Generic;

/// <summary>
/// 아이템 동적 지속 버프(조건부 Cond*)를 버프창 수집에 연결하는 어댑터.
///
/// HudPresenter가 GameRunSession 바인딩 시 1회 등록(AddSource)한다. 매 수집(Collect)마다
/// <see cref="ItemEffectManager"/>의 현재 활성 상태를 pull로 읽으므로, 인벤토리 Rebuild/효과 교체와
/// 순서 의존 없이 안전하다. 동작/밸런스는 건드리지 않는다(읽기 전용 수집).
/// </summary>
public sealed class ItemBuffViewSource : IBuffViewSource
{
    private readonly ItemEffectManager _effects;

    public ItemBuffViewSource(ItemEffectManager effects) => _effects = effects;

    public void Contribute(List<BuffViewItem> into) => _effects?.CollectActiveBuffViews(into);
}
