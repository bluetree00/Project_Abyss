using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using RelicFairy.Monster;

/// <summary>
/// 환영베기 — 카타나 R스킬. 낮게 웅크렸다 한 번에 뽑아 베고, 칼을 털면 맞은 적이 X자로 갈라진다(10-22 개편 — 사용자
/// 「카타나 R — 애매한 애니 · 리소스 · 타격 방식 · 불편한 후딜 → 컨셉에 맞게 아예 개편」).
///   1단계: 발도(앞 부채꼴) → 칼을 털고 나면(0.53초 뒤) 맞은 적마다 늦게 갈라짐(X자 · 마무리)
///   2단계: + 이어 갈라짐(각인 택1 — 아래)
///   3단계: + 늦게 갈라짐이 둘레로 터진다(반경 3 m)
/// 예전엔 제자리 5연타(앞 1 m 중심 반경 2 m · 넷째와 다섯째 사이 0.56초 빈틈)에 이펙트 넷이 섞여 있었다.
/// 클립은 낮게 웅크린 발도(SPAttack06 — 성광베기가 쓰던 것)이고, 성광베기는 앞으로 뛰어들며 베기(Dodge_Attack_F)로 옮겼다.
/// 이 클립의 휘두름은 발도 하나뿐이다(실측 67: 44.7 m/s · 뒤 0.22초 움직임은 7 m/s 칼 털기) — 판정도 하나.
///
/// 각인(09-25 시범, 기획 스킬구성_재련소연결 A안): 2단계에 오를 때 둘 중 하나를 고르면 「이어 갈라짐」이 바뀐다.
///   잔상 추격 — 갈라짐이 <b>8 m 안 가까운 적부터 하나씩 번진다</b>(흩어진 적). 고르지 않았으면(선택 화면 전 · 옛 세이브) 이쪽.
///   응축     — 갈라짐을 <b>가장 체력이 많은 적 하나에 모아</b> 크게 가른다(단단한 한 놈).
/// </summary>
[CreateAssetMenu(menuName = "Game/Skill/PhantomDanceBehavior")]
public class PhantomDanceBehaviorSO : SkillBehaviorSO
{
    [Header("발도 (전 단계) — 클립의 휘두름에 맞춘다")]
    [Tooltip("발도 순간(스킬 시작부터 초). 첫 값에 클립의 첫 발도가 닿게 재생한다(SkillAnimSync)")]
    [SerializeField] private float[] drawHitTimes = { 0.35f };
    [SerializeField] private float drawRange = 4.5f;
    [SerializeField] private float drawAngle = 120f;
    [SerializeField] private float drawDamage = 600f;
    [SerializeField] private float knockbackMultiplier = 0.1f;

    [Header("늦게 갈라짐 — 발도에 맞은 적마다 X자")]
    [Tooltip("마지막 발도 뒤(초) — 클립의 칼 털기가 끝나는 때")]
    [SerializeField] private float cutDelay = 0.53f;
    [SerializeField] private float cutDamage = 410f;

    [Header("2단계: 이어 갈라짐 (각인 택1)")]
    [SerializeField] private int slashProjectileCount = 3;
    [SerializeField] private float slashProjectileDelay = 0.1f;
    [SerializeField] private float slashProjectileDamage = 100f;
    [Tooltip("잔상 추격: 번질 적을 찾는 반경(m)")]
    [SerializeField] private float pursuitRadius = 8f;
    [Tooltip("잔상 추격: 적 자리에서 가르는 반경(m)")]
    [SerializeField] private float pursuitHitRadius = 1.2f;
    [Tooltip("응축: 이어 갈라짐 N번 피해 합 × 이 배율을 한 번에")]
    [SerializeField] private float condenseDamageMult = 1.2f;
    [SerializeField] private float condenseRadius = 2.4f;
    [SerializeField] private float condenseEffectScaleMult = 2f;

    [Header("3단계: 갈라짐 폭발")]
    [SerializeField] private float slashExplodeRadius = 3f;
    [SerializeField] private float slashExplodeDamage = 250f;

    [Header("마무리")]
    [Tooltip("마지막 피해 뒤 칼을 거두는 동안(초) — 후딜이라 회피 · 공격 · 스킬로 끊긴다")]
    [SerializeField] private float endDelay = 0.2f;

