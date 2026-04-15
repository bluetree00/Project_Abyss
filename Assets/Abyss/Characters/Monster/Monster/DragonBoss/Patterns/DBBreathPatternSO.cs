using UnityEngine;

namespace Abyss.Monster
{
[CreateAssetMenu(fileName = "DBBreathPatternSO",
                 menuName = "Abyss/Boss/DragonBoss/Breath")]
public class DBBreathPatternSO : BossPatternSO
{
    [Header("Animation")]
    [SerializeField] private string animName = "Attack01";
    [SerializeField] private float crossFade = 0.1f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxPrefab;
    [SerializeField] private Vector3 breathLocalOffset = new Vector3(0f, 1.2f, 2.2f);
    [SerializeField] private Vector3 breathLocalScale = new Vector3(1.6f, 1.6f, 5.5f);

    [Header("Breath")]
    [SerializeField] private float breathDuration = 5f;
    [SerializeField] private float tickInterval = 0.3f;
    [SerializeField] private float castRadius = 1.2f;

    [Header("Flight")]
    [SerializeField] private float riseHeight = 8f;

    [Header("Beam Tracking")]
    [SerializeField] private float beamApproachRadius = 8f;
    [SerializeField] private float beamTrackSpeed = 3f;
    [SerializeField] private float beamWidth = 0.6f;

    private BreathState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new BreathState(this);

