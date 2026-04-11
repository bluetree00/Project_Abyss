using UnityEngine;

namespace Abyss.Monster
{
[CreateAssetMenu(fileName = "DBIceLandingPatternSO",
                 menuName = "Abyss/Boss/DragonBoss/IceLanding")]
public class DBIceLandingPatternSO : BossPatternSO
{
    [Header("Animation")]
    [SerializeField] private string animName = "FlyFWD";
    [SerializeField] private float crossFade = 0.15f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxPrefab;

    [Header("Landing")]
    [SerializeField] private float warningDuration = 2f;
    [SerializeField] private float blastRadius = 5f;
    [SerializeField] private float shockwaveRadius = 14f;
    [SerializeField] private float safeEdgePadding = 2f;
    [SerializeField] private float columnRingRadius = 4f;
    [SerializeField] private int columnCount = 6;

    [Header("Flight")]
    [SerializeField] private float riseHeight = 8f;
    [SerializeField] private float diveDuration = 0.35f;

    [Header("Cooldown")]
    [SerializeField] private float patternCooldown = 30f;

    private float _cooldownEndTime = -999f;
    private IceLandingState _state;

    public override void Initialize(BossPatternContext ctx) { _cooldownEndTime = float.MinValue; _state = new IceLandingState(this); }

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (Time.time < _cooldownEndTime) return false;
        var dragon = ctx.Boss as DragonBossMonster;
        return dragon == null || dragon.DBBlackboard.CurrentElement == DragonBossBlackboard.DragonElement.Ice;
    }

    internal void StartCooldown() => _cooldownEndTime = Time.time + patternCooldown;
    internal bool IsOnCooldown    => Time.time < _cooldownEndTime;

    public override SpecialStateBase GetRuntimeState() => _state;

    private sealed class IceLandingState : FullLockState<DBIceLandingPatternSO>
    {
        private float _timer;
        private int _phase;
        private Vector3 _startPos;
        private Vector3 _flyPos;
        private Vector3 _targetPos;
        private bool _originalUpdatePosition;
        private bool _originalUpdateRotation;

        public IceLandingState(DBIceLandingPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            if (ctx.Agent.isOnNavMesh)
                ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;

            _originalUpdatePosition = ctx.Agent.updatePosition;
            _originalUpdateRotation = ctx.Agent.updateRotation;
            ctx.Agent.updatePosition = false;
            ctx.Agent.updateRotation = false;

            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animName, Data.crossFade);

            var dragon = ctx.Monster as DragonBossMonster;
            Vector3 spawnXZ = dragon?.DBBlackboard?.SpawnPosition ?? ctx.Transform.position;
            // 실제 지면 y를 레이캐스트로 찾아 착지 위치 보정
            spawnXZ.y = DragonBossVisualHelper.GetGroundY(spawnXZ);
            _targetPos = spawnXZ;

            _startPos = ctx.Transform.position;
            _flyPos = _targetPos + Vector3.up * Data.riseHeight;

            var iceColor      = DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Ice);
            var dangerColor   = new Color(1f, 0.20f, 0.20f, 1f);
            var warningColor  = new Color(1f, 0.70f, 0.10f, 1f);  // 채워지는 원 시작 색
            var safeColor     = new Color(0.3f, 1f, 0.4f, 1f);
            float dangerBound = Data.shockwaveRadius - Data.safeEdgePadding;

            // ── 파편 퍼짐 표현: 채워지는 원 (중심→피해경계) ────────
            // warningColor(주황) → dangerColor(빨강)로 채워지며, 착지 직전 완성
            MonsterGroundWarning.SpawnFillCircle(
                _targetPos,
                dangerBound,
                Data.warningDuration,
                warningColor,
                dangerColor);

            // ── 안전 구역 외곽 경계선 ────────────────────────────────
            MonsterGroundWarning.Spawn(_targetPos, Data.shockwaveRadius, Data.warningDuration, safeColor);

            // ── 착지 중심 직격 범위 ───────────────────────────────────
            MonsterGroundWarning.Spawn(_targetPos, Data.blastRadius, Data.warningDuration, iceColor);

