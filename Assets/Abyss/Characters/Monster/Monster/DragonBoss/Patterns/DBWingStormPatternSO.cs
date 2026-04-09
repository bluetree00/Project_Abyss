using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 드래곤 보스 — 윙 스톰.
/// 제자리에서 플레이어 방향으로 날개를 강하게 휘저어 폭풍을 일으킨다.
/// 2초 경고(모션) 후 플레이어 방향 직사각형 범위에 광역 데미지.
/// </summary>
[CreateAssetMenu(fileName = "DBWingStormPatternSO",
                 menuName  = "Abyss/Boss/DragonBoss/WingStorm")]
public class DBWingStormPatternSO : BossPatternSO
{
    [Header("Animation")]
    [SerializeField] private string animName  = "Attack01";
    [SerializeField] private float  crossFade = 0.1f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxPrefab;

    [Header("공격 설정")]
    [SerializeField] private float warningDuration = 2f;
    [SerializeField] private float stormWidth      = 6f;    // 폭풍 너비
    [SerializeField] private float stormLength     = 14f;   // 폭풍 길이 (플레이어 방향)

    [Header("Cooldown")]
    [SerializeField] private float patternCooldown = 15f;

    private float _cooldownEndTime = -999f;
    private WingStormState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new WingStormState(this);

    public override bool CanExecute(BossPatternContext ctx) => Time.time >= _cooldownEndTime;

    public override SpecialStateBase GetRuntimeState() => _state;

    internal void StartCooldown() => _cooldownEndTime = Time.time + patternCooldown;

    private sealed class WingStormState : FullLockState<DBWingStormPatternSO>
    {
        private float   _timer;
        private int     _phase;
        private Vector3 _stormForward;   // 경고 시작 시 기록한 플레이어 방향

        public WingStormState(DBWingStormPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            if (ctx.Agent.isOnNavMesh)
                ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;

            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animName, Data.crossFade);

            // 현재 플레이어 방향 고정 (경고 표시에 사용)
            _stormForward = GetPlayerDir(ctx);

            // 직사각형 경고 장판 (보스 위치에서 플레이어 방향으로 뻗음)
            var warnColor = new Color(0.9f, 0.7f, 0f);
            MonsterGroundWarning.SpawnRect(
                ctx.Transform.position,
                _stormForward,
                Data.stormWidth,
                Data.stormLength,
                Data.warningDuration,
                warnColor);

            _phase = 0;
            _timer = 0f;
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            switch (_phase)
            {
                case 0:
                    // 경고 대기 — 이 동안 서서히 플레이어 방향 회전
                    FacePlayer(ctx, 45f);
                    if (_timer < Data.warningDuration) return;

                    _stormForward = GetPlayerDir(ctx);
                    DoBlast(ctx);
                    _phase = 1;
                    _timer = 0f;
                    break;

                case 1:
                    // 공격 후 짧은 딜레이
                    if (_timer >= 0.4f)
                        ctx.Monster.ChangeState<PatrolState>();
                    break;
            }
        }

        public override void Exit(MonsterContext ctx)
        {
            Data.StartCooldown();
        }

        private void DoBlast(MonsterContext ctx)
        {
            int   damage  = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier);
            float kbForce = ctx.Stat.knockbackForce;

            if (Data.vfxPrefab != null)
                BossEffectPool.SpawnOneShot(Data.vfxPrefab, ctx.Transform.position, ctx.Transform.rotation);

            // 직사각형 중심: 보스 위치에서 stormForward 방향으로 stormLength * 0.5f
            Vector3 boxCenter = ctx.Transform.position
                              + _stormForward * (Data.stormLength * 0.5f)
                              + Vector3.up * 1.5f;

            Quaternion boxRot = _stormForward.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(_stormForward)
                : Quaternion.identity;

            var hits = Physics.OverlapBox(
                boxCenter,
                new Vector3(Data.stormWidth * 0.5f, 2f, Data.stormLength * 0.5f),
                boxRot);

            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                Vector3 dir = (col.transform.position - ctx.Transform.position).normalized;
                dir.y = 0.3f;

                player.TakeDamage(damage);

                var dragon  = ctx.Monster as DragonBossMonster;
                var element = dragon?.DBBlackboard?.CurrentElement
                              ?? DragonBossBlackboard.DragonElement.Ice;
                switch (element)
                {
                    case DragonBossBlackboard.DragonElement.Ice:
                        player.ApplyKnockback(dir.normalized * kbForce, 2f);
                        break;
                    case DragonBossBlackboard.DragonElement.Thunder:
                        player.ApplyKnockback(dir.normalized * kbForce, 0.5f);
                        break;
                    default: // Fire
                        player.ApplySlow(0.4f, 2f);
                        player.ApplyKnockback(dir.normalized * kbForce, 0.3f);
                        break;
                }
            }
        }

        private Vector3 GetPlayerDir(MonsterContext ctx)
        {
            if (ctx.Runtime.PlayerTarget == null) return ctx.Transform.forward;
            Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
            dir.y = 0f;
            return dir.sqrMagnitude > 0.001f ? dir.normalized : ctx.Transform.forward;
        }

        private void FacePlayer(MonsterContext ctx, float speed)
        {
            Vector3 dir = GetPlayerDir(ctx);
            if (dir.sqrMagnitude < 0.001f) return;
            Quaternion target = Quaternion.LookRotation(dir);
            ctx.Transform.rotation = Quaternion.RotateTowards(ctx.Transform.rotation, target, speed * Time.deltaTime);
        }
    }
}
}