    [Header("이펙트")]
    [SerializeField] private string drawEffectKey = "SlashAttack";
    [SerializeField] private float drawEffectScale = 1f;
    [SerializeField] private string cutEffectKey = "KatanaDelayedCut";
    [SerializeField] private float cutEffectScale = 0.15f;
    [Tooltip("3단계 — 갈라짐이 터지는 크기(폭발 반경에 맞춘다)")]
    [SerializeField] private float explodeEffectScale = 0.3f;
    [Tooltip("맞은 적 타격 이펙트 — 카타나 계열(붉은 피 + 섬광, 평타와 같다). 비우면 공용 노란 베기")]
    [SerializeField] private string hitEffectKey = "SwordHitImpact";
    [SerializeField] private float hitEffectScale = 0.29f;

    [Header("애니메이션")]
    [Tooltip("낮게 웅크린 발도 클립을 무는 상태(카타나는 QSkill_Iasen → Katana_ESkill = SPAttack06)")]
    [SerializeField] private string drawState = "QSkill_02";

    public const string EngravePursuit  = "phantom_pursuit";    // 잔상 추격
    public const string EngraveCondense = "phantom_condense";   // 응축

    public override ISkillRuntime CreateRuntime() => new Runtime(this);

    private class Runtime : ISkillRuntime
    {
        private readonly PhantomDanceBehaviorSO _data;
        private enum Phase { Draw, Cut, Chain, End }
        private Phase _phase;
        private float _timer;        // 단계 경과
        private float _drawTime;     // 발도 단계 경과(발도 순간 기준)
        private int _drawIndex;
        private int _chainIndex;
        private int _skillTier;
        private int _slashCount;     // 이어 갈라짐 수 — 환영 베기 강화 증강(10-15)이 늘린다
        private bool _condense;
        private readonly List<GameObject> _cutTargets = new();      // 발도에 맞은 적(늦게 갈라질 자리)
        private readonly List<GameObject> _chainTargets = new();    // 잔상 추격 — 가까운 순
        private readonly List<GameObject> _buf = new();
        private readonly HashSet<GameObject> _burstHit = new();

        public Runtime(PhantomDanceBehaviorSO data) => _data = data;

        public void OnEnter(SkillExecutionContext ctx)
        {
            ctx.RotateToMouse();
            ctx.SetMoveScale(0f);

            _skillTier = ctx.SkillTier;
            _slashCount = _data.slashProjectileCount + Mathf.RoundToInt(MeleeAugments.Value2On(ctx.WeaponData, MeleeAugmentCatalog.KatanaPhantom));
            _condense = ctx.WeaponData != null && !ctx.WeaponData.HasEngraving(EngravePursuit) && ctx.WeaponData.HasEngraving(EngraveCondense);

            _cutTargets.Clear();
            _chainTargets.Clear();
            _burstHit.Clear();
            _drawIndex = 0;
            _chainIndex = 0;
            _timer = 0f;
            _drawTime = 0f;
            _phase = Phase.Draw;

            float firstDraw = _data.drawHitTimes != null && _data.drawHitTimes.Length > 0 ? _data.drawHitTimes[0] : 0.35f;
            SkillAnimSync.CrossFadeToContact(ctx.Animator, _data.drawState, firstDraw, 0.05f);   // 첫 발도에 클립의 베기가 닿게 — 앞 웅크림이 보인다

            // 스킬 진입 시 잔여 공격 입력 제거
            ctx.Controller.InputBuffer.TryConsume(Game.Inputs.Command.Light);
        }

        public void OnUpdate(SkillExecutionContext ctx)
        {
            _timer += ctx.DeltaTime;   // 애니메이터와 같은 시계(슬로모 — 10-09)
            switch (_phase)
            {
                case Phase.Draw:  UpdateDraw(ctx);  break;
                case Phase.Cut:   UpdateCut(ctx);   break;
                case Phase.Chain: UpdateChain(ctx); break;
                case Phase.End:   UpdateEnd(ctx);   break;
            }
        }

        public void OnExit(SkillExecutionContext ctx)
        {
            ctx.SetMoveScale(1f);
            _cutTargets.Clear();
            _chainTargets.Clear();
            _burstHit.Clear();
        }

