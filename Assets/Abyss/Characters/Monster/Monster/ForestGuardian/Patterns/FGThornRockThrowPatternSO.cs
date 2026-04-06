using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 파워 폼 패턴 — 돌 던지기.
/// 자신의 앞에 돌을 소환하고 플레이어에게 던진다.
/// 공격 범위: 지름 8m (반경 4m).
/// </summary>
[CreateAssetMenu(fileName = "FGThornRockThrowPatternSO",
                 menuName  = "Abyss/Boss/ForestGuardian/Thorn/RockThrow")]
public class FGThornRockThrowPatternSO : BossPatternSO
{
    [Header("애니메이션")]
    [SerializeField] private string animThrow = "GrabAndThrowAttack";
    [SerializeField] private float  crossFade = 0.15f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxRockSpawn;
    [SerializeField] private GameObject vfxImpact;

    [Header("돌 던지기 설정")]
    [SerializeField] private float impactRadius     = 4f;    // 착탄 반경 (지름 8m)
    [SerializeField] private float spawnDuration    = 1f;    // 돌 소환 시간
    [SerializeField] private float throwDuration    = 0.5f;  // 투척 비행 시간
    [SerializeField] private float warningDuration  = 1.2f;  // 경고 표시 시간

    private FGThornRockThrowState _state;

    public override void Initialize(BossPatternContext ctx)
        => _state = new FGThornRockThrowState(this);

    public override bool CanExecute(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;

    // ── 내부 상태 ────────────────────────────────────────────────────

    private sealed class FGThornRockThrowState : FullLockState<FGThornRockThrowPatternSO>
    {
        private enum ThrowPhase { Spawn, Throw, Impact }

        private ThrowPhase _phase;
        private float      _timer;
        private Vector3    _targetPos;
        private GameObject _rockVfx;

        public FGThornRockThrowState(FGThornRockThrowPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;
            _phase = ThrowPhase.Spawn;
            _timer = 0f;

            _targetPos = ctx.Runtime.PlayerTarget != null
                ? ctx.Runtime.PlayerTarget.position
                : ctx.Transform.position + ctx.Transform.forward * 5f;

            // 플레이어 위치에 경고 원
            MonsterGroundWarning.Spawn(
                _targetPos, Data.impactRadius,
                Data.warningDuration, new Color(1f, 0.3f, 0f, 0.9f));

            // 보스 앞에 돌 VFX 소환
            Vector3 rockPos = ctx.Transform.position + ctx.Transform.forward * 1.5f + Vector3.up * 2f;
            if (Data.vfxRockSpawn != null)
                _rockVfx = BossEffectPool.Spawn(Data.vfxRockSpawn, rockPos, Quaternion.identity);

            if (ctx.Animator != null && !string.IsNullOrEmpty(Data.animThrow))
                ctx.Animator.CrossFade(Data.animThrow, Data.crossFade);
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            switch (_phase)
            {
                case ThrowPhase.Spawn:
                    // 돌 소환 대기
                    if (_timer >= Data.spawnDuration)
                    {
                        _phase = ThrowPhase.Throw;
                        _timer = 0f;
                    }
                    break;

                case ThrowPhase.Throw:
                {
                    // 돌 VFX를 목표로 이동
                    if (_rockVfx != null)
                    {
                        Vector3 start = ctx.Transform.position + ctx.Transform.forward * 1.5f + Vector3.up * 2f;
                        float t = Mathf.Clamp01(_timer / Data.throwDuration);
                        Vector3 pos = Vector3.Lerp(start, _targetPos + Vector3.up * 0.5f, t);
                        pos.y += Mathf.Sin(t * Mathf.PI) * 3f; // 포물선
                        _rockVfx.transform.position = pos;
                    }

                    if (_timer >= Data.throwDuration)
                    {
                        _phase = ThrowPhase.Impact;
                        _timer = 0f;
                        ApplyImpact(ctx);
                    }
                    break;
                }

                case ThrowPhase.Impact:
                    if (_timer >= 0.5f)
                        ctx.Monster.ChangeState<PatrolState>();
                    break;
            }
        }

        public override void Exit(MonsterContext ctx)
        {
            if (_rockVfx != null)
                BossEffectPool.Release(_rockVfx);
        }

        private void ApplyImpact(MonsterContext ctx)
        {
            // 돌 VFX 제거 + 착탄 VFX
            if (_rockVfx != null)
            {
                BossEffectPool.Release(_rockVfx);
                _rockVfx = null;
            }

            if (Data.vfxImpact != null)
                BossEffectPool.SpawnOneShot(Data.vfxImpact, _targetPos, Quaternion.identity);

            // 착탄 범위 판정
            int   damage  = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * 1.5f);
            float kbForce = ctx.Stat.knockbackForce * 2f;
            var hits = Physics.OverlapSphere(_targetPos, Data.impactRadius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                          ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;
                player.TakeDamage(damage);
                Vector3 dir = (col.transform.position - _targetPos).normalized;
                dir.y = 0.5f;
                player.ApplyKnockback(dir.normalized * kbForce);
            }
        }
    }
}
}