            // ── 얼음 기둥 위치 경고 (지면 y에 스냅) ──────────────────
            for (int i = 0; i < Data.columnCount; i++)
            {
                float angle = i * (360f / Mathf.Max(1, Data.columnCount));
                Vector3 colPos = _targetPos + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * Data.columnRingRadius;
                colPos.y = DragonBossVisualHelper.GetGroundY(colPos);
                MonsterGroundWarning.Spawn(colPos, 1f, Data.warningDuration, iceColor);
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
                    // 부상: startPos → flyPos (warningDuration의 60% 동안)
                    float riseEnd = Data.warningDuration * 0.6f;
                    float riseT = Mathf.Clamp01(_timer / riseEnd);
                    ctx.Transform.position = Vector3.Lerp(_startPos, _flyPos, riseT);

                    if (_timer >= riseEnd)
                    {
                        _timer = 0f;
                        _phase = 1;
                    }
                    break;

                case 1:
                    // 공중 호버: flyPos에 고정 (나머지 40%)
                    ctx.Transform.position = _flyPos;
                    if (_timer >= Data.warningDuration * 0.4f)
                    {
                        _timer = 0f;
                        _phase = 2;
                    }
                    break;

                case 2:
                    // 낙하: flyPos → targetPos
                    float diveT = Mathf.Clamp01(_timer / Data.diveDuration);
                    ctx.Transform.position = Vector3.Lerp(_flyPos, _targetPos, diveT);

                    if (_timer >= Data.diveDuration)
                    {
                        ctx.Transform.position = _targetPos;
                        ctx.Agent.Warp(_targetPos);

                        if (Data.vfxPrefab != null)
                            BossEffectPool.SpawnOneShot(Data.vfxPrefab, _targetPos, Quaternion.identity);

                        DoLandingHit(ctx);

                        ctx.Agent.updatePosition = _originalUpdatePosition;
                        ctx.Agent.updateRotation = _originalUpdateRotation;

                        _timer = 0f;
                        _phase = 3;
                    }
                    break;

                case 3:
                    if (_timer >= 0.45f)
                        ctx.Monster.ChangeState<PatrolState>();
                    break;
            }
        }

        public override void Exit(MonsterContext ctx)
        {
            ctx.Agent.updatePosition = _originalUpdatePosition;
            ctx.Agent.updateRotation = _originalUpdateRotation;
            Data.StartCooldown();
        }

        private void DoLandingHit(MonsterContext ctx)
        {
            int   damage          = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * 1.5f);
            float dangerBoundary  = Data.shockwaveRadius - Data.safeEdgePadding;

            // ── 기둥 VFX ────────────────────────────────────────────
            for (int i = 0; i < Data.columnCount; i++)
            {
                float   angle = i * (360f / Mathf.Max(1, Data.columnCount));
                Vector3 pos   = _targetPos + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * Data.columnRingRadius;
                if (Data.vfxPrefab != null)
                    BossEffectPool.SpawnOneShot(Data.vfxPrefab, pos, Quaternion.identity);
            }

            // ── 파편 VFX: 착지 지점에서 방사형으로 스폰 ─────────────
            // (경고 링 대신 VFX로만 파편 표현)
            int shardCount = 8;
            for (int s = 0; s < shardCount; s++)
            {
                float   angle  = s * (360f / shardCount);
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * (dangerBoundary * 0.5f);
                Vector3 shardPos = _targetPos + offset;
                shardPos.y = _targetPos.y;
                if (Data.vfxPrefab != null)
                    BossEffectPool.SpawnOneShot(Data.vfxPrefab, shardPos,
                        Quaternion.Euler(0f, angle, 0f));
            }

            // ── 데미지: dangerBoundary 안쪽 플레이어만 적중 ─────────
            var hits = Physics.OverlapSphere(_targetPos, Data.shockwaveRadius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                float distXZ = Vector2.Distance(
                    new Vector2(col.transform.position.x, col.transform.position.z),
                    new Vector2(_targetPos.x, _targetPos.z));

                // dangerBoundary 밖은 안전 구역 — 피해 없음
                if (distXZ >= dangerBoundary) continue;

                player.TakeDamage(damage);
                player.ApplyKnockback(Vector3.zero, 2f);
            }
        }
    }
}
}
