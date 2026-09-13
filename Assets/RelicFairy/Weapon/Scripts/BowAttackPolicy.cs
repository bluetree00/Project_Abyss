using Game.Inputs;

/// <summary>
/// BowAttackPolicy - 누름/뗌을 약공격(발사) 1회로 변환한다.
///
/// [강공격 봉인] 당기기(Charge)·만충 강사격(Heavy) 경로는 제거됐다. 예전에는 만충 시 Tick이
/// PendingAttack만 Heavy로 세우고 입력 버퍼에는 넣지 않은 채 릴리즈를 '이중 발사 방지'로 막아,
/// 활을 끝까지 당겼다 놓으면 한 발도 나가지 않았다. 이제 릴리즈는 항상 한 발을 쏜다.
/// </summary>
public class BowAttackPolicy : IAttackInputPolicy
{
    private bool _holding;

    public void OnStarted(PlayerController c)
    {
        // 공격 중에도 _holding = true — OnCanceled에서 다음 발을 버퍼링할 수 있게 한다.
        _holding = true;
    }

    public void OnCanceled(PlayerController c)
    {
        if (!_holding) return;
        _holding = false;

        c.SetPendingAttack(Command.Light);
        c.InputBuffer.Push(Command.Light);
    }

    public void Tick(PlayerController c, float dt) { }
}
