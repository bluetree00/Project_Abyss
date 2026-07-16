using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 환영베기 — 카타나 Q스킬.
/// 제자리에서 전방 범위 내 적을 다수 타격 (환영검무 스타일).
/// 티어에 따라 검기 발사/폭발이 해금된다.
///   1단계: 제자리 5회 공격
///   2단계: + 전방 검기 3회 발사 (관통 데미지)
///   3단계: + 검기 발사 후 1회 폭발 범위 공격
/// </summary>
[CreateAssetMenu(menuName = "Game/Skill/PhantomDanceBehavior")]
public class PhantomDanceBehaviorSO : SkillBehaviorSO
{
    [Header("기본 공격 (전 티어)")]
    public int hitCount = 5;
    public float hitDelay = 0.12f;
    public float detectRadius = 2f;
    public float detectForwardOffset = 1f;
    public float baseDamagePerHit = 10f;
    public float knockbackMultiplier = 0.1f;

    [Header("2단계: 검기 발사")]
    public int slashProjectileCount = 3;
    public float slashProjectileDelay = 0.1f;
    public float slashProjectileDamage = 12f;
    public float slashProjectileDistance = 3f;
    public float slashProjectileRadius = 1.5f;
    public string slashProjectileEffectKey = "ShinySlash";
    public float slashProjectileEffectScale = 1f;

    [Header("3단계: 검기 폭발")]
    public float slashExplodeDelay = 0.3f;
    public float slashExplodeRadius = 3f;
    public float slashExplodeDamage = 25f;
    public string slashExplodeEffectKey = "MissileExplosion";
    public float slashExplodeEffectScale = 2f;

    [Header("마무리")]
    public float endDelay = 0.3f;

    [Header("이펙트")]
    public string attackEffectKey = "SlashAttack";
    public float attackEffectScale = 1f;
    public string hitEffectKey = "SwordHitImpact";
    public float hitEffectScale = 0.5f;
    public string finishEffectKey = "IasenFinishFlash";
    public float finishEffectScale = 1f;

    [Header("애니메이션")]
    public string animationOverride;
    [Tooltip("난무 시 번갈아 사용할 공격 상태들 (비어있으면 GroundLightAttack_01~03 사용)")]
    public string[] flurryAnimStates;

    public override ISkillRuntime CreateRuntime() => new Runtime(this);

    private class Runtime : ISkillRuntime
    {
        private readonly PhantomDanceBehaviorSO _data;
        private enum Phase { Attack, SlashProjectile, SlashExplode, End }
        private Phase _phase;
        private float _timer;
        private int _hitIndex;
        private int _slashIndex;
        private int _skillTier;
        private readonly List<IDamageable> _hitTargets = new();
        private readonly HashSet<GameObject> _hitObjects = new();
        private Vector3 _slashCenter;

        public Runtime(PhantomDanceBehaviorSO data) => _data = data;

        public void OnEnter(SkillExecutionContext ctx)
        {
            PlayAnimation(ctx);
            ctx.RotateToMouse();
            ctx.SetMoveScale(0f);

            _skillTier = Mathf.Clamp(ctx.WeaponData?.tier ?? 1, 1, 3);

            _hitTargets.Clear();
            _hitObjects.Clear();
            _hitIndex = 0;
            _slashIndex = 0;
            _timer = 0f;
            _phase = Phase.Attack;

            DetectEnemies(ctx);

            // 스킬 진입 시 잔여 공격 입력 제거
            ctx.Controller.InputBuffer.TryConsume(Game.Inputs.Command.Light);
            ctx.Controller.InputBuffer.TryConsume(Game.Inputs.Command.Heavy);
            ctx.Controller.InputBuffer.TryConsume(Game.Inputs.Command.Charge);
        }

        public void OnUpdate(SkillExecutionContext ctx)
        {
            _timer += Time.deltaTime;
            switch (_phase)
            {
                case Phase.Attack:          UpdateAttack(ctx);          break;
                case Phase.SlashProjectile: UpdateSlashProjectile(ctx); break;
                case Phase.SlashExplode:    UpdateSlashExplode(ctx);    break;
                case Phase.End:             UpdateEnd(ctx);             break;
            }
        }

        public void OnExit(SkillExecutionContext ctx)
        {
            ctx.SetMoveScale(1f);
            _hitTargets.Clear();
            _hitObjects.Clear();
        }

