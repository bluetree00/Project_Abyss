/// <summary>
/// 아이템 효과 중 "현재 활성 지속 버프"를 버프창에 노출할 수 있는 효과의 읽기 전용 질의 인터페이스.
/// <see cref="ItemEffectManager.CollectActiveBuffViews"/>가 활성 효과를 순회하며 호출한다.
///
/// ■ 표시 수집 전용 — 동작/밸런스 무변경(조건 평가 결과를 읽기만 한다).
/// ■ 조건 미충족(비활성)이면 false를 반환해 버프창에 뜨지 않게 한다.
/// </summary>
public interface IItemBuffViewProvider
{
    /// <summary>현재 조건이 충족돼 버프가 활성이면 true + 표시용 <see cref="BuffViewItem"/> 반환. 비활성이면 false.</summary>
    bool TryGetBuffView(ItemEffectContext ctx, out BuffViewItem item);
}
