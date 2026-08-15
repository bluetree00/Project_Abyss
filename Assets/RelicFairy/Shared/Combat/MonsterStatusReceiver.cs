using System;
using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
    /// <summary>
    /// 몬스터 상태이상 통합 수신기(6속성 공용 "한 틀"). MonsterBase가 1개 보유(plain class, 풀-안전).
    ///
    /// 3계열을 일반화한다:
    ///  • Cc   : 스턴/빙결/석화 등 무력화 — 활성 시 이동·FSM 정지. id별 만료시각 최대값 유지.
    ///           onExpire 콜백(빙결 만료 → 분쇄/빙하 연쇄). HasCc(id)로 상태 질의(분쇄 피해 증폭).
    ///  • Slow : 이속 감소 — id별 중첩·갱신, 합산 후 MoveSpeedMultiplier로 노출(서리).
    ///  • Dot  : 지속피해 — tickInterval마다 TakeSynergyDamage, 잔여 틱/피해 추적. HasDot(id)·GetRemainingDotDamage.
    ///           onExpire 콜백(점화 만료 → 작열 폭발). 향후 독·출혈도 같은 ApplyDot 재사용.
    ///
    /// 룬뿐 아니라 아이템 상태이상(Stun/Freeze/Petrify)도 이 틀의 클라이언트.
    /// 만료 콜백은 "정상 만료" 시 발화한다(피격 사망으로 인한 조기 종료는 발화하지 않음 — Reset에서 폐기).
    /// </summary>
    public sealed class MonsterStatusReceiver
    {
        // total = 부여 시점의 총 지속(초). 남은 시간만으론 게이지 비율을 그릴 수 없어 함께 보관한다.
        private sealed class CcSlot   { public float until; public float total; public Action onExpire; }
        private sealed class SlowSlot { public float magnitude; public float until; public float total; public int stacks; }
        private sealed class DotSlot
        {
            public float      damagePerTick;
            public float      interval;
            public float      timer;
            public int        remainingTicks;
            public int        totalTicks;    // 게이지 비율용(remainingTicks / totalTicks)
            public GameObject instigator;
            public float      defenseIgnore;
            public Action     onExpire;
            public Action     onTick;        // 틱마다(피해 직후) 발화 — 풀 독피해 회복 등.
        }

        private readonly Dictionary<string, CcSlot>     _cc        = new();
        private readonly Dictionary<string, SlowSlot>   _slow      = new();
        private readonly Dictionary<string, DotSlot>    _dot       = new();
        private readonly Dictionary<string, SlowSlot>   _atkSlow   = new();   // 적 공격속도 디버프(공격력 근사)
        private readonly Dictionary<string, float>      _cooldowns = new();   // 적별 내부 쿨(재빙결 방지 등). 풀 Reset로 자동 정리.

        // 만료 처리용 스크래치(루프 중 수정 방지). 재사용해 alloc 없음.
        private readonly List<string> _expireScratch = new();
        private readonly List<Action> _fireScratch   = new();

        // [가이드라인 비주얼] 마커 위치용 소유 몬스터(통지 전용 — 상태 로직엔 미사용).
        private MonsterBase _owner;
        public void AttachOwner(MonsterBase owner) => _owner = owner;
        private Transform OwnerTf => _owner != null ? _owner.transform : null;

        public bool  IsCcActive { get; private set; }
        public float MoveSpeedMultiplier { get; private set; } = 1f;
        public float AttackSpeedMultiplier { get; private set; } = 1f;   // 적 공격 디버프(풀 간파/지배)

        // ── CC (스턴/빙결/석화) ────────────────────────────
        /// <summary>무력화 부여. 같은 id는 더 긴 만료 시각 유지. onExpire는 정상 만료 시 1회 발화.</summary>
        public void ApplyCc(string id, float duration, Action onExpire = null)
        {
            if (string.IsNullOrEmpty(id) || duration <= 0f) return;
            if (!_cc.TryGetValue(id, out var c)) { c = new CcSlot(); _cc[id] = c; }
            c.until = Mathf.Max(c.until, Time.time + duration);
            c.total = Mathf.Max(0.01f, c.until - Time.time);   // 게이지 기준(부여 직후 = 100%)
            if (onExpire != null) c.onExpire = onExpire;
            GuidelineVisual.StatusApplied(OwnerTf, id, duration);
        }

        public bool HasCc(string id) => _cc.TryGetValue(id, out var c) && Time.time < c.until;
        public bool HasAnyCc => IsCcActive;

        /// <summary>CC 즉시 종료. fireExpire=true면 onExpire 발화(빙결 조기 해제 → 분쇄).</summary>
        public void ClearCc(string id, bool fireExpire = false)
        {
            if (!_cc.TryGetValue(id, out var c)) return;
            _cc.Remove(id);
            if (fireExpire) c.onExpire?.Invoke();
        }

        // ── Slow (이속 감소) ───────────────────────────────
        /// <summary>이속 감소 부여. magnitude=0.12 → -12%. 같은 id 중첩(최대 maxStacks), 갱신 시 타이머 리셋.</summary>
        public void ApplySlow(string id, float magnitudePct, float duration, int maxStacks = 1)
        {
            if (string.IsNullOrEmpty(id) || magnitudePct <= 0f || duration <= 0f) return;
            if (!_slow.TryGetValue(id, out var s)) { s = new SlowSlot(); _slow[id] = s; }
            s.magnitude = Mathf.Clamp01(magnitudePct);
            s.until     = Time.time + duration;
            s.total     = Mathf.Max(0.01f, duration);
            s.stacks    = Mathf.Clamp(s.stacks + 1, 1, Mathf.Max(1, maxStacks));
            GuidelineVisual.StatusApplied(OwnerTf, id, duration);
        }

        /// <summary>현재 슬로우 중첩 수(만료분은 다음 Tick에 정리됨). 빙결 조건(서리 2중첩) 판정용.</summary>
        public int GetSlowStacks(string id)
            => _slow.TryGetValue(id, out var s) && Time.time < s.until ? s.stacks : 0;

        // ── 적별 내부 쿨다운(재빙결 방지 등) ───────────────
        public void SetCooldown(string id, float duration)
        {
            if (!string.IsNullOrEmpty(id) && duration > 0f) _cooldowns[id] = Time.time + duration;
        }

        public bool IsOnCooldown(string id)
            => _cooldowns.TryGetValue(id, out var t) && Time.time < t;

        // ── DoT (점화/독/출혈) ─────────────────────────────
        /// <summary>
        /// 지속피해 부여. tickInterval마다 damagePerTick을 defenseIgnore(기본 방어무시)로 입힌다.
        /// 같은 id 재부여 = 갱신: 파라미터는 갱신하되 진행 중 타이머는 보존하고 잔여 틱을 top-up(연타 preempt 방지 — 풀 장판 체류 재적용에 필수).
        /// onTick은 각 틱 피해 직후, onExpire는 마지막 틱 후 정상 만료 시 발화.
        /// </summary>
        public void ApplyDot(string id, float damagePerTick, float tickInterval, int tickCount,
                             GameObject instigator, float defenseIgnore = 1f, Action onExpire = null, Action onTick = null)
        {
            if (string.IsNullOrEmpty(id) || damagePerTick <= 0f || tickInterval <= 0f || tickCount <= 0) return;
            bool isNew = !_dot.TryGetValue(id, out var d);
            if (isNew) { d = new DotSlot(); _dot[id] = d; d.timer = tickInterval; }
            d.damagePerTick  = damagePerTick;
            d.interval       = tickInterval;
            d.instigator     = instigator;
            d.defenseIgnore  = defenseIgnore;
            d.onExpire       = onExpire;
            d.onTick         = onTick;
            d.remainingTicks = Mathf.Max(d.remainingTicks, tickCount);   // top-up(보존 타이머)
            d.totalTicks     = Mathf.Max(d.totalTicks, d.remainingTicks); // 게이지 기준
            GuidelineVisual.StatusApplied(OwnerTf, id, tickInterval * d.remainingTicks);
        }

        /// <summary>적 공격 디버프(공격속도 배율 감소 → 공격력 근사). magnitude=0.2 → 공격속도 -20%. 같은 id 갱신.</summary>
        public void ApplyAttackSlow(string id, float magnitudePct, float duration)
        {
            if (string.IsNullOrEmpty(id) || magnitudePct <= 0f || duration <= 0f) return;
            if (!_atkSlow.TryGetValue(id, out var s)) { s = new SlowSlot(); _atkSlow[id] = s; }
            s.magnitude = Mathf.Clamp01(magnitudePct);
            s.until     = Time.time + duration;
            s.total     = Mathf.Max(0.01f, duration);
            s.stacks    = 1;
            GuidelineVisual.StatusApplied(OwnerTf, id, duration);
        }

        public bool  HasDot(string id) => _dot.ContainsKey(id);
        /// <summary>남은 DoT 총 피해(작열 폭발 합산용).</summary>
        public float GetRemainingDotDamage(string id)
            => _dot.TryGetValue(id, out var d) ? d.damagePerTick * d.remainingTicks : 0f;

        /// <summary>현재 틱당 피해(없으면 0). 중첩형 재부여가 "얼마에 얹을지" 알아야 해서 노출한다.</summary>
        public float GetDotDamagePerTick(string id)
            => _dot.TryGetValue(id, out var d) && d.remainingTicks > 0 ? d.damagePerTick : 0f;

        /// <summary>
        /// DoT의 잔여 틱을 fraction만큼 <b>차감하고 소모된 피해 가치를 반환</b>한다. 피해는 넣지 않는다 —
        /// 소모한 가치를 무엇으로 바꿀지(폭발·보호막·쿨감)는 부르는 쪽이 정한다.
        ///
        /// 두 가지를 일부러 하지 않는다:
        ///  • 딕셔너리에서 제거하지 않는다 — DoT 틱 피해가 적을 죽이면 그 처치가 서약을 물고
        ///    이 함수까지 돌아올 수 있다. 그 시점 <see cref="Tick"/>은 _dot을 순회 중이라
        ///    여기서 지우면 순회가 통째로 터진다. 잔여 0인 슬롯은 다음 Tick이 정리한다.
        ///  • onExpire를 발화시키지 않는다(전부 소모돼도) — 소모는 '정상 만료'가 아니다.
        ///    만료 연쇄(점화 만료 → 작열 폭발)까지 딸려오면 소모 전용 효과가 상태를 되살린다.
        /// </summary>
        public float ConsumeDot(string id, float fraction)
        {
            if (string.IsNullOrEmpty(id) || !_dot.TryGetValue(id, out var d) || d.remainingTicks <= 0) return 0f;

            fraction = Mathf.Clamp01(fraction);
            if (fraction <= 0f) return 0f;

            int consumed = Mathf.Clamp(Mathf.CeilToInt(d.remainingTicks * fraction), 1, d.remainingTicks);
            d.remainingTicks -= consumed;
            if (d.remainingTicks <= 0) d.onExpire = null;
            return d.damagePerTick * consumed;
        }

        // ── 표시(UI) ───────────────────────────────────────
        /// <summary>
        /// 지금 걸려 있는 상태이상을 UI 모델(BuffViewItem)로 뽑는다 — 플레이어 버프창과 같은 규격.
        ///
        /// 여태 상태는 id별 단건 조회(HasCc/HasDot 등)만 가능했다. "무엇이 걸려 있는지" 자체를
        /// 물어볼 방법이 없어서 디버프 UI를 만들 수 없었다 — 그 구멍을 메우는 유일한 열거 진입점.
        /// 라벨/아이콘은 EffectDescriptionFormatter가 statusId에서 해석한다(표시 규칙 단일화).
        /// </summary>
        public void CollectStatuses(List<BuffViewItem> into)
        {
            if (into == null) return;
            float now = Time.time;

            foreach (var kv in _cc)
            {
                float left = kv.Value.until - now;
                if (left <= 0f) continue;
                into.Add(MakeItem(kv.Key, 1, left / kv.Value.total, left));
            }

            foreach (var kv in _slow)
            {
                float left = kv.Value.until - now;
                if (left <= 0f) continue;
                into.Add(MakeItem(kv.Key, kv.Value.stacks, left / kv.Value.total, left));
            }

            foreach (var kv in _atkSlow)
            {
                float left = kv.Value.until - now;
                if (left <= 0f) continue;
                into.Add(MakeItem(kv.Key, 1, left / kv.Value.total, left));
            }

            foreach (var kv in _dot)
            {
                var d = kv.Value;
                if (d.remainingTicks <= 0) continue;
                float left = d.remainingTicks * d.interval;
                into.Add(MakeItem(kv.Key, 1, (float)d.remainingTicks / Mathf.Max(1, d.totalTicks), left));
            }
        }

        /// <summary>statusId → 표시 항목. 몬스터에게 걸린 것이므로 항상 디버프로 취급한다.</summary>
        internal static BuffViewItem MakeItem(string statusId, int stacks, float remaining01, float remainSec)
            => new BuffViewItem(
                EffectDescriptionFormatter.IconKeyForStatus(statusId),
                EffectDescriptionFormatter.LabelForStatus(statusId),
                Mathf.Max(1, stacks),
                Mathf.Clamp01(remaining01),
                remainSec >= 0f ? $"{remainSec:0.0}초" : string.Empty,
                BuffSource.Status,
                isDebuff: true);

        // ── 수명 ───────────────────────────────────────────
        public void Tick(float dt, MonsterBase owner)
        {
            float now = Time.time;

            // CC: 만료 수집 → 제거 → onExpire 발화
            bool anyCc = false;
            if (_cc.Count > 0)
            {
                _expireScratch.Clear(); _fireScratch.Clear();
                foreach (var kv in _cc)
                {
                    if (now < kv.Value.until) anyCc = true;
                    else { _expireScratch.Add(kv.Key); _fireScratch.Add(kv.Value.onExpire); }
                }
                for (int i = 0; i < _expireScratch.Count; i++) _cc.Remove(_expireScratch[i]);
                for (int i = 0; i < _fireScratch.Count; i++) _fireScratch[i]?.Invoke();
            }
            IsCcActive = anyCc;

            // Slow: 만료 제거 + 합산
            if (_slow.Count > 0)
            {
                _expireScratch.Clear();
                float total = 0f;
                foreach (var kv in _slow)
                {
                    if (now >= kv.Value.until) { _expireScratch.Add(kv.Key); continue; }
                    total += kv.Value.magnitude * kv.Value.stacks;
                }
                for (int i = 0; i < _expireScratch.Count; i++) _slow.Remove(_expireScratch[i]);
                MoveSpeedMultiplier = Mathf.Clamp(1f - total, 0.1f, 1f);
            }
            else MoveSpeedMultiplier = 1f;

            // DoT: 틱 피해 → 소진 시 제거 → onExpire 발화
            if (_dot.Count > 0 && owner != null)
            {
                _expireScratch.Clear(); _fireScratch.Clear();
                foreach (var kv in _dot)
                {
                    var d = kv.Value;
                    d.timer -= dt;
                    while (d.timer <= 0f && d.remainingTicks > 0)
                    {
                        // 상태 id로 속성을 해석해 데미지 텍스트에 속성색을 입힌다(점화=불, 독=풀 …).
                        RuneElement? el = RuneElementMap.FromStatusId(kv.Key, out var e) ? e : (RuneElement?)null;
                        owner.TakeSynergyDamage(d.damagePerTick, d.instigator, d.defenseIgnore, false, DamageKind.Dot, el);
                        d.onTick?.Invoke();
                        d.remainingTicks--;
                        d.timer += d.interval;
                        if (owner == null) break;
                    }
                    if (d.remainingTicks <= 0) { _expireScratch.Add(kv.Key); _fireScratch.Add(d.onExpire); }
                }
                for (int i = 0; i < _expireScratch.Count; i++) _dot.Remove(_expireScratch[i]);
                for (int i = 0; i < _fireScratch.Count; i++) _fireScratch[i]?.Invoke();
            }

            // 적 공격 디버프(공격속도 배율) 합산
            if (_atkSlow.Count > 0)
            {
                _expireScratch.Clear();
                float total = 0f;
                foreach (var kv in _atkSlow)
                {
                    if (now >= kv.Value.until) { _expireScratch.Add(kv.Key); continue; }
                    total += kv.Value.magnitude;
                }
                for (int i = 0; i < _expireScratch.Count; i++) _atkSlow.Remove(_expireScratch[i]);
                AttackSpeedMultiplier = Mathf.Clamp(1f - total, 0.1f, 1f);
            }
            else AttackSpeedMultiplier = 1f;
        }

        public void Reset()
        {
            _cc.Clear();
            _slow.Clear();
            _dot.Clear();
            _atkSlow.Clear();
            _cooldowns.Clear();
            IsCcActive = false;
            MoveSpeedMultiplier = 1f;
            AttackSpeedMultiplier = 1f;
        }
    }
}
