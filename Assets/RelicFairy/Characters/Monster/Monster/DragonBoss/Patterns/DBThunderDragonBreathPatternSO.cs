using UnityEngine;

namespace RelicFairy.Monster
{
[CreateAssetMenu(fileName = "DBThunderDragonBreathPatternSO",
                 menuName = "RelicFairy/Boss/DragonBoss/ThunderDragonBreath")]
public class DBThunderDragonBreathPatternSO : BossPatternSO
{
    [Header("Animation")]
    [SerializeField] private string animName = "Attack01";
    [SerializeField] private float crossFade = 0.1f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxPrefab;
    [SerializeField] private GameObject lightningVfxPrefab;

    [Header("Breath")]
    [SerializeField] private float warningDuration = 1f;
    [SerializeField] private float breathDuration = 3f;
    [SerializeField] private float breathTickInterval = 0.3f;
    [SerializeField] private float castRadius = 1.2f;

    [Header("Flight")]
    [SerializeField] private float riseHeight = 8f;
    [SerializeField] private float descentDuration = 1f;

    [Header("Beam Tracking")]
    [SerializeField] private float beamApproachRadius = 8f;
    [SerializeField] private float beamTrackSpeed = 3f;
    [SerializeField] private float beamWidth = 0.6f;

    [Header("Lightning Column")]
    [SerializeField] private int columnCount = 8;
    [SerializeField] private float columnTickInterval = 0.3f;
    [SerializeField] private float columnLifetime = 3f;
    [SerializeField] private float lightningRadius = 6f;
    [SerializeField] private float lightningRingRadius = 8f;

    private ThunderDragonBreathState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new ThunderDragonBreathState(this);

    public override bool CanExecute(BossPatternContext ctx) => true;

    public override SpecialStateBase GetRuntimeState() => _state;

    private sealed class ThunderDragonBreathState : FullLockState<DBThunderDragonBreathPatternSO>
    {
        private float _timer;
        private float _breathTick;
        private float _columnTick;
        private float _columnAngle;
        private int _spawnedColumns;
        private int _phase;
        private GameObject _activeVfx;
        private Transform _beamTransform;
        private Material _beamMat;
        private Vector3 _beamTarget;
        private Vector3[] _columnPositions;
        private float[] _columnSpawnTimes;
        private Vector3 _anchorPosition;
        private Vector3 _mapCenter;
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

        public ThunderDragonBreathState(DBThunderDragonBreathPatternSO data) : base(data) { }

        public override void Enter(MonsterContext ctx)
        {
            GameCameraController.Instance?.DeactivateDragonTopDownView(0.8f);
            if (ctx.Agent.isOnNavMesh)
                ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;
            _anchorPosition = ctx.Transform.position;
            _originalUpdatePosition = ctx.Agent.updatePosition;
            _originalUpdateRotation = ctx.Agent.updateRotation;
            ctx.Agent.updatePosition = false;
            ctx.Agent.updateRotation = false;

            _mapCenter = ctx.Runtime.SpawnPosition;
            _mapCenter.y = DragonPatternFloorUtils.GetFloorY(ctx.Runtime.SpawnPosition, ctx.Runtime.SpawnPosition.y);
            _flyPosition = _mapCenter + Vector3.up * Data.riseHeight;

            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animName, Data.crossFade);

            int count = Mathf.Max(1, Data.columnCount);
            _columnPositions = new Vector3[count];
            _columnSpawnTimes = new float[count];

