using System.Collections.Generic;
using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 숲의 정원사꾼 (ForestGuardian) 보스 MonoBehaviour.
///
/// 3가지 폼(리체/스파이더/가시)을 가지며, 2패턴마다 폼 전환을 수행한다.
/// HP 80% 이하부터 폼 전환이 활성화된다.
/// </summary>
public class ForestGuardianMonster : MonsterBase, IBoss
{
    public const string PrefabAddress = "ForestGuardian/ForestGuardian";

    protected override string ConfigAddress   => "ForestGuardian/ForestGuardianConfig";
    protected override string DataAddress     => string.Empty;
    protected override string HeadBoneName    => null;
    protected override float  HPBarHeadOffset => 0.3f;
    protected override bool   UseWorldHPBar   => false;

    // ── IBoss ────────────────────────────────────────────────────────
    public float HpRatio =>
        (_runtime != null && _config != null && _config.stat.maxHp > 0)
        ? (float)_runtime.CurrentHp / _config.stat.maxHp
        : 1f;

    public BossAttackBlackboard Blackboard => _coreBlackboard;

    // ── ForestGuardian 전용 ──────────────────────────────────────────
    /// <summary>폼 및 패턴 카운트 블랙보드. 패턴 SO에서 폼 판단 시 접근.</summary>
    public ForestGuardianBlackboard FGBlackboard => _fgBlackboard;

    private ForestGuardianBlackboard _fgBlackboard;
    private BossAttackBlackboard     _coreBlackboard;
    private BossPatternRunner        _runner;
    private BossPatternContext       _patternCtx;

    // ── 커스텀 ICondition ────────────────────────────────────────────

    /// <summary>FormChangePending == true 일 때 참.</summary>
    private sealed class FormChangePendingCondition : ICondition
    {
        private readonly ForestGuardianBlackboard _bb;
        public FormChangePendingCondition(ForestGuardianBlackboard bb) => _bb = bb;
        public bool Evaluate(BossPatternContext ctx) => _bb.FormChangePending;
    }

    /// <summary>CurrentForm == 지정 폼일 때 참.</summary>
    private sealed class CurrentFormCondition : ICondition
    {
        private readonly ForestGuardianBlackboard          _bb;
        private readonly ForestGuardianBlackboard.BossForm _form;
        public CurrentFormCondition(
            ForestGuardianBlackboard bb,
            ForestGuardianBlackboard.BossForm form)
        { _bb = bb; _form = form; }
        public bool Evaluate(BossPatternContext ctx) => _bb.CurrentForm == _form;
    }

    // ── 초기화 ──────────────────────────────────────────────────────

    protected override void OnInitialized()
    {
        var bossConfig = _config as BossConfigSO;
        if (bossConfig == null)
        {
            Debug.LogError("[ForestGuardianMonster] Config이 BossConfigSO가 아닙니다!", this);
            return;
        }

        _fgBlackboard   = new ForestGuardianBlackboard();
        _coreBlackboard = new BossAttackBlackboard();

        _patternCtx = new BossPatternContext
        {
            Boss       = this,
            Ctx        = _ctx,
            Blackboard = _coreBlackboard,
        };

        // BuiltConditions 직접 조립
        BuildPatternConditions(bossConfig);

        // 각 패턴 SO Initialize
        if (bossConfig.patternEntries != null)
        {
            foreach (var entry in bossConfig.patternEntries)
            {
                if (entry?.patterns == null) continue;
                foreach (var p in entry.patterns)
                    p?.Initialize(_patternCtx);
            }
        }

        _runner = new BossPatternRunner(
            bossConfig,
            _patternCtx,
            isAlive:     () => _runtime != null && !_runtime.IsDead && !IsPlayerDead(),
            isInRange:   () => _runtime?.PlayerTarget != null,
            changeState: s  => ChangeState(s),
            onExecuted:  p  =>
            {
                _coreBlackboard.LastPatternTag = p.patternTag;
                _coreBlackboard.NormalModeTimer = 0f;
                _fgBlackboard.OnPatternExecuted(HpRatio);
            });

        BindBossHud();
    }

    /// <summary>
    /// patternEntries 인덱스별 BuiltConditions 조립.
    ///
    /// Entry 0 (forceExecute): 폼 전환  — FormChangePending
    /// Entry 1               : 정면 타격 — DistToPlayer &lt;= 2m
    /// Entry 2               : 정면 방어 — DistToPlayer >= 4m
    /// Entry 3               : 리체 패턴 — CurrentForm == Liche
    /// Entry 4               : 스파이더  — CurrentForm == Spider
    /// Entry 5               : 가시 패턴 — CurrentForm == Thorn
    /// Entry 6               : fallback  — 항상 참 (BuiltConditions 비움)
    /// </summary>
    private void BuildPatternConditions(BossConfigSO cfg)
    {
        if (cfg.patternEntries == null || cfg.patternEntries.Count == 0) return;

        var entries = cfg.patternEntries;
        AssignIfExists(entries, 0, new ICondition[] { new FormChangePendingCondition(_fgBlackboard) });
        AssignIfExists(entries, 1, new ICondition[] { new MaxRangeCondition(2f) });
        AssignIfExists(entries, 2, new ICondition[0]); // 기획서: 정면 방어에 거리 조건 없음
        AssignIfExists(entries, 3, new ICondition[] { new CurrentFormCondition(_fgBlackboard, ForestGuardianBlackboard.BossForm.Liche) });
        AssignIfExists(entries, 4, new ICondition[] { new CurrentFormCondition(_fgBlackboard, ForestGuardianBlackboard.BossForm.Spider) });
        AssignIfExists(entries, 5, new ICondition[] { new CurrentFormCondition(_fgBlackboard, ForestGuardianBlackboard.BossForm.Thorn) });
        // Entry 6: fallback — BuiltConditions 비워두면 EvaluateConditions()가 항상 true 반환
    }

    private static void AssignIfExists(List<BossPatternEntry> entries, int index, ICondition[] conditions)
    {
        if (index < entries.Count)
            entries[index].BuiltConditions = conditions;
    }

    // ── 매 프레임 ────────────────────────────────────────────────────

    protected override void Update()
    {
        base.Update();
        if (_runner == null) return;

        _runner.Tick(Time.deltaTime);
        _coreBlackboard?.TickCooldowns(Time.deltaTime);

        if (_runner.IsPatternActive)
            _coreBlackboard.NormalModeTimer = 0f;
        else
            _coreBlackboard.NormalModeTimer += Time.deltaTime;
    }

    // ── 풀 재사용 ────────────────────────────────────────────────────

    protected override void OnEnable()
    {
        base.OnEnable();
        _runner?.Reset();
        _coreBlackboard?.Reset();
        _fgBlackboard?.Reset();
        BindBossHud();
    }

    protected override void OnDisable()
    {
        UnbindBossHudIfBound();
        base.OnDisable();
    }
}
}
