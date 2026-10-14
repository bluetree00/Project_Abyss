using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 일반 몬스터 공격 예고 반짝임 — 맞히기 직전 머리에서 한 번 반짝여 회피할 때를 알린다.
/// 사용자(10-07): 「일반 몬스터가 언제 공격하는지 전혀 알 수 없다 — 공격 전에 작은 반짝임 같은 이펙트 연출, 회피를 위해」.
/// 언제는 <see cref="MonsterBase.NotifyAttackIncoming"/>이 정하고, 여기서는 자리 · 크기만 정해 런 공용 이펙트(<see cref="RunFxSlot.AttackGlint"/>)를 재생한다.
/// 이펙트 목록이 아직 없으면 읽기만 걸고 이번 반짝임은 건너뛴다(판정 · 흐름과 무관).
/// </summary>
public static class MonsterAttackGlint
{
    private const float TowardCamera   = 0.5f;   // 몸에 묻히지 않게 카메라 쪽으로 띄운다
    private const float FallbackHeight = 1.2f;   // 머리 뼈가 없을 때 발에서 위로
    private const float RefHeight      = 1.6f;   // 이 높이의 몸이 배율 1
    private const float MinScale = 0.7f, MaxScale = 2.2f;
    private static readonly Color Tint = new(1f, 0.95f, 0.8f, 1f);   // 흰빛에 가까운 금빛 — 원래 색이 흰 프리팹에 곱한다

    public static void Play(MonsterBase monster, Transform head)
    {
        if (monster == null) return;
        if (!RunFx.IsReady) { RunFx.LoadAsync().Forget(); return; }

        Vector3 feet = monster.transform.position;
        Vector3 at   = head != null ? head.position : feet + Vector3.up * FallbackHeight;
        float height = Mathf.Max(0.3f, at.y - feet.y);
        var cam = Camera.main;
        if (cam != null) at += (cam.transform.position - at).normalized * TowardCamera;
        RunFx.Play(RunFxSlot.AttackGlint, at, Mathf.Clamp(height / RefHeight, MinScale, MaxScale), Tint);
    }
}
}
