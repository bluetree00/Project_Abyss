using UnityEngine;

/// <summary>
/// 엘레인의 서약 — 스킬의 여운을 극대화한다 (전투 리듬형: 스킬 리듬)
///
/// [선택]   스킬 사용 직후 window초 내 일반공격이 스킬 쿨타임을 reduce초 단축(최대 maxStacks회).
/// [강화]   단축량 상승 (+이동속도 버프는 P2 후속).
/// [각성]   창 내 공격 bonusReq회 이상이면 쿨타임 즉시 bonusRed초 추가 단축(1회).
///
/// 데이터 인덱스: [0]창초 [1]쿨단축초 [2]최대스택 [3]보너스임계(0=비활성) [4]보너스단축초
/// </summary>
public sealed class ElaineCovenant : CovenantBase
{
    private const int V_WINDOW   = 0;
    private const int V_REDUCE   = 1;
    private const int V_MAXSTACK = 2;
    private const int V_BONUSREQ = 3;
    private const int V_BONUSRED = 4;

    public override string CovenantId => CovenantFactory.Elaine;
    public override CovenantCategory Category => CovenantCategory.CombatRhythm;

    public override string DisplayName         => "엘레인의 서약";
    public override string LoreText            => "엘레인 — 스킬의 여운을 극대화하는 힘을 전달했다";
    public override string BasicDescription    => "스킬 사용 직후 3초 내 일반 공격이 스킬 쿨타임을 0.5초 단축. 최대 3회.";
    public override string EnhancedDescription => "쿨타임 단축 0.7초로 증가.";
    public override string EvolvedDescription  => "스킬 직후 3초 내 공격이 5회 이상이면 쿨타임 즉시 1초 추가 단축.";

    // ── 런타임 상태 ──────────────────────────────────────
    private float _windowEnd;
    private int   _stacks;
    private int   _attacksInWindow;
    private bool  _bonusApplied;

    private float Window    => V(V_WINDOW, 3f);
    private float Reduce    => V(V_REDUCE, 0.5f);
    private int   MaxStacks => Mathf.Max(1, VI(V_MAXSTACK, 3));
    private int   BonusReq  => VI(V_BONUSREQ, 0);
    private float BonusRed  => V(V_BONUSRED, 0f);

    // ── 이벤트 ──────────────────────────────────────────
    public override void OnSkillUse(SkillType skill)
    {
        // 스킬 직후 창 개시/갱신
        _windowEnd       = Time.time + Window;
        _stacks          = 0;
        _attacksInWindow = 0;
        _bonusApplied    = false;
    }

    public override void OnAttackHit(GameObject target, float dmg)
    {
        if (Time.time >= _windowEnd) return;

        var tracker = Ctx?.Player?.CooldownTracker;
        if (tracker == null) return;

        _attacksInWindow++;

        // 스택 한도 내에서 일반공격마다 쿨타임 단축
        if (_stacks < MaxStacks)
        {
            tracker.ReduceAllCooldowns(Reduce);
            _stacks++;
        }

        // 각성: 창 내 누적 공격 임계 도달 시 1회 추가 단축
        if (BonusReq > 0 && !_bonusApplied && _attacksInWindow >= BonusReq)
        {
            tracker.ReduceAllCooldowns(BonusRed);
            _bonusApplied = true;
        }
    }
}
