using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 최후의 일격 — 대검 Q스킬.
/// 차징 모션 후 전방 3m 반원을 강타한다.
///   1단계: 차징 → 전방 반원 1회 공격
///   2단계: + 피격 적에게 추가 강타 1회 (피격 이펙트 A)
///   3단계: + 피격 적에게 최대체력 비례 추가 1회 (피격 이펙트 B, 더 화려)
/// </summary>
[CreateAssetMenu(menuName = "Game/Skill/FinalStrikeBehavior")]
public class FinalStrikeBehaviorSO : SkillBehaviorSO
{
    [Header("차징")]
    [SerializeField] private float chargeDuration = 0.6f;
    [SerializeField] private string chargeEffectKey = "MagicCircleDarkStar";
    [SerializeField] private float chargeEffectScale = 0.5f;

    [Header("기본 공격 (전 티어)")]
    [SerializeField] private float baseDamage = 40f;
    [SerializeField] private float detectRadius = 3f;
    [SerializeField] private float detectAngle = 180f;
    [SerializeField] private float knockbackMultiplier = 2f;

    [Header("2단계: 추가 강타")]
    [SerializeField] private float extraDamage = 30f;
    [SerializeField] private float extraDelay = 0.4f;
    [SerializeField] private string tier2ExtraEffectKey = "DragonPunch";
    [SerializeField] private float tier2ExtraEffectScale = 0.5f;

    [Header("3단계: 최대체력 비례")]
    [SerializeField] private float maxHpDamageRatio = 0.1f;
    [SerializeField] private string tier3ExtraEffectKey = "EnergyExplosion";
    [SerializeField] private float tier3ExtraEffectScale = 0.7f;

    [Header("이펙트")]
    [SerializeField] private string strikeEffectKey = "GreatswordSlash";
    [SerializeField] private float strikeEffectScale = 0.5f;
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
        private enum Phase { Charge, Strike, ExtraHit, End }
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
            _phase = Phase.Charge;
            _hitTargets.Clear();
            _hitObjects.Clear();

            ctx.RotateToMouse();
            ctx.SetMoveScale(0f);
            PlayAnimation(ctx);

            // 차징 이펙트 (캐릭터 부착)
            SpawnChargeEffect(ctx);

