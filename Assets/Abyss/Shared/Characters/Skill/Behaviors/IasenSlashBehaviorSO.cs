using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 일섬 (居合斬り) — 마우스 방향으로 빠르게 전진 후 경로상 적에게 순차 타격.
/// </summary>
[CreateAssetMenu(menuName = "Game/Skill/IasenSlashBehavior")]
public class IasenSlashBehaviorSO : SkillBehaviorSO
{
    [Header("대시")]
    public float dashDuration = 0.15f;
    public float dashDistance = 8f;
    public float detectRadius = 2f;

    [Header("슬래시")]
    public int slashCount = 5;
    public float slashDelay = 0.15f;
    public float baseDamagePerSlash = 5f;
    public float knockbackMultiplier = 0.2f;

    [Header("마무리")]
    public float endDelay = 0.3f;

    [Header("이펙트")]
    public string dashEffectKey = "WindBlast";
    public string dashTrailKey = "DashTrail";
    public string slashEffectKey = "MultiSlash";
    public string hitEffectKey = "SwordHitImpact";
    public float hitEffectScale = 0.3f;

    [Header("트레일")]
    [Tooltip("대시 중 무기 히트 트레일 활성화")]
    public bool useWeaponTrail = true;
    [Tooltip("대시 중 플레이어에 부착할 트레일 이펙트 Addressable 키 (비어있으면 사용 안 함)")]
    public string playerTrailKey;
    public float playerTrailScale = 1f;

    [Header("애니메이션")]
    [Tooltip("비어있으면 WeaponAnimationSetSO에서 QSkill 매핑 사용")]
    public string animationOverride;

    public override ISkillRuntime CreateRuntime() => new Runtime(this);

    // ── Runtime ──────────────────────────────────────────────────
    private class Runtime : ISkillRuntime
    {
        private readonly IasenSlashBehaviorSO _data;
        private enum Phase { Dash, Slash, End }
        private Phase _phase;
        private float _timer;
        private int _slashIndex;
        private Vector3 _dashStart, _dashEnd;
        private readonly List<IDamageable> _hitTargets = new();
        private readonly HashSet<GameObject> _hitObjects = new();
        private GameObject _playerTrailInstance;

        public Runtime(IasenSlashBehaviorSO data) => _data = data;

        public void OnEnter(SkillExecutionContext ctx)
        {
            PlayAnimation(ctx);

            var dir = ctx.PlayerTransform.forward;
            _dashStart = ctx.PlayerTransform.position;
            _dashEnd = _dashStart + dir * _data.dashDistance;

            if (ctx.Rigidbody != null)
                ctx.Rigidbody.linearVelocity = Vector3.zero;

            _hitTargets.Clear();
            _hitObjects.Clear();
            _slashIndex = 0;
            _timer = 0f;
            _phase = Phase.Dash;

            // 대시 시작 이펙트 + 무기 트레일
            SpawnEffect(ctx, _data.dashTrailKey, ctx.PlayerTransform.position + Vector3.up * 0.5f, 0.8f);
            if (_data.useWeaponTrail)
                ctx.Controller.BeginWeaponTrail();
            SpawnPlayerTrail(ctx);
        }

        public void OnUpdate(SkillExecutionContext ctx)
        {
            _timer += Time.deltaTime;
            switch (_phase)
            {
                case Phase.Dash:  UpdateDash(ctx);  break;
                case Phase.Slash: UpdateSlash(ctx); break;
                case Phase.End:   UpdateEnd(ctx);   break;
            }
        }

        public void OnExit(SkillExecutionContext ctx)
        {
            _hitTargets.Clear();
            _hitObjects.Clear();
            DespawnPlayerTrail();
        }

        // ── Dash ──
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
                _phase = Phase.Slash;

                if (_data.useWeaponTrail)
                    ctx.Controller.EndWeaponTrail();
                DespawnPlayerTrail();

