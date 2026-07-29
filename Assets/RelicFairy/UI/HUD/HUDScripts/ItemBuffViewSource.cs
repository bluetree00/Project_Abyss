using System.Collections.Generic;

/// <summary>
/// 아이템 동적 지속 버프(조건부 Cond*)를 버프창 수집에 연결하는 어댑터.
///
/// HudPresenter가 GameRunSession 바인딩 시 1회 등록(AddSource)한다. 매 수집(Collect)마다
/// <see cref="ItemEffectManager"/>의 현재 활성 상태를 pull로 읽으므로, 인벤토리 Rebuild/효과 교체와
/// 순서 의존 없이 안전하다. 동작/밸런스는 건드리지 않는다(읽기 전용 수집).
///
/// <b>영구 효과는 버프창에 올리지 않는다.</b> 버프창은 "지금 잠깐 걸려 있는 것"을 보는 자리인데,
/// 아이템의 상시 스탯 효과까지 올라오면서 칸이 영구 항목으로 가득 차 실제 버프가 묻혔다.
/// 남은 시간이 있거나(일시적) 스택이 쌓이는 것만 통과시킨다 — 상시 효과는 캐릭터 정보창의 몫이다.
/// </summary>
public sealed class ItemBuffViewSource : IBuffViewSource
{
    private readonly ItemEffectManager _effects;
    private readonly List<BuffViewItem> _scratch = new();

    public ItemBuffViewSource(ItemEffectManager effects) => _effects = effects;

    public void Contribute(List<BuffViewItem> into)
    {
        if (_effects == null || into == null) return;

        _scratch.Clear();
        _effects.CollectActiveBuffViews(_scratch);

        for (int i = 0; i < _scratch.Count; i++)
            if (IsTemporary(_scratch[i]))
                into.Add(_scratch[i]);
    }

    /// <summary>버프창에 올릴 자격 — 잔여 게이지가 있거나(일시적) 스택이 쌓이는 것만.
    /// <see cref="BuffViewItem.Remaining01"/>은 무한/해당없음이면 -1, <see cref="BuffViewItem.Stacks"/>는 기본 1이다.</summary>
    private static bool IsTemporary(in BuffViewItem b)
        => b.Remaining01 > 0f || b.Stacks > 1;
}
