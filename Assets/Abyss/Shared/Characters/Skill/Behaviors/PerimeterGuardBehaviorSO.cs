using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 주변경계 — 대검 E스킬.
/// 전방 2m 주변 적을 원형으로 벤다.
///   1단계: 1회 시전
///   2단계: 2회 시전
///   3단계: 2회 시전 + 매 시전마다 칼의 궤적을 따라 원형 검기 발사
/// </summary>
[CreateAssetMenu(menuName = "Game/Skill/PerimeterGuardBehavior")]
public class PerimeterGuardBehaviorSO : SkillBehaviorSO
{
    [Header("기본 공격 (전 티어)")]
    [SerializeField] private float baseDamage = 25f;
    [SerializeField] private float detectRadius = 2f;
    [SerializeField] private float detectForwardOffset = 1f;
    [SerializeField] private float knockbackMultiplier = 1.5f;

    [Header("시전")]
    [SerializeField] private float castInterval = 0.6f;

    [Header("3단계: 원형 검기")]
    [SerializeField] private float swordWaveDamage = 15f;
    [SerializeField] private float swordWaveRadius = 2.5f;
    [SerializeField] private float swordWaveDelay = 0.2f;
    [SerializeField] private string swordWaveEffectKey = "ExplosionSlash";
    [SerializeField] private float swordWaveEffectScale = 0.8f;

    [Header("이펙트")]
    [SerializeField] private string slashEffectKey = "BasicSlashBlue";
    [SerializeField] private float slashEffectScale = 1f;
    [SerializeField] private string hitEffectKey = "GreatswordImpact";
    [SerializeField] private float hitEffectScale = 0.5f;

    [Header("마무리")]
    [SerializeField] private float endDelay = 0.3f;

    [Header("애니메이션")]
    [SerializeField] private string animationOverride;

    public override ISkillRuntime CreateRuntime() => new Runtime(this);

    private class Runtime : ISkillRuntime
    {
        private readonly PerimeterGuardBehaviorSO _data;
        private enum Phase { Cast, SwordWave, End }
        private Phase _phase;
        private float _timer;
        private int _castIndex;
        private int _maxCasts;
        private int _skillTier;
        private readonly HashSet<GameObject> _hitPerCast = new();

        public Runtime(PerimeterGuardBehaviorSO data) => _data = data;

        public void OnEnter(SkillExecutionContext ctx)
        {
            _skillTier = Mathf.Clamp(ctx.WeaponData?.tier ?? 1, 1, 3);
            _maxCasts = _skillTier >= 2 ? 2 : 1;
            _castIndex = 0;
            _timer = 0f;
            _phase = Phase.Cast;

            ctx.RotateToMouse();
            ctx.SetMoveScale(0f);
            PlayAnimation(ctx);

            // 첫 시전 즉시 실행
            ExecuteCast(ctx);
            _castIndex++;

            ctx.Controller.InputBuffer.TryConsume(Game.Inputs.Command.Light);
            ctx.Controller.InputBuffer.TryConsume(Game.Inputs.Command.Heavy);
        }

        public void OnUpdate(SkillExecutionContext ctx)
        {
            _timer += Time.deltaTime;
            switch (_phase)
            {
                case Phase.Cast:      UpdateCast(ctx);      break;
                case Phase.SwordWave: UpdateSwordWave(ctx); break;
                case Phase.End:       UpdateEnd(ctx);       break;
            }
        }

        public void OnExit(SkillExecutionContext ctx)
        {
            ctx.SetMoveScale(1f);
            _hitPerCast.Clear();
        }

        private void UpdateCast(SkillExecutionContext ctx)
        {
            if (_castIndex < _maxCasts && _timer >= _data.castInterval)
            {
                _timer = 0f;
                _hitPerCast.Clear();
                ExecuteCast(ctx);
                _castIndex++;
            }

            if (_castIndex >= _maxCasts && _timer >= _data.castInterval)
            {
                _timer = 0f;
                if (_skillTier >= 3)
                    _phase = Phase.SwordWave;
                else
                    _phase = Phase.End;
            }
        }

        private void UpdateSwordWave(SkillExecutionContext ctx)
        {
            if (_timer >= _data.swordWaveDelay)
            {
                _timer = 0f;

                var center = ctx.PlayerTransform.position
                    + ctx.PlayerTransform.forward * _data.detectForwardOffset
                    + Vector3.up * 0.5f;

                // 원형 검기 데미지
                float dmg = ctx.CalculateDamage(_data.swordWaveDamage);
                var colliders = Physics.OverlapSphere(center, _data.swordWaveRadius);
                foreach (var col in colliders)
                {
                    if (col.gameObject == ctx.Controller.gameObject) continue;
                    if (col.TryGetComponent<IDamageable>(out var d))
                        d.TakeDamage(dmg, ctx.Controller.gameObject, _data.knockbackMultiplier * 1.5f);
                }

                // 검기 이펙트
                SpawnEffect(ctx, _data.swordWaveEffectKey, center, _data.swordWaveEffectScale, 1f);

                _phase = Phase.End;
            }
        }

        private void UpdateEnd(SkillExecutionContext ctx)
        {
            if (_timer >= _data.endDelay)
                ctx.RequestEnd();
        }

        private void ExecuteCast(SkillExecutionContext ctx)
        {
            var center = ctx.PlayerTransform.position
                + ctx.PlayerTransform.forward * _data.detectForwardOffset;

            // 범위 내 적 감지 및 데미지
            float dmg = ctx.CalculateDamage(_data.baseDamage);
            var colliders = Physics.OverlapSphere(center, _data.detectRadius);
            foreach (var col in colliders)
            {
                if (col.gameObject == ctx.Controller.gameObject) continue;
                if (_hitPerCast.Contains(col.gameObject)) continue;
                if (col.TryGetComponent<IDamageable>(out var d))
                {
                    d.TakeDamage(dmg, ctx.Controller.gameObject, _data.knockbackMultiplier);
                    _hitPerCast.Add(col.gameObject);

                    // 피격 이펙트
                    SpawnEffect(ctx, _data.hitEffectKey,
                        col.transform.position + Vector3.up * 0.8f,
                        _data.hitEffectScale, 0.5f);
                }
            }

            // 베기 이펙트 — 원형으로 배치
            var pos = center + Vector3.up * 1f;
            float angle = _castIndex * 180f;
            var rot = ctx.PlayerTransform.rotation * Quaternion.Euler(0f, angle, 0f);
            SpawnEffectRotated(ctx, _data.slashEffectKey, pos, rot, _data.slashEffectScale, 0.6f);
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
