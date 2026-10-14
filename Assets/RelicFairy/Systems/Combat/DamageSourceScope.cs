using System;

/// <summary>
/// 「지금 일어나는 피해의 출처」를 잠깐 걸어 둔다(10-07 결과창 피해 비중 · 보류였던 M1 서약 피해 비중의 답).
/// 피해 호출 104곳을 고치지 않고 <b>피해를 일으키는 쪽</b>만 감싼다 — 주 피해 파이프라인(<see cref="CombatDamage.Deal"/>)과
/// 서약 연쇄(<see cref="CovenantSentence"/>). 몬스터 피해 함수가 <see cref="Resolve"/>로 읽어 <see cref="RunStats"/>에 쌓는다.
/// <para>규칙: 지속 피해는 늘 「지속」 · 서약 연쇄 안이면 「서약」 · 그 밖의 2차 즉발(룬 · 아이템 시너지)은 「룬」 ·
/// 주 피해는 걸린 출처(없으면 평타). 메인 스레드 전용 · 중첩 가능(안쪽이 이긴다) · using으로 반드시 되돌린다.</para>
/// </summary>
public static class DamageSourceScope
{
    private static DamageSource? s_current;

    public static DamageSource? Current => s_current;

    public static Handle Enter(DamageSource source)
    {
        var prev = s_current;
        s_current = source;
        return new Handle(prev);
    }

    public static DamageSource Resolve(DamageKind kind)
    {
        if (kind == DamageKind.Dot) return DamageSource.Dot;
        if (s_current == DamageSource.Covenant) return DamageSource.Covenant;
        if (kind == DamageKind.Synergy) return DamageSource.Rune;
        return s_current ?? DamageSource.Basic;
    }

    public static DamageSource FromAction(WeaponActionType a) => a switch
    {
        WeaponActionType.QSkill                            => DamageSource.RelicQ,
        WeaponActionType.ESkill or WeaponActionType.RSkill => DamageSource.Skill,
        _                                                  => DamageSource.Basic,
    };

    public readonly struct Handle : IDisposable
    {
        private readonly DamageSource? _prev;
        public Handle(DamageSource? prev) { _prev = prev; }
        public void Dispose() => s_current = _prev;
    }
}
