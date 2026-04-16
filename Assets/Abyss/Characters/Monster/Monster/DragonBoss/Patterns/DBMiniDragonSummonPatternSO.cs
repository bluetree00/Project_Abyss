using UnityEngine;

namespace Abyss.Monster
{
[CreateAssetMenu(fileName = "DBMiniDragonSummonPatternSO",
                 menuName = "Abyss/Boss/DragonBoss/MiniDragonSummon")]
public class DBMiniDragonSummonPatternSO : BossPatternSO
{
    [Header("Animation")]
    [SerializeField] private string animName    = "FlyFWD";
    [SerializeField] private float  crossFade   = 0.15f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxPrefab;

    [Header("Summon")]
    [SerializeField] private GameObject miniDragonPrefab; // MiniDragonController 있는 프리팹
    [SerializeField] private float miniDragonScale  = 0.5f;
    [SerializeField] private float spawnRadius      = 4f;
    [SerializeField] private float summonDelay      = 1.2f;
    [SerializeField] private float takeOffDuration  = 0.6f;
    [SerializeField] private float landDuration     = 0.7f;
    [SerializeField] private float flightHeight     = 10f;

    [Header("Landing")]
    [Tooltip("미니드래곤 전멸 후 재사용할 얼음 착지 패턴. 쿨타임 중이면 그냥 PatrolState로 복귀.")]
    [SerializeField] private DBIceLandingPatternSO iceLandingPattern;

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
        private float   _timer;
        private int     _phase;
        private Vector3 _groundPos;
        private Vector3 _airPos;
        private bool    _originalUpdatePosition;
        private bool    _originalUpdateRotation;
        private Renderer[] _renderers;
        private float   _lockedThreshold;
        private DragonBossBlackboard.DragonElement _element;

        public SummonState(DBMiniDragonSummonPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            if (ctx.Agent.isOnNavMesh) ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;

            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animName, Data.crossFade);

            _groundPos = ctx.Transform.position;
            float baseY = (ctx.Monster as DragonBossMonster)?.DBBlackboard?.SpawnY ?? _groundPos.y;
            _groundPos.y = baseY;
            _airPos      = _groundPos + Vector3.up * Data.flightHeight;
            _renderers   = ctx.Transform.GetComponentsInChildren<Renderer>(true);

            _originalUpdatePosition = ctx.Agent.updatePosition;
            _originalUpdateRotation = ctx.Agent.updateRotation;
            ctx.Agent.updatePosition = false;
            ctx.Agent.updateRotation = false;

            // HP 임계값 결정 → 임계값 기준으로 원소 고정 (얼음→번개→불 순서 보장)
            var dragon = ctx.Monster as DragonBossMonster;
            if (dragon != null)
            {
                float hp = dragon.HpRatio;
                if      (hp <= 0.1f && !dragon.DBBlackboard.IsSummonTriggered(0.1f)) _lockedThreshold = 0.1f;
                else if (hp <= 0.5f && !dragon.DBBlackboard.IsSummonTriggered(0.5f)) _lockedThreshold = 0.5f;
                else                                                                   _lockedThreshold = 0.8f;
                dragon.DBBlackboard.MarkSummonUsed(_lockedThreshold);
            }

            // 소환 회차(임계값)에 따라 원소 고정 — 현재 HP 원소와 무관
            _element = _lockedThreshold <= 0.11f
                ? DragonBossBlackboard.DragonElement.Fire
                : _lockedThreshold <= 0.51f
                    ? DragonBossBlackboard.DragonElement.Thunder
                    : DragonBossBlackboard.DragonElement.Ice;

            _phase = 0;
            _timer = 0f;
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            switch (_phase)
            {
                // Phase 0: 이륙
                case 0:
                    LerpPosition(ctx, _groundPos, _airPos, _timer / Mathf.Max(0.01f, Data.takeOffDuration));
                    if (_timer < Data.takeOffDuration) return;

                    SetRenderersEnabled(false);
                    ctx.Transform.position = _airPos;
                    if (Data.vfxPrefab != null)
                        BossEffectPool.SpawnOneShot(Data.vfxPrefab, _groundPos, ctx.Transform.rotation);
                    SpawnMiniDragons(ctx);
                    _phase = 1;
                    _timer = 0f;
                    break;

                // Phase 1: 소환 연출 대기
                case 1:
                    if (_timer < Data.summonDelay) return;
                    _phase = 2;
                    _timer = 0f;
                    break;

                // Phase 2: 미니 드래곤 전멸 대기 (보스 공중 대기, 무적)
                case 2:
                {
                    var dragon = ctx.Monster as DragonBossMonster;
                    if (dragon == null) break;
                    if (dragon.DBBlackboard.ActiveMiniDragonCount > 0) return;

                    // 모두 처치됨 → 렌더러/Agent 복구
                    SetRenderersEnabled(true);
                    RestoreAgentTracking(ctx);

                    // IceLanding 패턴 재사용 (쿨타임 중이면 그냥 Patrol)
                    var iceLanding = Data.iceLandingPattern;
                    if (iceLanding != null && !iceLanding.IsOnCooldown)
                        ctx.Monster.ChangeState(iceLanding.GetRuntimeState());
                    else
                        ctx.Monster.ChangeState<PatrolState>();
                    break;
                }
            }
        }

        public override void Exit(MonsterContext ctx)
        {
            SetRenderersEnabled(true);
            RestoreAgentTracking(ctx);
        }

        // ── 미니 드래곤 소환 ────────────────────────────────────────────

        private void SpawnMiniDragons(MonsterContext ctx)
        {
            var dragon = ctx.Monster as DragonBossMonster;
            if (dragon == null) return;

            const int count = 3;
            dragon.DBBlackboard.ActiveMiniDragonCount = count;

            for (int i = 0; i < count; i++)
            {
                float   angle    = i * (360f / count);
                Vector3 offset   = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * Data.spawnRadius;
                Vector3 spawnPos = _groundPos + offset;
                spawnPos.y = DragonBossVisualHelper.GetGroundY(spawnPos);

                MiniDragonController mini;
                if (Data.miniDragonPrefab != null)
                {
                    var go  = Object.Instantiate(Data.miniDragonPrefab, spawnPos, Quaternion.identity);
                    go.transform.localScale = Vector3.one * Data.miniDragonScale;
                    mini = go.GetComponent<MiniDragonController>();
                    if (mini == null) mini = go.AddComponent<MiniDragonController>();
                }
                else
                {
                    mini = MiniDragonController.CreateFallback(spawnPos);
                }

                mini.Init(dragon, ctx.Runtime.PlayerTarget, _element);
            }
        }

        // ── 유틸 ────────────────────────────────────────────────────────

        private void LerpPosition(MonsterContext ctx, Vector3 from, Vector3 to, float t)
        {
            t = Mathf.Clamp01(t);
            ctx.Transform.position = Vector3.Lerp(from, to, t);

            Vector3 dir = to - from;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                ctx.Transform.rotation = Quaternion.LookRotation(dir.normalized);
        }

        private void SetRenderersEnabled(bool enabled)
        {
            if (_renderers == null) return;
            foreach (var r in _renderers)
                if (r != null) r.enabled = enabled;
        }

        private void RestoreAgentTracking(MonsterContext ctx)
        {
            ctx.Agent.updatePosition = _originalUpdatePosition;
            ctx.Agent.updateRotation = _originalUpdateRotation;
        }
    }
}
}
