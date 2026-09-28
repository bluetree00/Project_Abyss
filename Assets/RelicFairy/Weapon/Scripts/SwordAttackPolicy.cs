using Game.Inputs;

/// <summary>
/// SwordAttackPolicy - 누름/뗌을 약공격 1회로 변환한다.
///
/// [강공격 봉인] 모으기(Charge)·강공격(Heavy) 경로는 제거됐다. 예전에는 enterThreshold를 넘겨
/// 쥐고 있으면 Command.Charge를 밀어 넣었는데, 라우팅(DefaultRouteInputsToLayers)이 그 커맨드를
/// 소비만 하고 버리는 한편 정책은 '차지 소진' 플래그 때문에 릴리즈까지 막아, 버튼을 길게 누르면
/// 공격이 아예 나가지 않았다. 봉인을 되돌릴 때는 이 클래스가 아니라 라우팅부터 복구해야 한다.
/// </summary>
public class SwordAttackPolicy : IAttackInputPolicy
{
    private bool _holding;

    public void OnStarted(PlayerController c)
    {
        _holding = true;
    }

    public void OnCanceled(PlayerController c)
    {
        if (!_holding) return;
        _holding = false;

        c.InputBuffer.Push(Command.Light);
    }

    public void Tick(PlayerController c, float dt) { }
}
