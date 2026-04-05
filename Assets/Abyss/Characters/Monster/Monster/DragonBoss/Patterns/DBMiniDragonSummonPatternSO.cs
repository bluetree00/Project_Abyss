using UnityEngine;

namespace Abyss.Monster
{
[CreateAssetMenu(fileName = "DBMiniDragonSummonPatternSO",
                 menuName = "Abyss/Boss/DragonBoss/MiniDragonSummon")]
public class DBMiniDragonSummonPatternSO : BossPatternSO
{
    [Header("Animation")]
    [SerializeField] private string animName = "FlyFWD";
    [SerializeField] private float crossFade = 0.15f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxPrefab;

    [Header("Summon")]
    [SerializeField] private MiniDragonController miniDragonPrefab;
    [SerializeField] private float spawnRadius = 4f;
    [SerializeField] private float summonDelay = 1.2f;
    [SerializeField] private float takeOffDuration = 0.6f;
    [SerializeField] private float landDuration = 0.7f;
    [SerializeField] private float flightHeight = 10f;
    [SerializeField] private float landingHitRadius = 4f;

    private SummonState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new SummonState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        var dragon = ctx.Boss as DragonBossMonster;
        if (dragon == null) return false;

        float hp = ctx.Boss.HpRatio;
        if (hp <= 0.8f && !dragon.DBBlackboard.IsSummonTriggered(0.8f)) return true;
        if (hp <= 0.5f && !dragon.DBBlackboard.IsSummonTriggered(0.5f)) return true;
        if (hp <= 0.1f && !dragon.DBBlackboard.IsSummonTriggered(0.1f)) return true;
        return false;
    }

    public override bool CanForceInterrupt(BossPatternContext ctx) => CanExecute(ctx);

    public override SpecialStateBase GetRuntimeState() => _state;

    private sealed class SummonState : InvincibleState<DBMiniDragonSummonPatternSO>
    {
        private float _timer;
        private int _phase;
        private Vector3 _groundPos;
        private Vector3 _airPos;
        private Vector3 _landingPos;
        private bool _originalUpdatePosition;
        private bool _originalUpdateRotation;
        private Renderer[] _renderers;
        private float _lockedThreshold;

        public SummonState(DBMiniDragonSummonPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            if (ctx.Agent.isOnNavMesh)
                ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;

            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animName, Data.crossFade);

            _groundPos = ctx.Transform.position;
            float baseY = (ctx.Monster as DragonBossMonster)?.DBBlackboard?.SpawnY ?? _groundPos.y;
            _groundPos.y = baseY;
            _airPos = _groundPos + Vector3.up * Data.flightHeight;
            _landingPos = _groundPos;
            _renderers = ctx.Transform.GetComponentsInChildren<Renderer>(true);

            _originalUpdatePosition = ctx.Agent.updatePosition;
            _originalUpdateRotation = ctx.Agent.updateRotation;
            ctx.Agent.updatePosition = false;
            ctx.Agent.updateRotation = false;

            // 즉시 사용 표시 — 다음 틱부터 IsSummonTriggered가 true를 반환하여
            // HandleForceInterrupts가 동일 패턴을 _pendingForce에 큐잉하지 않도록 방지
            var dragon = ctx.Monster as DragonBossMonster;
            if (dragon != null)
            {
                float hp = dragon.HpRatio;
                if (hp <= 0.1f && !dragon.DBBlackboard.IsSummonTriggered(0.1f))
                    _lockedThreshold = 0.1f;
                else if (hp <= 0.5f && !dragon.DBBlackboard.IsSummonTriggered(0.5f))
                    _lockedThreshold = 0.5f;
                else
                    _lockedThreshold = 0.8f;
                dragon.DBBlackboard.MarkSummonUsed(_lockedThreshold);
            }

