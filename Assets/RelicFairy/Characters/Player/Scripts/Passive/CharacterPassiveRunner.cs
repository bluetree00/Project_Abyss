using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 캐릭터(유물) 패시브 실행기 — 등록 · 트리거 발동 · 시간 틱 · 발동 토스트 스로틀.
/// PlayerController가 소유하고, 상태 클래스·외부는 PlayerController.FirePassive로 부른다.
/// </summary>
public sealed class CharacterPassiveRunner
{
    // ── Constants ─────────────────────────────────────────────────
    // 패시브명별 토스트 스로틀 — 같은 패시브는 이 간격 안에 1회만 표시.
    private const float ToastInterval = 2f;

    // ── Private ───────────────────────────────────────────────────
    private readonly List<ICharacterPassive>  _passives = new();
    private readonly Dictionary<string, float> _toastAt = new();

    // ── Public Methods ────────────────────────────────────────────
    public void Register(ICharacterPassive passive) => _passives.Add(passive);

    /// <summary>트리거 조건이 맞는 패시브를 모두 실행한다.</summary>
    public void Fire(PlayerController owner, PassiveTrigger trigger, in PassiveContext ctx)
    {
        foreach (var p in _passives)
            if (p.Trigger == trigger && p.CanApply(owner, ctx))
            {
                p.Apply(owner, ctx);
                // [가이드라인 비주얼] 유물/캐릭터 패시브 발동 토스트(통지만).
                // OnAttackHit 패시브가 4종이라 매 타 4줄이 쏟아져 화면을 덮었다 → 이름별 스로틀.
                // (CovenantHandler.ProcToast와 같은 패턴)
                if (p is CharacterPassiveBase cb && cb.SuppressAutoToast) continue;
                if (!ToastReady(p.PassiveName)) continue;
                GuidelineVisual.Toast(owner.transform.position + Vector3.up * 2.4f, p.PassiveName, GuidelineVisual.ToastKind.Relic);
            }
    }

    // ── Private Methods ───────────────────────────────────────────
    private bool ToastReady(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        float now = Time.unscaledTime;   // 토스트는 unscaled 수명이라 동일 기준
        if (_toastAt.TryGetValue(name, out float last) && now - last < ToastInterval) return false;
        _toastAt[name] = now;
        return true;
    }
}
