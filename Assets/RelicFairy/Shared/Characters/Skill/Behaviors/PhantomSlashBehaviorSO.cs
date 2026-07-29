using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 환영베기 (幻影斬り) — 제자리에서 전방 범위 내 적을 다수 타격.
/// 카타나 Q스킬 (궁극기).
/// </summary>
[CreateAssetMenu(menuName = "Game/Skill/PhantomSlashBehavior")]
public class PhantomSlashBehaviorSO : SkillBehaviorSO
{
    [Header("공격 설정")]
    public int hitCount = 5;
    public float hitDelay = 0.3f;
    public float detectRadius = 2f;
    public float detectForwardOffset = 1f;
    public float baseDamagePerHit = 8f;
    public float knockbackMultiplier = 0.1f;

    [Header("마무리")]
    public float endDelay = 0.3f;

    [Header("이펙트")]
    public string attackEffectKey = "SlashAttack";
    public float attackEffectScale = 1f;
    public string hitEffectKey = "SwordHitImpact";
    public float hitEffectScale = 0.5f;
    public string finishEffectKey;
    public float finishEffectScale = 1f;

    [Header("애니메이션")]
    [Tooltip("비어있으면 WeaponAnimationSetSO에서 QSkill 매핑 사용")]
    public string animationOverride;

    public override ISkillRuntime CreateRuntime() => new Runtime(this);

    private class Runtime : ISkillRuntime
    {
        private readonly PhantomSlashBehaviorSO _data;
        private enum Phase { Attack, End }
        private Phase _phase;
        private float _timer;
        private int _hitIndex;
        private readonly List<IDamageable> _hitTargets = new();
        private readonly HashSet<GameObject> _hitObjects = new();

        public Runtime(PhantomSlashBehaviorSO data) => _data = data;

        public void OnEnter(SkillExecutionContext ctx)
        {
            Debug.Log($"[PhantomSlash] OnEnter - hitCount={_data.hitCount}, hitDelay={_data.hitDelay}, attackEffect={_data.attackEffectKey}");
            PlayAnimation(ctx);
            ctx.RotateToMouse();

            _hitTargets.Clear();
            _hitObjects.Clear();
            _hitIndex = 0;
            _timer = 0f;
            _phase = Phase.Attack;

            // 초기 적 감지
            DetectEnemies(ctx);
        }

        public void OnUpdate(SkillExecutionContext ctx)
        {
            _timer += Time.deltaTime;
            switch (_phase)
            {
                case Phase.Attack: UpdateAttack(ctx); break;
                case Phase.End:    UpdateEnd(ctx);    break;
            }
        }

        public void OnExit(SkillExecutionContext ctx)
        {
            _hitTargets.Clear();
            _hitObjects.Clear();
        }

        private void UpdateAttack(SkillExecutionContext ctx)
        {
            if (_timer >= _data.hitDelay)
            {
                _timer = 0f;

                // 매 타격마다 적 재감지 (새로 들어온 적도 맞음)
                DetectEnemies(ctx);
                ApplyHit(ctx);

                // 공격 이펙트 — 플레이어에서 전방으로 쏘아내는 느낌
                float progress = (float)_hitIndex / Mathf.Max(_data.hitCount - 1, 1);
                float forwardDist = Mathf.Lerp(0.5f, _data.detectForwardOffset + _data.detectRadius, progress);
                var randomOffset = new Vector3(
                    Random.Range(-0.3f, 0.3f),
                    Random.Range(0.6f, 1.2f),
                    Random.Range(-0.2f, 0.2f));
                var pos = ctx.PlayerTransform.position
                    + ctx.PlayerTransform.forward * forwardDist
                    + randomOffset;
                SpawnEffect(ctx, _data.attackEffectKey, pos, _data.attackEffectScale, 0.4f);

                _hitIndex++;
                if (_hitIndex >= _data.hitCount)
                {
                    _timer = 0f;
                    _phase = Phase.End;

                    // 마무리 이펙트 — 최종 도달점에서
                    if (!string.IsNullOrEmpty(_data.finishEffectKey))
                    {
                        var finishPos = ctx.PlayerTransform.position
                            + ctx.PlayerTransform.forward * (_data.detectForwardOffset + _data.detectRadius)
                            + Vector3.up * 1f;
                        SpawnEffect(ctx, _data.finishEffectKey, finishPos, _data.finishEffectScale, 0.8f);
                    }
                }
            }
        }

        private void UpdateEnd(SkillExecutionContext ctx)
        {
            if (_timer >= _data.endDelay)
                ctx.RequestEnd();
        }

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

        private void ApplyHit(SkillExecutionContext ctx)
        {
            if (_hitTargets.Count == 0) return;

            float dmg = ctx.CalculateDamage(_data.baseDamagePerHit);
            foreach (var target in _hitTargets)
                ctx.DealDamage(target, dmg, _data.knockbackMultiplier);

            // 히트 이펙트
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

        private void PlayAnimation(SkillExecutionContext ctx)
        {
            string animName = !string.IsNullOrEmpty(_data.animationOverride)
                ? _data.animationOverride
                : "QSkill_01";

            if (string.IsNullOrEmpty(_data.animationOverride))
            {
                var wd = ctx.WeaponData;
                if (wd?.animationSet is WeaponAnimationSetSO animSet)
                {
                    var mapping = animSet.GetMappings(WeaponAnimGroup.Ground, ctx.ActionType)
                                         .FirstOrDefault(m => !string.IsNullOrEmpty(m.baseClipName));
                    if (mapping != null) animName = mapping.baseClipName;
                }
            }

            ctx.Animator.CrossFade(animName, 0.05f);
        }

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

        private static async void DespawnAfter(GameObject obj, float delay)
        {
            await Cysharp.Threading.Tasks.UniTask.Delay(
                (int)(delay * 1000), cancellationToken: obj.GetCancellationTokenOnDestroy());
            if (obj != null && obj.activeInHierarchy)
                Managers.ObjectPooler.Despawn(obj);
        }
    }
}
