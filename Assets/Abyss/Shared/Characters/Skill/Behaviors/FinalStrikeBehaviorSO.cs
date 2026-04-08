using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 최후의 일격 — 대검 Q스킬.
/// 제자리에서 전방 3m 반원 범위 내 적을 1회 강타한다.
///   1단계: 반원 범위 1회 공격
///   2단계: 반원 1회 + 강한 추가 데미지 1회
///   3단계: 반원 1회 + 최대체력 비례 데미지 1회
/// </summary>
[CreateAssetMenu(menuName = "Game/Skill/FinalStrikeBehavior")]
public class FinalStrikeBehaviorSO : SkillBehaviorSO
{
    [Header("기본 공격 (전 티어)")]
    [SerializeField] private float baseDamage = 40f;
    [SerializeField] private float detectRadius = 3f;
    [SerializeField] private float detectAngle = 180f;
    [SerializeField] private float knockbackMultiplier = 2f;

    [Header("2단계: 추가 강타")]
    [SerializeField] private float extraDamage = 30f;
    [SerializeField] private float extraDelay = 0.5f;

    [Header("3단계: 최대체력 비례")]
    [SerializeField] private float maxHpDamageRatio = 0.1f;

    [Header("이펙트")]
    [SerializeField] private string strikeEffectKey = "DragonPunch";
    [SerializeField] private float strikeEffectScale = 0.7f;
    [SerializeField] private string extraHitEffectKey = "EnergyExplosion";
    [SerializeField] private float extraHitEffectScale = 0.5f;
    [SerializeField] private string hitEffectKey = "GreatswordImpact";
    [SerializeField] private float hitEffectScale = 0.5f;

    [Header("마무리")]
    [SerializeField] private float endDelay = 0.3f;

    [Header("애니메이션")]
    [SerializeField] private string animationOverride;

    public override ISkillRuntime CreateRuntime() => new Runtime(this);

    private class Runtime : ISkillRuntime
    {
        private readonly FinalStrikeBehaviorSO _data;
        private enum Phase { Strike, ExtraHit, End }
        private Phase _phase;
        private float _timer;
        private int _skillTier;
        private readonly List<IDamageable> _hitTargets = new();
        private readonly List<GameObject> _hitObjects = new();

        public Runtime(FinalStrikeBehaviorSO data) => _data = data;

        public void OnEnter(SkillExecutionContext ctx)
        {
            _skillTier = Mathf.Clamp(ctx.WeaponData?.tier ?? 1, 1, 3);
            _timer = 0f;
            _phase = Phase.Strike;
            _hitTargets.Clear();
            _hitObjects.Clear();

            ctx.RotateToMouse();
            ctx.SetMoveScale(0f);
            PlayAnimation(ctx);

            // 반원 범위 공격 즉시 실행
            ExecuteStrike(ctx);

            ctx.Controller.InputBuffer.TryConsume(Game.Inputs.Command.Light);
            ctx.Controller.InputBuffer.TryConsume(Game.Inputs.Command.Heavy);
        }

        public void OnUpdate(SkillExecutionContext ctx)
        {
            _timer += Time.deltaTime;
            switch (_phase)
            {
                case Phase.Strike:   UpdateStrike(ctx);   break;
                case Phase.ExtraHit: UpdateExtraHit(ctx); break;
                case Phase.End:      UpdateEnd(ctx);      break;
            }
        }

        public void OnExit(SkillExecutionContext ctx)
        {
            ctx.SetMoveScale(1f);
            _hitTargets.Clear();
            _hitObjects.Clear();
        }

        private void UpdateStrike(SkillExecutionContext ctx)
        {
            if (_skillTier >= 2 && _timer >= _data.extraDelay)
            {
                _timer = 0f;
                _phase = Phase.ExtraHit;
            }
            else if (_skillTier < 2 && _timer >= _data.endDelay)
            {
                _timer = 0f;
                _phase = Phase.End;
            }
        }

        private void UpdateExtraHit(SkillExecutionContext ctx)
        {
            // 추가 타격 1회 실행
            float extraDmg;
            if (_skillTier >= 3)
            {
                // 3단계: 최대체력 비례 데미지
                extraDmg = ctx.RuntimeStats.MaxHp * _data.maxHpDamageRatio;
            }
            else
            {
                // 2단계: 고정 추가 데미지
                extraDmg = ctx.CalculateDamage(_data.extraDamage);
            }

            foreach (var target in _hitTargets)
            {
                if (target == null) continue;
                target.TakeDamage(extraDmg, ctx.Controller.gameObject, _data.knockbackMultiplier * 1.5f);
            }

            // 추가 타격 이펙트
            foreach (var obj in _hitObjects)
            {
                if (obj == null) continue;
                SpawnEffect(ctx, _data.extraHitEffectKey,
                    obj.transform.position + Vector3.up * 1f,
                    _data.extraHitEffectScale, 0.8f);
            }

            _timer = 0f;
            _phase = Phase.End;
        }

        private void UpdateEnd(SkillExecutionContext ctx)
        {
            if (_timer >= _data.endDelay)
                ctx.RequestEnd();
        }

        private void ExecuteStrike(SkillExecutionContext ctx)
        {
            var playerPos = ctx.PlayerTransform.position;
            var forward = ctx.PlayerTransform.forward;
            var center = playerPos + forward * (_data.detectRadius * 0.5f);
            float halfAngle = _data.detectAngle * 0.5f;

            // 반원 범위 감지
            float dmg = ctx.CalculateDamage(_data.baseDamage);
            var colliders = Physics.OverlapSphere(center, _data.detectRadius);
            foreach (var col in colliders)
            {
                if (col.gameObject == ctx.Controller.gameObject) continue;

                // 각도 체크 (반원)
                Vector3 dirToTarget = (col.transform.position - playerPos).normalized;
                dirToTarget.y = 0f;
                float angle = Vector3.Angle(forward, dirToTarget);
                if (angle > halfAngle) continue;

                if (col.TryGetComponent<IDamageable>(out var d))
                {
                    d.TakeDamage(dmg, ctx.Controller.gameObject, _data.knockbackMultiplier);
                    _hitTargets.Add(d);
                    _hitObjects.Add(col.gameObject);

                    // 피격 이펙트
                    SpawnEffect(ctx, _data.hitEffectKey,
                        col.transform.position + Vector3.up * 0.8f,
                        _data.hitEffectScale, 0.5f);
                }
            }

            // 메인 이펙트
            var strikePos = playerPos + forward * _data.detectRadius * 0.6f + Vector3.up * 0.5f;
            SpawnEffect(ctx, _data.strikeEffectKey, strikePos, _data.strikeEffectScale, 1f);
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
                    foreach (var m in animSet.GetMappings(WeaponAnimGroup.Ground, ctx.ActionType))
                    {
                        if (!string.IsNullOrEmpty(m.baseClipName))
                        {
                            animName = m.baseClipName;
                            break;
                        }
                    }
                }
            }

            ctx.Animator.CrossFade(animName, 0.05f);
        }

        private static async void SpawnEffect(SkillExecutionContext ctx, string key,
            Vector3 pos, float scale, float lifetime)
        {
            if (string.IsNullOrEmpty(key) || ctx.Controller == null) return;
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
            try
            {
                await UniTask.Delay((int)(delay * 1000),
                    cancellationToken: obj.GetCancellationTokenOnDestroy());
                if (obj != null && obj.activeInHierarchy)
                    Managers.ObjectPooler.Despawn(obj);
            }
            catch (System.OperationCanceledException) { }
        }
    }
}
