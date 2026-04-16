using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// 드래곤 보스 — 지상 브레스.
/// ① 조준(warningDuration): 빠르게 플레이어 방향으로 고개를 돌리며 직사각형 경고 장판 표시
/// ② 브레스(breathDuration): _beamEnd가 플레이어를 향해 서서히 이동(레이저 구조).
///    몸체는 빔 방향으로 60°/s 회전(반동 표현). 직사각형 경고는 빔 방향 갱신.
/// </summary>
[CreateAssetMenu(fileName = "DBBreathPatternSO",
                 menuName = "Abyss/Boss/DragonBoss/Breath")]
public class DBBreathPatternSO : BossPatternSO
{
    [Header("Animation")]
    [SerializeField] private string animName  = "Attack01";
    [SerializeField] private float  crossFade = 0.1f;

    [Header("VFX")]
    [SerializeField] private GameObject vfxPrefab;
    [SerializeField] private Vector3    breathLocalOffset = new Vector3(0f, 1.2f, 2.2f);
    [SerializeField] private Vector3    breathLocalScale  = new Vector3(1.6f, 1.6f, 5.5f);

    [Header("Breath")]
    [SerializeField] private float warningDuration  = 1.5f;   // 조준 시간
    [SerializeField] private float breathDuration   = 5f;
    [SerializeField] private float tickInterval     = 0.25f;
    [SerializeField] private float castRadius       = 1.5f;   // SphereCast 반경
    [SerializeField] private float castMaxDist      = 16f;    // 브레스 사거리
    [SerializeField] private float beamWidth        = 0.8f;   // 원통 빔 굵기

    [Header("Rotation")]
    [SerializeField] private float aimTurnSpeed    = 120f;   // 조준 중 회전 속도

    [Header("Beam Tracking")]
    [SerializeField] private float beamTrackSpeed  = 3f;     // 빔 끝점이 플레이어 향해 이동하는 속도 (ThunderBreath 동일)

    [Header("Approach")]
    [SerializeField] private float approachSpeed    = 1.5f;  // 브레스 중 접근 속도 (m/s)
    [SerializeField] private float approachStopDist = 5f;    // 이 거리 이하로는 접근 안 함

    [Header("Cooldown")]
    [SerializeField] private float patternCooldown = 18f;

    private float _cooldownEndTime = -999f;
    private GroundBreathState _state;

    public override void Initialize(BossPatternContext ctx) { _cooldownEndTime = float.MinValue; _state = new GroundBreathState(this); }
    public override bool CanExecute(BossPatternContext ctx) => Time.time >= _cooldownEndTime;
    public override SpecialStateBase GetRuntimeState() => _state;
    internal void StartCooldown() => _cooldownEndTime = Time.time + patternCooldown;

    private sealed class GroundBreathState : FullLockState<DBBreathPatternSO>
    {
        private float      _timer;
        private float      _tickTimer;
        private int        _phase;
        private Color      _breathColor;
        private GameObject _activeVfx;
        private Transform  _beamTransform;
        private Material   _beamMat;
        private Vector3    _beamEnd;        // 레이저 구조: 빔 끝점 (서서히 플레이어 추적)

        public GroundBreathState(DBBreathPatternSO data) : base(data) { }

        // ── 빔 원통 ─────────────────────────────────────────────────
        private void CreateBeam(Color color)
        {
            var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cyl.name = "[BreathBeam_Ground]";
            Object.Destroy(cyl.GetComponent<Collider>());
            _beamMat = new Material(cyl.GetComponent<Renderer>().sharedMaterial);
            if (_beamMat.HasProperty("_BaseColor")) _beamMat.SetColor("_BaseColor", color);
            if (_beamMat.HasProperty("_Color"))     _beamMat.SetColor("_Color",     color);
            cyl.GetComponent<Renderer>().material = _beamMat;
            _beamTransform = cyl.transform;
        }

        private void UpdateBeam(Vector3 start, Vector3 end)
        {
            if (_beamTransform == null) return;
            Vector3 dir = end - start;
            float len = dir.magnitude;
            if (len < 0.01f) return;
            _beamTransform.position   = (start + end) * 0.5f;
            _beamTransform.up         = dir.normalized;
            _beamTransform.localScale = new Vector3(Data.beamWidth, len * 0.5f, Data.beamWidth);
        }

