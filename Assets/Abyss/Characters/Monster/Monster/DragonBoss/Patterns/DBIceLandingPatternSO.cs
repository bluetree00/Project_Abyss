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

    private IceLandingState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new IceLandingState(this);

    public override bool CanExecute(BossPatternContext ctx) => true;

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
            _targetPos = dragon?.DBBlackboard?.SpawnPosition ?? ctx.Transform.position;
            _targetPos.y = dragon?.DBBlackboard?.SpawnY ?? ctx.Transform.position.y;

            _startPos = ctx.Transform.position;
            _flyPos = _targetPos + Vector3.up * Data.riseHeight;

            var iceColor = new Color(0.3f, 0.7f, 1f);

            // 착지 지점 경고
            MonsterGroundWarning.Spawn(_targetPos, Data.blastRadius, Data.warningDuration, iceColor);

            // 얼음 기둥 위치 경고
            for (int i = 0; i < Data.columnCount; i++)
            {
                float angle = i * (360f / Mathf.Max(1, Data.columnCount));
                Vector3 colPos = _targetPos + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * Data.columnRingRadius;
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
        }

        private void DoLandingHit(MonsterContext ctx)
        {
            int damage = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * 1.5f);

            for (int i = 0; i < Data.columnCount; i++)
            {
                float angle = i * (360f / Mathf.Max(1, Data.columnCount));
                Vector3 pos = _targetPos + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * Data.columnRingRadius;
                if (Data.vfxPrefab != null)
                    BossEffectPool.SpawnOneShot(Data.vfxPrefab, pos, Quaternion.identity);
            }

            var hits = Physics.OverlapSphere(_targetPos, Data.shockwaveRadius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                    ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                float distFromCenter = Vector3.Distance(
                    new Vector3(col.transform.position.x, 0f, col.transform.position.z),
                    new Vector3(_targetPos.x, 0f, _targetPos.z));
                if (distFromCenter >= Data.shockwaveRadius - Data.safeEdgePadding)
                    continue;

                player.TakeDamage(damage);
                player.ApplyKnockback(Vector3.zero, 2f);
            }
        }
    }
}
}
