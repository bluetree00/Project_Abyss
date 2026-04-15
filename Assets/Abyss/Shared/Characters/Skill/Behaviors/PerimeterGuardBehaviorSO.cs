using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 주변경계 — 대검 E스킬.
/// 플레이어 중심 360° 원형으로 검을 휘둘러 피해를 준다.
///   1단계: 360° 1회 휘두르기
///   2단계: 360° 2회 연속 휘두르기
///   3단계: 2회 휘두르기 + 퍼지는 이펙트가 확산하며 추가 데미지
/// </summary>
[CreateAssetMenu(menuName = "Game/Skill/PerimeterGuardBehavior")]
public class PerimeterGuardBehaviorSO : SkillBehaviorSO
{
    [Header("기본 공격 (전 티어)")]
    [SerializeField] private float baseDamage = 25f;
    [SerializeField] private float detectRadius = 2f;
    [SerializeField] private float knockbackMultiplier = 1.5f;

    [Header("시전")]
    [SerializeField] private float castInterval = 0.5f;

    [Header("3단계: 확산 공격")]
    [SerializeField] private float expandDamage = 20f;
    [SerializeField] private float expandRadius = 4f;
    [SerializeField] private float expandDelay = 0.3f;
    [SerializeField] private string expandEffectKey = "MeteorHit";
    [SerializeField] private float expandEffectScale = 0.8f;

    [Header("이펙트")]
    [SerializeField] private string slashEffectKey = "BasicSlashBlue";
    [SerializeField] private float slashEffectScale = 1.2f;
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
        private enum Phase { Cast, Expand, End }
        private Phase _phase;
        private float _timer;
        private int _castIndex;
        private int _maxCasts;
        private int _skillTier;
        private int _expandWave;
        private const int MaxExpandWaves = 3;
        private readonly HashSet<GameObject> _hitPerCast = new();
        private readonly HashSet<GameObject> _hitByExpand = new();

        public Runtime(PerimeterGuardBehaviorSO data) => _data = data;

        public void OnEnter(SkillExecutionContext ctx)
        {
            _skillTier = Mathf.Clamp(ctx.WeaponData?.tier ?? 1, 1, 3);

            // 3단계: 3파에 걸쳐 검기가 커지며 공격
            _maxCasts = _skillTier >= 3 ? 3 : (_skillTier >= 2 ? 2 : 1);

            _castIndex = 0;
            _timer = 0f;
            _phase = Phase.Cast;

            ctx.RotateToMouse();
            ctx.SetMoveScale(0f);

            _expandWave = 0;
            _hitByExpand.Clear();

            // 첫 시전: T1은 기본공격 애니메이션, T2+는 스킬 애니메이션
            if (_skillTier <= 1)
                ctx.Animator.Play("GroundLightAttack_01", 0, 0f);
            else
                PlayAnimation(ctx);

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
                case Phase.Cast:   UpdateCast(ctx);   break;
                case Phase.Expand: UpdateExpand(ctx);  break;
                case Phase.End:    UpdateEnd(ctx);     break;
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

                // T3 2타째에만 애니메이션 재생 (1타는 OnEnter에서 이미 재생)
                if (_skillTier >= 3 && _castIndex == 1)
                    PlayAnimation(ctx);

                ExecuteCast(ctx);
                _castIndex++;
            }

            if (_castIndex >= _maxCasts && _timer >= _data.castInterval)
            {
                _timer = 0f;
                _phase = Phase.End;
            }
        }

        // ── 3단계: 다단계 확산 공격 (3파) ──
        private void UpdateExpand(SkillExecutionContext ctx)
        {
            if (_timer >= _data.expandDelay)
            {
                _timer = 0f;
                _expandWave++;

                var center = ctx.PlayerTransform.position;
                float waveRadius = _data.expandRadius * (_expandWave / (float)MaxExpandWaves);
                float waveScale = _data.expandEffectScale * (0.5f + _expandWave * 0.3f);

                // 해당 파의 범위 내 데미지
                float dmg = ctx.CalculateDamage(_data.expandDamage);
                var colliders = Physics.OverlapSphere(center, waveRadius);
                foreach (var col in colliders)
                {
                    if (col.gameObject == ctx.Controller.gameObject) continue;
                    if (_hitByExpand.Contains(col.gameObject)) continue;
                    if (col.TryGetComponent<IDamageable>(out var d))
                    {
                        d.TakeDamage(dmg, ctx.Controller.gameObject, _data.knockbackMultiplier * 2f);
                        _hitByExpand.Add(col.gameObject);

                        SpawnEffect(ctx, _data.hitEffectKey,
                            col.transform.position + Vector3.up * 0.8f,
                            _data.hitEffectScale * 1.5f, 0.5f);
                    }
                }

                // 확산 이펙트 — 플레이어 위치에 생성
                SpawnEffect(ctx, _data.expandEffectKey,
                    ctx.PlayerTransform.position + Vector3.up * 0.5f, waveScale, 1.5f);

                if (_expandWave >= MaxExpandWaves)
                    _phase = Phase.End;
            }
        }

        private void UpdateEnd(SkillExecutionContext ctx)
        {
            if (_timer >= _data.endDelay)
                ctx.RequestEnd();
        }

        // ── 360° 원형 공격 ──
        private void ExecuteCast(SkillExecutionContext ctx)
        {
            var center = ctx.PlayerTransform.position;
            bool isTier3 = _skillTier >= 3;

            // 3단계 3타만 범위 확대
            bool isLastWave = isTier3 && _castIndex == _maxCasts - 1;
            float radius = isLastWave ? _data.detectRadius * 1.6f : _data.detectRadius;

            float dmg = ctx.CalculateDamage(_data.baseDamage);
            var colliders = Physics.OverlapSphere(center, radius);
            foreach (var col in colliders)
            {
                if (col.gameObject == ctx.Controller.gameObject) continue;
                if (_hitPerCast.Contains(col.gameObject)) continue;
                if (col.TryGetComponent<IDamageable>(out var d))
                {
                    d.TakeDamage(dmg, ctx.Controller.gameObject, _data.knockbackMultiplier);
                    _hitPerCast.Add(col.gameObject);

                    SpawnEffect(ctx, _data.hitEffectKey,
                        col.transform.position + Vector3.up * 0.8f,
                        _data.hitEffectScale, 0.5f);
                }
            }

            if (isLastWave)
            {
                // 3타: 6개 검격 중첩 — 플레이어 중심에서 방사
                int effectCount = 6;
                float scale = _data.slashEffectScale * 1.8f;
                for (int i = 0; i < effectCount; i++)
                {
                    float angle = i * 60f + Random.Range(-10f, 10f);
                    Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                    Vector3 pos = center + dir * (radius * 0.4f) + Vector3.up * 1f;
                    Quaternion rot = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, Random.Range(-30f, 30f));
                    SpawnEffectRotated(ctx, _data.slashEffectKey, pos, rot, scale, 0.8f);
                }
            }
            else
            {
                // 1, 2타: 3개 검격 — 플레이어 가까이에 모아서
                int effectCount = 3;
                float scale = _data.slashEffectScale;
                for (int i = 0; i < effectCount; i++)
                {
                    float angle = (_castIndex * 60f) + (i * 120f);
                    Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                    Vector3 pos = center + dir * 0.3f + Vector3.up * 1f;
                    Quaternion rot = Quaternion.LookRotation(dir);
                    SpawnEffectRotated(ctx, _data.slashEffectKey, pos, rot, scale, 0.6f);
                }
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

            ctx.Animator.Play(animName, 0, 0f);
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