    public override bool CanExecute(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;

    private sealed class BreathState : FullLockState<DBBreathPatternSO>
    {
        private float _breathTimer;
        private float _tickTimer;
        private float _warningTimer;
        private int _phase;
        private GameObject _activeVfx;
        private Color _breathColor;
        private Vector3 _anchorPosition;
        private Vector3 _flyPosition;
        private Vector3 _beamTarget;
        private bool _originalUpdatePosition;
        private bool _originalUpdateRotation;
        private Transform _beamTransform;
        private Material _beamMat;

        private void CreateBeam(Color color)
        {
            var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = "[BreathBeam]";
            Object.Destroy(cylinder.GetComponent<Collider>());
            _beamMat = new Material(cylinder.GetComponent<Renderer>().sharedMaterial);
            if (_beamMat.HasProperty("_BaseColor")) _beamMat.SetColor("_BaseColor", color);
            if (_beamMat.HasProperty("_Color"))     _beamMat.SetColor("_Color",     color);
            cylinder.GetComponent<Renderer>().material = _beamMat;
            _beamTransform = cylinder.transform;
        }

        private void UpdateBeam(Vector3 start, Vector3 end)
        {
            if (_beamTransform == null) return;
            Vector3 dir = end - start;
            float len = dir.magnitude;
            if (len < 0.01f) return;
            _beamTransform.position = (start + end) * 0.5f;
            _beamTransform.up = dir.normalized;
            _beamTransform.localScale = new Vector3(Data.beamWidth, len * 0.5f, Data.beamWidth);
        }

        private void DestroyBeam()
        {
            if (_beamTransform != null)
            {
                Object.Destroy(_beamMat);
                Object.Destroy(_beamTransform.gameObject);
                _beamTransform = null;
                _beamMat = null;
            }
        }

        private void DrawBeamTrail(Vector3 groundFrom, Vector3 groundTo, Color color)
        {
            Vector3 dir = groundTo - groundFrom;
            float dist = dir.magnitude;
            if (dist < 0.01f) return;
            int steps = Mathf.Max(2, Mathf.RoundToInt(dist / Data.beamWidth));
            float lifetime = 0.08f;
            for (int i = 0; i <= steps; i++)
            {
                Vector3 pos = Vector3.Lerp(groundFrom, groundTo, (float)i / steps);
                MonsterGroundWarning.Spawn(pos, Data.beamWidth * 0.5f, lifetime, color);
            }
        }

        public BreathState(DBBreathPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            if (ctx.Agent.isOnNavMesh)
                ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;
            _anchorPosition = ctx.Transform.position;
            _flyPosition = _anchorPosition + Vector3.up * Data.riseHeight;
            _originalUpdatePosition = ctx.Agent.updatePosition;
            _originalUpdateRotation = ctx.Agent.updateRotation;
            ctx.Agent.updatePosition = false;
            ctx.Agent.updateRotation = false;

            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animName, Data.crossFade);

            var dragon = ctx.Monster as DragonBossMonster;
            var element = dragon?.DBBlackboard?.CurrentElement ?? DragonBossBlackboard.DragonElement.Ice;
            _breathColor = DragonBossVisualHelper.GetElementColor(element);

            _phase = 0;
            _warningTimer = 0f;
            _breathTimer = 0f;
            _tickTimer = 0f;
        }

        public override void Update(MonsterContext ctx)
        {
            switch (_phase)
            {
                case 0:
                    _warningTimer += Time.deltaTime;
                    float riseT = Mathf.Clamp01(_warningTimer / 0.8f);
                    ctx.Transform.position = Vector3.Lerp(_anchorPosition, _flyPosition, riseT);
                    if (_warningTimer < 0.8f) return;

                    if (Data.vfxPrefab != null)
                    {
                        _activeVfx = BossEffectPool.Spawn(
                            Data.vfxPrefab,
                            _anchorPosition,
                            ctx.Transform.rotation,
                            ctx.Transform,
                            false);
                        if (_activeVfx != null)
                        {
                            _activeVfx.transform.localPosition = Data.breathLocalOffset;
                            _activeVfx.transform.localRotation = Quaternion.identity;
                            _activeVfx.transform.localScale = Data.breathLocalScale;
                            DragonBossVisualHelper.ApplyEffectTint(_activeVfx, _breathColor);
                        }
                    }

                    CreateBeam(_breathColor);

                    // 빔 시작점: 플레이어 반대쪽 (보스 방향) beamApproachRadius 거리
                    if (ctx.Runtime.PlayerTarget != null)
                    {
                        Vector3 playerPos = ctx.Runtime.PlayerTarget.position;
                        Vector3 awayDir = (_flyPosition - playerPos);
                        awayDir.y = 0f;
                        awayDir = awayDir.sqrMagnitude > 0.001f ? awayDir.normalized : Vector3.forward;
                        _beamTarget = playerPos + awayDir * Data.beamApproachRadius;
                        _beamTarget.y = playerPos.y;
                    }
                    else
                    {
                        _beamTarget = _flyPosition + ctx.Transform.forward * 5f;
                    }

                    _phase = 1;
                    break;

                case 1:
                    _breathTimer += Time.deltaTime;
                    _tickTimer += Time.deltaTime;
                    ctx.Transform.position = _flyPosition;

                    if (ctx.Runtime.PlayerTarget != null)
                    {
                        Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
                        if (toPlayer.sqrMagnitude > 0.01f)
                        {
                            Quaternion targetRot = Quaternion.LookRotation(toPlayer.normalized);
                            ctx.Transform.rotation = Quaternion.RotateTowards(ctx.Transform.rotation, targetRot, 60f * Time.deltaTime);
                        }

                        // 빔 끝점이 플레이어 쪽으로 수렴
                        _beamTarget = Vector3.MoveTowards(
                            _beamTarget,
                            ctx.Runtime.PlayerTarget.position,
                            Data.beamTrackSpeed * Time.deltaTime);
                    }

                    UpdateBeam(_flyPosition, _beamTarget);

                    if (_tickTimer >= Data.tickInterval)
                    {
                        _tickTimer -= Data.tickInterval;
                        Vector3 warnPos = _beamTarget;
                        warnPos.y = _anchorPosition.y;
                        MonsterGroundWarning.Spawn(warnPos, Data.castRadius, Data.tickInterval + 0.05f, _breathColor);
                        DoBreathHit(ctx);
                    }

                    if (_breathTimer >= Data.breathDuration)
                        ctx.Monster.ChangeState<PatrolState>();
                    break;
            }
        }

        public override void Exit(MonsterContext ctx)
        {
            ctx.Transform.position = _anchorPosition;
            ctx.Agent.updatePosition = _originalUpdatePosition;
            ctx.Agent.updateRotation = _originalUpdateRotation;
            ctx.Agent.Warp(_anchorPosition);

            if (_activeVfx != null)
            {
                BossEffectPool.Release(_activeVfx);
                _activeVfx = null;
            }

            DestroyBeam();
        }

        private void DoBreathHit(MonsterContext ctx)
        {
            int damage = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * 0.5f);

            var hits = Physics.OverlapSphere(_beamTarget, Data.castRadius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                    ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                var dragon = ctx.Monster as DragonBossMonster;
                var element = dragon?.DBBlackboard?.CurrentElement ?? DragonBossBlackboard.DragonElement.Ice;

                player.TakeDamage(damage);
                switch (element)
                {
                    case DragonBossBlackboard.DragonElement.Ice:
                        player.ApplyKnockback(Vector3.zero, 2f);
                        break;
                    case DragonBossBlackboard.DragonElement.Thunder:
                        player.ApplyKnockback(Vector3.zero, 0.5f);
                        break;
                    default:
                        player.ApplySlow(0.4f, 2f);
                        break;
                }
                break; // 플레이어 한 명에게만 적용
            }
        }
    }
}
}
