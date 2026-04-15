using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 성광베기 — 카타나 E스킬.
/// 전방 5m를 빠르게 베고 지나간다.
/// 티어에 따라 추가 공격/폭발이 해금된다.
///   1단계: 대시 베기 (경로상 적 타격)
///   2단계: + 적중한 적에게 3회 추가 공격
///   3단계: + 3회 추가 공격 + 1회 강력한 폭발 범위 공격
/// </summary>
[CreateAssetMenu(menuName = "Game/Skill/HolySlashBehavior")]
public class HolySlashBehaviorSO : SkillBehaviorSO
{
    [Header("대시")]
    public float dashDuration = 0.15f;
    public float dashDistance = 5f;
    public float detectRadius = 2f;

    [Header("대시 데미지")]
    public float baseDashDamage = 15f;
    public float knockbackMultiplier = 0.3f;

    [Header("2단계: 추가 공격")]
    public int extraHitCount = 3;
    public float extraHitDelay = 0.15f;
    public float extraHitDamage = 8f;

    [Header("3단계: 폭발")]
    public float explodeDelay = 0.2f;
    public float explodeRadius = 3f;
    public float explodeDamage = 30f;
    public string explodeEffectKey = "FrontAttackHit";
    public float explodeEffectScale = 2f;

    [Header("마무리")]
    public float endDelay = 0.3f;

    [Header("이펙트")]
    public string dashEffectKey = "IasenDashLaser";
    public string dashTrailKey = "IasenDashFire";
    public string slashEffectKey = "IasenFinishFlash";
    public string hitEffectKey = "SwordHitImpact";
    public float hitEffectScale = 0.4f;

    [Header("대시 라인 이펙트 (전 티어)")]
    public string dashLineEffectKey = "";
    public float dashLineScale = 1f;

    [Header("2단계 추가 이펙트")]
    public string tier2ExtraHitKey = "";
    public float tier2ExtraHitScale = 0.5f;
    public string tier2DashMuzzleKey = "";
    public float tier2DashMuzzleScale = 1f;

    [Header("3단계 폭발 이펙트")]
    public string tier3BurstEffectKey = "";
    public float tier3BurstScale = 3f;

    [Header("3단계 대시 라인 추가 이펙트")]
    public string tier3DashLineExtraKey = "";
    public float tier3DashLineExtraScale = 1.5f;

    [Header("트레일")]
    public bool useWeaponTrail = true;

    [Header("애니메이션")]
    public string animationOverride;

    public override ISkillRuntime CreateRuntime() => new Runtime(this);

    private class Runtime : ISkillRuntime
    {
        private readonly HolySlashBehaviorSO _data;
        private enum Phase { Dash, ExtraHit, Explode, End }
        private Phase _phase;
        private float _timer;
        private int _skillTier;
        private int _extraHitIndex;
        private Vector3 _dashStart, _dashEnd;
        private readonly List<IDamageable> _hitTargets = new();
        private readonly HashSet<GameObject> _hitObjects = new();

        public Runtime(HolySlashBehaviorSO data) => _data = data;

        public void OnEnter(SkillExecutionContext ctx)
        {
            PlayAnimation(ctx);
            ctx.RotateToMouse();
            ctx.SetMoveScale(0f);

            _skillTier = Mathf.Clamp(ctx.WeaponData?.tier ?? 1, 1, 3);

            var dir = ctx.PlayerTransform.forward;
            _dashStart = ctx.PlayerTransform.position;
            _dashEnd = _dashStart + dir * _data.dashDistance;

            if (ctx.Rigidbody != null)
                ctx.Rigidbody.linearVelocity = Vector3.zero;

            _hitTargets.Clear();
            _hitObjects.Clear();
            _extraHitIndex = 0;
            _timer = 0f;
            _phase = Phase.Dash;

            // 대시 시작 이펙트
            SpawnEffect(ctx, _data.dashTrailKey, ctx.PlayerTransform.position + Vector3.up * 0.5f, 0.8f);

            // 대시 라인 이펙트 (전 티어)
            if (!string.IsNullOrEmpty(_data.dashLineEffectKey))
                SpawnDashLine(ctx, ctx.PlayerTransform.position + dir * (_data.dashDistance * 0.5f) + Vector3.up * 0.5f);

            // 3단계: 대시 라인에 번개 이펙트 추가
            if (_skillTier >= 3 && !string.IsNullOrEmpty(_data.tier3DashLineExtraKey))
                SpawnEffectScaled(ctx, _data.tier3DashLineExtraKey,
                    ctx.PlayerTransform.position + dir * (_data.dashDistance * 0.5f) + Vector3.up * 0.5f,
                    _data.tier3DashLineExtraScale, 2f);

            // 2단계 이상: 대시 머즐 이펙트
            if (_skillTier >= 2 && !string.IsNullOrEmpty(_data.tier2DashMuzzleKey))
                SpawnEffectScaled(ctx, _data.tier2DashMuzzleKey,
                    ctx.PlayerTransform.position + Vector3.up * 1f, _data.tier2DashMuzzleScale, 1f);

            if (_data.useWeaponTrail)
                ctx.Controller.BeginWeaponTrail();
        }