            _phase = 0;
            _timer = 0f;
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            switch (_phase)
            {
                case 0:
                    UpdateFlight(ctx, _groundPos, _airPos, _timer / Mathf.Max(0.01f, Data.takeOffDuration));
                    if (_timer < Data.takeOffDuration) return;

                    SetRenderersEnabled(false);
                    ctx.Transform.position = _airPos;
                    if (Data.vfxPrefab != null)
                    {
                        BossEffectPool.SpawnOneShot(
                            Data.vfxPrefab,
                            _groundPos,
                            ctx.Transform.rotation);
                    }
                    SpawnMiniDragons(ctx);
                    _phase = 1;
                    _timer = 0f;
                    break;

                case 1:
                    if (_timer < Data.summonDelay) return;
                    _phase = 2;
                    _timer = 0f;
                    break;

                case 2:
                    var dragon = ctx.Monster as DragonBossMonster;
                    if (dragon == null) break;
                    if (dragon.DBBlackboard.ActiveMiniDragonCount > 0) return;

                    _landingPos = ctx.Runtime.PlayerTarget != null
                        ? ctx.Runtime.PlayerTarget.position
                        : _groundPos;
                    _landingPos.y = _groundPos.y;
                    SetRenderersEnabled(true);
                    _phase = 3;
                    _timer = 0f;
                    break;

                case 3:
                    UpdateFlight(ctx, _airPos, _landingPos, _timer / Mathf.Max(0.01f, Data.landDuration));
                    if (_timer < Data.landDuration) return;

                    ctx.Transform.position = _landingPos;
                    BossEffectPool.SpawnOneShot(Data.vfxPrefab, _landingPos, ctx.Transform.rotation);
                    DoLandingHit(ctx);
                    RestoreAgentTracking(ctx);
                    ctx.Agent.Warp(_landingPos);
                    ctx.Monster.ChangeState<PatrolState>();
                    break;
            }
        }

        public override void Exit(MonsterContext ctx)
        {
            SetRenderersEnabled(true);
            RestoreAgentTracking(ctx);
        }

        private void UpdateFlight(MonsterContext ctx, Vector3 from, Vector3 to, float t)
        {
            t = Mathf.Clamp01(t);
            ctx.Transform.position = Vector3.Lerp(from, to, t);

            Vector3 dir = to - from;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                ctx.Transform.rotation = Quaternion.LookRotation(dir.normalized);
        }

        private void SpawnMiniDragons(MonsterContext ctx)
        {
            var dragon = ctx.Monster as DragonBossMonster;
            if (dragon == null) return;

            // _lockedThreshold는 Enter()에서 이미 결정 및 MarkSummonUsed 완료
            const int count = 3;
            dragon.DBBlackboard.ActiveMiniDragonCount = count;

            for (int i = 0; i < count; i++)
            {
                float angle = i * (360f / count);
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * Data.spawnRadius;
                Vector3 spawnPos = _groundPos + offset;

                var mini = Data.miniDragonPrefab != null
                    ? Object.Instantiate(Data.miniDragonPrefab, spawnPos, Quaternion.identity)
                    : MiniDragonController.CreateFallback(spawnPos);
                mini.Init(dragon, ctx.Runtime.PlayerTarget);
            }
        }

        private void DoLandingHit(MonsterContext ctx)
        {
            int damage = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
            float kbForce = ctx.Stat.knockbackForce;

            var hits = Physics.OverlapSphere(_landingPos, Data.landingHitRadius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                    ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                Vector3 dir = (col.transform.position - _landingPos).normalized;
                dir.y = 0.3f;
                player.TakeDamage(damage);
                player.ApplyKnockback(dir.normalized * kbForce, 0.35f);
            }
        }

        private void SetRenderersEnabled(bool enabled)
        {
            if (_renderers == null) return;
            foreach (var renderer in _renderers)
            {
                if (renderer != null)
                    renderer.enabled = enabled;
            }
        }

        private void RestoreAgentTracking(MonsterContext ctx)
        {
            ctx.Agent.updatePosition = _originalUpdatePosition;
            ctx.Agent.updateRotation = _originalUpdateRotation;
        }
    }
}
}
