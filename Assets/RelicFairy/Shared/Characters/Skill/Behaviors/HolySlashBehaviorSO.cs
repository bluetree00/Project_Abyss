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
    [Tooltip("마무리 이펙트 크기(10-22 B4: 주황 노바 → 붉은 베기 조각)")]
    public float slashEffectScale = 1f;
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
        private int _extraHits;   // 추가 타격 수 — 발도 연속 증강(10-15)이 늘린다
        private Vector3 _dashStart, _dashEnd;
        private readonly List<IDamageable> _hitTargets = new();
        private readonly HashSet<GameObject> _hitObjects = new();

        public Runtime(HolySlashBehaviorSO data) => _data = data;

        public void OnEnter(SkillExecutionContext ctx)
        {
            PlayAnimation(ctx);
            ctx.RotateToMouse();
            ctx.SetMoveScale(0f);

            _skillTier = ctx.SkillTier;

            var dir = ctx.AimForward;   // 방금 요청한 회전(B1 — transform.forward는 옛 방향)
            _dashStart = ctx.PlayerTransform.position;
            float dashDist = _data.dashDistance * (1f + MeleeAugments.ValueOn(ctx.WeaponData, MeleeAugmentCatalog.KatanaDraw) * 0.01f);   // 발도 연속 증강(10-15)
            _extraHits = _data.extraHitCount + Mathf.RoundToInt(MeleeAugments.Value2On(ctx.WeaponData, MeleeAugmentCatalog.KatanaDraw));
            _dashEnd = ctx.DashEndBeforeWall(_dashStart, dir, dashDist);   // 벽 앞에서 멈춘다(10-14 — 끝점 Lerp가 벽을 뚫었다)
            MeleeAugments.ReportSkillDash(ctx.WeaponData, ctx.Slot, _dashStart, _dashEnd, _data.dashDuration,
                                          ctx.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee));   // 일섬 전설 증강(10-24 W7)

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
                SpawnDashLine(ctx, Vector3.Lerp(_dashStart, _dashEnd, 0.5f) + Vector3.up * 0.5f);

            // 3단계: 대시 라인에 번개 이펙트 추가
            if (_skillTier >= 3 && !string.IsNullOrEmpty(_data.tier3DashLineExtraKey))
                SpawnEffectScaled(ctx, _data.tier3DashLineExtraKey,
                    Vector3.Lerp(_dashStart, _dashEnd, 0.5f) + Vector3.up * 0.5f,
                    _data.tier3DashLineExtraScale, 2f);

            // 2단계 이상: 대시 머즐 이펙트
            if (_skillTier >= 2 && !string.IsNullOrEmpty(_data.tier2DashMuzzleKey))
                SpawnEffectScaled(ctx, _data.tier2DashMuzzleKey,
                    ctx.PlayerTransform.position + Vector3.up * 1f, _data.tier2DashMuzzleScale, 1f);

        }

        public void OnUpdate(SkillExecutionContext ctx)
        {
            _timer += ctx.DeltaTime;   // 애니메이터와 같은 시계(슬로모 — 10-09)
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

            // 대시 중 벽 충돌 등으로 누적된 angular/linear velocity 제거
            if (ctx.Rigidbody != null)
            {
                ctx.Rigidbody.linearVelocity  = Vector3.zero;
                ctx.Rigidbody.angularVelocity = Vector3.zero;
            }
        }

        private void UpdateDash(SkillExecutionContext ctx)
        {
            float t = Mathf.Clamp01(_timer / _data.dashDuration);
            // ease-out: 1 - (1-t)^2 — 초반 가속 / 후반 감속 (자연스러운 일섬 느낌)
            float u = 1f - t;
            float eased = 1f - u * u;
            Vector3 targetPos = Vector3.Lerp(_dashStart, _dashEnd, eased);

            // Rigidbody.MovePosition 으로 적용 — Rigid.interpolation=Interpolate 와 함께
            // Cinemachine 카메라가 부드럽게 추적 (transform 직접 대입 시 보간 우회로 카메라 흔들림)
            if (ctx.Rigidbody != null && !ctx.Rigidbody.isKinematic)
                ctx.Rigidbody.MovePosition(targetPos);
            else
                ctx.PlayerTransform.position = targetPos;

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

                // 대시 완료 이펙트
                SpawnEffect(ctx, _data.dashEffectKey,
                    ctx.PlayerTransform.position + Vector3.up * 0.5f, 1f);
                SpawnEffectScaled(ctx, _data.slashEffectKey,
                    ctx.PlayerTransform.position + Vector3.up * 1f, _data.slashEffectScale, 0.6f);
                Managers.Sound?.PlayEvent(SoundEvent.PlayerSwing);   // 베는 소리(A6 · C8 — 스킬 클립엔 이벤트가 없다)

                // 대시 데미지 적용 (1단계) — 뒤에 추가타가 없으면 이 타가 마무리(A5)
                ApplyDamageToAll(ctx, _data.baseDashDamage, finisher: _skillTier < 2);
                SpawnHitEffects(ctx);

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
                // 2단계 마지막 추가타가 마무리(3단계는 뒤 폭발이 마무리) — A5
                ApplyDamageToAll(ctx, _data.extraHitDamage, finisher: _skillTier < 3 && _extraHitIndex + 1 >= _extraHits);
                SpawnHitEffects(ctx);
                _extraHitIndex++;

                if (_extraHitIndex >= _extraHits)
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
            ctx.EnterRecovery();   // 후딜 — 회피로 끊을 수 있다 · 스탠스 해제(10-08 A2 · A4)
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
                    ctx.DealDamage(d, dmg, _data.knockbackMultiplier * 2f, isFinisher: true);   // 마무리 폭발
            }
        }

        private void ApplyDamageToAll(SkillExecutionContext ctx, float baseDmg, bool finisher = false)
        {
            if (_hitTargets.Count == 0) return;
            float dmg = ctx.CalculateDamage(baseDmg);
            foreach (var target in _hitTargets)
                ctx.DealDamage(target, dmg, _data.knockbackMultiplier, finisher, skipHitVfx: true);   // 타격 이펙트는 아래에서 직접
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
                // hitEffectScale이 수명 자리로 들어가 크기는 늘 1이었다(10-08 수정) — 크기로 넘기고 수명은 칼 타격 이펙트 길이(0.5초)
                SpawnEffectScaled(ctx, _data.hitEffectKey, obj.transform.position + offset, _data.hitEffectScale, 0.5f);

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

            // 대시가 끝나는 순간(첫 판정)에 베기가 닿게 — 클립별 닿는 순간은 SkillAnimSync 표(10-22~ 뛰어들며 베기 Dodge_Attack_F).
            SkillAnimSync.CrossFadeToContact(ctx.Animator, animName, _data.dashDuration, 0.05f);
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
                _data.dashLineEffectKey, ObjectPoolerManager.PoolType.Effect, pos, Quaternion.LookRotation(ctx.AimForward));   // 돌진 방향(10-13)
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

        /// <summary>수명을 그 인스턴스에 맡긴다(<see cref="PooledVfxLifetime"/>) — 여기서 타이머를 들면 그 사이 풀로 돌아가 다른 데서 다시 쓰인 이펙트를 옛 타이머가 껐다(10-29 풀 점검 4).</summary>
        private static void DespawnAfter(GameObject obj, float delay)
        {
            if (!obj.TryGetComponent<PooledVfxLifetime>(out var life)) life = obj.AddComponent<PooledVfxLifetime>();
            life.SetLife(delay);
        }
    }
}
