using UnityEngine;

namespace Abyss.Monster
{
[CreateAssetMenu(fileName = "DBIceDragonBreathPatternSO",
                 menuName = "Abyss/Boss/DragonBoss/IceDragonBreath")]
public class DBIceDragonBreathPatternSO : BossPatternSO
{
    [Header("Animation")]
    [SerializeField] private string animName = "Attack01";
    [SerializeField] private float crossFade = 0.1f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxPrefab;
    [SerializeField] private GameObject iceColumnVfxPrefab;

    [Header("Attack")]
    [SerializeField] private float warningDuration = 0.8f;
    [SerializeField] private float breathDuration = 5f;
    [SerializeField] private float breathTickInterval = 0.3f;
    [SerializeField] private float columnTickInterval = 0.5f;
    [SerializeField] private float columnDuration = 3f;
    [SerializeField] private float columnRadius = 1.5f;
    [SerializeField] private float columnRingRadius = 8f;
    [SerializeField] private float breathCastRadius = 1.2f;

    [Header("Flight")]
    [SerializeField] private float riseHeight = 8f;

    [Header("Beam Tracking")]
    [SerializeField] private float beamApproachRadius = 8f;
    [SerializeField] private float beamTrackSpeed = 3f;
    [SerializeField] private float beamWidth = 0.6f;

    [Header("Cooldown")]
    [SerializeField] private float patternCooldown = 20f;

    private float _cooldownEndTime = -999f;
    private IceDragonBreathState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new IceDragonBreathState(this);

    public override bool CanExecute(BossPatternContext ctx) => Time.time >= _cooldownEndTime;

    internal void StartCooldown() => _cooldownEndTime = Time.time + patternCooldown;

    public override SpecialStateBase GetRuntimeState() => _state;

    private sealed class IceDragonBreathState : FullLockState<DBIceDragonBreathPatternSO>
    {
        // 12시(0도)부터 시계방향 45도 간격
        private static readonly float[] s_colAngles = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };

        private float _timer;
        private float _breathTick;
        private float _columnTick;
        private int _columnSpawnStep;
        private int _phase;
        private GameObject _activeVfx;
        private Transform _beamTransform;
        private Material _beamMat;
        private Vector3 _beamTarget;
        private readonly Vector3[] _colPositions = new Vector3[8];
        private Vector3 _anchorPosition;
        private Vector3 _flyPosition;
        private bool _originalUpdatePosition;
        private bool _originalUpdateRotation;

        public IceDragonBreathState(DBIceDragonBreathPatternSO data) : base(data) { }

        private void CreateBeam(Color color)
        {
            var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = "[IceBreathBeam]";
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
            var iceColor = DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Ice);

            // 8개 기둥 위치 사전 계산 + 경고 장판 (실제 지면 y에 스냅)
            for (int i = 0; i < s_colAngles.Length; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, s_colAngles[i], 0f) * Vector3.forward;
                Vector3 rawPos = center + dir * Data.columnRingRadius;
                rawPos.y = DragonBossVisualHelper.GetGroundY(rawPos);
                _colPositions[i] = rawPos;
                MonsterGroundWarning.Spawn(_colPositions[i], Data.columnRadius, Data.warningDuration, iceColor);
            }

            _phase = 0;
            _timer = 0f;
            _breathTick = 0f;
            _columnTick = 0f;
            _columnSpawnStep = 0;
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
                        _activeVfx.transform.localPosition = new Vector3(0f, 1.2f, 2.2f);
                        _activeVfx.transform.localRotation = Quaternion.identity;
                        _activeVfx.transform.localScale = new Vector3(1.6f, 1.6f, 5.5f);
                        DragonBossVisualHelper.ApplyEffectTint(
                            _activeVfx,
                            DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Ice));
                    }
                }

                // 빔 생성 및 초기 목표 설정
                var iceColor = DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Ice);
                CreateBeam(iceColor);

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

            // phase 1: 브레스 + 기둥 순차 생성
            _breathTick += Time.deltaTime;
            _columnTick += Time.deltaTime;
            ctx.Transform.position = _flyPosition;

            if (ctx.Runtime.PlayerTarget != null)
            {
                Vector3 toPlayer = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
                if (toPlayer.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(toPlayer.normalized);
                    ctx.Transform.rotation = Quaternion.RotateTowards(ctx.Transform.rotation, targetRot, 60f * Time.deltaTime);
                }

                _beamTarget = Vector3.MoveTowards(_beamTarget, ctx.Runtime.PlayerTarget.position, Data.beamTrackSpeed * Time.deltaTime);
            }

            UpdateBeam(_flyPosition, _beamTarget);

            if (_columnTick >= Data.columnTickInterval)
            {
                _columnTick -= Data.columnTickInterval;
                SpawnColumnStep(ctx);
            }

            if (_breathTick >= Data.breathTickInterval)
            {
                _breathTick -= Data.breathTickInterval;
                Vector3 warnPos = _beamTarget;
                warnPos.y = _anchorPosition.y;
                var iceWarnColor = DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Ice);
                MonsterGroundWarning.Spawn(warnPos, Data.breathCastRadius, Data.breathTickInterval + 0.05f, iceWarnColor);
                DoBreathHit(ctx);
            }

            if (_timer >= Data.breathDuration)
                ctx.Monster.ChangeState<PatrolState>();
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
            Data.StartCooldown();
        }

        // 12시+6시 → 1시30분+7시30분 → 3시+9시 → 4시30분+10시30분 순서로 2개씩 생성
        private void SpawnColumnStep(MonsterContext ctx)
        {
            if (_columnSpawnStep >= 4) return;

            int idx1 = _columnSpawnStep;        // 12시 방향쪽 (0, 45, 90, 135)
            int idx2 = _columnSpawnStep + 4;    // 6시 방향쪽 (180, 225, 270, 315)

            SpawnColumnAt(ctx, _colPositions[idx1]);
            SpawnColumnAt(ctx, _colPositions[idx2]);

            _columnSpawnStep++;
        }

        private void SpawnColumnAt(MonsterContext ctx, Vector3 pos)
        {
            var iceColor = DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Ice);

            if (Data.iceColumnVfxPrefab != null)
            {
                var column = BossEffectPool.SpawnOneShot(Data.iceColumnVfxPrefab, pos, Quaternion.identity, null, Data.columnDuration);
                DragonBossVisualHelper.ApplyEffectTint(column, iceColor);
            }

            // 기둥 유지 시간 동안 경계 장판 + 원통 hazard 생성 (tick 데미지 + 원소 효과)
            MonsterGroundWarning.Spawn(pos, Data.columnRadius, Data.columnDuration, iceColor);
            BossColumnHazard.Spawn(
                pos,
                Data.columnRadius,
                Data.columnDuration,
                0.5f,
                (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier),
                DragonBossBlackboard.DragonElement.Ice);
        }

        private void DoBreathHit(MonsterContext ctx)
        {
            int damage = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * 0.8f);

            var hits = Physics.OverlapSphere(_beamTarget, Data.breathCastRadius);
            foreach (var col in hits)
            {
                var player = col.GetComponent<PlayerController>()
                    ?? col.GetComponentInParent<PlayerController>();
                if (player == null) continue;
                player.TakeDamage(damage);
                player.ApplyKnockback(Vector3.zero, 2f);
                break;
            }
        }
    }
}
}