        private void DestroyBeam()
        {
            if (_beamTransform == null) return;
            Object.Destroy(_beamMat);
            Object.Destroy(_beamTransform.gameObject);
            _beamTransform = null;
            _beamMat       = null;
        }

        // ── 입/출 ────────────────────────────────────────────────────
        public override void Enter(MonsterContext ctx)
        {
            if (ctx.Agent.isOnNavMesh)
                ctx.Agent.ResetPath();
            ctx.Agent.velocity = Vector3.zero;

            if (ctx.Animator != null)
                ctx.Animator.CrossFade(Data.animName, Data.crossFade);

            var dragon  = ctx.Monster as DragonBossMonster;
            var element = dragon?.DBBlackboard?.CurrentElement ?? DragonBossBlackboard.DragonElement.Ice;
            _breathColor = DragonBossVisualHelper.GetElementColor(element);

            // 빔 끝점 초기화: 현재 전방 최대 사거리
            Vector3 mouthPos = GetMouthPos(ctx);
            _beamEnd   = mouthPos + GetFlatForward(ctx) * Data.castMaxDist;
            _beamEnd.y = mouthPos.y;

            _phase     = 0;
            _timer     = 0f;
            _tickTimer = 0f;
        }

        public override void Update(MonsterContext ctx)
        {
            _timer += Time.deltaTime;

            // ── phase 0: 조준 ─────────────────────────────────────────
            if (_phase == 0)
            {
                FacePlayer(ctx, Data.aimTurnSpeed);

                // 조준 중 빔 끝점을 전방 방향으로 갱신 (경고 장판이 고개와 함께 돌아감)
                Vector3 mp = GetMouthPos(ctx);
                _beamEnd   = mp + GetFlatForward(ctx) * Data.castMaxDist;
                _beamEnd.y = mp.y;

                _tickTimer += Time.deltaTime;
                if (_tickTimer >= 0.2f)
                {
                    _tickTimer = 0f;
                    SpawnBreathRect(ctx, 0.25f);
                }

                if (_timer < Data.warningDuration) return;

                // ── 브레스 시작 ─────────────────────────────────────
                if (ctx.Agent.isOnNavMesh)
                {
                    ctx.Agent.speed            = Data.approachSpeed;
                    ctx.Agent.stoppingDistance = Data.approachStopDist;
                }

                if (Data.vfxPrefab != null)
                {
                    _activeVfx = BossEffectPool.Spawn(
                        Data.vfxPrefab,
                        ctx.Transform.position,
                        ctx.Transform.rotation,
                        ctx.Transform,
                        false);
                    if (_activeVfx != null)
                    {
                        _activeVfx.transform.localPosition = Data.breathLocalOffset;
                        _activeVfx.transform.localRotation = Quaternion.identity;
                        _activeVfx.transform.localScale    = Data.breathLocalScale;
                        DragonBossVisualHelper.ApplyEffectTint(_activeVfx, _breathColor);
                    }
                }

                CreateBeam(_breathColor);

                _phase     = 1;
                _timer     = 0f;
                _tickTimer = 0f;
                return;
            }

            // ── phase 1: 브레스 발사 ──────────────────────────────────
            // 빔 끝점: 플레이어를 향해 서서히 이동 (레이저 구조, ThunderDragonBreath 동일)
            Vector3 mouthPos = GetMouthPos(ctx);
            if (ctx.Runtime.PlayerTarget != null)
            {
                Vector3 playerFlat = ctx.Runtime.PlayerTarget.position;
                playerFlat.y = mouthPos.y;  // 수평 유지
                _beamEnd = Vector3.MoveTowards(_beamEnd, playerFlat, Data.beamTrackSpeed * Time.deltaTime);
            }

            // 몸체: 빔 방향으로 60°/s 회전 (반동 표현)
            Vector3 beamDir = _beamEnd - mouthPos;
            beamDir.y = 0f;
            if (beamDir.sqrMagnitude > 0.01f)
            {
                ctx.Transform.rotation = Quaternion.RotateTowards(
                    ctx.Transform.rotation,
                    Quaternion.LookRotation(beamDir.normalized),
                    60f * Time.deltaTime);
            }

            // 천천히 플레이어 방향으로 접근
            if (ctx.Agent.isOnNavMesh && ctx.Runtime.PlayerTarget != null)
                ctx.Agent.SetDestination(ctx.Runtime.PlayerTarget.position);

            // 빔 원통 갱신
            UpdateBeam(mouthPos, _beamEnd);

            _tickTimer += Time.deltaTime;
            if (_tickTimer >= Data.tickInterval)
            {
                _tickTimer -= Data.tickInterval;
                SpawnBreathRect(ctx, Data.tickInterval + 0.05f);
                DoBreathHit(ctx);
            }

            if (_timer >= Data.breathDuration)
                ctx.Monster.ChangeState<PatrolState>();
        }

