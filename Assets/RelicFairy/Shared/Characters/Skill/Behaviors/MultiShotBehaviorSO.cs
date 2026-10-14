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

    public override string StartSoundEvent => SoundEvent.SkillBuff;   // 시작음(10-08 A6)

    private class Runtime : ISkillRuntime
    {
        private readonly MultiShotBehaviorSO _data;
        private float _castLeft;   // 서서 쓴 시전 자세 남은 시간


        public Runtime(MultiShotBehaviorSO data) => _data = data;

        public void OnEnter(SkillExecutionContext ctx)
        {
            ctx.EnterRecovery();   // 자기 강화 — 피해가 없으니 처음부터 회피로 끊을 수 있다(10-08 A2)
            ctx.Controller.RaiseSkillBuffStarted(ctx.Slot, _data.buffDuration);   // HUD 남은 시간 막대(A9)
            int tier = ctx.SkillTier;

            int extraShots = tier switch
            {
                3 => _data.tier3ExtraShots,
                2 => _data.tier2ExtraShots,
                _ => _data.tier1ExtraShots,
            };

            // 추가 발사 버프 적용
            ctx.Controller.ExtraShotCount = extraShots;
            int gen = ++s_buffGen;   // 다시 쓰면 앞 시전의 해제가 새 버프를 끊지 않게(10-09 출시 점검)

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
            ScheduleRemoveBuff(ctx, gen).Forget();

            // 09-21: 예전엔 여기서 바로 끝내 <b>동작이 하나도 안 나왔다</b>(가만히 선 채 버프만 걸렸다).
            // C5(10-08 · 결정 S5): 자기 강화는 움직이며 쓴다 — 이동은 묶지 않는다.
            // 10-09 출시 점검: C5에서 바로 끝내자 끝(대기 진입)이 같은 프레임에 시전 자세를 이동 블렌드로 덮어 다시 동작이 사라졌다.
            // 서 있으면 시전 자세를 castTime 동안 보여 주고(움직이기 시작하면 바로 끝낸다), 움직이는 중이면 이동 동작 그대로 바로 끝낸다.
            ctx.SetMoveScale(1f);
            if (IsMoving(ctx))
            {
                ctx.RequestEnd();
                return;
            }
            PlayCastAnimation(ctx);
            _castLeft = _data.castTime;
        }

        public void OnUpdate(SkillExecutionContext ctx)
        {
            _castLeft -= ctx.DeltaTime;
            if (_castLeft <= 0f || IsMoving(ctx)) ctx.RequestEnd();
        }

        private static bool IsMoving(SkillExecutionContext ctx) => ctx.Controller.MoveDirection.sqrMagnitude > 0.0025f;

        private void PlayCastAnimation(SkillExecutionContext ctx)
        {
            var anim = ctx.Animator;
            if (anim == null) return;
            string state = string.IsNullOrEmpty(_data.animationOverride) ? "QSkill_01" : _data.animationOverride;
            anim.CrossFade(state, 0.05f, 0);
        }
        public void OnExit(SkillExecutionContext ctx) { }

        private static int s_buffGen;

        private async UniTaskVoid ScheduleRemoveBuff(SkillExecutionContext ctx, int gen)
        {
            try
            {
                var token = ctx.Controller.gameObject.GetCancellationTokenOnDestroy();
                await UniTask.Delay((int)(_data.buffDuration * 1000), cancellationToken: token);

                if (ctx.Controller != null && gen == s_buffGen)
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