                // 대시 완료 이펙트
                SpawnEffect(ctx, _data.dashEffectKey,
                    ctx.PlayerTransform.position + Vector3.up * 0.5f, 1f);
                SpawnEffect(ctx, _data.slashEffectKey,
                    ctx.PlayerTransform.position + Vector3.up * 1f, 0.6f);
            }
        }

        // ── Slash ──
        private void UpdateSlash(SkillExecutionContext ctx)
        {
            if (_timer >= _data.slashDelay)
            {
                _timer = 0f;
                ApplySlashDamage(ctx);
                _slashIndex++;

                if (_slashIndex >= _data.slashCount)
                {
                    _timer = 0f;
                    _phase = Phase.End;
                }
            }
        }

        // ── End ──
        private void UpdateEnd(SkillExecutionContext ctx)
        {
            if (_timer >= _data.endDelay)
                ctx.RequestEnd();
        }

        // ── Damage ──
        private void ApplySlashDamage(SkillExecutionContext ctx)
        {
            if (_hitTargets.Count == 0) return;

            float dmg = ctx.CalculateDamage(_data.baseDamagePerSlash);

            foreach (var target in _hitTargets)
                target.TakeDamage(dmg, ctx.Controller.gameObject, _data.knockbackMultiplier);

            // 히트 이펙트
            foreach (var obj in _hitObjects)
            {
                if (obj == null) continue;
                var offset = new Vector3(
                    Random.Range(-0.3f, 0.3f),
                    Random.Range(0.5f, 1.5f),
                    Random.Range(-0.3f, 0.3f));
                SpawnEffect(ctx, _data.hitEffectKey, obj.transform.position + offset, _data.hitEffectScale);
            }
        }

        // ── Animation ──
        private void PlayAnimation(SkillExecutionContext ctx)
        {
            string animName = !string.IsNullOrEmpty(_data.animationOverride)
                ? _data.animationOverride
                : "QSkill_01";

            var wd = ctx.WeaponData;
            if (string.IsNullOrEmpty(_data.animationOverride) && wd?.animationSet is WeaponAnimationSetSO animSet)
            {
                var mapping = animSet.GetMappings(WeaponAnimGroup.Ground, ctx.ActionType)
                                     .FirstOrDefault(m => !string.IsNullOrEmpty(m.baseClipName));
                if (mapping != null) animName = mapping.baseClipName;
            }

            ctx.Animator.CrossFade(animName, 0.05f);
        }

        // ── Effect Helper ──
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

        private static async void DespawnAfter(GameObject obj, float delay)
        {
            await Cysharp.Threading.Tasks.UniTask.Delay(
                (int)(delay * 1000), cancellationToken: obj.GetCancellationTokenOnDestroy());
            if (obj != null && obj.activeInHierarchy)
                Managers.ObjectPooler.Despawn(obj);
        }

        // ── Player Trail (Local 부착) ──
        private async void SpawnPlayerTrail(SkillExecutionContext ctx)
        {
            if (string.IsNullOrEmpty(_data.playerTrailKey)) return;
            var obj = await Managers.ObjectPooler.SpawnAsync(
                _data.playerTrailKey, ObjectPoolerManager.PoolType.Effect,
                ctx.PlayerTransform.position, ctx.PlayerTransform.rotation);
            if (obj == null) return;

            obj.transform.SetParent(ctx.PlayerTransform, true);
            obj.transform.localPosition = Vector3.up * 0.8f;
            obj.transform.localScale = Vector3.one * _data.playerTrailScale;
            _playerTrailInstance = obj;
        }

        private void DespawnPlayerTrail()
        {
            if (_playerTrailInstance != null)
            {
                // 부모만 해제 — TrailRenderer가 자연스럽게 페이드아웃
                // EffectBehaviour의 lifetime이 끝나면 자동 디스폰
                _playerTrailInstance.transform.SetParent(null);
                _playerTrailInstance = null;
            }
        }
    }
}