        // ── 발도 — 앞 부채꼴, 클립의 휘두름 순간마다 ──
        private void UpdateDraw(SkillExecutionContext ctx)
        {
            _drawTime += ctx.DeltaTime;
            int count = _data.drawHitTimes != null ? _data.drawHitTimes.Length : 0;
            if (_drawIndex < count && _drawTime < _data.drawHitTimes[_drawIndex]) return;

            if (_drawIndex < count)
            {
                Draw(ctx, _drawIndex);
                _drawIndex++;
                if (_drawIndex < count) return;
            }

            _timer = 0f;
            _phase = _cutTargets.Count > 0 ? Phase.Cut : Phase.End;   // 헛치면 갈라질 것이 없다 — 바로 후딜
        }

        private void Draw(SkillExecutionContext ctx, int index)
        {
            var origin = ctx.PlayerTransform.position;
            var fwd = ctx.PlayerTransform.forward;
            SkillRangeRing.Fan(origin, fwd, _data.drawRange, _data.drawAngle);
            Managers.Sound?.PlayEvent(SoundEvent.PlayerSwing);   // 발도마다 베는 소리(A6)

            float dmg = ctx.CalculateDamage(_data.drawDamage);
            CombatQuery.GetDamageablesInCone(origin, fwd, _data.drawRange, _data.drawAngle * 0.5f, ctx.Controller.gameObject, 32, _buf, showGuide: false);
            foreach (var go in _buf)
            {
                ctx.DealDamage(go, dmg, _data.knockbackMultiplier, hitEffectKey: _data.hitEffectKey, hitEffectScale: _data.hitEffectScale);
                if (!_cutTargets.Contains(go)) _cutTargets.Add(go);
            }

            // 베기 — 부채꼴 판정에 맞춘 가로 베기 하나. 둘째 발도는 반대로 돌아온다
            var rot = Quaternion.LookRotation(fwd) * Quaternion.Euler(0f, 0f, index % 2 == 0 ? 8f : 172f);
            SpawnEffect(ctx, _data.drawEffectKey, origin + fwd * (_data.drawRange * 0.4f) + Vector3.up * 1f, rot, _data.drawEffectScale, 0.5f, ownedFx: false);
        }

        // ── 늦게 갈라짐 — 맞은 적마다 X자(3단계는 둘레로 터진다) ──
        private void UpdateCut(SkillExecutionContext ctx)
        {
            if (_timer < _data.cutDelay) return;
            _timer = 0f;

            bool finisher = _skillTier < 2;   // 2단계부터는 뒤 이어 갈라짐이 마무리(A5)
            bool burst = _skillTier >= 3;
            float dmg = ctx.CalculateDamage(_data.cutDamage);
            float burstDmg = ctx.CalculateDamage(_data.slashExplodeDamage);
            Managers.Sound?.PlayEvent(SoundEvent.PlayerSwing);
            foreach (var go in _cutTargets)
            {
                if (go == null || !go.activeInHierarchy) continue;
                var at = go.transform.position;
                ctx.DealDamage(go, dmg, _data.knockbackMultiplier * 2f, finisher, hitEffectKey: _data.hitEffectKey, hitEffectScale: _data.hitEffectScale);
                SpawnCross(ctx, at + Vector3.up * 1f, burst ? _data.explodeEffectScale : _data.cutEffectScale);
                if (!burst) continue;

                CombatQuery.GetNearbyDamageables(at, _data.slashExplodeRadius, ctx.Controller.gameObject, 32, _buf);
                foreach (var b in _buf)
                    if (_burstHit.Add(b)) ctx.DealDamage(b, burstDmg, _data.knockbackMultiplier * 2f, finisher, skipHitVfx: true);   // 터짐 — 타격 이펙트는 X자가 맡는다
            }

            if (_skillTier < 2) { _phase = Phase.End; return; }
            if (!_condense)
            {
                // 잔상 추격 — 8 m 안 가까운 적부터(적이 갈라짐 수보다 적으면 가까운 쪽부터 다시)
                CombatQuery.GetNearbyDamageables(ctx.PlayerTransform.position, _data.pursuitRadius, ctx.Controller.gameObject, 16, _chainTargets);
                if (_chainTargets.Count == 0) { _phase = Phase.End; return; }
            }
            _chainIndex = 0;
            _phase = Phase.Chain;
        }

