using Game.Inputs;

/// <summary>
/// SwordAttackPolicy - 누르는 순간 약공격 1회 · 꾹 누르고 있으면 계속(<see cref="AttackHoldRepeat"/>, 10-25 ④).
/// 예전엔 「뗄 때 1회」라 꾹 누르면 뗄 때까지 아무것도 안 나갔다.
///
/// [강공격 봉인] 모으기(Charge)·강공격(Heavy) 경로는 제거됐다. 예전에는 enterThreshold를 넘겨
/// 쥐고 있으면 Command.Charge를 밀어 넣었는데, 라우팅(DefaultRouteInputsToLayers)이 그 커맨드를
/// 소비만 하고 버리는 한편 정책은 '차지 소진' 플래그 때문에 릴리즈까지 막아, 버튼을 길게 누르면
/// 공격이 아예 나가지 않았다. 봉인을 되돌릴 때는 이 클래스가 아니라 라우팅부터 복구해야 한다.
/// </summary>
public class SwordAttackPolicy : IAttackInputPolicy
{
    private readonly AttackHoldRepeat _hold = new();

    public void OnStarted(PlayerController c)
    {
        c.InputBuffer.Push(Command.Light);
        _hold.Press();
    }

    public void OnCanceled(PlayerController c) => _hold.Release();

    public void Tick(PlayerController c, float dt) => _hold.Tick(c, dt);
}
