using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 궁수의 집중 — 활 Q스킬.
/// 1초간 차징 후 강한 화살을 발사한다.
/// 티어에 따라 발사 수가 줄고 개당 데미지/이펙트가 강력해진다.
///   1단계: 3발 발사
///   2단계: 2발 발사 (더 강함)
///   3단계: 1발 발사 (가장 강함)
/// </summary>
[CreateAssetMenu(menuName = "Game/Skill/FocusShotBehavior")]
public class FocusShotBehaviorSO : SkillBehaviorSO
{
    [Header("차징")]
    [Tooltip("차징 시간 (초)")]
    public float chargeTime = 1f;
    public string chargeEffectKey = "";
    public float chargeEffectScale = 1f;

    [Header("발사 설정")]
    public string arrowKey = "Basic_Arrow_01";
    [Tooltip("연발 간 딜레이")]
    public float shotDelay = 0.15f;
    public float forwardOffset = 1f;
    public float upOffset = 1f;

    [Header("티어별 데미지 (1~3단계)")]
    [Tooltip("1단계: 3발, 화살당 데미지")]
    public float tier1DamagePerArrow = 30f;
    [Tooltip("2단계: 2발, 화살당 데미지")]
    public float tier2DamagePerArrow = 50f;
    [Tooltip("3단계: 1발, 화살당 데미지")]
    public float tier3DamagePerArrow = 100f;

    [Header("티어별 이펙트 크기")]
    public float tier1ArrowScale = 1f;
    public float tier2ArrowScale = 1.5f;
    public float tier3ArrowScale = 2.5f;

    [Header("발사 이펙트")]
    public string muzzleEffectKey = "";
    public float tier1MuzzleScale = 0.5f;
    public float tier2MuzzleScale = 1f;
    public float tier3MuzzleScale = 1.5f;

    [Header("투사체 비주얼")]
    [Tooltip("비어있으면 기본 화살 메시 사용")]
    public string projectileEffectKey = "";
    public float tier1ProjectileScale = 1f;
    public float tier2ProjectileScale = 1.5f;
    public float tier3ProjectileScale = 2f;

    [Header("티어별 추가 이펙트")]
    [Tooltip("2단계: 발사 시 추가 머즐 이펙트")]
    public string tier2ExtraMuzzleKey = "";
    public float tier2ExtraMuzzleScale = 1f;
    [Tooltip("3단계: 피격 시 추가 히트 이펙트 (화살에 설정)")]
    public string tier3ExtraHitKey = "";
    public float tier3ExtraHitScale = 1f;

    [Header("마무리")]
    public float endDelay = 0.3f;

    [Header("애니메이션")]
    [Tooltip("차징 애니메이션 (비어있으면 QSkill_01)")]
    public string animationOverride;
    [Tooltip("발사 애니메이션 (비어있으면 QSkill_02)")]
    public string fireAnimationOverride;

    public override ISkillRuntime CreateRuntime() => new Runtime(this);

    private class Runtime : ISkillRuntime
    {
        private readonly FocusShotBehaviorSO _data;
        private enum Phase { Charge, Fire, End }
        private Phase _phase;
        private float _timer;
        private int _skillTier;
        private int _totalShots;
        private int _shotsFired;
        private float _damagePerArrow;
        private float _arrowScale;
        private float _muzzleScale;
        private float _projectileScale;
        private GameObject _chargeEffect;

        public Runtime(FocusShotBehaviorSO data) => _data = data;

        public void OnEnter(SkillExecutionContext ctx)
        {
            PlayAnimation(ctx);
            ctx.RotateToMouse();
            ctx.SetMoveScale(0f);

            _skillTier = Mathf.Clamp(ctx.WeaponData?.tier ?? 1, 1, 3);

            // 티어별 설정: 높을수록 적은 발수, 높은 데미지, 큰 이펙트
            switch (_skillTier)
            {
                case 1:
                    _totalShots = 3;
                    _damagePerArrow = ctx.CalculateDamage(_data.tier1DamagePerArrow);
                    _arrowScale = _data.tier1ArrowScale;
                    _muzzleScale = _data.tier1MuzzleScale;
                    _projectileScale = _data.tier1ProjectileScale;
                    break;
                case 2:
                    _totalShots = 2;
                    _damagePerArrow = ctx.CalculateDamage(_data.tier2DamagePerArrow);
                    _arrowScale = _data.tier2ArrowScale;
                    _muzzleScale = _data.tier2MuzzleScale;
                    _projectileScale = _data.tier2ProjectileScale;
                    break;
                default:
                    _totalShots = 1;
                    _damagePerArrow = ctx.CalculateDamage(_data.tier3DamagePerArrow);
                    _arrowScale = _data.tier3ArrowScale;
                    _muzzleScale = _data.tier3MuzzleScale;
                    _projectileScale = _data.tier3ProjectileScale;
                    break;
            }

            _shotsFired = 0;
            _timer = 0f;
            _phase = Phase.Charge;

            // 차징 이펙트
            if (!string.IsNullOrEmpty(_data.chargeEffectKey))
                SpawnChargeEffect(ctx);
        }