            ctx.Controller.InputBuffer.TryConsume(Game.Inputs.Command.Light);
            ctx.Controller.InputBuffer.TryConsume(Game.Inputs.Command.Heavy);
        }

        public void OnUpdate(SkillExecutionContext ctx)
        {
            _timer += Time.deltaTime;
            switch (_phase)
            {
                case Phase.Charge:   UpdateCharge(ctx);   break;
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

        // ── 차징 페이즈 ──
        private void UpdateCharge(SkillExecutionContext ctx)
        {
            if (_timer >= _data.chargeDuration)
            {
                _timer = 0f;
                ctx.RotateToMouse();
                ExecuteStrike(ctx);
                _phase = Phase.Strike;
            }
        }

        // ── 공격 후 대기 ──
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

        // ── 추가 타격 (티어별 이펙트 차별화) ──
        private void UpdateExtraHit(SkillExecutionContext ctx)
        {
            float extraDmg;
            string effectKey;
            float effectScale;

            if (_skillTier >= 3)
            {
                // 3단계: 최대체력 비례 + 화려한 이펙트
                extraDmg = ctx.RuntimeStats.MaxHp * _data.maxHpDamageRatio;
                effectKey = _data.tier3ExtraEffectKey;
                effectScale = _data.tier3ExtraEffectScale;
            }
            else
            {
                // 2단계: 고정 추가 데미지 + 기본 이펙트
                extraDmg = ctx.CalculateDamage(_data.extraDamage);
                effectKey = _data.tier2ExtraEffectKey;
                effectScale = _data.tier2ExtraEffectScale;
            }

            foreach (var target in _hitTargets)
            {
                if (target == null) continue;
                ctx.DealDamage(target, extraDmg, _data.knockbackMultiplier * 1.5f);
            }

            // 티어별 추가 피격 이펙트
            foreach (var obj in _hitObjects)
            {
                if (obj == null) continue;
                SpawnEffect(ctx, effectKey,
                    obj.transform.position + Vector3.up * 1f,
                    effectScale, 0.8f);
            }

            _timer = 0f;
            _phase = Phase.End;
        }

        private void UpdateEnd(SkillExecutionContext ctx)
        {
            if (_timer >= _data.endDelay)
                ctx.RequestEnd();
        }

        // ── 전방 반원 공격 실행 ──
        private void ExecuteStrike(SkillExecutionContext ctx)
        {
            var playerPos = ctx.PlayerTransform.position;
            var forward = ctx.PlayerTransform.forward;
            var center = playerPos + forward * (_data.detectRadius * 0.5f);
            float halfAngle = _data.detectAngle * 0.5f;

            float dmg = ctx.CalculateDamage(_data.baseDamage);
            var colliders = Physics.OverlapSphere(center, _data.detectRadius);
            foreach (var col in colliders)
            {
                if (col.gameObject == ctx.Controller.gameObject) continue;

                Vector3 dirToTarget = (col.transform.position - playerPos).normalized;
                dirToTarget.y = 0f;
                float angle = Vector3.Angle(forward, dirToTarget);
                if (angle > halfAngle) continue;

                if (col.TryGetComponent<IDamageable>(out var d))
                {
                    ctx.DealDamage(d, dmg, _data.knockbackMultiplier);
                    _hitTargets.Add(d);
                    _hitObjects.Add(col.gameObject);
                }
            }

            // 타격 이펙트 — 1회만 (첫 번째 적 위치 또는 전방)
            if (_hitObjects.Count > 0 && _hitObjects[0] != null)
            {
                SpawnEffect(ctx, _data.hitEffectKey,
                    _hitObjects[0].transform.position + Vector3.up * 0.8f,
                    _data.hitEffectScale, 0.5f);
            }

            // 전방 휘두르기 이펙트 — T1은 1개, T2+는 3개 부채꼴
            var strikeCenter = playerPos + forward * _data.detectRadius * 0.5f + Vector3.up * 1f;
            if (_skillTier <= 1)
            {
                SpawnEffectRotated(ctx, _data.strikeEffectKey, strikeCenter,
                    ctx.PlayerTransform.rotation, _data.strikeEffectScale, 1f);
            }
            else
            {
                for (int i = -1; i <= 1; i++)
                {
                    float spreadAngle = i * 20f;
                    Quaternion rot = ctx.PlayerTransform.rotation * Quaternion.Euler(0f, spreadAngle, 0f);
                    Vector3 offset = Quaternion.Euler(0f, spreadAngle, 0f) * forward * 0.3f * Mathf.Abs(i);
                    SpawnEffectRotated(ctx, _data.strikeEffectKey, strikeCenter + offset,
                        rot, _data.strikeEffectScale, 1f);
                }
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

        private async void SpawnChargeEffect(SkillExecutionContext ctx)
        {
            if (string.IsNullOrEmpty(_data.chargeEffectKey) || ctx.Controller == null) return;
            var obj = await Managers.ObjectPooler.SpawnAsync(
                _data.chargeEffectKey, ObjectPoolerManager.PoolType.Effect,
                ctx.PlayerTransform.position + Vector3.up * 2f,
                Quaternion.identity);
            if (obj == null) return;
            obj.transform.localScale = Vector3.one * _data.chargeEffectScale;
            obj.transform.SetParent(ctx.PlayerTransform, true);
            if (obj.TryGetComponent<EffectBehaviour>(out var eb))
                eb.Initialize(eb.behaviorSO, ctx.PlayerTransform, _data.chargeDuration);
            else
                DespawnAfter(obj, _data.chargeDuration);
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

        private static async void SpawnEffectRotated(SkillExecutionContext ctx, string key,
            Vector3 pos, Quaternion rot, float scale, float lifetime)
        {
            if (string.IsNullOrEmpty(key) || ctx.Controller == null) return;
            var obj = await Managers.ObjectPooler.SpawnAsync(
                key, ObjectPoolerManager.PoolType.Effect, pos, rot);
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
