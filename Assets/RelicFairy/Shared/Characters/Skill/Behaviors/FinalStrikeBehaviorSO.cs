using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;
using RelicFairy.Monster;

/// <summary>
/// 최후의 일격 — 대검 R스킬. 겨눈 곳으로 뛰어올라 내려찍는다(10-22 개편 — 사용자 「대검 스킬이 대검 컨셉에 비해 너무 가볍다」).
///   전 단계: 웅크렸다 도약(경직 면역) → 겨눈 적 앞(최대 4 m)에 내려찍기 → 착지점 둘레 원 3.5 m
///   2단계: + 0.3초 뒤 여진 1번(반경 5 m)
///   3단계: + 여진 2번(5 m · 6.5 m, 피해는 둘로 나눔)
/// 예전엔 제자리 차지(마법진) 뒤 앞 반원을 벴고, 2 · 3단계 추가타가 맞은 적마다 주황 · 초록 폭발(파편이 떨어지는 Hit 25)을 띄웠다
/// — 사용자 「내려찍기 추가 이펙트가 지저분하게 떨어진다」. 이제 충격은 땅 균열 · 흙먼지 고리 · 칼끝 섬광 하나로 보인다.
/// 클립은 가웨인 Q와 같던 SPAttack01 대신 공중 강공(JumpAttack04)이다(무기 · 유물끼리 동작 겹침 금지).
/// 여진은 땅에서 퍼지는 것이라 스킬이 끝나도(회피로 끊어도) 이어진다.
/// </summary>
[CreateAssetMenu(menuName = "Game/Skill/FinalStrikeBehavior")]
public class FinalStrikeBehaviorSO : SkillBehaviorSO
{
    [Header("도약")]
    [Tooltip("누른 뒤 착지(판정)까지(초) — 클립의 내려찍기가 이때 닿게 재생한다")]
    [FormerlySerializedAs("chargeDuration")]
    [SerializeField] private float leapTime = 0.55f;
    [Tooltip("착지 전 이만큼(초) 동안 수평으로 날아간다 — 앞은 웅크림")]
    [SerializeField] private float leapAirTime = 0.42f;
    [SerializeField] private float leapMaxDistance = 4f;
    [Tooltip("겨눈 적 앞에서 멈출 거리(m, 적 몸 반경에 더한다)")]
    [SerializeField] private float leapTargetGap = 1.2f;
    [Tooltip("겨눈 적이 없을 때 앞으로 뛰는 거리(m)")]
    [SerializeField] private float leapNoTargetDistance = 3f;
    [SerializeField] private float leapTargetCone = 15f;

    [Header("착지 충격 (전 단계)")]
    [SerializeField] private float baseDamage = 500f;
    [SerializeField] private float detectRadius = 3.5f;
    [SerializeField] private float knockbackMultiplier = 2f;

    [Header("여진 (2 · 3단계 · 충격 증강 III)")]
    [Tooltip("2단계 여진 1번 피해")]
    [SerializeField] private float extraDamage = 250f;
    [Tooltip("3단계 여진 2번 피해 합(한 번에 반씩)")]
    [SerializeField] private float tier3ExtraDamage = 350f;
    [FormerlySerializedAs("extraDelay")]
    [SerializeField] private float aftershockDelay = 0.3f;
    [SerializeField] private float aftershockGap = 0.25f;
    [SerializeField] private float aftershockRadius = 5f;
    [SerializeField] private float aftershockRadius2 = 6.5f;

    [Header("이펙트")]
    [SerializeField] private string crackEffectKey = "GreatswordGroundCrack";
    [Tooltip("판정 반경 1 m당 크기")]
    [SerializeField] private float crackScalePerMeter = 0.71f;
    [SerializeField] private string shockEffectKey = "GreatswordEFinish";
    [SerializeField] private float shockScalePerMeter = 0.26f;
    [SerializeField] private string flashEffectKey = "SwordStrikeFlash";
    [SerializeField] private float flashEffectScale = 1.2f;
    [Tooltip("맞은 적 타격 이펙트 — 대검 계열(푸른 타격). 비우면 공용 노란 베기")]
    [SerializeField] private string hitEffectKey = "GreatswordHitImpact";
    [SerializeField] private float hitEffectScale = 3.4f;

    [Header("마무리")]
    [Tooltip("착지 뒤 칼끝을 박은 자세(초) — 후딜이라 회피 · 공격 · 스킬로 끊긴다")]
    [SerializeField] private float endDelay = 0.25f;

    [Header("애니메이션")]
    [SerializeField] private string animationOverride;

    public override ISkillRuntime CreateRuntime() => new Runtime(this);

    private class Runtime : ISkillRuntime
    {
        private readonly FinalStrikeBehaviorSO _data;
        private enum Phase { Leap, End }
        private Phase _phase;
        private float _timer;
        private int _skillTier;
        private float _radiusMult;   // 착지 · 여진 반경 — 근접 강화 증강 「최후의 일격 충격」(10-15 · 10-22)이 늘린다
        private Vector3 _leapStart, _leapEnd;
        private readonly List<GameObject> _hits = new();