        public void OnUpdate(SkillExecutionContext ctx)
        {
            _timer += Time.deltaTime;
            switch (_phase)
            {
                case Phase.Dash:     UpdateDash(ctx);     break;
                case Phase.ExtraHit: UpdateExtraHit(ctx); break;
                case Phase.Explode:  UpdateExplode(ctx);  break;
                case Phase.End:      UpdateEnd(ctx);      break;
            }
        }

        public void OnExit(SkillExecutionContext ctx)
        {
            ctx.SetMoveScale(1f);
            _hitTargets.Clear();
            _hitObjects.Clear();
        }

        private void UpdateDash(SkillExecutionContext ctx)
        {
            float t = Mathf.Clamp01(_timer / _data.dashDuration);
            ctx.PlayerTransform.position = Vector3.Lerp(_dashStart, _dashEnd, t);

            // 경로상 적 감지
            var colliders = Physics.OverlapSphere(ctx.PlayerTransform.position, _data.detectRadius);
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

            if (t >= 1f)
            {
                _timer = 0f;

                if (_data.useWeaponTrail)
                    ctx.Controller.EndWeaponTrail();

                // 대시 완료 이펙트
                SpawnEffect(ctx, _data.dashEffectKey,
                    ctx.PlayerTransform.position + Vector3.up * 0.5f, 1f);
                SpawnEffect(ctx, _data.slashEffectKey,
                    ctx.PlayerTransform.position + Vector3.up * 1f, 0.6f);

                // 대시 데미지 적용 (1단계)
                ApplyDamageToAll(ctx, _data.baseDashDamage);

                // 티어별 분기
                if (_skillTier >= 2 && _hitTargets.Count > 0)
                {
                    _phase = Phase.ExtraHit;
                    _extraHitIndex = 0;
                }
                else
                {
                    _phase = Phase.End;
                }
            }
        }

        private void UpdateExtraHit(SkillExecutionContext ctx)
        {
            if (_timer >= _data.extraHitDelay)
            {
                _timer = 0f;
                ApplyDamageToAll(ctx, _data.extraHitDamage);
                SpawnHitEffects(ctx);
                _extraHitIndex++;

                if (_extraHitIndex >= _data.extraHitCount)
                {
                    _timer = 0f;
                    if (_skillTier >= 3)
                        _phase = Phase.Explode;
                    else
                        _phase = Phase.End;
                }
            }
        }

        private void UpdateExplode(SkillExecutionContext ctx)
        {
            if (_timer >= _data.explodeDelay)
            {
                _timer = 0f;

                // 폭발 범위 데미지 — 적중한 각 적 위치에서 범위 폭발
                float dmg = ctx.CalculateDamage(_data.explodeDamage);
                string burstKey = !string.IsNullOrEmpty(_data.tier3BurstEffectKey)
                    ? _data.tier3BurstEffectKey : _data.explodeEffectKey;
                float burstScale = !string.IsNullOrEmpty(_data.tier3BurstEffectKey)
                    ? _data.tier3BurstScale : _data.explodeEffectScale;

                // 적이 없으면 플레이어 위치에서 폭발
                if (_hitObjects.Count == 0)
                {
                    var center = ctx.PlayerTransform.position;
                    DoExplosionAt(ctx, center, dmg);
                    SpawnEffectScaled(ctx, burstKey, center + Vector3.up * 0.5f, burstScale, 2f);
                }
                else
                {
                    // 각 적중 대상 위치에서 폭발
                    var alreadyHit = new HashSet<GameObject>();
                    foreach (var obj in _hitObjects)
                    {
                        if (obj == null) continue;
                        var center = obj.transform.position;
                        DoExplosionAt(ctx, center, dmg, alreadyHit);
                        SpawnEffectScaled(ctx, burstKey, center + Vector3.up * 0.5f, burstScale, 2f);
                    }
                }

                _phase = Phase.End;
            }
        }

