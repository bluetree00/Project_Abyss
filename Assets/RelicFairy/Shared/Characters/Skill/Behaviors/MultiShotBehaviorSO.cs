using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 더 빨리쏘기 — 석궁 Q스킬.
/// 5초간 기본공격에 추가 공격이 발생한다.
///   1단계: 추가 1회
///   2단계: 추가 2회
///   3단계: 추가 3회
/// </summary>
[CreateAssetMenu(menuName = "Game/Skill/MultiShotBehavior")]
public class MultiShotBehaviorSO : SkillBehaviorSO
{
    [Header("버프 설정")]
    [SerializeField] private float buffDuration = 5f;
    [SerializeField] private int tier1ExtraShots = 1;
    [SerializeField] private int tier2ExtraShots = 2;
    [SerializeField] private int tier3ExtraShots = 3;

    [Header("이펙트 (단계별)")]
    [SerializeField] private string tier1EffectKey = "GoldDot";
    [SerializeField] private string tier2EffectKey = "Buff5";
    [SerializeField] private string tier3EffectKey = "MagicSparks";
    [SerializeField] private float buffEffectScale = 0.5f;

    [Header("애니메이션")]
    [SerializeField] private string animationOverride;
    [Tooltip("시전 자세를 보여 줄 시간(초). 즉시 버프라도 이 동안은 스킬 상태를 유지해 동작이 보이게 한다.")]
    [SerializeField] private float castTime = 0.35f;

    public override ISkillRuntime CreateRuntime() => new Runtime(this);

    private class Runtime : ISkillRuntime
    {
        private readonly MultiShotBehaviorSO _data;

        private float _castEndTime;

        public Runtime(MultiShotBehaviorSO data) => _data = data;

        public void OnEnter(SkillExecutionContext ctx)
        {
            int tier = ctx.SkillTier;

            int extraShots = tier switch
            {
                3 => _data.tier3ExtraShots,
                2 => _data.tier2ExtraShots,
                _ => _data.tier1ExtraShots,
            };

            // 추가 발사 버프 적용
            ctx.Controller.ExtraShotCount = extraShots;

            // 단계별 버프 이펙트
            string effectKey = tier switch
            {
                3 => _data.tier3EffectKey,
                2 => _data.tier2EffectKey,
                _ => _data.tier1EffectKey,
            };
            SpawnBuffEffect(ctx, effectKey);

            // 2단계 이상이면 기본 이펙트도 추가
            if (tier >= 2)
                SpawnBuffEffect(ctx, _data.tier1EffectKey);

            // 비동기로 버프 해제 예약
            ScheduleRemoveBuff(ctx).Forget();

            // 09-21: 예전엔 여기서 바로 끝내 <b>동작이 하나도 안 나왔다</b>(가만히 선 채 버프만 걸렸다).
            // 짧은 시전 자세를 보여 준 뒤 끝낸다 — 클립은 무기 애니 세트가 석궁용으로 덮어쓴다.
            PlayCastAnimation(ctx);
            _castEndTime = Time.time + Mathf.Max(0f, _data.castTime);
            if (_data.castTime <= 0f) ctx.RequestEnd();
        }

        public void OnUpdate(SkillExecutionContext ctx)
        {
            if (_castEndTime > 0f && Time.time >= _castEndTime)
            {
                _castEndTime = 0f;
                ctx.RequestEnd();
            }
        }

        private void PlayCastAnimation(SkillExecutionContext ctx)
        {
            var anim = ctx.Animator;
            if (anim == null) return;
            string state = string.IsNullOrEmpty(_data.animationOverride) ? "QSkill_01" : _data.animationOverride;
            anim.CrossFade(state, 0.05f, 0);
        }
        public void OnExit(SkillExecutionContext ctx) { }

        private async UniTaskVoid ScheduleRemoveBuff(SkillExecutionContext ctx)
        {
            try
            {
                var token = ctx.Controller.gameObject.GetCancellationTokenOnDestroy();
                await UniTask.Delay((int)(_data.buffDuration * 1000), cancellationToken: token);

                if (ctx.Controller != null)
                    ctx.Controller.ExtraShotCount = 0;
            }
            catch (System.OperationCanceledException) { }
        }

        private static async void SpawnBuffEffect(SkillExecutionContext ctx, string key)
        {
            if (string.IsNullOrEmpty(key) || ctx.Controller == null) return;
            var obj = await Managers.ObjectPooler.SpawnAsync(
                key, ObjectPoolerManager.PoolType.Effect,
                ctx.PlayerTransform.position + Vector3.up * 0.5f,
                ctx.PlayerTransform.rotation);
            if (obj == null) return;
            obj.transform.localScale = Vector3.one * 0.5f;
            obj.transform.SetParent(ctx.PlayerTransform, true);

            // 루프 강제 OFF — 한 사이클만 재생
            foreach (var ps in obj.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = false;
                ps.Clear(true);
                ps.Play(true);
            }

            if (obj.TryGetComponent<EffectBehaviour>(out var eb))
                eb.Initialize(eb.behaviorSO, ctx.PlayerTransform, 5f);
            else
                DespawnAfter(obj, 5f);
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
