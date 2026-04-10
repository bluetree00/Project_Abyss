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

    [Header("Landing Hit")]
    [SerializeField] private float landingHitRadius   = 5f;
    [SerializeField] private float columnRingRadius   = 6f;
    [SerializeField] private float columnDuration     = 2.5f;
    [SerializeField] private float columnRadius       = 1.5f;
    [SerializeField] private GameObject landingVfxPrefab;

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
        private Vector3 _landingPos;
        private bool    _originalUpdatePosition;
        private bool    _originalUpdateRotation;
        private Renderer[] _renderers;
        private float   _lockedThreshold;
        private DragonBossBlackboard.DragonElement _element;

        // 착지 충격파용 8방향 기둥 각도 (12시~시계방향)
        private static readonly float[] s_colAngles = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };

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
            _landingPos  = _groundPos;
            _renderers   = ctx.Transform.GetComponentsInChildren<Renderer>(true);

            _originalUpdatePosition = ctx.Agent.updatePosition;
            _originalUpdateRotation = ctx.Agent.updateRotation;
            ctx.Agent.updatePosition = false;
            ctx.Agent.updateRotation = false;

            // 현재 원소 캐싱 (소환 중에 원소가 바뀔 수 있어서 고정)
            var dragon = ctx.Monster as DragonBossMonster;
            _element = dragon?.DBBlackboard?.CurrentElement ?? DragonBossBlackboard.DragonElement.Ice;

            // HP 임계값 결정 및 즉시 사용 표시
            if (dragon != null)
            {
                float hp = dragon.HpRatio;
                if      (hp <= 0.1f && !dragon.DBBlackboard.IsSummonTriggered(0.1f)) _lockedThreshold = 0.1f;
                else if (hp <= 0.5f && !dragon.DBBlackboard.IsSummonTriggered(0.5f)) _lockedThreshold = 0.5f;
                else                                                                   _lockedThreshold = 0.8f;
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

                    // 모두 처치됨 → 착지 목표 결정
                    _landingPos = ctx.Runtime.PlayerTarget != null
                        ? ctx.Runtime.PlayerTarget.position
                        : _groundPos;
                    _landingPos.y = _groundPos.y;

                    // 착지 경고 장판
                    var elemColor = DragonBossVisualHelper.GetElementColor(_element);
                    MonsterGroundWarning.Spawn(_landingPos, Data.landingHitRadius, Data.landDuration, elemColor);

                    SetRenderersEnabled(true);
                    _phase = 3;
                    _timer = 0f;
                    break;
                }

                // Phase 3: 착지 강하
                case 3:
                    LerpPosition(ctx, _airPos, _landingPos, _timer / Mathf.Max(0.01f, Data.landDuration));
                    if (_timer < Data.landDuration) return;

                    ctx.Transform.position = _landingPos;
                    DoLandingShockwave(ctx);
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

        // ── 착지 충격파 (IceLanding과 유사한 원소별 패턴) ──────────────

        private void DoLandingShockwave(MonsterContext ctx)
        {
            int   damage   = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * 1.2f);
            float kbForce  = ctx.Stat.knockbackForce;
            // 소환 착지 충격파는 항상 얼음 속성
            var   elemColor = DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Ice);

            // 착지 VFX
            var vfxSrc = Data.landingVfxPrefab ?? Data.vfxPrefab;
            if (vfxSrc != null)
                BossEffectPool.SpawnOneShot(vfxSrc, _landingPos, ctx.Transform.rotation);

            // 근거리 플레이어 피해
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

            // 원소별 8방향 기둥 — 3초 유지 hazard
            for (int i = 0; i < s_colAngles.Length; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, s_colAngles[i], 0f) * Vector3.forward;
                Vector3 pos = _landingPos + dir * Data.columnRingRadius;
                pos.y = DragonBossVisualHelper.GetGroundY(pos);

                MonsterGroundWarning.Spawn(pos, Data.columnRadius, Data.columnDuration, elemColor);
                BossColumnHazard.Spawn(
                    pos,
                    Data.columnRadius,
                    Data.columnDuration,
                    0.5f,
                    (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * 0.5f),
                    DragonBossBlackboard.DragonElement.Ice);
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
