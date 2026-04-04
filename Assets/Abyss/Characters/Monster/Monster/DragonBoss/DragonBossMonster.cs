using System.Collections.Generic;
using UnityEngine;

namespace Abyss.Monster
{
public class DragonBossMonster : MonsterBase, IBoss
{
    public const string PrefabAddress = "DragonBoss/DragonBoss";

    protected override string ConfigAddress => "DragonBoss/DragonBossConfig";
    protected override string DataAddress => string.Empty;
    protected override string HeadBoneName => null;
    protected override float HPBarHeadOffset => 1.5f;
    protected override bool UseWorldHPBar => false;

    public float HpRatio => (_runtime != null && _config != null && _config.stat.maxHp > 0)
        ? (float)_runtime.CurrentHp / _config.stat.maxHp
        : 1f;

    public BossAttackBlackboard Blackboard => _coreBlackboard;
    public DragonBossBlackboard DBBlackboard => _dbBlackboard;

    private DragonBossBlackboard _dbBlackboard;
    private BossAttackBlackboard _coreBlackboard;
    private BossPatternRunner _runner;
    private BossPatternContext _patternCtx;
    private Renderer[] _renderers;
    private DragonBossBlackboard.DragonElement _currentVisualElement;

    private sealed class SummonThresholdCondition : ICondition
    {
        private readonly DragonBossBlackboard _bb;
        private readonly float _threshold;

        public SummonThresholdCondition(DragonBossBlackboard bb, float threshold)
        {
            _bb = bb;
            _threshold = threshold;
        }

        public bool Evaluate(BossPatternContext ctx)
            => ctx.Boss.HpRatio <= _threshold && !_bb.IsSummonTriggered(_threshold);
    }

    private sealed class DragonElementCondition : ICondition
    {
        private readonly DragonBossBlackboard _bb;
        private readonly DragonBossBlackboard.DragonElement _element;

        public DragonElementCondition(
            DragonBossBlackboard bb,
            DragonBossBlackboard.DragonElement element)
        {
            _bb = bb;
            _element = element;
        }

        public bool Evaluate(BossPatternContext ctx) => _bb.CurrentElement == _element;
    }

    public void OnMiniDragonDied()
    {
        _dbBlackboard.ActiveMiniDragonCount = Mathf.Max(0, _dbBlackboard.ActiveMiniDragonCount - 1);
    }

    protected override void OnInitialized()
    {
        var bossConfig = _config as BossConfigSO;
        if (bossConfig == null)
        {
            Debug.LogError("[DragonBossMonster] BossConfigSO is required.", this);
            return;
        }

        _dbBlackboard = new DragonBossBlackboard();
        _dbBlackboard.Init(() => HpRatio);
        _dbBlackboard.SpawnPosition = transform.position;
        _dbBlackboard.SpawnY = transform.position.y;
        _coreBlackboard = new BossAttackBlackboard();
        _renderers = GetComponentsInChildren<Renderer>(true);
        _currentVisualElement = _dbBlackboard.CurrentElement;
        ApplyCurrentElementVisual();

        _patternCtx = new BossPatternContext
        {
            Boss = this,
            Ctx = _ctx,
            Blackboard = _coreBlackboard,
        };

        BuildPatternConditions(bossConfig);

        if (bossConfig.patternEntries != null)
        {
            foreach (var entry in bossConfig.patternEntries)
            {
                if (entry?.patterns == null) continue;
                foreach (var pattern in entry.patterns)
                    pattern?.Initialize(_patternCtx);
            }
        }

        _runner = new BossPatternRunner(
            bossConfig,
            _patternCtx,
            isAlive: () => _runtime != null && !_runtime.IsDead && !IsPlayerDead(),
            isInRange: () => _runtime?.PlayerTarget != null,
            changeState: state => ChangeState(state),
            onExecuted: pattern =>
            {
                _coreBlackboard.LastPatternTag = pattern.patternTag;
                _coreBlackboard.NormalModeTimer = 0f;
            });

        BindBossHud();
    }

