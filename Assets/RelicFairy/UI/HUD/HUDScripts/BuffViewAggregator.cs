using System.Collections.Generic;

/// <summary>
/// 추가 지속 버프 소스의 수집 훅. P2에서 룬 리소스/유물 메커닉 어댑터가 이 인터페이스를 구현해
/// <see cref="BuffViewAggregator.AddSource"/>로 등록한다. into에 자기 항목을 append만 한다(비파괴).
/// </summary>
public interface IBuffViewSource
{
    void Contribute(List<BuffViewItem> into);
}

/// <summary>
/// 분산된 지속 버프 소스를 단일 <see cref="BuffViewItem"/> 목록으로 모으는 수집기(plain class).
///
/// ■ P1: 완비된 소스인 방버프(<see cref="RoomBuffHandler"/>)만 직접 연결.
/// ■ P2: 룬 리소스/유물 메커닉은 <see cref="IBuffViewSource"/> 어댑터로 AddSource 등록(현재 훅만 열어둠).
///
/// 표시 레이어는 <see cref="Collect"/> 결과만 받는다. 동작/밸런스 상태는 건드리지 않는다(읽기 전용 수집).
/// </summary>
public sealed class BuffViewAggregator
{
    private RoomBuffHandler _roomBuffs;
    private readonly List<IBuffViewSource> _sources = new();
    private readonly List<BuffViewItem> _scratch = new();

    /// <summary>현재 등록된 동적 소스 수(룬/유물/아이템/서약 등). HUD가 폴링 필요 여부 판단에 사용.</summary>
    public int DynamicSourceCount => _sources.Count;

    /// <summary>방버프 소스 연결/해제. HudPresenter가 BuffHandler 바인딩 시 호출.</summary>
    public void SetRoomBuffSource(RoomBuffHandler handler) => _roomBuffs = handler;

    /// <summary>추가 지속 버프 소스 등록(P2: 룬/유물 어댑터).</summary>
    public void AddSource(IBuffViewSource source)
    {
        if (source != null && !_sources.Contains(source))
            _sources.Add(source);
    }

    /// <summary>추가 소스 해제.</summary>
    public void RemoveSource(IBuffViewSource source)
    {
        if (source != null) _sources.Remove(source);
    }

    /// <summary>전체 소스 해제(런 종료/언바인드).</summary>
    public void Clear()
    {
        _roomBuffs = null;
        _sources.Clear();
    }

    /// <summary>
    /// 현재 활성 지속 버프 전체를 수집해 반환. 반환 리스트는 내부 재사용 버퍼이므로
    /// 호출 측은 즉시 순회만 하고 보관하지 않는다(다음 Collect에서 덮어씀).
    /// </summary>
    public IReadOnlyList<BuffViewItem> Collect()
    {
        _scratch.Clear();
        CollectRoomBuffs(_scratch);
        for (int i = 0; i < _sources.Count; i++)
            _sources[i]?.Contribute(_scratch);
        return _scratch;
    }

    private void CollectRoomBuffs(List<BuffViewItem> into)
    {
        if (_roomBuffs == null) return;
        var buffs = _roomBuffs.ActiveBuffs;
        if (buffs == null) return;
        for (int i = 0; i < buffs.Count; i++)
            into.Add(BuffViewItem.FromRoomBuff(buffs[i]));
    }
}