        public Runtime(FinalStrikeBehaviorSO data) => _data = data;

        public void OnEnter(SkillExecutionContext ctx)
        {
            _skillTier = ctx.SkillTier;
            _radiusMult = 1f + MeleeAugments.ValueOn(ctx.WeaponData, MeleeAugmentCatalog.GreatFinal) * 0.01f;
            _timer = 0f;
            _phase = Phase.Leap;

            ctx.RotateToMouse();
            ctx.SetMoveScale(0f);
            if (ctx.Rigidbody != null) ctx.Rigidbody.linearVelocity = Vector3.zero;

            _leapStart = ctx.PlayerTransform.position;
            _leapEnd = ctx.DashEndBeforeWall(_leapStart, ctx.AimForward, LeapDistance(ctx));   // 벽 앞에서 멈춘다(10-14)
            SkillRangeRing.Circle(_leapEnd, _data.detectRadius * _radiusMult, _data.leapTime);
            PlayAnimation(ctx);

            ctx.Controller.InputBuffer.TryConsume(Game.Inputs.Command.Light);
        }

        public void OnUpdate(SkillExecutionContext ctx)
        {
            _timer += ctx.DeltaTime;   // 애니메이터와 같은 시계(슬로모 — 10-09)
            switch (_phase)
            {
                case Phase.Leap: UpdateLeap(ctx); break;
                case Phase.End:  UpdateEnd(ctx);  break;
            }
        }

        public void OnExit(SkillExecutionContext ctx)
        {
            ctx.SetMoveScale(1f);
            _hits.Clear();
            if (ctx.Rigidbody != null)
            {
                ctx.Rigidbody.linearVelocity  = Vector3.zero;
                ctx.Rigidbody.angularVelocity = Vector3.zero;
            }
        }

        // ── 도약 — 앞은 웅크림, 착지 전 leapAirTime 동안 날아간다 ──
        private void UpdateLeap(SkillExecutionContext ctx)
        {
            float air = Mathf.Clamp(_data.leapAirTime, 0.01f, _data.leapTime);
            float u = Mathf.Clamp01((_timer - (_data.leapTime - air)) / air);
            if (u > 0f)
            {
                Vector3 pos = Vector3.Lerp(_leapStart, _leapEnd, u * u * (3f - 2f * u));
                // MovePosition — 보간 켠 Rigidbody와 함께 카메라가 부드럽게 따라온다(성광베기와 같은 길)
                if (ctx.Rigidbody != null && !ctx.Rigidbody.isKinematic) ctx.Rigidbody.MovePosition(pos);
                else ctx.PlayerTransform.position = pos;
            }
            if (_timer < _data.leapTime) return;

            _timer = 0f;
            Land(ctx);
            _phase = Phase.End;
        }

        private void UpdateEnd(SkillExecutionContext ctx)
        {
            ctx.EnterRecovery();   // 후딜 — 회피로 끊을 수 있다 · 스탠스 해제(10-08 A2 · A4)
            if (_timer >= _data.endDelay)
                ctx.RequestEnd();
        }

        // ── 착지 — 둘레 원 판정 · 땅 균열 · 흙먼지 고리 · 칼끝 섬광 ──
        private void Land(SkillExecutionContext ctx)
        {
            var center = ctx.PlayerTransform.position;
            var rot = Quaternion.LookRotation(ctx.PlayerTransform.forward);
            float radius = _data.detectRadius * _radiusMult;
            SkillRangeRing.Circle(center, radius);
            Managers.Sound?.PlayEvent(SoundEvent.SkillHeavyImpact);   // 내려찍기 충격(A6)

            // 스킬 자체가 한 방 — 본타가 막타(멈칫이 길다)
            float dmg = ctx.CalculateDamage(_data.baseDamage);
            CombatQuery.GetNearbyDamageables(center, radius, ctx.Controller.gameObject, 32, _hits);
            foreach (var go in _hits)
                ctx.DealDamage(go, dmg, _data.knockbackMultiplier, isFinisher: true, hitEffectKey: _data.hitEffectKey, hitEffectScale: _data.hitEffectScale);
            // 맞은 적이 없어도 땅이 울린다 — 맞혔으면 막타 흔들림이 이미 있다(겹치면 「살짝」 상한을 넘는다)
            if (_hits.Count == 0) HitFeelService.CameraShake(HitFeelService.DealtShakeFinisher, 0.2f);

            SpawnEffect(ctx, _data.crackEffectKey, center + Vector3.up * 0.05f, rot, radius * _data.crackScalePerMeter, 2f, ownedFx: true);
            SpawnEffect(ctx, _data.shockEffectKey, center + Vector3.up * 0.1f, rot, radius * _data.shockScalePerMeter, 1.5f, ownedFx: false);
            SpawnEffect(ctx, _data.flashEffectKey, center + Vector3.up * 0.4f, rot, _data.flashEffectScale, 0.6f, ownedFx: false);

            ScheduleAftershocks(ctx, center, rot);
        }

