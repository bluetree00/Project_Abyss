using UnityEngine;

/// <summary>
/// 아이템 전투 효과 가이드라인 비주얼 파사드(플레이스홀더).
///
/// 룬/서약/유물과 동일하게 <see cref="GuidelineVisual"/> 위에 얇게 얹어, 아이템 효과 발동을
/// 토스트(효과 라벨)·플래시(즉발)·광역 디스크(부채꼴/원)·배지(버프 지속)로 인게임에 표시한다.
/// 상태이상 마커는 <see cref="RelicFairy.Monster.MonsterStatusReceiver"/>가 ApplyDot/ApplyCc 시 자동 표시.
///
/// 진짜 VFX로 교체할 때 이 한 곳(또는 GuidelineVisualRunner)만 바꾸면 전 아이템에 반영된다.
/// 색조는 아이템 = 유물 계열(금색, ToastKind.Relic / BadgeTint.Relic)로 통일.
/// </summary>
public static class ItemGuide
{
    /// <summary>효과 발동 토스트(라벨). 플레이어/대상 위에 짧게 표시.</summary>
    public static void Toast(Vector3 pos, string label)
        => GuidelineVisual.Toast(pos + Vector3.up * 1.6f, label, GuidelineVisual.ToastKind.Relic);

    /// <summary>즉발 피해 플래시(대상 위치).</summary>
    public static void Flash(Vector3 pos, bool isCrit = false)
        => GuidelineVisual.SynergyDamage(pos + Vector3.up * 1.2f, isCrit);

    /// <summary>광역 폭발/원형 범위 디스크 플래시.</summary>
    public static void Aoe(Vector3 center, float radius)
        => GuidelineVisual.AoeBurst(center, radius, GuidelineVisual.ToastKind.Relic);

    /// <summary>두 점 연결(전파/체인) 라인.</summary>
    public static void Chain(Vector3 from, Vector3 to)
        => GuidelineVisual.Chain(from, to);

    /// <summary>플레이어 머리 위 지속 배지(무장/충전/연속 N 등). 같은 key 재호출=갱신.</summary>
    public static void Badge(Transform anchor, string key, string text)
        => GuidelineVisual.SetBadge(anchor, key, text, GuidelineVisual.BadgeTint.Relic);

    public static void ClearBadge(string key) => GuidelineVisual.ClearBadge(key);

    /// <summary>대상 위 상태 마커(취약/낙인/표식 등) — id별 색/라벨은 GuidelineVisual에서 자동.</summary>
    public static void Status(Transform target, string statusId, float duration)
        => GuidelineVisual.StatusApplied(target, statusId, duration);
}
