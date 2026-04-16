using UnityEngine;

namespace Abyss.Monster
{
[CreateAssetMenu(fileName = "DBThunderShieldPatternSO",
                 menuName = "Abyss/Boss/DragonBoss/ThunderShield")]
public class DBThunderShieldPatternSO : BossPatternSO
{
    [Header("Animation")]
    [SerializeField] private string animName = "Attack01";
    [SerializeField] private float crossFade = 0.1f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxPrefab;

    [Header("Shield")]
    [SerializeField] private float shieldDuration = 3f;
    [SerializeField] private float reflectRadius = 3.5f;
    [SerializeField] private float reflectTickInterval = 0.5f;

    [Header("Barrier Visual")]
    [SerializeField] private float barrierLineWidth = 0.2f;

    private ThunderShieldState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new ThunderShieldState(this);

    public override bool CanExecute(BossPatternContext ctx)
    {
        var dragon = ctx.Boss as DragonBossMonster;
        return dragon == null || dragon.DBBlackboard.CurrentElement == DragonBossBlackboard.DragonElement.Thunder;
    }

    public override SpecialStateBase GetRuntimeState() => _state;

    private sealed class ThunderShieldState : InvincibleState<DBThunderShieldPatternSO>
    {
        private const int BarrierSegments = 64;

        private float _timer;
        private float _shieldTimer;
        private float _reflectTick;
        private int _phase;
        private float _phaseDuration;
        private GameObject _activeVfx;
        private LineRenderer _barrierLine;
        private Material     _barrierMat;

        public ThunderShieldState(DBThunderShieldPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            if (ctx.Agent.isOnNavMesh)
                ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;

            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animName, Data.crossFade);

            var dragon = ctx.Monster as DragonBossMonster;
            if (dragon != null)
                dragon.DBBlackboard.ThunderShieldActive = true;

            // ── 보스 주위를 감싸는 원형 배리어 LineRenderer ────────
            var lineGo = new GameObject("[ThunderBarrier]");
            lineGo.transform.position = ctx.Transform.position;

            _barrierMat = new Material(Shader.Find("Sprites/Default"));
            _barrierLine = lineGo.AddComponent<LineRenderer>();
            _barrierLine.loop               = true;
            _barrierLine.useWorldSpace      = true;
            _barrierLine.positionCount      = BarrierSegments;
            _barrierLine.widthMultiplier    = Data.barrierLineWidth;
            _barrierLine.material           = _barrierMat;
            _barrierLine.startColor         = new Color(1f, 0.9f, 0.1f, 1f);
            _barrierLine.endColor           = new Color(1f, 0.9f, 0.1f, 1f);
            _barrierLine.shadowCastingMode  = UnityEngine.Rendering.ShadowCastingMode.Off;
            _barrierLine.receiveShadows     = false;
            UpdateBarrierRing(ctx.Transform.position);

            if (Data.vfxPrefab != null)
            {
                _activeVfx = BossEffectPool.Spawn(
                    Data.vfxPrefab,
                    ctx.Transform.position,
                    ctx.Transform.rotation);
            }

            _phase = 0;
            _timer = 0f;
            _shieldTimer = 0f;
            _reflectTick = 0f;
            _phaseDuration = Data.shieldDuration;
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;
            _shieldTimer += Time.deltaTime;
            _reflectTick += Time.deltaTime;

            if (_activeVfx != null)
                _activeVfx.transform.position = ctx.Transform.position;

            // 배리어 링 보스 위치 추종 + 깜빡임
            if (_barrierLine != null)
            {
                UpdateBarrierRing(ctx.Transform.position);
                float blink = (Mathf.Sin(_shieldTimer * 6f) + 1f) * 0.5f; // 0~1 부드러운 펄스
                Color bc = Color.Lerp(
                    new Color(1f, 0.9f, 0.1f, 0.6f),
                    new Color(1f, 1f,   0.4f, 1.0f),
                    blink);
                _barrierLine.startColor = bc;
                _barrierLine.endColor   = bc;
            }

            if (_reflectTick >= Data.reflectTickInterval)
            {
                _reflectTick -= Data.reflectTickInterval;
                DoReflectHit(ctx);
            }

            if (_phase == 0 && _shieldTimer >= Data.shieldDuration)
            {
                _phase = 1;
                _phaseDuration = 0.3f;
                _timer = 0f;
            }

            if (_phase == 1 && _timer >= _phaseDuration)
            {
                EndShield(ctx);
                ctx.Monster.ChangeState<PatrolState>();
            }
        }

        public override void Exit(MonsterContext ctx)
        {
            EndShield(ctx);
        }

        private void EndShield(MonsterContext ctx)
        {
            var dragon = ctx.Monster as DragonBossMonster;
            if (dragon != null)
                dragon.DBBlackboard.ThunderShieldActive = false;

            if (_activeVfx != null)
            {
                BossEffectPool.Release(_activeVfx);
                _activeVfx = null;
            }

            if (_barrierLine != null)
            {
                Object.Destroy(_barrierLine.gameObject);
                _barrierLine = null;
            }
            if (_barrierMat != null)
            {
                Object.Destroy(_barrierMat);
                _barrierMat = null;
            }
        }

        private void UpdateBarrierRing(Vector3 center)
        {
            if (_barrierLine == null) return;
            float r    = Data.reflectRadius;
            float midY = center.y + 1.2f; // 보스 허리 높이
            for (int i = 0; i < BarrierSegments; i++)
            {
                float a = (float)i / BarrierSegments * Mathf.PI * 2f;
                _barrierLine.SetPosition(i,
                    new Vector3(center.x + Mathf.Cos(a) * r,
                                midY,
                                center.z + Mathf.Sin(a) * r));
            }
        }

        private void DoReflectHit(MonsterContext ctx)
        {
            var hits = Physics.OverlapSphere(ctx.Transform.position, Data.reflectRadius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                    ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                player.TakeDamage((int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * 0.3f));
                player.ApplyKnockback(Vector3.zero, 0.5f);
            }
        }
    }
}
}
