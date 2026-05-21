using UnityEngine;

namespace RelicFairy.Monster
{
[CreateAssetMenu(fileName = "DBIceLandingPatternSO",
                 menuName = "RelicFairy/Boss/DragonBoss/IceLanding")]
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
    [SerializeField] private float shockwaveRadius = 12f;
    [SerializeField] private float safeEdgePadding = 2f;
    [SerializeField] private float columnRingRadius = 8f;
    [SerializeField] private float columnRadius = 6f;
    [SerializeField] private int columnCount = 8;

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

            // 맵 중앙 = 착지 지점
            _targetPos = ctx.Runtime.SpawnPosition;
            _targetPos.y = ctx.Runtime.SpawnPosition.y;

            _startPos = ctx.Transform.position;
            _flyPos = _targetPos + Vector3.up * Data.riseHeight;

            var iceColor = DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Ice);

            // 착지 지점(맵 중앙) 경고
            MonsterGroundWarning.Spawn(_targetPos, Data.blastRadius, Data.warningDuration, iceColor);

            // 얼음 기둥 위치 경고 (맵 중앙 기준, 넓은 범위)
            for (int i = 0; i < Data.columnCount; i++)
            {
                float angle = i * (360f / Mathf.Max(1, Data.columnCount));
                Vector3 colPos = _targetPos + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * Data.columnRingRadius;
                MonsterGroundWarning.Spawn(colPos, Data.columnRadius, Data.warningDuration, iceColor);
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
                {
                    // 맵 중앙으로 이동하며 상승 (warningDuration의 50%)
                    float moveEnd = Data.warningDuration * 0.5f;
                    float moveT = Mathf.Clamp01(_timer / moveEnd);
                    ctx.Transform.position = Vector3.Lerp(_startPos, _flyPos, moveT);

                    if (_timer >= moveEnd)
                    {
                        _timer = 0f;
                        _phase = 1;
                    }
                    break;
                }

                case 1:
                    // 공중 호버: 맵 중앙 상공 고정 (나머지 50%)
                    ctx.Transform.position = _flyPos;
                    if (_timer >= Data.warningDuration * 0.5f)
                    {
                        _timer = 0f;
                        _phase = 2;
                    }
                    break;

                case 2:
                {
                    // 급강하: 맵 중앙으로 착지
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
                }

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
            int columnDamage = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * 0.8f);

            // 기둥 VFX + 기둥별 데미지 판정
            for (int i = 0; i < Data.columnCount; i++)
            {
                float angle = i * (360f / Mathf.Max(1, Data.columnCount));
                Vector3 pos = _targetPos + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * Data.columnRingRadius;
                if (Data.vfxPrefab != null)
                    BossEffectPool.SpawnOneShot(Data.vfxPrefab, pos, Quaternion.identity);

                var columnHits = Physics.OverlapSphere(pos, Data.columnRadius);
                foreach (var col in columnHits)
                {
                    var p = col.GetComponent<PlayerController>()
                        ?? col.GetComponentInParent<PlayerController>();
                    if (p == null) continue;
                    p.TakeDamage(columnDamage);
                    p.ApplyKnockback(Vector3.zero, 2f);
                }
            }

            // 중앙 충격파 데미지
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
