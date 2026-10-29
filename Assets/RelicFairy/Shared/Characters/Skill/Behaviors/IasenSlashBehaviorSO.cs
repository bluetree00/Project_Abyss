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

    [Header("2단계 (스킬 단계 ≥ 2 — SkillTierResolver)")]
    [Tooltip("2단계에서 추가되는 연참 수")]
    public int tier2ExtraSlashCount = 2;
    [Tooltip("2단계 마무리 이펙트(대시 종점, 연참이 끝난 뒤). 비우면 없음")]
    public string tier2FinishEffectKey = "";
    public float tier2FinishEffectScale = 1f;
    [Tooltip("2단계 마무리가 베는 반경(m) — 그림이 보이는 크기와 같게(10-22 B3: 예전엔 그림만 있고 피해가 없었다). 피해 = 연참 한 타")]
    public float tier2FinishRadius = 2f;

    [Header("마무리")]
    public float endDelay = 0.3f;

    [Header("이펙트")]
    public string dashEffectKey = "WindBlast";
    public string dashTrailKey = "DashTrail";
    public string slashEffectKey = "MultiSlash";
    public string hitEffectKey = "SwordHitImpact";
    public float hitEffectScale = 0.3f;

    [Header("트레일")]
    [Tooltip("대시 중 플레이어에 부착할 트레일 이펙트 Addressable 키 (비어있으면 사용 안 함)")]
    public string playerTrailKey;
    public float playerTrailScale = 1f;

    [Header("애니메이션")]
    [Tooltip("비어있으면 WeaponAnimationSetSO에서 QSkill 매핑 사용")]
    public string animationOverride;

    // 10-08 C1 — 발도 클립(QSkill_Iasen 2.6초 · 닿는 순간 1.63초 · 상태 속도 1.5)은 닿은 뒤 약 0.65초가 남는다.
    // 연참 7개(2단계)를 0.15초 간격으로 베면 모션이 끝난 뒤에도 벴다 → 첫 베기 뒤 나머지를 이 창 안에 몰아 넣는다.
    private const float SlashWindow      = 0.62f;
    private const float Tier3PathRadius  = 3f;     // 3단계 마지막 일섬 — 지나온 길 폭(감지 반경 2 m보다 넓게)
    private const float Tier3PathDamageX = 3f;     // 연참 한 타의 몇 배

    public override ISkillRuntime CreateRuntime() => new Runtime(this);

    // ── Runtime ──────────────────────────────────────────────────
    private class Runtime : ISkillRuntime
    {
        private readonly IasenSlashBehaviorSO _data;
        private enum Phase { Dash, Slash, End }
        private Phase _phase;
        private float _timer;
        private int _slashIndex;
        private int _slashTotal;
        private float _slashInterval;   // 둘째 베기부터의 간격(C1 — 모션 안에)
        private int _skillTier;
        private Vector3 _dashStart, _dashEnd;
        private readonly List<IDamageable> _hitTargets = new();
        private readonly HashSet<GameObject> _hitObjects = new();
        private GameObject _playerTrailInstance;

        public Runtime(IasenSlashBehaviorSO data) => _data = data;

        public void OnEnter(SkillExecutionContext ctx)
        {
            PlayAnimation(ctx);

            // 대시 직전, 캐릭터를 마우스 방향으로 정면 고정한다.
            // (현재 forward를 그대로 쓰면 정면이 마우스를 향하지 않은 상태에서 엉뚱하게 전진함)
            Quaternion aimRot = ctx.Controller.ComputeSkillAimRotation();   // 장비 스킬 조준 보조(A1)
            ctx.Controller.RequestFacing(aimRot);

            var dir = aimRot * Vector3.forward;
            _dashStart = ctx.PlayerTransform.position;
            _dashEnd = ctx.DashEndBeforeWall(_dashStart, dir, _data.dashDistance);   // 벽 앞에서 멈춘다(10-14 — 끝점 Lerp가 벽을 뚫었다)
            MeleeAugments.ReportSkillDash(ctx.WeaponData, ctx.Slot, _dashStart, _dashEnd, _data.dashDuration,
                                          ctx.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee));   // 무형일섬 잔상 증강(10-15)

            if (ctx.Rigidbody != null)
            {
                ctx.Rigidbody.linearVelocity  = Vector3.zero;
                ctx.Rigidbody.angularVelocity = Vector3.zero; // 잔류 회전 방지
            }

            _hitTargets.Clear();
            _hitObjects.Clear();
            _slashIndex = 0;
            _skillTier  = ctx.SkillTier;
            _slashTotal = _data.slashCount + (_skillTier >= 2 ? Mathf.Max(0, _data.tier2ExtraSlashCount) : 0);
            _timer = 0f;
            _phase = Phase.Dash;
            _slashInterval = Mathf.Min(_data.slashDelay, SlashWindow / Mathf.Max(1, _slashTotal - 1));
            ctx.Controller.SetInvincible(_data.dashDuration);   // 돌진 중 무적(C1) — 꿰뚫고 지나가는 동안 맞지 않는다

            // 대시 시작 이펙트
            SpawnEffect(ctx, _data.dashTrailKey, ctx.PlayerTransform.position + Vector3.up * 0.5f, 0.8f);
            SpawnPlayerTrail(ctx);
        }

        public void OnUpdate(SkillExecutionContext ctx)
        {
            _timer += ctx.DeltaTime;   // 애니메이터와 같은 시계(슬로모 — 10-09)
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

            // 대시 중 벽 충돌 등으로 누적된 angular/linear velocity 제거
            if (ctx.Rigidbody != null)
            {
                ctx.Rigidbody.linearVelocity  = Vector3.zero;
                ctx.Rigidbody.angularVelocity = Vector3.zero;
            }
        }

        // ── Dash ──
        private void UpdateDash(SkillExecutionContext ctx)
        {
            float t = Mathf.Clamp01(_timer / _data.dashDuration);
            Vector3 targetPos = Vector3.Lerp(_dashStart, _dashEnd, t);

            // Rigidbody.MovePosition 으로 적용 — 벽 등 정적 콜라이더와 정상 충돌 (transform 직접 대입 시 관통 + 잔류 angularVelocity)
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
                // A3(10-08): 스쳐 간 적이 없으면 연참(5 × 0.15초)을 접고 바로 끝 — 예전엔 아무도 없는데 0.75초 서 있었다.
                // 3단계 길 가르기는 길 폭(3 m)이 감지(2 m)보다 넓어 헛스윙이어도 벤다(C1)
                _phase = _hitTargets.Count > 0 ? Phase.Slash : Phase.End;
                if (_hitTargets.Count == 0 && _skillTier >= 3) CutWholePath(ctx);

                // 대시 종료 시점에 잔류 회전/속도 다시 제거 (벽 충돌 잔여물 정리)
                if (ctx.Rigidbody != null)
                {
                    ctx.Rigidbody.linearVelocity  = Vector3.zero;
                    ctx.Rigidbody.angularVelocity = Vector3.zero;
                }

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
            float wait = _slashIndex == 0 ? _data.slashDelay : _slashInterval;
            if (_timer >= wait)
            {
                _timer -= wait;   // 넘친 시간을 다음 간격에 넘긴다 — 0으로 지우면 연참마다 한 프레임씩 밀렸다(C1 실측 마지막 1.02초)
                ApplySlashDamage(ctx);
                Managers.Sound?.PlayEvent(SoundEvent.PlayerSwing);   // 연참마다 베는 소리(A6)
                if (_skillTier >= 3 && _slashIndex == _slashTotal - 1) CutWholePath(ctx);   // 3단계 마지막 일섬(C1)
                _slashIndex++;

                if (_slashIndex >= _slashTotal)
                {
                    _timer = 0f;
                    _phase = Phase.End;

                    // 2단계 마무리 — 연참이 끝난 자리(대시 종점)에서 한 번 벤다. 3단계는 길 전체 베기(CutWholePath)가 마무리라 끝 그림을 따로 안 띄운다
                    if (_skillTier == 2 && !string.IsNullOrEmpty(_data.tier2FinishEffectKey))
                        FinishCut(ctx);
                }
            }
        }

        // ── End ──
        private void UpdateEnd(SkillExecutionContext ctx)
        {
            ctx.EnterRecovery();   // 후딜 — 회피로 끊을 수 있다 · 스탠스 해제(10-08 A2 · A4)
            if (_timer >= _data.endDelay)
                ctx.RequestEnd();
        }

        // ── Damage ──
        /// <summary>
        /// 3단계 마지막 일섬(C1 · 설명 「공간을 가른다」) — 지나온 길 전체(폭 <see cref="Tier3PathRadius"/> m)의 적을 한 번에 벤다. 막타.
        /// 예전엔 3단계가 2단계와 똑같았다(설명만 「공간을 가른다」).
        /// </summary>
        private void CutWholePath(SkillExecutionContext ctx)
        {
            Vector3 a = _dashStart + Vector3.up * 0.8f, b = _dashEnd + Vector3.up * 0.8f;
            // 연참 한 타(기본 + 공격력)의 3배 — 예전엔 기본값만 3배(15 + 공격력)라 문구 「연참 3배」에 못 미쳤다(10-08 강화 점검)
            float dmg = ctx.CalculateDamage(_data.baseDamagePerSlash) * Tier3PathDamageX;
            var seen = new HashSet<GameObject>();
            foreach (var col in Physics.OverlapCapsule(a, b, Tier3PathRadius))
            {
                if (col.gameObject == ctx.Controller.gameObject || !seen.Add(col.gameObject)) continue;
                if (col.TryGetComponent<IDamageable>(out var d))
                    ctx.DealDamage(d, dmg, _data.knockbackMultiplier * 2f, isFinisher: true);
            }
            Vector3 mid = (_dashStart + _dashEnd) * 0.5f + Vector3.up * 1f;
            SpawnEffect(ctx, string.IsNullOrEmpty(_data.tier2FinishEffectKey) ? _data.slashEffectKey : _data.tier2FinishEffectKey,
                        mid, 1.5f, _data.tier2FinishEffectScale * 1.5f);   // 길 폭 3 m에 맞춤(10-13 ⑥ 실측: 마무리 ×2 → 14.5 m)
        }

        /// <summary>2단계 마무리(10-22 B3) — 대시 종점 둘레(그림 크기)를 연참 한 타로 한 번 벤다. 예전엔 「공간 베기」 그림만 나가 피해가 없었다.</summary>
        private void FinishCut(SkillExecutionContext ctx)
        {
            Vector3 at = ctx.PlayerTransform.position;
            float dmg = ctx.CalculateDamage(_data.baseDamagePerSlash);
            var hits = new List<GameObject>(16);
            CombatQuery.GetNearbyDamageables(at, _data.tier2FinishRadius, ctx.Controller.gameObject, 32, hits);
            foreach (var go in hits)
                ctx.DealDamage(go, dmg, _data.knockbackMultiplier * 2f, isFinisher: true, hitEffectKey: _data.hitEffectKey, hitEffectScale: _data.hitEffectScale);
            SkillRangeRing.Circle(at, _data.tier2FinishRadius);
            SpawnEffect(ctx, _data.tier2FinishEffectKey, at + Vector3.up * 1f, 1.5f, _data.tier2FinishEffectScale);
        }

        private void ApplySlashDamage(SkillExecutionContext ctx)
        {
            if (_hitTargets.Count == 0) return;

            float dmg = ctx.CalculateDamage(_data.baseDamagePerSlash);

            bool last = _slashIndex >= _slashTotal - 1 && _skillTier < 3;   // 증가 전에 불린다 — 마지막 베기가 막타(3단계는 뒤 「길 가르기」가 막타)
            foreach (var target in _hitTargets)
                ctx.DealDamage(target, dmg, _data.knockbackMultiplier, last, skipHitVfx: true);   // 타격 이펙트는 아래에서 직접

            // 히트 이펙트
            foreach (var obj in _hitObjects)
            {
                if (obj == null) continue;
                var offset = new Vector3(
                    Random.Range(-0.3f, 0.3f),
                    Random.Range(0.5f, 1.5f),
                    Random.Range(-0.3f, 0.3f));
                // hitEffectScale이 수명 자리로 들어가 크기는 늘 1이었다(10-08 수정) — 크기로 넘기고 수명은 칼 타격 이펙트 길이(0.5초)
                SpawnEffect(ctx, _data.hitEffectKey, obj.transform.position + offset, 0.5f, _data.hitEffectScale);
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

            // 첫 베기 판정(대시 + 베기 지연)에 발도가 닿게 — 발도 클립은 1.63초에 닿는다(09-25 실측).
            SkillAnimSync.CrossFadeToContact(ctx.Animator, animName, _data.dashDuration + _data.slashDelay, 0.05f);
        }

        // ── Effect Helper ──
        private static async void SpawnEffect(SkillExecutionContext ctx, string key, Vector3 pos, float lifetime, float scale = 1f)
        {
            if (string.IsNullOrEmpty(key)) return;
            var obj = await Managers.ObjectPooler.SpawnAsync(
                key, ObjectPoolerManager.PoolType.Effect, pos, ctx.PlayerTransform.rotation);
            if (obj == null) return;
            if (!Mathf.Approximately(scale, 1f)) obj.transform.localScale = Vector3.one * scale;
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