    private void BuildPatternConditions(BossConfigSO cfg)
    {
        if (cfg.patternEntries == null) return;

        foreach (var entry in cfg.patternEntries)
        {
            if (entry == null || entry.conditions == null || entry.conditions.Count == 0)
            {
                entry.BuiltConditions = null;
                continue;
            }

            var built = new List<ICondition>(entry.conditions.Count);
            foreach (var key in entry.conditions)
            {
                var condition = BuildCondition(cfg, key);
                if (condition != null)
                    built.Add(condition);
            }

            entry.BuiltConditions = built.Count > 0 ? built.ToArray() : null;
        }
    }

    private ICondition BuildCondition(BossConfigSO cfg, BossConditionKey key)
    {
        switch (key)
        {
            case BossConditionKey.Phase2:
                return new HpBelowCondition(cfg.condPhase2HpThreshold);
            case BossConditionKey.Dist_Close:
                return new MaxRangeCondition(cfg.condDistClose);
            case BossConditionKey.Dist_Far:
                return new MinRangeCondition(cfg.condDistFar);
            case BossConditionKey.AfterBackstep:
                return new LastTagCondition("backstep");
            case BossConditionKey.AfterSidestep:
                return new LastTagCondition("sidestep");
            case BossConditionKey.TimePressure:
                return new NormalModeTimerCondition(cfg.condTimePressureSecs);
            case BossConditionKey.Dragon_Summon80:
                return new SummonThresholdCondition(_dbBlackboard, 0.8f);
            case BossConditionKey.Dragon_Summon50:
                return new SummonThresholdCondition(_dbBlackboard, 0.5f);
            case BossConditionKey.Dragon_Summon10:
                return new SummonThresholdCondition(_dbBlackboard, 0.1f);
            case BossConditionKey.Dragon_ElementIce:
                return new DragonElementCondition(_dbBlackboard, DragonBossBlackboard.DragonElement.Ice);
            case BossConditionKey.Dragon_ElementThunder:
                return new DragonElementCondition(_dbBlackboard, DragonBossBlackboard.DragonElement.Thunder);
            case BossConditionKey.Dragon_ElementFire:
                return new DragonElementCondition(_dbBlackboard, DragonBossBlackboard.DragonElement.Fire);
            default:
                return null;
        }
    }

    protected override void Update()
    {
        base.Update();
        if (_runner == null) return;

        _runner.Tick(Time.deltaTime);
        _coreBlackboard?.TickCooldowns(Time.deltaTime);
        UpdateElementVisualIfNeeded();

        if (_runner.IsPatternActive)
            _coreBlackboard.NormalModeTimer = 0f;
        else
            _coreBlackboard.NormalModeTimer += Time.deltaTime;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        _runner?.Reset();
        _coreBlackboard?.Reset();
        _dbBlackboard?.Reset();
        if (_dbBlackboard != null)
        {
            _dbBlackboard.SpawnPosition = transform.position;
            _dbBlackboard.SpawnY = transform.position.y;
            _currentVisualElement = _dbBlackboard.CurrentElement;
            ApplyCurrentElementVisual();
        }
        BindBossHud();
    }

    protected override void OnDisable()
    {
        UnbindBossHudIfBound();
        base.OnDisable();
    }

    private void UpdateElementVisualIfNeeded()
    {
        if (_dbBlackboard == null) return;

        var next = _dbBlackboard.CurrentElement;
        if (next == _currentVisualElement) return;

        _currentVisualElement = next;
        ApplyCurrentElementVisual();
    }

    private void ApplyCurrentElementVisual()
    {
        if (_dbBlackboard == null) return;

        DragonBossVisualHelper.ApplyRendererTint(
            _renderers,
            DragonBossVisualHelper.GetElementColor(_dbBlackboard.CurrentElement));
    }
}
}
