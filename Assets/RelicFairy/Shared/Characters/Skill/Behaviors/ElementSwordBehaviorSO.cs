using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 속성 검 — 대검 R(10-26 개편 · 설계 기획/RelicFairy_대검R_속성검버프_설계_20261026.md §7). 사용자 「r을 누르면 처음 검을 뽑는 애니메이션과 함께
/// 속성 검을 휘두르고 재련 내용에 맞게 유지」 — 옛 도약 내려찍기(FinalStrikeBehaviorSO · 10-26 레거시 정리로 지움)를 대신한다.
///   ① 검 뽑기(사무라이 팩 · 아무 무기도 안 쓰는 클립) — 뽑기가 끝나면 칼이 커지고 속성이 붙는다(<see cref="GiantSwordBuff"/> · 칼날 이펙트)
///   ② 그 속성 검으로 크게 한 번 — 대검 3타 휘두르기 클립을 R 상태에 덮어 무겁게(1.5배속) 재생. 칼이 닿는 순간(클립 베기 이벤트) 둘레 판정 = 커진 3타 반경 · 맞은 적 모두 속성 효과
///   ③ 버프 지속 = 6초 + 재련 n × 0.5초 + 「속성 검 연장」(<see cref="GiantSwordBuff.DurationFor"/>)
/// 뽑는 중엔 회피로 끊을 수 있고(끊으면 버프 없음) · 휘두름은 칼이 닿을 때까지 못 끊는다. 휘두른 뒤 후딜은 끊긴다.
/// </summary>
[CreateAssetMenu(menuName = "Game/Skill/ElementSwordBehavior")]
public class ElementSwordBehaviorSO : SkillBehaviorSO
{
    // 2단계 각인(11-03 사용자 「클라렌트만 없으면 이상한데 · 변형할 수 있는 추가 이점」) — 칼리번 환영베기(잔상 추격 · 응축)와 짝
    public const string EngraveSpread   = "element_spread";     // 번지는 속성
    public const string EngraveCondense = "element_condense";   // 모이는 속성
    private const int   MaxSpreadFx     = 5;                    // 번지는 폭발 이펙트 상한(판정은 다 낸다 · 이펙트 절제)

    [Header("검 뽑기")]
    [SerializeField] private string drawState = "QSkill_01";
    [Tooltip("뽑기 클립 앞부분을 건너뛴다(상태 시간 초) — 빈 손으로 손잡이를 잡으러 가는 구간")]
    [SerializeField] private float drawSkip = 0f;
    [Tooltip("뽑기를 보이는 시간(초) — 끝나면 칼이 커지고 속성이 붙은 뒤 휘두른다")]
    [SerializeField] private float drawTime = 0.45f;

    [Header("휘두름")]
    [SerializeField] private string swingState = "QSkill_02";
    [Tooltip("휘두름 시작 → 칼이 닿는 순간(초) — 클립의 베기 이벤트를 이때에 맞춘다")]
    [SerializeField] private float swingContact = 0.3f;
    [SerializeField] private float baseDamage = 400f;
    [SerializeField] private float knockbackMultiplier = 2f;
    [Tooltip("맞은 적 타격 이펙트 — 흰 섬광(색은 속성 점 타격이 낸다 · 10-26 사용자 「타격 이펙트 색상」)")]
    [SerializeField] private string hitEffectKey = "SwordStrikeFlash";
    [SerializeField] private float hitEffectScale = 1.2f;

    [Header("2단계: 각인 택1 — 번지는 속성 / 모이는 속성(11-03)")]
    [Tooltip("휘두름 뒤 각인이 터지기까지(초) — 한 박자 뒤에 번지거나 모인다")]
    [SerializeField] private float engraveDelay = 0.18f;
    [Tooltip("번지는 속성 — 맞은 적 자리에서 다시 터지는 둘레(m · 이펙트 크기 = 판정)")]
    [SerializeField] private float spreadRadius = 3f;
    [Tooltip("번지는 속성 — 피해(공격력 %) · 적마다 한 번")]
    [SerializeField] private float spreadDamage = 120f;
    [Tooltip("모이는 속성 — 가장 단단한 적에게 모아 내려치는 피해(공격력 %)")]
    [SerializeField] private float condenseDamage = 360f;
    [Tooltip("모이는 속성 — 그 적 둘레(m · 이펙트 크기 = 판정)")]
    [SerializeField] private float condenseRadius = 2f;

    [Header("마무리")]
    [Tooltip("휘두른 뒤 자세(초) — 후딜이라 회피 · 공격 · 스킬로 끊긴다")]
    [SerializeField] private float endDelay = 0.35f;

    // 시작음 = 검을 뽑는 쇳소리 · 강화음은 뽑기가 끝나 속성이 붙을 때(11-02 Q21 「대검 R 사운드가 화살 소리」 — 예전엔 시작부터 강화음 하나)
    public override string StartSoundEvent => SoundEvent.SwordPickup;

