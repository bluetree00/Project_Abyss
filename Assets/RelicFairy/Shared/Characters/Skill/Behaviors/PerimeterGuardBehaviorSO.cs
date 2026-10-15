using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 주변경계 — 대검 E스킬.
/// 플레이어 중심 360° 원형으로 검을 휘둘러 피해를 준다.
///   1단계: 360° 1회 휘두르기
///   2단계: 360° 2회 연속 휘두르기
///   3단계: 360° 3회 연속 휘두르기 + 확산 검기(반경 6.5 m)가 퍼지며 추가 데미지
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
    [SerializeField] private string hitEffectKey = "SwordHitImpact";
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
        private bool  _firstCastPending;          // 첫 판정 대기(B7)
        private const float FirstCastWindup = 0.15f;   // 누른 뒤 첫 판정까지 — 휘두르는 모습이 보일 만큼만(조작감 손해 최소)
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
            _skillTier = ctx.SkillTier;

            // 3단계: 3파에 걸쳐 검기가 커지며 공격
            _maxCasts = _skillTier >= 3 ? 3 : (_skillTier >= 2 ? 2 : 1);
            _maxCasts += Mathf.RoundToInt(MeleeAugments.ValueOn(ctx.WeaponData, MeleeAugmentCatalog.GreatPerimeter));   // 주변경계 연장 증강(10-15)

            _castIndex = 0;
            _timer = 0f;
            _phase = Phase.Cast;

            ctx.RotateToMouse();
            ctx.SetMoveScale(0f);
            SkillRangeRing.Circle(ctx.PlayerTransform.position, _data.detectRadius);   // 휘두르는 0.15초 동안 맞을 둘레(A10)

            _expandWave = 0;
            _hitByExpand.Clear();

            // 첫 시전: T1은 기본공격 애니메이션, T2+는 스킬 애니메이션.
            // B7(10-08): 예전엔 누른 프레임에 피해가 먼저 들어갔다(클립도 닿는 순간부터 재생 — 휘두르는 모습 없이 맞았다).
            // 이제 클립이 FirstCastWindup초 뒤에 닿도록 재생하고 그때 첫 판정을 낸다.
            if (_skillTier <= 1)
                ctx.Animator.CrossFadeInFixedTime("GroundLightAttack_01", 0.05f, 0, 0f);   // 즉시 재생은 앞 자세에서 한 프레임에 튀었다(10-09 출시 점검)
            else
                PlayAnimation(ctx, FirstCastWindup);
            _firstCastPending = true;

            ctx.Controller.InputBuffer.TryConsume(Game.Inputs.Command.Light);
        }

        public void OnUpdate(SkillExecutionContext ctx)
        {
            _timer += ctx.DeltaTime;   // 애니메이터와 같은 시계(슬로모 — 10-09)
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
            if (_firstCastPending)
            {
                if (_timer < FirstCastWindup) return;
                _firstCastPending = false;
                _timer = 0f;
                ExecuteCast(ctx);
                _castIndex++;
                return;
            }

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
                // 3단계는 확산 검기 3파(C3) — 예전엔 Expand로 한 번도 넘어가지 않았다. 반경(에셋 6.5 m)은 셋째 시전(감지 × 1.6 = 5.6 m)보다
                // 넓게 — 4 m면 셋째 시전에 밀려난 적에게 닿지 않았다(10-08 실측)
                if (_skillTier >= 3)
                {
                    _phase = Phase.Expand;
                    SkillRangeRing.Circle(ctx.PlayerTransform.position, _data.expandRadius, _data.expandDelay * MaxExpandWaves);
                }
                else _phase = Phase.End;
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
                        ctx.DealDamage(d, dmg, _data.knockbackMultiplier * 2f, _expandWave >= MaxExpandWaves, skipHitVfx: true);   // 마지막 확산파가 막타 · 타격 이펙트는 아래에서 직접
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
            ctx.EnterRecovery();   // 후딜 — 회피로 끊을 수 있다 · 스탠스 해제(10-08 A2 · A4)
            if (_timer >= _data.endDelay)
                ctx.RequestEnd();
        }

        // ── 360° 원형 공격 ──
        private void ExecuteCast(SkillExecutionContext ctx)
        {
            Managers.Sound?.PlayEvent(SoundEvent.PlayerSwingHeavy);   // 회전 베기 — 대검 무거운 휘두름(A6)
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
                    // 마지막 시전이 마무리(A5) — 3단계는 뒤 확산 검기 마지막 파가 마무리
                    bool finisher = !isTier3 && _castIndex == _maxCasts - 1;
                    ctx.DealDamage(d, dmg, _data.knockbackMultiplier, finisher, skipHitVfx: true);   // 타격 이펙트는 아래에서 직접
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
                float scale = _data.slashEffectScale * 1.4f;   // 판정 3.5 m에 맞춤(10-13 실측: ×1 → 2.5 m)
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

        private void PlayAnimation(SkillExecutionContext ctx, float contactIn = 0f)
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

            // 판정 시각에 회전 베기가 가장 빠른 순간(1.27초)이 오도록 중간부터 재생(09-25). 첫 시전은 contactIn만큼 휘두르는 모습을 남긴다(B7).
            if (contactIn > 0f) SkillAnimSync.CrossFadeToContact(ctx.Animator, animName, contactIn, 0.05f);
            else                SkillAnimSync.CrossFadeToContact(ctx.Animator, animName, 0f, 0.06f);   // 2 · 3파 — 즉시 재생은 회전 끝 자세에서 튀었다(10-09 출시 점검)
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