        // ── Phase: Attack (5회 타격) ──
        private void UpdateAttack(SkillExecutionContext ctx)
        {
            if (_timer >= _data.hitDelay)
            {
                _timer = 0f;
                DetectEnemies(ctx);
                ApplyHit(ctx);

                // 난무 애니메이션 번갈아 재생
                PlayFlurryAnim(ctx, _hitIndex);

                // 공격 이펙트 — 전방에서 좌우 교차하며 중앙 타격
                var fwd = ctx.PlayerTransform.forward;
                var right = ctx.PlayerTransform.right;
                Vector3 targetCenter = ctx.PlayerTransform.position + fwd * _data.detectForwardOffset + Vector3.up * 0.8f;

                // 좌/우 교차: 홀수=왼쪽, 짝수=오른쪽
                float side = (_hitIndex % 2 == 0) ? 1f : -1f;
                float spreadDist = 1.5f;
                Vector3 spawnPos = targetCenter + right * side * spreadDist + Vector3.up * Random.Range(-0.3f, 0.3f);

                // 중앙을 향하도록 회전 + 세워짐/누워짐 교차 (홀수=세로, 짝수=가로)
                Vector3 toCenter = (targetCenter - spawnPos).normalized;
                Quaternion baseRot = Quaternion.LookRotation(toCenter);
                float tilt = (_hitIndex % 2 == 0) ? Random.Range(60f, 80f) : Random.Range(-20f, 20f);
                Quaternion finalRot = baseRot * Quaternion.Euler(0f, 0f, tilt);

                SpawnEffectRotated(ctx, _data.attackEffectKey, spawnPos, finalRot, _data.attackEffectScale, 0.5f);

                _hitIndex++;
                if (_hitIndex >= _data.hitCount)
                {
                    _timer = 0f;

                    // 마무리 이펙트
                    if (!string.IsNullOrEmpty(_data.finishEffectKey))
                    {
                        var finishPos = ctx.PlayerTransform.position
                            + ctx.PlayerTransform.forward * (_data.detectForwardOffset + _data.detectRadius)
                            + Vector3.up * 1f;
                        SpawnEffect(ctx, _data.finishEffectKey, finishPos, _data.finishEffectScale, 0.8f);
                    }

                    // 티어별 분기
                    if (_skillTier >= 2)
                    {
                        _slashCenter = ctx.PlayerTransform.position
                            + ctx.PlayerTransform.forward * _data.slashProjectileDistance;
                        _phase = Phase.SlashProjectile;
                        _slashIndex = 0;
                    }
                    else
                    {
                        _phase = Phase.End;
                    }
                }
            }
        }

        // ── Phase: SlashProjectile (검기 발사 3회) ──
        private void UpdateSlashProjectile(SkillExecutionContext ctx)
        {
            if (_timer >= _data.slashProjectileDelay)
            {
                _timer = 0f;

                // 전방 검기 데미지
                float dmg = ctx.CalculateDamage(_data.slashProjectileDamage);
                var colliders = Physics.OverlapSphere(_slashCenter, _data.slashProjectileRadius);
                foreach (var col in colliders)
                {
                    if (col.gameObject == ctx.Controller.gameObject) continue;
                    if (col.TryGetComponent<IDamageable>(out var d))
                        ctx.DealDamage(d, dmg, _data.knockbackMultiplier);
                }

                // 검기 이펙트
                var slashOffset = new Vector3(
                    Random.Range(-0.5f, 0.5f),
                    Random.Range(0.5f, 1.5f),
                    Random.Range(-0.3f, 0.3f));
                SpawnEffectScaled(ctx, _data.slashProjectileEffectKey,
                    _slashCenter + slashOffset, _data.slashProjectileEffectScale, 0.6f);

                _slashIndex++;
                if (_slashIndex >= _data.slashProjectileCount)
                {
                    _timer = 0f;
                    if (_skillTier >= 3)
                        _phase = Phase.SlashExplode;
                    else
                        _phase = Phase.End;
                }
            }
        }

        // ── Phase: SlashExplode (검기 폭발) ──
        private void UpdateSlashExplode(SkillExecutionContext ctx)
        {
            if (_timer >= _data.slashExplodeDelay)
            {
                _timer = 0f;

                // 폭발 범위 데미지
                float dmg = ctx.CalculateDamage(_data.slashExplodeDamage);
                var colliders = Physics.OverlapSphere(_slashCenter, _data.slashExplodeRadius);
                foreach (var col in colliders)
                {
                    if (col.gameObject == ctx.Controller.gameObject) continue;
                    if (col.TryGetComponent<IDamageable>(out var d))
                    {
                        ctx.DealDamage(d, dmg, _data.knockbackMultiplier * 2f);

                        // 피격 이펙트
                        var hitOffset = new Vector3(
                            Random.Range(-0.5f, 0.5f),
                            Random.Range(0.3f, 1.5f),
                            Random.Range(-0.5f, 0.5f));
                        SpawnEffect(ctx, _data.hitEffectKey, col.transform.position + hitOffset, _data.hitEffectScale * 1.5f, 0.5f);
                    }
                }

                // 폭발 이펙트 — 다중 + 크게
                SpawnEffectScaled(ctx, _data.slashExplodeEffectKey,
                    _slashCenter + Vector3.up * 0.5f, _data.slashExplodeEffectScale, 2f);
                SpawnEffectScaled(ctx, _data.slashExplodeEffectKey,
                    _slashCenter + Vector3.up * 1.2f + ctx.PlayerTransform.right * 0.5f,
                    _data.slashExplodeEffectScale * 0.7f, 1.5f);
                SpawnEffectScaled(ctx, _data.slashExplodeEffectKey,
                    _slashCenter + Vector3.up * 0.3f - ctx.PlayerTransform.right * 0.5f,
                    _data.slashExplodeEffectScale * 0.7f, 1.5f);

                _phase = Phase.End;
            }
        }