        // 2단계 1번(5 m) · 3단계 2번(5 m · 6.5 m, 피해 반씩) · 충격 증강 III은 바깥으로 한 번 더(앞 여진과 같은 피해)
        private void ScheduleAftershocks(SkillExecutionContext ctx, Vector3 center, Quaternion rot)
        {
            int count = _skillTier >= 3 ? 2 : (_skillTier >= 2 ? 1 : 0);
            float each = _skillTier >= 3 ? _data.tier3ExtraDamage * 0.5f : _data.extraDamage;
            if (MeleeAugments.RankOn(ctx.WeaponData, MeleeAugmentCatalog.GreatFinal) >= 3) count++;
            if (count == 0 || ctx.Controller == null) return;
            AftershocksAsync(ctx, _data, center, rot, count, each, _radiusMult,
                             ctx.Controller.GetCancellationTokenOnDestroy()).Forget();
        }

        private static async UniTaskVoid AftershocksAsync(SkillExecutionContext ctx, FinalStrikeBehaviorSO data, Vector3 center, Quaternion rot,
                                                          int count, float damagePct, float radiusMult, CancellationToken ct)
        {
            var hits = new List<GameObject>(16);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    float wait = i == 0 ? data.aftershockDelay : data.aftershockGap;
                    await UniTask.Delay(TimeSpan.FromSeconds(wait), DelayType.DeltaTime, PlayerLoopTiming.Update, ct);
                    if (ctx.Controller == null) return;

                    float radius = (i == 0 ? data.aftershockRadius : data.aftershockRadius2 + 1.5f * (i - 1)) * radiusMult;
                    float dmg = ctx.CalculateDamage(damagePct);
                    CombatQuery.GetNearbyDamageables(center, radius, ctx.Controller.gameObject, 32, hits);
                    foreach (var go in hits)
                        ctx.DealDamage(go, dmg, data.knockbackMultiplier, isFinisher: i == count - 1,   // 마지막 여진이 마무리
                                       hitEffectKey: data.hitEffectKey, hitEffectScale: data.hitEffectScale);

                    SkillRangeRing.Circle(center, radius);
                    SpawnEffect(ctx, data.shockEffectKey, center + Vector3.up * 0.1f, rot, radius * data.shockScalePerMeter, 1.5f, ownedFx: false);
                }
            }
            catch (OperationCanceledException) { }
        }

        // 겨눈 적(마우스 콘) 앞까지, 없으면 앞으로 정한 거리 — 최대 leapMaxDistance
        private float LeapDistance(SkillExecutionContext ctx)
        {
            ctx.Controller.ComputeMouseAimAssistRotation(_data.leapMaxDistance + _data.leapTargetGap + 2f, _data.leapTargetCone, 1f,
                                                         out var enemy, out float enemyDist);
            if (enemy == null) return _data.leapNoTargetDistance;
            var body = enemy.GetComponentInParent<MonsterBase>();
            float gap = _data.leapTargetGap + (body != null ? body.BodyRadius : 0f);
            return Mathf.Clamp(enemyDist - gap, 0f, _data.leapMaxDistance);
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

            // 착지(판정)에 클립의 내려찍기가 닿게 — 클립 앞부분(서 있는 자세)을 건너뛴다(SkillAnimSync)
            SkillAnimSync.CrossFadeToContact(ctx.Animator, animName, _data.leapTime, 0.05f);
        }

        /// <param name="ownedFx">이 스킬만 쓰는 이펙트(땅 균열 — 원본이 반복 재생) — 한 번만 처음부터 튼다.
        /// 여러 곳이 같이 쓰는 이펙트는 풀 인스턴스 설정을 바꾸지 않는다. 크기는 그대로 키워도 비례한다(실측 80 — 계층 크기로 바꾸면 균열이 12.5배).</param>
        private static async void SpawnEffect(SkillExecutionContext ctx, string key,
            Vector3 pos, Quaternion rot, float scale, float lifetime, bool ownedFx)
        {
            if (string.IsNullOrEmpty(key) || ctx.Controller == null) return;
            var obj = await Managers.ObjectPooler.SpawnAsync(
                key, ObjectPoolerManager.PoolType.Effect, pos, rot);
            if (obj == null) return;
            obj.transform.localScale = Vector3.one * scale;
            if (ownedFx)
            {
                foreach (var ps in obj.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.loop = false;
                    ps.Clear(true);
                    ps.Play(true);
                }
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
                await UniTask.Delay((int)(delay * 1000),
                    cancellationToken: obj.GetCancellationTokenOnDestroy());
                if (obj != null && obj.activeInHierarchy)
                    Managers.ObjectPooler.Despawn(obj);
            }
            catch (System.OperationCanceledException) { }
        }
    }
}