    public override ISkillRuntime CreateRuntime() => new Runtime(this);

    private class Runtime : ISkillRuntime
    {
        private readonly ElementSwordBehaviorSO _data;
        private enum Phase { Draw, Swing, Engrave, End }
        private Phase _phase;
        private float _timer;
        private int   _skillTier;
        private bool  _condense;
        private RuneElement? _element;
        private readonly List<GameObject> _hits = new();
        private readonly List<GameObject> _buf  = new();
        private readonly HashSet<GameObject> _burstHit = new();

        public Runtime(ElementSwordBehaviorSO data) => _data = data;

        public void OnEnter(SkillExecutionContext ctx)
        {
            _timer = 0f;
            _phase = Phase.Draw;
            _skillTier = ctx.SkillTier;
            _condense  = ctx.WeaponData != null && !ctx.WeaponData.HasEngraving(EngraveSpread) && ctx.WeaponData.HasEngraving(EngraveCondense);
            ctx.RotateToMouse();
            ctx.SetMoveScale(0f);
            if (ctx.Rigidbody != null) ctx.Rigidbody.linearVelocity = Vector3.zero;
            if (ctx.Animator != null) ctx.Animator.CrossFadeInFixedTime(_data.drawState, 0.08f, 0, _data.drawSkip);
            ctx.Controller.InputBuffer.TryConsume(Game.Inputs.Command.Light);
        }

        public void OnUpdate(SkillExecutionContext ctx)
        {
            _timer += ctx.DeltaTime;   // 애니메이터와 같은 시계(슬로모 — 10-09)
            switch (_phase)
            {
                case Phase.Draw:  UpdateDraw(ctx);  break;
                case Phase.Swing:   UpdateSwing(ctx);   break;
                case Phase.Engrave: UpdateEngrave(ctx); break;
                case Phase.End:     UpdateEnd(ctx);     break;
            }
        }

        public void OnExit(SkillExecutionContext ctx)
        {
            ctx.SetMoveScale(1f);
            ctx.LockCancel(false);
            _hits.Clear();
            _buf.Clear();
            _burstHit.Clear();
            if (ctx.Rigidbody != null)
            {
                ctx.Rigidbody.linearVelocity  = Vector3.zero;
                ctx.Rigidbody.angularVelocity = Vector3.zero;
            }
        }

        // ── ① 검 뽑기 — 끝나면 속성 검(칼 ×1.6 · 속성 · 칼날 이펙트) → 휘두름 ──
        private void UpdateDraw(SkillExecutionContext ctx)
        {
            if (_timer < _data.drawTime) return;
            _timer = 0f;
            GiantSwordBuff.Begin(ctx.Controller, GiantSwordBuff.DurationFor(ctx.WeaponData));
            Managers.Sound?.PlayEvent(SoundEvent.SkillBuff);   // 속성이 붙는 순간 강화음(11-02 Q21)
            var element = GiantSwordBuff.ElementOf(ctx.Controller);
            if (element.HasValue) ElementVfxPlayer.PlayActivation(element.Value, ctx.PlayerTransform.position);   // 속성 문장 한 번 — 칼에 속성이 붙었다(11-02 Q21 뽑기 연출)
            ctx.RotateToMouse();
            ctx.LockCancel(true);   // 칼이 닿을 때까지 — 뽑아 든 칼을 휘두르다 끊기면 버프만 남는다
            SkillAnimSync.CrossFadeToEvent(ctx.Animator, _data.swingState, "SpawnSlashEffect", _data.swingContact, 0.08f);
            SkillRangeRing.Circle(ctx.PlayerTransform.position, Radius(ctx), _data.swingContact);
            _phase = Phase.Swing;
        }

        // ── ② 휘두름 — 칼이 닿는 순간 둘레 판정 · 속성 자국 ──
        private void UpdateSwing(SkillExecutionContext ctx)
        {
            if (_timer < _data.swingContact) return;
            _timer = 0f;
            ctx.LockCancel(false);
            Strike(ctx);
            // 2단계 — 맞힌 적이 있으면 한 박자 뒤 각인(번지거나 모인다)
            _phase = _skillTier >= 2 && _hits.Count > 0 && _element.HasValue ? Phase.Engrave : Phase.End;
        }

        // ── ③ 2단계 각인 — 번지는 속성 / 모이는 속성 ──
        private void UpdateEngrave(SkillExecutionContext ctx)
        {
            if (_timer < _data.engraveDelay) return;
            _timer = 0f;
            if (_condense) Condense(ctx, _element.Value);
            else           Spread(ctx, _element.Value);
            _phase = Phase.End;
        }