        // ── Phase: End ──
        private void UpdateEnd(SkillExecutionContext ctx)
        {
            if (_timer >= _data.endDelay)
                ctx.RequestEnd();
        }

        // ── Detect ──
        private void DetectEnemies(SkillExecutionContext ctx)
        {
            var center = ctx.PlayerTransform.position + ctx.PlayerTransform.forward * _data.detectForwardOffset;
            var colliders = Physics.OverlapSphere(center, _data.detectRadius);
            foreach (var col in colliders)
            {
                if (col.gameObject == ctx.Controller.gameObject) continue;
                if (_hitObjects.Contains(col.gameObject)) continue;
                if (col.TryGetComponent<IDamageable>(out var damageable))
                {
                    _hitTargets.Add(damageable);
                    _hitObjects.Add(col.gameObject);
                }
            }
        }

        // ── Damage ──
        private void ApplyHit(SkillExecutionContext ctx)
        {
            if (_hitTargets.Count == 0) return;
            float dmg = ctx.CalculateDamage(_data.baseDamagePerHit);
            foreach (var target in _hitTargets)
                ctx.DealDamage(target, dmg, _data.knockbackMultiplier);

            foreach (var obj in _hitObjects)
            {
                if (obj == null) continue;
                var offset = new Vector3(
                    Random.Range(-0.4f, 0.4f),
                    Random.Range(0.3f, 1.5f),
                    Random.Range(-0.4f, 0.4f));
                SpawnEffect(ctx, _data.hitEffectKey, obj.transform.position + offset, _data.hitEffectScale, 0.4f);
            }
        }

        // ── Animation ──
        private static readonly string[] DefaultFlurryStates = { "GroundLightAttack_01", "GroundLightAttack_02", "GroundLightAttack_03" };

        private void PlayAnimation(SkillExecutionContext ctx)
        {
            PlayFlurryAnim(ctx, 0);
        }

        private void PlayFlurryAnim(SkillExecutionContext ctx, int index)
        {
            var states = (_data.flurryAnimStates != null && _data.flurryAnimStates.Length > 0)
                ? _data.flurryAnimStates
                : DefaultFlurryStates;
            string animName = states[index % states.Length];
            ctx.Animator.CrossFade(animName, 0.03f);
        }

        // ── Effect ──
        private static async void SpawnEffect(SkillExecutionContext ctx, string key, Vector3 pos, float scale, float lifetime)
        {
            if (string.IsNullOrEmpty(key)) return;
            var obj = await Managers.ObjectPooler.SpawnAsync(
                key, ObjectPoolerManager.PoolType.Effect, pos, ctx.PlayerTransform.rotation);
            if (obj == null) return;
            obj.transform.localScale = Vector3.one * scale;
            if (obj.TryGetComponent<EffectBehaviour>(out var eb))
                eb.Initialize(eb.behaviorSO, ctx.PlayerTransform, lifetime);
            else
                DespawnAfter(obj, lifetime);
        }

        private static async void SpawnEffectRotated(SkillExecutionContext ctx, string key, Vector3 pos, Quaternion rot, float scale, float lifetime)
        {
            if (string.IsNullOrEmpty(key)) return;
            var obj = await Managers.ObjectPooler.SpawnAsync(
                key, ObjectPoolerManager.PoolType.Effect, pos, rot);
            if (obj == null) return;
            obj.transform.localScale = Vector3.one * scale;
            if (obj.TryGetComponent<EffectBehaviour>(out var eb))
                eb.Initialize(eb.behaviorSO, ctx.PlayerTransform, lifetime);
            else
                DespawnAfter(obj, lifetime);
        }

        private static async void SpawnEffectScaled(SkillExecutionContext ctx, string key, Vector3 pos, float scale, float lifetime)
        {
            if (string.IsNullOrEmpty(key)) return;
            var obj = await Managers.ObjectPooler.SpawnAsync(
                key, ObjectPoolerManager.PoolType.Effect, pos, ctx.PlayerTransform.rotation);
            if (obj == null) return;
            obj.transform.localScale = Vector3.one * scale;
            foreach (var trail in obj.GetComponentsInChildren<TrailRenderer>(true))
                trail.Clear();
            foreach (var ps in obj.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Clear(true);
                ps.Play(true);
            }
            if (obj.TryGetComponent<EffectBehaviour>(out var eb))
                eb.Initialize(eb.behaviorSO, ctx.PlayerTransform, lifetime);
            else
                DespawnAfter(obj, lifetime);
        }

        private static async void DespawnAfter(GameObject obj, float delay)
        {
            try
            {
                await UniTask.Delay(
                    (int)(delay * 1000), cancellationToken: obj.GetCancellationTokenOnDestroy());
                if (obj != null && obj.activeInHierarchy)
                    Managers.ObjectPooler.Despawn(obj);
            }
            catch (System.OperationCanceledException) { }
        }
    }
}
