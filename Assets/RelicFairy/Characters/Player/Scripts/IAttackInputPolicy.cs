// AttackInputPolicy.cs
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