        private void UpdateEnd(SkillExecutionContext ctx)
        {
            if (_timer >= _data.endDelay)
                ctx.RequestEnd();
        }

        private void DoExplosionAt(SkillExecutionContext ctx, Vector3 center, float dmg, HashSet<GameObject> alreadyHit = null)
        {
            var colliders = Physics.OverlapSphere(center, _data.explodeRadius);
            foreach (var col in colliders)
            {
                if (col.gameObject == ctx.Controller.gameObject) continue;
                if (alreadyHit != null && !alreadyHit.Add(col.gameObject)) continue;
                if (col.TryGetComponent<IDamageable>(out var d))
                    d.TakeDamage(dmg, ctx.Controller.gameObject, _data.knockbackMultiplier * 2f);
            }
        }

        private void ApplyDamageToAll(SkillExecutionContext ctx, float baseDmg)
        {
            if (_hitTargets.Count == 0) return;
            float dmg = ctx.CalculateDamage(baseDmg);
            foreach (var target in _hitTargets)
                target.TakeDamage(dmg, ctx.Controller.gameObject, _data.knockbackMultiplier);
        }

        private void SpawnHitEffects(SkillExecutionContext ctx)
        {
            foreach (var obj in _hitObjects)
            {
                if (obj == null) continue;
                var offset = new Vector3(
                    Random.Range(-0.4f, 0.4f),
                    Random.Range(0.3f, 1.5f),
                    Random.Range(-0.4f, 0.4f));
                SpawnEffect(ctx, _data.hitEffectKey, obj.transform.position + offset, _data.hitEffectScale);

                // 2단계 이상: 추가 히트 이펙트
                if (_skillTier >= 2 && !string.IsNullOrEmpty(_data.tier2ExtraHitKey))
                    SpawnEffectScaled(ctx, _data.tier2ExtraHitKey,
                        obj.transform.position + offset, _data.tier2ExtraHitScale, 0.5f);
            }
        }

        private void PlayAnimation(SkillExecutionContext ctx)
        {
            string animName = !string.IsNullOrEmpty(_data.animationOverride)
                ? _data.animationOverride
                : "ESkill_01";

            var wd = ctx.WeaponData;
            if (string.IsNullOrEmpty(_data.animationOverride) && wd?.animationSet is WeaponAnimationSetSO animSet)
            {
                var mapping = animSet.GetMappings(WeaponAnimGroup.Ground, ctx.ActionType)
                                     .FirstOrDefault(m => !string.IsNullOrEmpty(m.baseClipName));
                if (mapping != null) animName = mapping.baseClipName;
            }

            ctx.Animator.CrossFade(animName, 0.05f);
        }

        private static async void SpawnEffect(SkillExecutionContext ctx, string key, Vector3 pos, float lifetime)
        {
            if (string.IsNullOrEmpty(key)) return;
            var obj = await Managers.ObjectPooler.SpawnAsync(
                key, ObjectPoolerManager.PoolType.Effect, pos, ctx.PlayerTransform.rotation);
            if (obj == null) return;
            if (obj.TryGetComponent<EffectBehaviour>(out var eb))
                eb.Initialize(eb.behaviorSO, ctx.PlayerTransform, lifetime);
            else
                DespawnAfter(obj, lifetime);
        }

        private async void SpawnDashLine(SkillExecutionContext ctx, Vector3 pos)
        {
            if (string.IsNullOrEmpty(_data.dashLineEffectKey)) return;
            var obj = await Managers.ObjectPooler.SpawnAsync(
                _data.dashLineEffectKey, ObjectPoolerManager.PoolType.Effect, pos, ctx.PlayerTransform.rotation);
            if (obj == null) return;
            obj.transform.localScale = Vector3.one * _data.dashLineScale;

            // 루프 끄고 1회만 재생
            foreach (var ps in obj.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = false;
                ps.Clear(true);
                ps.Play(true);
            }
            foreach (var trail in obj.GetComponentsInChildren<TrailRenderer>(true))
                trail.Clear();

            DespawnAfter(obj, 2f);
        }

        private static async void SpawnEffectScaled(SkillExecutionContext ctx, string key, Vector3 pos, float scale, float lifetime)
        {
            if (string.IsNullOrEmpty(key)) return;
            var obj = await Managers.ObjectPooler.SpawnAsync(
                key, ObjectPoolerManager.PoolType.Effect, pos, ctx.PlayerTransform.rotation);
            if (obj == null) return;
            obj.transform.localScale = Vector3.one * scale;

            // 풀 재사용 시 잔상 제거
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