        // ── 2단계: 이어 갈라짐 ──
        private void UpdateChain(SkillExecutionContext ctx)
        {
            if (_condense) { UpdateCondense(ctx); return; }
            if (_timer < _data.slashProjectileDelay) return;
            _timer = 0f;

            var target = _chainTargets[_chainIndex % _chainTargets.Count];
            bool last = _chainIndex >= _slashCount - 1;
            if (target != null && target.activeInHierarchy)
            {
                var at = target.transform.position;
                float dmg = ctx.CalculateDamage(_data.slashProjectileDamage);
                CombatQuery.GetNearbyDamageables(at, _data.pursuitHitRadius, ctx.Controller.gameObject, 16, _buf);
                foreach (var go in _buf)
                    ctx.DealDamage(go, dmg, _data.knockbackMultiplier, last, hitEffectKey: _data.hitEffectKey, hitEffectScale: _data.hitEffectScale);
                var rot = Quaternion.LookRotation(ctx.PlayerTransform.forward) * Quaternion.Euler(0f, 0f, _chainIndex % 2 == 0 ? 45f : -45f);
                SpawnEffect(ctx, _data.cutEffectKey, at + Vector3.up * 1f, rot, _data.cutEffectScale, 0.6f, ownedFx: true);
            }

            _chainIndex++;
            if (_chainIndex >= _slashCount) { _timer = 0f; _phase = Phase.End; }
        }

        // 응축 — 갈라짐을 가장 체력이 많은 적 하나에 모아 크게(둘레도 함께)
        private void UpdateCondense(SkillExecutionContext ctx)
        {
            if (_timer < _data.slashProjectileDelay * 2f) return;   // 모으는 한 박자
            _timer = 0f;

            GameObject target = null;
            int bestHp = -1;
            foreach (var go in _cutTargets)
            {
                if (go == null || !go.activeInHierarchy) continue;
                int hp = go.TryGetComponent<MonsterBase>(out var mb) ? mb.CurrentHp : 0;
                if (target == null || hp > bestHp) { target = go; bestHp = hp; }
            }
            if (target != null)
            {
                var at = target.transform.position;
                float dmg = ctx.CalculateDamage(_data.slashProjectileDamage) * _slashCount * _data.condenseDamageMult;
                CombatQuery.GetNearbyDamageables(at, _data.condenseRadius, ctx.Controller.gameObject, 16, _buf);
                foreach (var go in _buf)
                    ctx.DealDamage(go, dmg, _data.knockbackMultiplier * 2f, isFinisher: true, hitEffectKey: _data.hitEffectKey, hitEffectScale: _data.hitEffectScale);
                SpawnCross(ctx, at + Vector3.up * 1f, _data.cutEffectScale * _data.condenseEffectScaleMult);
            }
            _phase = Phase.End;
        }

        private void UpdateEnd(SkillExecutionContext ctx)
        {
            ctx.EnterRecovery();   // 후딜 — 회피로 끊을 수 있다 · 스탠스 해제(10-08 A2 · A4)
            if (_timer >= _data.endDelay)
                ctx.RequestEnd();
        }

        // X자 — 같은 베기 둘을 엇갈려
        private void SpawnCross(SkillExecutionContext ctx, Vector3 at, float scale)
        {
            var face = Quaternion.LookRotation(ctx.PlayerTransform.forward);
            SpawnEffect(ctx, _data.cutEffectKey, at, face * Quaternion.Euler(0f, 0f, 45f), scale, 0.6f, ownedFx: true);
            SpawnEffect(ctx, _data.cutEffectKey, at, face * Quaternion.Euler(0f, 0f, -45f), scale, 0.6f, ownedFx: true);
        }

        /// <param name="ownedFx">이 스킬만 쓰는 이펙트(늦게 갈라짐) — 풀에서 다시 꺼내면 처음부터 다시 튼다.
        /// 여러 곳이 같이 쓰는 이펙트(평타 베기)는 건드리지 않는다.</param>
        private static async void SpawnEffect(SkillExecutionContext ctx, string key, Vector3 pos, Quaternion rot, float scale, float lifetime, bool ownedFx)
        {
            if (string.IsNullOrEmpty(key)) return;
            var obj = await Managers.ObjectPooler.SpawnAsync(
                key, ObjectPoolerManager.PoolType.Effect, pos, rot);
            if (obj == null) return;
            obj.transform.localScale = Vector3.one * scale;
            if (ownedFx)
            {
                foreach (var ps in obj.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ps.Clear(true);
                    ps.Play(true);
                }
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
