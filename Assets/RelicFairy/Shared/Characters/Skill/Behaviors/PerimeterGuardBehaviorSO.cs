using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 주변경계 — 대검 E스킬. 칼을 끌었다가 한 바퀴 크게 벤다(10-22 개편 — 사용자 「대검 스킬이 대검 컨셉에 비해 너무 가볍다」).
///   전 단계: 0.3초 끌기(경직 면역) → 360° 회전 베기 반경 3.5 m · 발밑 흙먼지 고리
///   2단계: + 충격 고리 1번(반경 5 m)
///   3단계: + 충격 고리 2번(5 m · 6.5 m)
///   주변경계 연장 증강: 고리 +N(바깥으로 1.5 m씩, 8 m까지)
/// 예전엔 1단계가 평타 1타 클립이었고 누른 뒤 0.15초에 맞았으며, 2 · 3단계는 같은 빠른 회전을 0.5초마다 되풀이해 바빠 보였다.
/// 충격 고리는 땅에서 퍼지는 것이라 스킬이 끝나도(회피로 끊어도) 이어진다.
/// </summary>
[CreateAssetMenu(menuName = "Game/Skill/PerimeterGuardBehavior")]
public class PerimeterGuardBehaviorSO : SkillBehaviorSO
{
    [Header("회전 베기 (전 단계)")]
    [SerializeField] private float baseDamage = 310f;
    [SerializeField] private float detectRadius = 3.5f;
    [SerializeField] private float knockbackMultiplier = 1.5f;
    [Tooltip("누른 뒤 베기(판정)까지(초) — 칼을 끄는 모습이 보이게. 클립의 회전 베기가 이때 닿는다")]
    [SerializeField] private float windup = 0.3f;
    [Tooltip("베기 뒤 휘두름이 끝나는 만큼(초) — 그 뒤는 후딜(공격 · 스킬 · 회피로 끊긴다)")]
    [SerializeField] private float follow = 0.2f;

    [Header("충격 고리 (2 · 3단계 · 연장 증강)")]
    [SerializeField] private float ringDamage = 310f;
    [SerializeField] private float ringRadius = 5f;
    [SerializeField] private float ringRadiusStep = 1.5f;
    [SerializeField] private float ringRadiusMax = 8f;
    [Tooltip("베기 뒤 첫 고리까지(초)")]
    [SerializeField] private float ringDelay = 0.25f;
    [SerializeField] private float ringGap = 0.25f;

    [Header("이펙트")]
    [SerializeField] private string slashEffectKey = "BasicSlashBlue";
    [SerializeField] private float slashEffectScale = 1f;
    [FormerlySerializedAs("expandEffectKey")]
    [SerializeField] private string shockEffectKey = "GreatswordEFinish";
    [Tooltip("판정 반경 1 m당 크기")]
    [SerializeField] private float shockScalePerMeter = 0.26f;
    [Tooltip("맞은 적 타격 이펙트 — 대검 계열(푸른 타격). 비우면 공용 노란 베기")]
    [SerializeField] private string hitEffectKey = "GreatswordHitImpact";
    [SerializeField] private float hitEffectScale = 2.9f;

    [Header("마무리")]
    [Tooltip("후딜(초) — 클립은 회전 뒤 자세가 멈춰 있어 길면 굳어 보인다(실측 67)")]
    [SerializeField] private float endDelay = 0.15f;

    [Header("애니메이션")]
    [SerializeField] private string animationOverride;

    public override ISkillRuntime CreateRuntime() => new Runtime(this);

    private class Runtime : ISkillRuntime
    {
        private readonly PerimeterGuardBehaviorSO _data;
        private enum Phase { Windup, Follow, End }
        private Phase _phase;
        private float _timer;
        private int _rings;
        private readonly List<GameObject> _hits = new();

        public Runtime(PerimeterGuardBehaviorSO data) => _data = data;

        public void OnEnter(SkillExecutionContext ctx)
        {
            int tier = ctx.SkillTier;
            _rings = (tier >= 3 ? 2 : (tier >= 2 ? 1 : 0))
                     + Mathf.RoundToInt(MeleeAugments.ValueOn(ctx.WeaponData, MeleeAugmentCatalog.GreatPerimeter));   // 주변경계 연장 증강(10-15 · 10-22 회전 → 고리)
            _timer = 0f;
            _phase = Phase.Windup;

            ctx.RotateToMouse();
            ctx.SetMoveScale(0f);
            SkillRangeRing.Circle(ctx.PlayerTransform.position, _data.detectRadius, _data.windup);   // 끄는 동안 맞을 둘레(A10)
            PlayAnimation(ctx);

            ctx.Controller.InputBuffer.TryConsume(Game.Inputs.Command.Light);
        }

