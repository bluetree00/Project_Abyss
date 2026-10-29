// AttackInputPolicy.cs
using Game.Inputs;
using UnityEngine;

public interface IAttackInputPolicy
{
    // 마우스/버튼 누름
    void OnStarted(PlayerController c);
    // 마우스/버튼 뗌 (Release)
    void OnCanceled(PlayerController c);
    // 매 프레임(차지 이펙트/오토 발사 등)
    void Tick(PlayerController c, float dt);
}

/// <summary>
/// 꾹 누름 연속 공격(10-25 ④ 사용자 「공격 키를 꾹 누르고 있으면 계속 공격」) — 두 정책 공용.
/// 누른 지 <see cref="RepeatAfter"/>초부터, 스킬 중이 아니면 입력 버퍼에 평타 하나가 늘 대기하게 채운다 → 콤보가 받는 대로 나간다(연타와 같은 빠르기).
/// </summary>
public sealed class AttackHoldRepeat
{
    private const float RepeatAfter = 0.2f;    // 짧게 누른 한 번이 두 번 나가지 않게 — 이만큼은 눌러야 「꾹」
    private const float QueueWindow = 0.15f;   // 버퍼 창(0.18초) 안 — 대기 중인 평타가 이보다 새것이면 더 넣지 않는다

    private float _heldFor = -1f;   // 누른 뒤 흐른 시간(-1 = 안 누름)

    public void Press() => _heldFor = 0f;

    public void Release() => _heldFor = -1f;

    public void Tick(PlayerController c, float dt)
    {
        if (_heldFor < 0f) return;
        if (!c.IsAttackButtonHeld()) { _heldFor = -1f; return; }   // 떼는 신호 없이 입력이 꺼졌다(막는 팝업 · 연출) — 멈춘다
        _heldFor += dt;
        if (_heldFor < RepeatAfter || c.IsInSkillAction || Time.timeScale <= 0f) return;
        if (!c.InputBuffer.HasRecent(Command.Light, QueueWindow)) c.InputBuffer.Push(Command.Light);
    }
}