            _phase = 0;
            _timer = 0f;
            _breathTick = 0f;
            _columnTick = 0f;
            _columnAngle = 0f;
            _spawnedColumns = 0;
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            // phase 0: 상승
            if (_phase == 0)
            {
                float riseT = Mathf.Clamp01(_timer / Data.warningDuration);
                ctx.Transform.position = Vector3.Lerp(_anchorPosition, _flyPosition, riseT);
                if (_timer < Data.warningDuration) return;

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
                            DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Thunder));
                    }
                }

                var thunderColor = DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Thunder);
                CreateBeam(thunderColor);

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

            // phase 1: 브레스 + 순차 기둥 스폰
            if (_phase == 1)
            {
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
                }

                if (ctx.Runtime.PlayerTarget != null)
                    _beamTarget = Vector3.MoveTowards(_beamTarget, ctx.Runtime.PlayerTarget.position, Data.beamTrackSpeed * Time.deltaTime);

                UpdateBeam(_flyPosition, _beamTarget);

                if (_breathTick >= Data.breathTickInterval)
                {
                    _breathTick -= Data.breathTickInterval;
                    DoBreathHit(ctx);
                }

                if (_spawnedColumns < Data.columnCount && _columnTick >= Data.columnTickInterval)
                {
                    _columnTick -= Data.columnTickInterval;
                    SpawnColumn(ctx);
                }

                UpdateColumns(ctx);

                if (_timer >= Data.breathDuration)
                {
                    if (_activeVfx != null)
                    {
                        BossEffectPool.Release(_activeVfx);
                        _activeVfx = null;
                    }
                    DestroyBeam();

                    _phase = 2;
                    _timer = 0f;
                }
                return;
            }

            // phase 2: 맵 중앙으로 자연스럽게 하강
            if (_phase == 2)
            {
                float descentT = Mathf.Clamp01(_timer / Data.descentDuration);
                ctx.Transform.position = Vector3.Lerp(_flyPosition, _mapCenter, descentT);

                UpdateColumns(ctx);

                if (descentT >= 1f)
                    ctx.Monster.ChangeState<PatrolState>();
            }
        }

        public override void Exit(MonsterContext ctx)
        {
            ctx.Transform.position = _mapCenter;
            ctx.Agent.updatePosition = _originalUpdatePosition;
            ctx.Agent.updateRotation = _originalUpdateRotation;
            ctx.Agent.Warp(_mapCenter);

            if (_activeVfx != null)
            {
                BossEffectPool.Release(_activeVfx);
                _activeVfx = null;
            }

            DestroyBeam();
        }

        private void SpawnColumn(MonsterContext ctx)
        {
            float angleDeg = 360f / Data.columnCount;
            Vector3 dir = Quaternion.Euler(0f, _columnAngle, 0f) * Vector3.forward;
            Vector3 pos = _mapCenter + dir * Data.lightningRingRadius;
            pos.y = DragonPatternFloorUtils.GetFloorY(pos, _mapCenter.y);

            _columnPositions[_spawnedColumns] = pos;
            _columnSpawnTimes[_spawnedColumns] = _timer;

            var thunderColor = DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Thunder);

            MonsterGroundWarning.Spawn(pos, Data.lightningRadius, Data.columnLifetime, thunderColor);

            if (Data.lightningVfxPrefab != null)
            {
                BossEffectPool.SpawnOneShot(
                    Data.lightningVfxPrefab,
                    pos,
                    Quaternion.identity,
                    null,
                    Data.columnLifetime);
            }

            _spawnedColumns++;
            _columnAngle += angleDeg;
        }

        private void UpdateColumns(MonsterContext ctx)
        {
            int damage = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * 0.8f);

            for (int i = 0; i < _spawnedColumns; i++)
            {
                if (_timer - _columnSpawnTimes[i] > Data.columnLifetime) continue;

                var hits = Physics.OverlapSphere(_columnPositions[i], Data.lightningRadius);
                foreach (var col in hits)
                {
                    var player = col.GetComponent<PlayerController>()
                        ?? col.GetComponentInParent<PlayerController>();
                    if (player == null) continue;

                    player.TakeDamage(damage);
                    player.ApplyKnockback(Vector3.zero, 0.5f);
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
                player.ApplyKnockback(Vector3.zero, 0.5f);
                break;
            }
        }
    }
}
}
