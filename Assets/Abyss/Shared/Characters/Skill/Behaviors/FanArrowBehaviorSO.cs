using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 빨리쏘기 — 활 E스킬.
/// 부채꼴 60도 범위 내 3발의 화살을 동시에 발사한다.
/// 티어에 따라 관통/폭발 기능이 해금된다.
///   1단계: 기본 발사
///   2단계: 관통
///   3단계: 관통 + 적중 시 폭발 데미지
/// </summary>
[CreateAssetMenu(menuName = "Game/Skill/FanArrowBehavior")]
public class FanArrowBehaviorSO : SkillBehaviorSO
{
    [Header("발사 설정")]
    public int arrowCount = 3;
    public float spreadAngle = 60f;
    public string arrowKey = "Basic_Arrow_01";
    public float baseDamagePerArrow = 25f;
    public float fireDelay = 0.2f;

    [Header("발사 위치")]
    public float forwardOffset = 1f;
    public float upOffset = 1f;

    [Header("2단계: 관통")]
    public int pierceMaxCount = 5;

    [Header("3단계: 폭발")]
    public float explodeRadius = 2f;
    public float explodeDamageRatio = 0.5f;
    public string explodeEffectKey = "FrontAttackHit";
    public float explodeEffectScale = 1f;

    [Header("투사체 비주얼")]
    [Tooltip("비어있으면 기본 화살 메시 사용")]
    public string projectileEffectKey = "";
    public float projectileEffectScale = 1f;

    [Header("티어별 추가 이펙트")]
    [Tooltip("2단계: 발사 시 추가 머즐 이펙트")]
    public string tier2ExtraMuzzleKey = "";
    public float tier2ExtraMuzzleScale = 1f;
    [Tooltip("3단계: 피격 시 추가 히트 이펙트")]
    public string tier3ExtraHitKey = "";
    public float tier3ExtraHitScale = 1f;

    [Header("이펙트")]
    public string muzzleEffectKey = "";
    public float muzzleEffectScale = 1f;

    [Header("마무리")]
    public float endDelay = 0.3f;

    [Header("애니메이션")]
    public string animationOverride;

    public override ISkillRuntime CreateRuntime() => new Runtime(this);

    private class Runtime : ISkillRuntime
    {
        private readonly FanArrowBehaviorSO _data;
        private enum Phase { WindUp, Fire, End }
        private Phase _phase;
        private float _timer;
        private bool _fired;
        private int _skillTier;

        public Runtime(FanArrowBehaviorSO data) => _data = data;

        public void OnEnter(SkillExecutionContext ctx)
        {
            PlayAnimation(ctx);
            ctx.RotateToMouse();
            ctx.SetMoveScale(0f);

            // 무기 티어로 스킬 단계 결정 (1~3)
            _skillTier = Mathf.Clamp(ctx.WeaponData?.tier ?? 1, 1, 3);

            _phase = Phase.WindUp;
            _timer = 0f;
            _fired = false;
        }

        public void OnUpdate(SkillExecutionContext ctx)
        {
            _timer += Time.deltaTime;
            switch (_phase)
            {
                case Phase.WindUp: UpdateWindUp(ctx); break;
                case Phase.Fire:   UpdateFire(ctx);   break;
                case Phase.End:    UpdateEnd(ctx);     break;
            }
        }

        public void OnExit(SkillExecutionContext ctx)
        {
            ctx.SetMoveScale(1f);
        }

        private void UpdateWindUp(SkillExecutionContext ctx)
        {
            if (_timer >= _data.fireDelay)
            {
                _timer = 0f;
                _phase = Phase.Fire;
            }
        }

        private void UpdateFire(SkillExecutionContext ctx)
        {
            if (_fired) return;
            _fired = true;
            FireFanArrows(ctx);
            _phase = Phase.End;
            _timer = 0f;
        }

        private void UpdateEnd(SkillExecutionContext ctx)
        {
            if (_timer >= _data.endDelay)
                ctx.RequestEnd();
        }

        private void FireFanArrows(SkillExecutionContext ctx)
        {
            var forward = ctx.PlayerTransform.forward;
            forward.y = 0f;
            forward.Normalize();

            float dmg = ctx.CalculateDamage(_data.baseDamagePerArrow);
            float halfSpread = _data.spreadAngle * 0.5f;

            // 머즐 이펙트
            if (!string.IsNullOrEmpty(_data.muzzleEffectKey))
            {
                var muzzlePos = ctx.PlayerTransform.position
                    + forward * _data.forwardOffset + Vector3.up * _data.upOffset;
                SpawnEffect(ctx, _data.muzzleEffectKey, muzzlePos, _data.muzzleEffectScale, 1f);
            }

            // 2단계 이상: 추가 머즐 이펙트
            if (_skillTier >= 2 && !string.IsNullOrEmpty(_data.tier2ExtraMuzzleKey))
            {
                var extraPos = ctx.PlayerTransform.position
                    + forward * _data.forwardOffset + Vector3.up * _data.upOffset;
                SpawnEffect(ctx, _data.tier2ExtraMuzzleKey, extraPos, _data.tier2ExtraMuzzleScale, 1.5f);
            }

            for (int i = 0; i < _data.arrowCount; i++)
            {
                float t = _data.arrowCount <= 1
                    ? 0f
                    : (float)i / (_data.arrowCount - 1);
                float angle = Mathf.Lerp(-halfSpread, halfSpread, t);
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * forward;

                Vector3 firePos = ctx.PlayerTransform.position
                    + forward * _data.forwardOffset
                    + Vector3.up * _data.upOffset;

                SpawnArrow(ctx, firePos, dir, dmg, _skillTier);
            }
        }

        private async void SpawnArrow(SkillExecutionContext ctx, Vector3 pos, Vector3 dir, float dmg, int tier)
        {
            var obj = await Managers.ObjectPooler.SpawnAsync(
                _data.arrowKey, ObjectPoolerManager.PoolType.Effect, pos, Quaternion.LookRotation(dir));
            if (obj == null) return;

            if (!obj.TryGetComponent<BasicArrow>(out var arrow)) return;

            arrow.Fire(dir, ctx.Controller.gameObject, dmg);

            // 투사체 비주얼 이펙트
            if (!string.IsNullOrEmpty(_data.projectileEffectKey))
                arrow.SetVisualEffect(_data.projectileEffectKey, _data.projectileEffectScale);

            // 2단계 이상: 관통
            if (tier >= 2)
                arrow.SetPierce(_data.pierceMaxCount);

            // 3단계: 관통 + 폭발 + 추가 히트 이펙트
            if (tier >= 3)
            {
                string hitKey = !string.IsNullOrEmpty(_data.tier3ExtraHitKey)
                    ? _data.tier3ExtraHitKey : _data.explodeEffectKey;
                float hitScale = !string.IsNullOrEmpty(_data.tier3ExtraHitKey)
                    ? _data.tier3ExtraHitScale : _data.explodeEffectScale;
                arrow.SetExplosion(_data.explodeRadius, dmg * _data.explodeDamageRatio,
                    hitKey, hitScale);
            }
        }

        private void PlayAnimation(SkillExecutionContext ctx)
        {
            string animName = !string.IsNullOrEmpty(_data.animationOverride)
                ? _data.animationOverride
                : "ESkill_01";

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
