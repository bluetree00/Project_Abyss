using UnityEngine;

namespace Abyss.Monster
{
[CreateAssetMenu(fileName = "DBFireDragonBreathPatternSO",
                 menuName = "Abyss/Boss/DragonBoss/FireDragonBreath")]
public class DBFireDragonBreathPatternSO : BossPatternSO
{
    [Header("Animation")]
    [SerializeField] private string animName = "FlyFWD";
    [SerializeField] private float crossFade = 0.15f;

    [Header("VFX")]
    [SerializeField] private GameObject breathVfxPrefab;
    [SerializeField] private GameObject fireExplosionVfxPrefab;

    [Header("Breath")]
    [SerializeField] private float warningDuration = 1f;
    [SerializeField] private float breathDuration = 10f;
    [SerializeField] private float breathTickInterval = 0.3f;
    [SerializeField] private float castRadius = 1.2f;
    [SerializeField] private float castMaxDist = 22f;

    [Header("Flight")]
    [SerializeField] private float riseHeight = 8f;

    [Header("Beam Tracking")]
    [SerializeField] private float beamApproachRadius = 8f;
    [SerializeField] private float beamTrackSpeed = 3f;
    [SerializeField] private float beamWidth = 0.6f;

    [Header("Tornado")]
    [SerializeField] private float tornadoDuration = 7f;
    [SerializeField] private float tornadoRadius = 1.8f;
    [SerializeField] private float tornadoStartRadius = 12f;
    [SerializeField] private float tornadoSpeed = 3.5f;

    [Header("Cooldown")]
    [SerializeField] private float patternCooldown = 25f;

    private float _cooldownEndTime = -999f;
    private FireDragonBreathState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new FireDragonBreathState(this);

    public override bool CanExecute(BossPatternContext ctx) => Time.time >= _cooldownEndTime;

    internal void StartCooldown() => _cooldownEndTime = Time.time + patternCooldown;

    public override SpecialStateBase GetRuntimeState() => _state;

    private sealed class FireDragonBreathState : FullLockState<DBFireDragonBreathPatternSO>
    {
        private static readonly float[] s_angles = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };

        private float _timer;
        private float _breathTick;
        private int _phase;
        private GameObject _activeBreathVfx;
        private Transform _beamTransform;
        private Material _beamMat;
        private Vector3 _beamTarget;
        private readonly Vector3[] _tornadoPositions = new Vector3[8];
        private readonly GameObject[] _tornadoVfx = new GameObject[8];
        private Vector3 _anchorPosition;
        private Vector3 _flyPosition;
        private bool _originalUpdatePosition;
        private bool _originalUpdateRotation;

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

        public FireDragonBreathState(DBFireDragonBreathPatternSO data) : base(data) { }

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
            Vector3 center = dragon?.DBBlackboard?.SpawnPosition ?? ctx.Transform.position;
            center.y = dragon?.DBBlackboard?.SpawnY ?? ctx.Transform.position.y;
            var fireColor = DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Fire);


