using Game.Inputs;

/// <summary>
/// BowAttackPolicy - 누르는 순간 한 발 · 꾹 누르고 있으면 계속 쏜다(<see cref="AttackHoldRepeat"/>, 10-25 ④).
/// 예전엔 「뗄 때 한 발」이라 꾹 누르면 뗄 때까지 아무것도 안 나갔다.
///
/// [강공격 봉인] 당기기(Charge)·만충 강사격(Heavy) 경로는 제거됐다. 예전에는 만충 시 Tick이
/// PendingAttack만 Heavy로 세우고 입력 버퍼에는 넣지 않은 채 릴리즈를 '이중 발사 방지'로 막아,
/// 활을 끝까지 당겼다 놓으면 한 발도 나가지 않았다. 이제 릴리즈는 항상 한 발을 쏜다.
/// </summary>
public class BowAttackPolicy : IAttackInputPolicy
{
    private readonly AttackHoldRepeat _hold = new();

    public void OnStarted(PlayerController c)
    {
        c.InputBuffer.Push(Command.Light);   // 공격 중에 눌러도 다음 발로 버퍼에 남는다
        _hold.Press();
    }

    public void OnCanceled(PlayerController c) => _hold.Release();

    public void Tick(PlayerController c, float dt) => _hold.Tick(c, dt);
}