        /// <summary>번지는 속성 — 맞은 적마다 그 자리에서 속성이 한 번 더 터진다(둘레 · 적마다 한 번 · 곁의 적도 속성 효과).</summary>
        private void Spread(SkillExecutionContext ctx, RuneElement element)
        {
            float dmg = ctx.CalculateDamage(_data.spreadDamage);
            _burstHit.Clear();
            int bursts = 0;
            for (int i = 0; i < _hits.Count; i++)
            {
                var src = _hits[i];
                if (src == null) continue;
                Vector3 at = src.transform.position;
                if (bursts++ < MaxSpreadFx) ElementVfxPlayer.PlayBurst(element, at, _data.spreadRadius);   // 그림 = 판정 반경
                CombatQuery.GetNearbyDamageables(at, _data.spreadRadius, ctx.Controller.gameObject, 32, _buf);
                foreach (var go in _buf)
                {
                    if (!_burstHit.Add(go)) continue;
                    if (ctx.DealDamage(go, dmg, _data.knockbackMultiplier * 0.5f, false, skipHitVfx: true) > 0f)
                        GiantSwordBuff.HitElement(ctx.Controller, go);   // 곁의 적도 속성 효과(맞은 적은 0.45초 틱 간격이라 두 번 안 걸린다)
                }
            }
            Debug.Log($"[ElementSword] 번지는 속성 — 터짐 {bursts} · 맞은 적 {_burstHit.Count}");
        }

        /// <summary>모이는 속성 — 맞은 적 중 체력이 가장 많은 적에게 모아 한 번 더 내려친다(둘레 함께).</summary>
        private void Condense(SkillExecutionContext ctx, RuneElement element)
        {
            GameObject target = null;
            int bestHp = -1;
            foreach (var go in _hits)
            {
                if (go == null || !go.activeInHierarchy) continue;
                int hp = go.TryGetComponent<MonsterBase>(out var mb) ? mb.CurrentHp : 0;
                if (target == null || hp > bestHp) { target = go; bestHp = hp; }
            }
            if (target == null) return;
            Vector3 at = target.transform.position;
            float dmg = ctx.CalculateDamage(_data.condenseDamage);
            ElementVfxPlayer.PlayBurst(element, at, _data.condenseRadius);   // 그림 = 판정 반경
            CombatQuery.GetNearbyDamageables(at, _data.condenseRadius, ctx.Controller.gameObject, 16, _buf);
            int n = 0;
            foreach (var go in _buf)
                if (ctx.DealDamage(go, dmg, _data.knockbackMultiplier, isFinisher: true, hitEffectKey: _data.hitEffectKey, hitEffectScale: _data.hitEffectScale) > 0f) n++;
            HitFeelService.CameraShake(HitFeelService.DealtShakeFinisher, 0.2f);
            Debug.Log($"[ElementSword] 모이는 속성 — {target.name} · 맞은 적 {n}");
        }

        private void UpdateEnd(SkillExecutionContext ctx)
        {
            ctx.EnterRecovery();   // 후딜 — 회피로 끊을 수 있다 · 스탠스 해제(10-08 A2 · A4)
            if (_timer >= _data.endDelay)
                ctx.RequestEnd();
        }

        private void Strike(SkillExecutionContext ctx)
        {
            var center = ctx.PlayerTransform.position;
            float radius = Radius(ctx);
            float dmg = ctx.CalculateDamage(_data.baseDamage);
            CombatQuery.GetNearbyDamageables(center, radius, ctx.Controller.gameObject, 32, _hits);
            var element = GiantSwordBuff.ElementOf(ctx.Controller);
            _element = element;
            if (element.HasValue) ElementVfxPlayer.PlayBurst(element.Value, center, radius);   // 판정 이펙트 = 판정 반경 그대로(11-02 Q21 「뽑을 때 판정 이펙트」)
            foreach (var go in _hits)
            {
                float dealt = ctx.DealDamage(go, dmg, _data.knockbackMultiplier, isFinisher: true, hitEffectKey: _data.hitEffectKey, hitEffectScale: _data.hitEffectScale);
                if (dealt > 0f) GiantSwordBuff.HitElement(ctx.Controller, go);   // 맞은 적 모두 속성 효과 · 속성 점 타격(장판은 E만 · 10-26 사용자 · 무적이면 안 건다)
            }
            if (_hits.Count == 0) HitFeelService.CameraShake(HitFeelService.DealtShakeFinisher, 0.2f);   // 맞혔으면 막타 흔들림이 이미 있다(「살짝」 상한)
        }

        /// <summary>커진 3타 반경(3.08 m × 평타 배율 — 속성 검 ×1.3 · 「거대한 칼날」 포함).</summary>
        private static float Radius(SkillExecutionContext ctx) => GiantSwordBuff.SwingRadiusFor(ctx.Controller, 2);
    }
}