            // 8방향 토네이도 시작 위치 사전 계산 + 경고 장판 (실제 지면 y에 스냅)
            for (int i = 0; i < s_angles.Length; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, s_angles[i], 0f) * Vector3.forward;
                Vector3 rawPos = center + dir * Data.tornadoStartRadius;
                rawPos.y = DragonBossVisualHelper.GetGroundY(rawPos);
                _tornadoPositions[i] = rawPos;
                MonsterGroundWarning.Spawn(
                    _tornadoPositions[i],
                    Data.tornadoRadius,
                    Data.warningDuration,
                    fireColor);
            }

            _phase = 0;
            _timer = 0f;
            _breathTick = 0f;
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            // phase 0: 경고 대기 + 부상
            if (_phase == 0)
            {
                float riseT = Mathf.Clamp01(_timer / Data.warningDuration);
                ctx.Transform.position = Vector3.Lerp(_anchorPosition, _flyPosition, riseT);
                if (_timer < Data.warningDuration) return;

                // 브레스 VFX 생성
                if (Data.breathVfxPrefab != null)
                {
                    _activeBreathVfx = BossEffectPool.Spawn(
                        Data.breathVfxPrefab,
                        _anchorPosition,
                        ctx.Transform.rotation,
                        ctx.Transform,
                        false);
                    if (_activeBreathVfx != null)
                    {
                        _activeBreathVfx.transform.localPosition = new Vector3(0f, 1.2f, 2.2f);
                        _activeBreathVfx.transform.localRotation = Quaternion.identity;
                        _activeBreathVfx.transform.localScale = new Vector3(1.8f, 1.8f, 6f);
                        DragonBossVisualHelper.ApplyEffectTint(
                            _activeBreathVfx,
                            DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Fire));
                    }
                }

                // 토네이도 VFX 생성 (위치는 Enter에서 계산됨)
                var fireColor = DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Fire);
                for (int i = 0; i < _tornadoPositions.Length; i++)
                {
                    Vector3 dir = _tornadoPositions[i] - _anchorPosition;
                    dir.y = 0f;
                    if (Data.fireExplosionVfxPrefab != null)
                    {
                        _tornadoVfx[i] = BossEffectPool.Spawn(
                            Data.fireExplosionVfxPrefab,
                            _tornadoPositions[i],
                            dir.sqrMagnitude > 0.001f ? Quaternion.LookRotation(-dir.normalized) : Quaternion.identity);
                        DragonBossVisualHelper.ApplyEffectTint(_tornadoVfx[i], fireColor);
                    }
                }

                var fireColor2 = DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Fire);
                CreateBeam(fireColor2);

                // 빔 시작점: 플레이어 반대쪽 beamApproachRadius 거리
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
                _timer = 0f;
                return;
            }

            // phase 1: 브레스 + 토네이도
            _breathTick += Time.deltaTime;
            ctx.Transform.position = _flyPosition;

            if (ctx.Runtime.PlayerTarget != null)
            {
                Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
                if (toPlayer.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(toPlayer.normalized);
                    ctx.Transform.rotation = Quaternion.RotateTowards(ctx.Transform.rotation, targetRot, 50f * Time.deltaTime);
                }
            }

            if (ctx.Runtime.PlayerTarget != null)
                _beamTarget = Vector3.MoveTowards(_beamTarget, ctx.Runtime.PlayerTarget.position, Data.beamTrackSpeed * Time.deltaTime);

            UpdateBeam(_flyPosition, _beamTarget);

            if (_breathTick >= Data.breathTickInterval)
            {
                _breathTick -= Data.breathTickInterval;
                Vector3 warnPos = _beamTarget;
                warnPos.y = _anchorPosition.y;
                var fireWarnColor = DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Fire);
                MonsterGroundWarning.Spawn(warnPos, Data.castRadius, Data.breathTickInterval + 0.05f, fireWarnColor);
                DoBreathHit(ctx);
            }

            if (_timer <= Data.tornadoDuration)
                UpdateTornadoes(ctx);

            if (_timer >= Data.breathDuration)
                ctx.Monster.ChangeState<PatrolState>();
        }

        public override void Exit(MonsterContext ctx)
        {
            ctx.Transform.position = _anchorPosition;
            ctx.Agent.updatePosition = _originalUpdatePosition;
            ctx.Agent.updateRotation = _originalUpdateRotation;
            ctx.Agent.Warp(_anchorPosition);

            if (_activeBreathVfx != null)
            {
                BossEffectPool.Release(_activeBreathVfx);
                _activeBreathVfx = null;
            }

            for (int i = 0; i < _tornadoVfx.Length; i++)
            {
                if (_tornadoVfx[i] != null)
                {
                    BossEffectPool.Release(_tornadoVfx[i]);
                    _tornadoVfx[i] = null;
                }
            }

            DestroyBeam();
            Data.StartCooldown();
        }

        private void UpdateTornadoes(MonsterContext ctx)
        {
            if (ctx.Runtime.PlayerTarget == null) return;

            int damage = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * 0.8f);
            float kbForce = ctx.Stat.knockbackForce;
            Vector3 target = ctx.Runtime.PlayerTarget.position;
            target.y = _tornadoPositions[0].y;

            for (int i = 0; i < _tornadoPositions.Length; i++)
            {
                Vector3 toPlayer = target - _tornadoPositions[i];
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 0.01f)
                {
                    _tornadoPositions[i] += toPlayer.normalized * Data.tornadoSpeed * Time.deltaTime;
                    if (_tornadoVfx[i] != null)
                        _tornadoVfx[i].transform.SetPositionAndRotation(_tornadoPositions[i], Quaternion.LookRotation(toPlayer.normalized));
                }

                var hits = Physics.OverlapSphere(_tornadoPositions[i], Data.tornadoRadius);
                foreach (var col in hits)
                {
                    var player = col.GetComponent<PlayerController>()
                        ?? col.GetComponentInParent<PlayerController>();
                    if (player == null) continue;

                    player.TakeDamage(damage);
                    player.ApplySlow(0.4f, 2f); // 불: 2초 이동속도 감소
                }
            }
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
                player.TakeDamage(damage);
                player.ApplySlow(0.4f, 2f);
                break;
            }
        }
    }
}
}