        public void OnUpdate(SkillExecutionContext ctx)
        {
            _timer += Time.deltaTime;
            switch (_phase)
            {
                case Phase.Charge: UpdateCharge(ctx); break;
                case Phase.Fire:   UpdateFire(ctx);   break;
                case Phase.End:    UpdateEnd(ctx);     break;
            }
        }

        public void OnExit(SkillExecutionContext ctx)
        {
            ctx.SetMoveScale(1f);
            if (_chargeEffect != null && _chargeEffect.activeInHierarchy)
                Managers.ObjectPooler.Despawn(_chargeEffect);
            _chargeEffect = null;
        }

        private void UpdateCharge(SkillExecutionContext ctx)
        {
            if (_timer >= _data.chargeTime)
            {
                // 차징 이펙트 제거
                if (_chargeEffect != null && _chargeEffect.activeInHierarchy)
                    Managers.ObjectPooler.Despawn(_chargeEffect);
                _chargeEffect = null;

                _timer = 0f;
                _phase = Phase.Fire;

                // 발사 애니메이션으로 전환
                string fireAnim = !string.IsNullOrEmpty(_data.fireAnimationOverride)
                    ? _data.fireAnimationOverride
                    : "QSkill_02";
                ctx.Animator.CrossFade(fireAnim, 0.05f);

                // 즉시 첫 발 발사
                FireOneShot(ctx);
            }
        }

        private void UpdateFire(SkillExecutionContext ctx)
        {
            if (_shotsFired >= _totalShots)
            {
                _timer = 0f;
                _phase = Phase.End;
                return;
            }

            if (_timer >= _data.shotDelay)
            {
                _timer = 0f;
                FireOneShot(ctx);
            }
        }

        private void UpdateEnd(SkillExecutionContext ctx)
        {
            if (_timer >= _data.endDelay)
                ctx.RequestEnd();
        }

        private void FireOneShot(SkillExecutionContext ctx)
        {
            ctx.RotateToMouse();

            var forward = ctx.PlayerTransform.forward;
            forward.y = 0f;
            forward.Normalize();

            Vector3 firePos = ctx.PlayerTransform.position
                + forward * _data.forwardOffset
                + Vector3.up * _data.upOffset;

            // 머즐 이펙트
            if (!string.IsNullOrEmpty(_data.muzzleEffectKey))
                SpawnEffect(ctx, _data.muzzleEffectKey, firePos, _muzzleScale, 1f);

            // 2단계 이상: 추가 머즐 이펙트
            if (_skillTier >= 2 && !string.IsNullOrEmpty(_data.tier2ExtraMuzzleKey))
                SpawnEffect(ctx, _data.tier2ExtraMuzzleKey, firePos, _data.tier2ExtraMuzzleScale, 1.5f);

            SpawnArrow(ctx, firePos, forward, _damagePerArrow, _arrowScale);
            _shotsFired++;
        }

        private async void SpawnArrow(SkillExecutionContext ctx, Vector3 pos, Vector3 dir, float dmg, float scale)
        {
            var obj = await Managers.ObjectPooler.SpawnAsync(
                _data.arrowKey, ObjectPoolerManager.PoolType.Effect, pos, Quaternion.LookRotation(dir));
            if (obj == null) return;

            obj.transform.localScale = Vector3.one * scale;

            if (obj.TryGetComponent<BasicArrow>(out var arrow))
            {
                arrow.Fire(dir, ctx.Controller.gameObject, dmg);
                arrow.SetPierce(99);

                if (!string.IsNullOrEmpty(_data.projectileEffectKey))
                    arrow.SetVisualEffect(_data.projectileEffectKey, _projectileScale);

                // 3단계: 피격 시 추가 히트 이펙트
                if (_skillTier >= 3 && !string.IsNullOrEmpty(_data.tier3ExtraHitKey))
                    arrow.SetExplosion(2f, dmg * 0.3f, _data.tier3ExtraHitKey, _data.tier3ExtraHitScale);
            }
        }

        private async void SpawnChargeEffect(SkillExecutionContext ctx)
        {
            var obj = await Managers.ObjectPooler.SpawnAsync(
                _data.chargeEffectKey, ObjectPoolerManager.PoolType.Effect,
                ctx.PlayerTransform.position + Vector3.up * 1f, ctx.PlayerTransform.rotation);
            if (obj == null) return;
            obj.transform.localScale = Vector3.one * _data.chargeEffectScale;
            obj.transform.SetParent(ctx.PlayerTransform, true);
            _chargeEffect = obj;
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