        public override void Exit(MonsterContext ctx)
        {
            if (_activeVfx != null)
            {
                BossEffectPool.Release(_activeVfx);
                _activeVfx = null;
            }
            DestroyBeam();

            if (ctx.Agent.isOnNavMesh)
            {
                ctx.Agent.speed            = ctx.Stat.moveSpeed;
                ctx.Agent.stoppingDistance = ctx.Monster.GetCombatStopDistance(ctx);
                ctx.Agent.ResetPath();
            }

            Data.StartCooldown();
        }

        // ── 직사각형 경고 장판 (빔 끝점 방향 기준) ──────────────────────
        private void SpawnBreathRect(MonsterContext ctx, float duration)
        {
            Vector3 mouthPos = GetMouthPos(ctx);
            Vector3 toEnd    = _beamEnd - mouthPos;
            toEnd.y = 0f;
            float   dist = Mathf.Min(toEnd.magnitude, Data.castMaxDist);
            Vector3 fwd  = toEnd.sqrMagnitude > 0.001f ? toEnd.normalized : GetFlatForward(ctx);

            var dragon  = ctx.Monster as DragonBossMonster;
            float groundY = dragon?.DBBlackboard?.SpawnY
                ?? DragonBossVisualHelper.GetGroundY(ctx.Transform.position + fwd * (dist * 0.5f));

            Vector3 origin = ctx.Transform.position;
            origin.y = groundY;

            MonsterGroundWarning.SpawnRect(origin, fwd, Data.castRadius * 2f, dist, duration, _breathColor);
        }

        // ── 데미지 (빔 끝점 방향으로 SphereCast) ────────────────────────
        private void DoBreathHit(MonsterContext ctx)
        {
            Vector3 origin  = GetMouthPos(ctx);
            Vector3 toEnd   = _beamEnd - origin;
            toEnd.y = 0f;
            float   dist    = Mathf.Min(toEnd.magnitude, Data.castMaxDist);
            Vector3 fwd     = toEnd.sqrMagnitude > 0.001f ? toEnd.normalized : GetFlatForward(ctx);
            int     damage  = (int)(ctx.Stat.attackPower * ctx.Runtime.AttackMultiplier * 0.5f);

            var hits = Physics.SphereCastAll(origin, Data.castRadius, fwd, dist);
            foreach (var hit in hits)
            {
                var player = hit.collider.GetComponent<PlayerController>()
                          ?? hit.collider.GetComponentInParent<PlayerController>();
                if (player == null) continue;

                player.TakeDamage(damage);

                var dragon  = ctx.Monster as DragonBossMonster;
                var element = dragon?.DBBlackboard?.CurrentElement ?? DragonBossBlackboard.DragonElement.Ice;
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
                break;
            }
        }

        // ── 헬퍼 ─────────────────────────────────────────────────────
        private void FacePlayer(MonsterContext ctx, float speed)
        {
            if (ctx.Runtime.PlayerTarget == null) return;
            Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;
            ctx.Transform.rotation = Quaternion.RotateTowards(
                ctx.Transform.rotation,
                Quaternion.LookRotation(dir.normalized),
                speed * Time.deltaTime);
        }

        private Vector3 GetMouthPos(MonsterContext ctx)
            => ctx.Transform.position + Vector3.up * 1.5f + ctx.Transform.forward * 1.5f;

        private Vector3 GetFlatForward(MonsterContext ctx)
        {
            Vector3 fwd = ctx.Transform.forward;
            fwd.y = 0f;
            return fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
        }
    }
}
}