        public void OnUpdate(SkillExecutionContext ctx)
        {
            _timer += ctx.DeltaTime;   // 애니메이터와 같은 시계(슬로모 — 10-09)
            switch (_phase)
            {
                case Phase.Windup:
                    if (_timer < _data.windup) return;
                    _timer = 0f;
                    Spin(ctx);
                    _phase = Phase.Follow;
                    break;
                case Phase.Follow:
                    if (_timer < _data.follow) return;
                    _timer = 0f;
                    _phase = Phase.End;
                    break;
                case Phase.End:
                    ctx.EnterRecovery();   // 후딜 — 회피로 끊을 수 있다 · 스탠스 해제(10-08 A2 · A4)
                    if (_timer >= _data.endDelay) ctx.RequestEnd();
                    break;
            }
        }

        public void OnExit(SkillExecutionContext ctx)
        {
            ctx.SetMoveScale(1f);
            _hits.Clear();
        }

        // ── 360° 회전 베기 ──
        private void Spin(SkillExecutionContext ctx)
        {
            Managers.Sound?.PlayEvent(SoundEvent.PlayerSwingHeavy);   // 회전 베기 — 대검 무거운 휘두름(A6)
            var center = ctx.PlayerTransform.position;
            var rot = Quaternion.LookRotation(ctx.PlayerTransform.forward);
            SkillRangeRing.Circle(center, _data.detectRadius);

            float dmg = ctx.CalculateDamage(_data.baseDamage);
            CombatQuery.GetNearbyDamageables(center, _data.detectRadius, ctx.Controller.gameObject, 32, _hits);
            foreach (var go in _hits)
                ctx.DealDamage(go, dmg, _data.knockbackMultiplier, isFinisher: _rings == 0,   // 고리가 없으면 베기가 마무리(A5)
                               hitEffectKey: _data.hitEffectKey, hitEffectScale: _data.hitEffectScale);

            // 검격 3개를 120°씩 — 판정 둘레에 맞춘 크기 · 발밑 흙먼지 고리가 같은 둘레로 퍼진다
            for (int i = 0; i < 3; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 120f, 0f) * ctx.PlayerTransform.forward;
                SpawnEffect(ctx, _data.slashEffectKey, center + dir * 0.3f + Vector3.up * 1f, Quaternion.LookRotation(dir), _data.slashEffectScale, 0.6f);
            }
            SpawnEffect(ctx, _data.shockEffectKey, center + Vector3.up * 0.1f, rot, _data.detectRadius * _data.shockScalePerMeter, 1.5f);

            if (_rings > 0 && ctx.Controller != null)
                RingsAsync(ctx, _data, center, rot, _rings, ctx.Controller.GetCancellationTokenOnDestroy()).Forget();
        }

        // ── 충격 고리 — 베기 자리에서 바깥으로 퍼진다(원판 판정 · 고리마다 따로 맞는다) ──
        private static async UniTaskVoid RingsAsync(SkillExecutionContext ctx, PerimeterGuardBehaviorSO data, Vector3 center, Quaternion rot,
                                                    int count, CancellationToken ct)
        {
            var hits = new List<GameObject>(16);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(i == 0 ? data.ringDelay : data.ringGap), DelayType.DeltaTime, PlayerLoopTiming.Update, ct);
                    if (ctx.Controller == null) return;

                    float radius = Mathf.Min(data.ringRadius + data.ringRadiusStep * i, data.ringRadiusMax);
                    float dmg = ctx.CalculateDamage(data.ringDamage);
                    CombatQuery.GetNearbyDamageables(center, radius, ctx.Controller.gameObject, 32, hits);
                    foreach (var go in hits)
                        ctx.DealDamage(go, dmg, data.knockbackMultiplier, isFinisher: i == count - 1,   // 마지막 고리가 마무리
                                       hitEffectKey: data.hitEffectKey, hitEffectScale: data.hitEffectScale);

                    SkillRangeRing.Circle(center, radius);
                    SpawnEffect(ctx, data.shockEffectKey, center + Vector3.up * 0.1f, rot, radius * data.shockScalePerMeter, 1.5f);
                }
            }
            catch (OperationCanceledException) { }
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

            // 판정(windup초 뒤)에 회전 베기가 가장 빠른 순간(1.27초)이 오게 — 그 앞 끌기 동작이 windup 동안 보인다(SkillAnimSync)
            SkillAnimSync.CrossFadeToContact(ctx.Animator, animName, _data.windup, 0.05f);
        }

        private static async void SpawnEffect(SkillExecutionContext ctx, string key,
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
