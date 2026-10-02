using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 랜슬롯 조각의 공통 바탕 — 허브(<see cref="LancelotMemoryHub"/>)를 잡고 사건에 붙는다.
/// 줄 ②③은 <see cref="RelicPartEffect.Line2"/> · <see cref="RelicPartEffect.Line3"/>로 그때그때 본다(「선명하게」가 즉시 반영).
/// 계단 조각은 광기가 그 계단 이상일 때만 켜진다(광란 중엔 늘 켜짐).
/// </summary>
public abstract class LancelotFragment : RelicPartEffect
{
    protected LancelotMemoryHub Hub;
    protected PlayerController  Player;
    protected readonly List<MonsterBase> Buf = new(16);
    protected readonly List<MonsterBase> Scratch = new(16);

    protected LancelotFragment(string key) : base(key) { }

    public override void OnAcquire(PlayerController player)
    {
        Player = player;
        Hub = LancelotMemoryHub.Ensure(player);
        if (Hub != null) Bind();
    }

    public override void OnRemove(PlayerController player)
    {
        if (Hub != null) Unbind();
        Hub = null;
    }

    protected virtual void Bind() { }
    protected virtual void Unbind() { }

    protected float Atk => Hub != null ? Hub.Atk : 0f;
    protected GameObject Owner => Player != null ? Player.gameObject : null;

    protected static MonsterBase AsMonster(GameObject go)
    {
        if (go == null) return null;
        return go.TryGetComponent<MonsterBase>(out var mb) ? mb : go.GetComponentInParent<MonsterBase>();
    }

    /// <summary>반경 안 적을 <see cref="Scratch"/>에 옮겨 담는다(피해가 다른 조회를 불러도 목록이 안 바뀌게).</summary>
    protected List<MonsterBase> Near(Vector3 at, float radius, GameObject exclude, int max)
    {
        int n = CombatQuery.GetNearbyEnemies(at, radius, exclude, max, Buf);
        Scratch.Clear();
        for (int i = 0; i < n; i++) if (Buf[i] != null && !Buf[i].IsDead) Scratch.Add(Buf[i]);
        return Scratch;
    }

    /// <summary>대시 1회 몫의 게이지를 돌려준다(갈라진 맹세 ③ · 광란의 걸음).</summary>
    protected void RefundDash()
    {
        if (Player == null || Player.Stamina == null || Player.CharacterData == null || Player.RuntimeStats == null) return;
        Player.Stamina.Refund(Player.CharacterData.dodgeStaminaCost * NightmareRules.DodgeStaminaMultiplier, Player.RuntimeStats.MaxStamina);
    }

    /// <summary>한 대상 상태 피해 베기 + X자 이펙트.</summary>
    protected void Cut(MonsterBase mb, float damage, int xVfx = 1)
    {
        if (mb == null || mb.IsDead) return;
        CombatQuery.DealSynergyDamage(mb, damage, Owner, 1f, false, RuneElement.Dark);
        LancelotMemoryHub.PlayX(mb.transform.position, xVfx);
    }

    /// <summary>조각 발동 글자(진홍) — 출시 빌드에서도 뜬다. 처음 켜진 조각이면 화면 아래 자막 한 줄(계정당 1회).</summary>
    protected void Say(Vector3 at, string text)
    {
        RelicFloatText.Show(at, text, LancelotMemoryHub.LancelotColor);
        RelicMemoryHints.TryShowOnce(EffectKey);
    }
}

// ── 계단 10 ─────────────────────────────────────────────────────

/// <summary>L1 갈라진 맹세 — 광기 10+: 대시가 적을 관통하며 벤다 · 맞힌 적마다 광기 +2 · ② 관통한 적 낙인 +1 · ③ 3체 이상이면 대시 1회 회복.</summary>
public sealed class LSplitOathEffect : LancelotFragment
{
    private const float Sweep = 0.35f, Radius = 1.3f, CutRatio = 0.6f;
    private const int   MadnessPerHit = 2;
    private float _until;
    private bool  _sweeping;
    private int   _count;
    private readonly HashSet<int> _hit = new();
    public LSplitOathEffect() : base("l_split_oath") { }
    protected override void Bind()   => Player.OnDodgeStart += OnDodge;
    protected override void Unbind() { if (Player != null) Player.OnDodgeStart -= OnDodge; }
    private void OnDodge()
    {
        if (Hub == null || !Hub.AtRung(1)) return;
        if (Hub.IsFrenzy && LFrenzyStepEffect.ActiveOn(Player)) return;   // 광란 중 대시 베기는 광란의 걸음 몫(같은 대시를 두 번 베지 않는다)
        _until = Time.time + Sweep; _sweeping = true; _count = 0; _hit.Clear();
    }
    public override void Tick(float dt, PlayerController player)
    {
        if (!_sweeping || Hub == null) return;
        foreach (var mb in Near(player.transform.position, Radius, player.gameObject, 8))
        {
            if (!_hit.Add(mb.GetInstanceID())) continue;
            _count++;
            Cut(mb, Atk * CutRatio);
            Hub.Relic?.AddStack(MadnessPerHit);
            if (Line2) Hub.AddBrand(mb, 1, overflowToGrudge: false, ignoreGap: true);
        }
        if (Time.time < _until) return;
        _sweeping = false;
        if (_count > 0) Debug.Log($"[LancelotMemory] 갈라진 맹세 — 관통 {_count} · 광기 +{_count * MadnessPerHit}");
        if (Line3 && _count >= 3)
        {
            RefundDash();
            Say(player.transform.position, "갈라진 맹세");
        }
    }
}

/// <summary>L2 피 냄새 — 광기 10+: 체력 30% 이하로 몰린 적을 처치하면 2초 동안 광기가 줄지 않는다 · ② 그 적의 낙인이 가장 가까운 적에게 · ③ 넘치면 원한.</summary>
public sealed class LBloodScentEffect : LancelotFragment
{
    private const float LowHp = 0.3f, Hold = 2f, TransferRadius = 6f;
    private readonly HashSet<int> _wounded = new();
    public LBloodScentEffect() : base("l_blood_scent") { }
    public override void OnHit(in HitInfo hit, PlayerController player)
    {
        var mb = AsMonster(hit.Target);
        if (mb == null || mb.IsDead || mb.EffectiveMaxHp <= 0) return;
        if (mb.CurrentHp <= mb.EffectiveMaxHp * LowHp)
        {
            if (_wounded.Count > 256) _wounded.Clear();
            _wounded.Add(mb.GetInstanceID());
        }
    }
    public override void OnKill(GameObject deadEnemy, PlayerController player)
    {
        var mb = AsMonster(deadEnemy);
        if (mb == null || Hub == null || !_wounded.Remove(mb.GetInstanceID()) || !Hub.AtRung(1)) return;
        Hub.Madness.HoldDecayUntil = Time.time + Hold;
        Vector3 at = mb.transform.position;
        Say(at, "피 냄새");
        if (!Line2) return;
        int b = LancelotMemoryHub.BrandOf(mb);
        if (b <= 0) return;
        var near = Near(at, TransferRadius, mb.gameObject, 1);
        if (near.Count == 0) return;
        var to = near[0];
        ElementVfxPlayer.PlayBeam(RuneElement.Dark, at + Vector3.up, to.transform.position + Vector3.up, 0.3f);
        Hub.AddBrand(to, b, overflowToGrudge: Line3, ignoreGap: true);
        Debug.Log($"[LancelotMemory] 피 냄새 — 낙인 {b} → {to.name}");
    }
}

// ── 계단 20 ─────────────────────────────────────────────────────

/// <summary>L3 검은 잔상 — 광기 20+: 3번째 평타마다 그림자 잔상이 0.5초 뒤 같은 공격을 되풀이(피해 50%) · ② 잔상도 낙인 · ③ 광란 중 잔상 2개.</summary>
public sealed class LBlackAfterimageEffect : LancelotFragment
{
    private const float Delay = 0.5f, Delay2 = 0.8f, EchoRatio = 0.5f, Window = 0.12f;
    private const int   MaxEcho = 8;
    private struct Echo { public MonsterBase target; public float dmg; }
    private readonly List<Echo> _echo = new(MaxEcho);
    private float   _recordUntil, _fireAt1, _fireAt2;
    private Vector3 _ghostAt;
    public LBlackAfterimageEffect() : base("l_black_afterimage") { }
    protected override void Bind()   { Hub.Swing += OnSwing; Hub.Hit += OnHubHit; }
    protected override void Unbind() { Hub.Swing -= OnSwing; Hub.Hit -= OnHubHit; }
    private void OnSwing(HitInfo hit, int no)
    {
        if (no % 3 != 0 || !Hub.AtRung(2) || _fireAt1 > 0f) return;
        _echo.Clear();
        _recordUntil = Time.time + Window;
        _fireAt1 = Time.time + Delay;
        _fireAt2 = Line3 && Hub.IsFrenzy ? Time.time + Delay2 : 0f;
        _ghostAt = Player.transform.position;
        Record(hit);
    }
    private void OnHubHit(HitInfo hit)
    {
        if (Time.time > _recordUntil || hit.ActionType != WeaponActionType.GroundLight) return;
        Record(hit);
    }
    private void Record(in HitInfo hit)
    {
        var mb = AsMonster(hit.Target);
        if (mb == null || hit.Damage <= 0f || _echo.Count >= MaxEcho) return;
        for (int i = 0; i < _echo.Count; i++) if (_echo[i].target == mb) return;
        _echo.Add(new Echo { target = mb, dmg = hit.Damage });
    }
    public override void Tick(float dt, PlayerController player)
    {
        if (_fireAt1 > 0f && Time.time >= _fireAt1) { _fireAt1 = 0f; FireEcho(1); }
        if (_fireAt2 > 0f && Time.time >= _fireAt2) { _fireAt2 = 0f; FireEcho(2); }
    }
    private void FireEcho(int which)
    {
        if (Hub == null) return;
        ElementVfxPlayer.PlayBurst(RuneElement.Dark, _ghostAt, 1f);   // 잔상이 선 자리
        int hits = 0;
        for (int i = 0; i < _echo.Count; i++)
        {
            var e = _echo[i];
            if (e.target == null || e.target.IsDead) continue;
            hits++;
            Cut(e.target, e.dmg * EchoRatio);
            if (Line2) Hub.AddBrand(e.target, 1, overflowToGrudge: false, ignoreGap: true);
        }
        if (hits > 0) Debug.Log($"[LancelotMemory] 검은 잔상 {which} — 되풀이 {hits}체 · 피해 {EchoRatio:P0}");
    }
}

/// <summary>L4 배신자의 걸음 — 광기 20+: 긴급 회피 성공 → 광기 +8 · 피한 적 낙인 상한까지 · ② 반격 첫 공격이 그 적에게 그림자 베기 · ③ 광란 중이면 심판 1회 더(광란당 1번).</summary>
public sealed class LBetrayerStepEffect : LancelotFragment
{
    private const float SlashWindow = 0.8f, SlashRatio = 1.0f;
    private const int   StepMadness = 8;
    private MonsterBase _mark;
    private float _slashUntil;
    private bool  _usedThisFrenzy;
    public LBetrayerStepEffect() : base("l_betrayer_step") { }
    protected override void Bind()   { Player.OnPerfectDodge += OnPerfectDodge; Hub.FrenzyStarted += OnFrenzy; }
    protected override void Unbind() { if (Player != null) Player.OnPerfectDodge -= OnPerfectDodge; Hub.FrenzyStarted -= OnFrenzy; }
    private void OnFrenzy() => _usedThisFrenzy = false;
    private void OnPerfectDodge(float duration)
    {
        if (Hub == null || !Hub.AtRung(2)) return;
        var src = Player.PerfectDodgeSource;
        var mb  = src != null ? src.GetComponentInParent<MonsterBase>() : null;
        Hub.Relic?.AddStack(StepMadness);
        Vector3 at = Player.transform.position;
        if (mb != null && !mb.IsDead)
        {
            Hub.FillBrand(mb);
            _mark = mb;
            _slashUntil = Time.unscaledTime + SlashWindow;   // 긴급 회피는 슬로모 — 실시간으로 잰다
            at = mb.transform.position;
        }
        Say(at, "배신자의 걸음");
        if (Line3 && Hub.IsFrenzy && !_usedThisFrenzy)
        {
            _usedThisFrenzy = true;
            Hub.Relic?.GrantJudgmentCharge();
            Say(Player.transform.position, "심판 +1");
        }
        Debug.Log($"[LancelotMemory] 배신자의 걸음 — 광기 +{StepMadness} · 낙인 상한 {(mb != null ? mb.name : "없음")}");
    }
    public override void OnHit(in HitInfo hit, PlayerController player)
    {
        if (!Line2 || _mark == null || Time.unscaledTime > _slashUntil) return;
        var mb = AsMonster(hit.Target);
        if (mb != _mark) return;
        _mark = null;
        Cut(mb, Atk * SlashRatio, 2);
        Say(mb.transform.position, "배신의 일격");
        Debug.Log($"[LancelotMemory] 배신자의 걸음 ② — 그림자 베기 {mb.name} · {Atk * SlashRatio:0}");
    }
}

// ── 계단 30 ─────────────────────────────────────────────────────

/// <summary>L5 찢긴 맹세의 검 — 광기 30+: 4번째 평타마다 작은 심판(짧은 콘)이 낙인 1을 찢는다 · ② 찢은 낙인 1 = 원한 1 · ③ 원한이 있으면 원형.</summary>
public sealed class LTornOathBladeEffect : LancelotFragment
{
    private const float ConeRange = 4f, ConeHalf = 40f, CircleRadius = 3f, Ratio = 0.8f;
    public LTornOathBladeEffect() : base("l_torn_oath_blade") { }
    protected override void Bind()   => Hub.Swing += OnSwing;
    protected override void Unbind() => Hub.Swing -= OnSwing;
    private void OnSwing(HitInfo hit, int no)
    {
        if (no % 4 != 0 || !Hub.AtRung(3)) return;
        Vector3 pos = Player.transform.position;
        Vector3 fwd = Player.transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) return;
        fwd.Normalize();
        bool circle = Line3 && Hub.Grudge != null && Hub.Grudge.Value >= 1;
        List<MonsterBase> targets;
        if (circle)
        {
            targets = Near(pos, CircleRadius, Owner, 16);
            ElementVfxPlayer.PlayBurst(RuneElement.Dark, pos, 2f);   // 판정 3 m — 어둠 폭발은 같은 크기면 화면을 덮는다
        }
        else
        {
            int n = CombatQuery.GetEnemiesInCone(pos, fwd, ConeRange, ConeHalf, 16, Buf, showGuide: false);
            Scratch.Clear();
            for (int i = 0; i < n; i++) if (Buf[i] != null) Scratch.Add(Buf[i]);
            targets = Scratch;
            RelicStateVfx.PlayOneShot(LancelotMemoryHub.SlashVfxKey, pos + fwd * 1.6f + Vector3.up * 0.7f, 0.3f, fwd, 1.2f);
        }
        int torn = 0, cut = 0;
        var list = new List<MonsterBase>(targets);
        foreach (var mb in list)
        {
            if (mb == null || mb.IsDead) continue;
            cut++;
            torn += Hub.Tear(mb, LancelotMemoryHub.TearCause.SmallJudgment, 1);
            CombatQuery.DealSynergyDamage(mb, Atk * Ratio, Owner, 1f, false, RuneElement.Dark);
        }
        if (Line2 && torn > 0) Hub.SaveGrudge(torn);
        Say(pos, "작은 심판");
        Debug.Log($"[LancelotMemory] 작은 심판({(circle ? "원형" : "콘")}) — 벤 {cut} · 찢은 낙인 {torn}");
    }
}

/// <summary>L6 광기의 눈 — 광기 30+: 낙인 상한 3 → 5 · ② 광기 40에선 7 · ③ 상한에 찬 적은 붉게 빛나고 다음 첫 타에 경직(넉백 없음).</summary>
public sealed class LMadnessEyeEffect : LancelotFragment
{
    private const float Stagger = 0.35f;
    private readonly HashSet<int> _primed = new();
    public LMadnessEyeEffect() : base("l_madness_eye") { }
    protected override void Bind()   { Hub.EyeTier = Line2 ? 2 : 1; Hub.BrandAdded += OnBrand; }
    protected override void Unbind() { Hub.EyeTier = 0; Hub.BrandAdded -= OnBrand; }
    protected override void OnGradeChanged(RelicMemoryGrade before, PlayerController player)
    {
        if (Hub != null) Hub.EyeTier = Line2 ? 2 : 1;
    }
    private void OnBrand(MonsterBase mb, int added, int overflow, LancelotMemoryHub.BrandSource src)
    {
        if (!Line3 || mb == null || added <= 0) return;
        if (LancelotMemoryHub.BrandOf(mb) < Hub.BrandCap) return;
        if (_primed.Count > 128) _primed.Clear();
        if (_primed.Add(mb.GetInstanceID())) ElementVfxPlayer.PlayBurst(RuneElement.Fire, mb.transform.position, 0.8f);   // 붉게 빛난다
    }
    public override void OnHit(in HitInfo hit, PlayerController player)
    {
        if (!Line3 || _primed.Count == 0) return;
        var mb = AsMonster(hit.Target);
        if (mb == null || !_primed.Remove(mb.GetInstanceID())) return;
        if (mb.Grade != MonsterGrade.Boss) mb.ApplyStun(Stagger);
    }
}

// ── 계단 40 ─────────────────────────────────────────────────────

/// <summary>L7 끝의 문턱 — 40에 닿아도 계속 치는 동안(0.5초 안 연타) 최대 3초 광란을 미룬다(미룬 만큼 광란이 길어짐 · 피격 즉시 광란) · ② 미루는 동안 적중이 원한 · ③ 3초를 다 버티면 화면 안 낙인 적 모두 상한.</summary>
public sealed class LLastThresholdEffect : LancelotFragment
{
    private const float MaxHold = 3f, HitGap = 0.5f, GrudgeEvery = 0.4f;
    private bool  _holding;
    private float _start, _lastHit, _nextGrudge;
    public LLastThresholdEffect() : base("l_last_threshold") { }
    protected override void Bind()
    {
        if (Hub.Relic != null) { Hub.Relic.HoldFrenzyAtMax = true; Hub.Relic.MaxReachedHeld += OnHeld; }
        Hub.Hit += OnHubHit;
        Player.OnDamageTaken += OnDamaged;
    }
    protected override void Unbind()
    {
        if (_holding) Release();
        if (Hub.Relic != null) { Hub.Relic.HoldFrenzyAtMax = false; Hub.Relic.MaxReachedHeld -= OnHeld; }
        Hub.Hit -= OnHubHit;
        if (Player != null) Player.OnDamageTaken -= OnDamaged;
    }
    private void OnHeld()
    {
        _holding = true;
        Hub.HoldingThreshold = true;
        _start = _lastHit = Time.time;
        _nextGrudge = 0f;
        Say(Player.transform.position, "끝의 문턱");
    }
    private void OnHubHit(HitInfo hit)
    {
        if (!_holding) return;
        _lastHit = Time.time;
        if (Line2 && Time.time >= _nextGrudge)
        {
            _nextGrudge = Time.time + GrudgeEvery;
            Hub.SaveGrudge(1);
        }
    }
    private void OnDamaged() { if (_holding) Release(); }
    public override void Tick(float dt, PlayerController player)
    {
        if (!_holding) return;
        if (Time.time - _lastHit > HitGap || Time.time - _start >= MaxHold) Release();
    }
    private void Release()
    {
        _holding = false;
        if (Hub != null) Hub.HoldingThreshold = false;
        float held = Mathf.Min(MaxHold, Time.time - _start);
        Hub.Relic?.EnterFrenzyNow(held);
        bool full = held >= MaxHold - 0.05f;
        int filled = 0;
        if (Line3 && full && Player != null)
        {
            foreach (var mb in new List<MonsterBase>(Near(Player.transform.position, LancelotMemoryHub.ScreenRadius, Owner, 32)))
                if (LancelotMemoryHub.BrandOf(mb) > 0 && Hub.FillBrand(mb) >= 0) filled++;
            Say(Player.transform.position, "문턱을 넘었다");
        }
        Debug.Log($"[LancelotMemory] 끝의 문턱 — {held:0.00}초 미룸 → 광란 +{held:0.00}초{(filled > 0 ? $" · 낙인 상한 {filled}체" : "")}");
    }
}

/// <summary>L8 광기의 왕관 — 광란에 드는 순간 6 m 안 적 낙인 +2 · ② 원한 1개당 반경 +1 m · ③ 원한 2를 써서 광란 시작과 동시에 심판 1회.</summary>
public sealed class LMadnessCrownEffect : LancelotFragment
{
    private const float Radius = 6f;
    public LMadnessCrownEffect() : base("l_madness_crown") { }
    protected override void Bind()   => Hub.FrenzyStarted += OnFrenzy;
    protected override void Unbind() => Hub.FrenzyStarted -= OnFrenzy;
    private void OnFrenzy()
    {
        if (Player == null) return;
        Vector3 me = Player.transform.position;
        float r = Radius + (Line2 && Hub.Grudge != null ? Hub.Grudge.Value : 0);
        // 반경만큼 키운 어둠 폭발은 자홍 구름이 화면 · 캐릭터를 덮는다(10-02 실측) → 발밑 작게 + 낙인 받은 적마다 작은 표시
        ElementVfxPlayer.PlayBurst(RuneElement.Dark, me, 1.5f);
        int n = 0;
        foreach (var mb in new List<MonsterBase>(Near(me, r, Owner, 32)))
        {
            if (Hub.AddBrand(mb, 2, overflowToGrudge: false, ignoreGap: true) <= 0) continue;
            n++;
            ElementVfxPlayer.PlayBurst(RuneElement.Dark, mb.transform.position, 0.6f);
        }
        Say(me, "광기의 왕관");
        Debug.Log($"[LancelotMemory] 광기의 왕관 — 반경 {r:0.#} m · 낙인 +2 {n}체");
        if (Line3 && Hub.Grudge != null && Hub.Grudge.TrySpend(2) && Hub.Relic != null)
        {
            Say(me + Vector3.up * 0.4f, "왕관의 심판");
            Hub.Relic.PerformJudgmentStrike(Player.transform, 0, 1, LancelotMadnessRelic.InstantJudgmentShare);   // 한 방짜리 심판(막타 몫 · 광란의 심판 횟수는 쓰지 않는다)
        }
    }
}

// ── 광란 ────────────────────────────────────────────────────────

/// <summary>L9 끝나지 않는 광란 — 광란이 끝날 때 한 번 더(절반 길이) · ② 전체 길이 · ③ 이어지는 광란에서 심판 1회 더.</summary>
public sealed class LEndlessFrenzyEffect : LancelotFragment
{
    public LEndlessFrenzyEffect() : base("l_endless_frenzy") { }
    protected override void Bind()
    {
        Apply();
        if (Hub.Madness != null) Hub.Madness.OnFrenzyRenewed += OnRenewed;
    }
    protected override void Unbind()
    {
        if (Hub.Madness == null) return;
        Hub.Madness.SetEndlessFrenzy(false);
        Hub.Madness.OnFrenzyRenewed -= OnRenewed;
    }
    protected override void OnGradeChanged(RelicMemoryGrade before, PlayerController player) => Apply();
    private void Apply() => Hub?.Madness?.SetEndlessFrenzy(true, Line2 ? 1f : 0.5f);
    private void OnRenewed()
    {
        Say(Player.transform.position, "끝나지 않는 광란");
        if (Line3) Hub.Relic?.GrantJudgmentCharge();
        Debug.Log($"[LancelotMemory] 끝나지 않는 광란 — 이어짐({(Line2 ? "전체" : "절반")}){(Line3 ? " · 심판 +1" : "")}");
    }
}

/// <summary>L10 피의 광란 — 광란 중 공격이 출혈(낙인 수만큼 세게) · ② 출혈 적이 죽으면 출혈이 옮는다 · ③ 출혈로 죽은 적 = 원한 +1.</summary>
public sealed class LBloodFrenzyEffect : LancelotFragment
{
    private const float BleedRatio = 0.06f, BleedDur = 3f, SpreadRadius = 5f;
    public LBloodFrenzyEffect() : base("l_blood_frenzy") { }
    public override void OnHit(in HitInfo hit, PlayerController player)
    {
        if (Hub == null || !Hub.IsFrenzy) return;
        var mb = AsMonster(hit.Target);
        if (mb == null || mb.IsDead) return;
        int b = Mathf.Max(1, LancelotMemoryHub.BrandOf(mb));
        MonsterBleed.Apply(mb.gameObject, Atk * BleedRatio * b, BleedDur, Owner);
    }
    public override void OnKill(GameObject deadEnemy, PlayerController player)
    {
        if (Hub == null || deadEnemy == null) return;
        float rem = MonsterBleed.Remaining(deadEnemy);
        if (rem <= 0f) return;
        Vector3 at = deadEnemy.transform.position;
        if (Line3) Hub.SaveGrudge(1);
        if (!Line2) return;
        var near = Near(at, SpreadRadius, deadEnemy, 1);
        if (near.Count == 0) return;
        ElementVfxPlayer.PlayBeam(RuneElement.Dark, at + Vector3.up, near[0].transform.position + Vector3.up, 0.3f);
        MonsterBleed.Apply(near[0].gameObject, rem / BleedDur, BleedDur, Owner);
    }
}

/// <summary>L11 광란의 걸음 — 광란 중 대시가 공격이 되고 재사용 대기가 없다 · ② 대시로 벤 적 낙인 +1 · ③ 광란이 끝날 때 대시 궤적이 한꺼번에 베인다.</summary>
public sealed class LFrenzyStepEffect : LancelotFragment
{
    private const float Sweep = 0.35f, Radius = 1.3f, CutRatio = 0.7f, TrailRatio = 0.8f;
    private const int   MaxTrail = 8;
    private static readonly HashSet<int> s_owners = new();   // 이 조각을 가진 플레이어(갈라진 맹세가 광란 중 대시를 양보)
    private float   _until;
    private bool    _sweeping;
    private Vector3 _from;
    private readonly HashSet<int> _hit = new();
    private readonly List<Vector3> _trail = new(MaxTrail * 2);
    public LFrenzyStepEffect() : base("l_frenzy_step") { }
    public static bool ActiveOn(PlayerController p) => p != null && s_owners.Contains(p.GetInstanceID());
    protected override void Bind()
    {
        s_owners.Add(Player.GetInstanceID());
        Player.OnDodgeStart += OnDodge;
        Hub.FrenzyStarted += OnFrenzyStart;
        Hub.FrenzyEnded   += OnFrenzyEnd;
    }
    protected override void Unbind()
    {
        if (Player != null) { s_owners.Remove(Player.GetInstanceID()); Player.OnDodgeStart -= OnDodge; }
        Hub.FrenzyStarted -= OnFrenzyStart;
        Hub.FrenzyEnded   -= OnFrenzyEnd;
    }
    private void OnFrenzyStart() => _trail.Clear();
    private void OnDodge()
    {
        if (Hub == null || !Hub.IsFrenzy) return;
        RefundDash();
        _until = Time.time + Sweep; _sweeping = true; _hit.Clear();
        _from = Player.transform.position;
    }
    public override void Tick(float dt, PlayerController player)
    {
        if (!_sweeping || Hub == null) return;
        foreach (var mb in Near(player.transform.position, Radius, player.gameObject, 8))
        {
            if (!_hit.Add(mb.GetInstanceID())) continue;
            Cut(mb, Atk * CutRatio);
            if (Line2) Hub.AddBrand(mb, 1, overflowToGrudge: false, ignoreGap: true);
        }
        if (Time.time < _until) return;
        _sweeping = false;
        if (Line3 && _trail.Count < MaxTrail * 2) { _trail.Add(_from); _trail.Add(player.transform.position); }
    }
    private void OnFrenzyEnd()
    {
        if (!Line3 || _trail.Count < 2 || Player == null) { _trail.Clear(); return; }
        int lines = 0, hits = 0;
        for (int i = 0; i + 1 < _trail.Count; i += 2)
        {
            Vector3 d = _trail[i + 1] - _trail[i]; d.y = 0f;
            if (d.magnitude < 0.5f) continue;
            lines++;
            hits += Hub.Wave(_trail[i], d, d.magnitude, Atk * TrailRatio);
        }
        _trail.Clear();
        if (lines > 0) Say(Player.transform.position, "궤적이 베인다");
        Debug.Log($"[LancelotMemory] 광란의 걸음 ③ — 궤적 {lines}줄 · 적중 {hits}");
    }
}

/// <summary>L12 배신의 축제 — 광란 중 처치하면 그 적의 낙인이 5 m 안 2체에 옮는다 · ② 3체 · ③ 옮긴 낙인이 넘치면 원한.</summary>
public sealed class LBetrayalFeastEffect : LancelotFragment
{
    private const float Radius = 5f;
    public LBetrayalFeastEffect() : base("l_betrayal_feast") { }
    public override void OnKill(GameObject deadEnemy, PlayerController player)
    {
        if (Hub == null || !Hub.IsFrenzy) return;
        var mb = AsMonster(deadEnemy);
        int b = LancelotMemoryHub.BrandOf(mb);
        if (b <= 0) return;
        Vector3 at = mb.transform.position;
        var near = new List<MonsterBase>(Near(at, Radius, mb.gameObject, Line2 ? 3 : 2));
        foreach (var e in near)
        {
            ElementVfxPlayer.PlayBeam(RuneElement.Dark, at + Vector3.up, e.transform.position + Vector3.up, 0.3f);
            Hub.AddBrand(e, b, overflowToGrudge: Line3, ignoreGap: true);
        }
        if (near.Count > 0) Say(at, "배신의 축제");
        Debug.Log($"[LancelotMemory] 배신의 축제 — 낙인 {b} → {near.Count}체");
    }
}

// ── 심판 ────────────────────────────────────────────────────────

/// <summary>L13 찢는 심판 — 막타가 찢은 낙인 적끼리 「배신의 실」로 이어져 실 위 적이 모두 베인다 · ② 실 위 적 낙인 +1 · ③ 실이 3줄 이상이면 막타 범위 2배.</summary>
public sealed class LTearingJudgmentEffect : LancelotFragment
{
    private const float ThreadRatio = 0.5f, ThreadHalfWidth = 0.8f, ConeRange = 6f, ConeHalf = 45f;
    private readonly List<MonsterBase> _order = new(16);
    private readonly HashSet<int> _cut = new();
    public LTearingJudgmentEffect() : base("l_tearing_judgment") { }
    protected override void Bind()   { Hub.BeforeFinisher += OnBefore; Hub.FinisherLanded += OnLanded; }
    protected override void Unbind() { Hub.BeforeFinisher -= OnBefore; Hub.FinisherLanded -= OnLanded; }
    private void OnBefore(Vector3 pos, Vector3 fwd)
    {
        if (!Line3 || Hub.Relic == null) return;
        int n = CombatQuery.GetEnemiesInCone(pos, fwd, ConeRange, ConeHalf, 16, Buf, showGuide: false);
        int branded = 0;
        for (int i = 0; i < n; i++) if (LancelotMemoryHub.BrandOf(Buf[i]) > 0) branded++;
        if (branded < 4) return;   // 실 3줄 = 낙인 적 4
        Hub.Relic.FinisherRangeScale = 2f;
        Say(pos, "배신의 실 — 막타가 넓어진다");
        Debug.Log($"[LancelotMemory] 찢는 심판 ③ — 낙인 적 {branded} → 막타 범위 2배");
    }
    private void OnLanded(Vector3 pos, Vector3 fwd)
    {
        _order.Clear();
        foreach (var t in Hub.TornTargets) if (t != null && t.isActiveAndEnabled) _order.Add(t);
        if (_order.Count < 2) return;
        _order.Sort((a, b) => (a.transform.position - pos).sqrMagnitude.CompareTo((b.transform.position - pos).sqrMagnitude));
        _cut.Clear();
        int threads = 0, cut = 0;
        for (int i = 0; i + 1 < _order.Count; i++)
        {
            Vector3 a = _order[i].transform.position, b = _order[i + 1].transform.position;
            ElementVfxPlayer.PlayBeam(RuneElement.Dark, a + Vector3.up, b + Vector3.up, 0.45f);
            threads++;
            Vector3 mid = (a + b) * 0.5f;
            float half = Vector3.Distance(a, b) * 0.5f;
            foreach (var e in new List<MonsterBase>(Near(mid, half + ThreadHalfWidth + 0.5f, null, 16)))
            {
                if (LancelotMemoryHub.DistToSegmentXZ(e.transform.position, a, b) > ThreadHalfWidth + 0.4f) continue;
                if (!_cut.Add(e.GetInstanceID())) continue;
                cut++;
                CombatQuery.DealSynergyDamage(e, Atk * ThreadRatio, Owner, 1f, false, RuneElement.Dark);
                if (Line2) Hub.AddBrand(e, 1, overflowToGrudge: false, ignoreGap: true);
            }
        }
        Say(pos, "배신의 실");
        Debug.Log($"[LancelotMemory] 찢는 심판 — 실 {threads}줄 · 벤 적 {cut}");
    }
}

/// <summary>L14 원한의 칼날 — 원한 상한 5 → 8 · 원한이 가득 차면 다음 막타가 화면 안 모든 낙인 적에게 · ② 막타의 X자 = 원한 수 × 낙인 수 · ③ 원한을 쓴 심판 뒤 광란 +2초.</summary>
public sealed class LGrudgeBladeEffect : LancelotFragment
{
    private const int   Cap = 8;
    private const float FrenzyBonus = 2f;
    private bool _everywhere;
    public LGrudgeBladeEffect() : base("l_grudge_blade") { }
    protected override void Bind()
    {
        Hub.Grudge?.SetBaseCap(Cap);
        Hub.BeforeFinisher += OnBefore; Hub.FinisherLanded += OnLanded; Hub.PreTear += OnPreTear; Hub.GrudgeSpent += OnSpent;
    }
    protected override void Unbind()
    {
        Hub.Grudge?.SetBaseCap(LancelotMemoryHub.GrudgeCap);
        Hub.BeforeFinisher -= OnBefore; Hub.FinisherLanded -= OnLanded; Hub.PreTear -= OnPreTear; Hub.GrudgeSpent -= OnSpent;
    }
    private void OnBefore(Vector3 pos, Vector3 fwd) => _everywhere = Hub.Grudge != null && Hub.Grudge.IsFull;
    private void OnLanded(Vector3 pos, Vector3 fwd)
    {
        if (!_everywhere || Hub.Relic == null) return;
        _everywhere = false;
        float dmg = Hub.Relic.LastFinisherDamage;
        int n = 0;
        foreach (var mb in new List<MonsterBase>(Near(Player.transform.position, LancelotMemoryHub.ScreenRadius, Owner, 32)))
        {
            if (LancelotMemoryHub.BrandOf(mb) <= 0 || Hub.WasHitByFinisher(mb)) continue;
            n++;
            Hub.MarkFinisherHit(mb);
            Vector3 at = mb.transform.position;
            RelicStateVfx.PlayOneShot(LancelotMemoryHub.SlashVfxKey, at + Vector3.up * 0.9f, 0.45f, fwd, 1.2f);
            Hub.Tear(mb, LancelotMemoryHub.TearCause.Judgment);
            CombatDamage.Deal(new CombatDamage.Request
            {
                Target              = mb.gameObject,
                BaseDamage          = dmg,
                Owner               = Owner,
                ActionType          = WeaponActionType.QSkill,
                KnockbackMultiplier = 0.3f,
                HitPoint            = at + Vector3.up * 1.2f,
                SourcePosition      = pos,
            });
        }
        if (n > 0) Say(pos, "원한의 칼날");
        Debug.Log($"[LancelotMemory] 원한의 칼날 ① — 원한 가득 → 콘 밖 낙인 적 {n}체에 막타 {dmg:0}");
    }
    private void OnPreTear(LancelotMemoryHub.TearContext ctx)
    {
        if (Line2 && ctx.Cause == LancelotMemoryHub.TearCause.Judgment)
            ctx.XMultiplier *= Mathf.Max(1, Hub.GrudgeAtFinisher);
    }
    private void OnSpent(int n)
    {
        if (!Line3 || n <= 0 || !Hub.IsFrenzy) return;
        Hub.Madness.ExtendFrenzy(FrenzyBonus);
        Say(Player.transform.position, "광란 +2초");
    }
}

/// <summary>L15 배신자의 낙인 — 심판에 맞은 적이 체력 20% 이하면 처형 · 처형한 적의 낙인은 원한으로 · ② 처형 자리 붉은 균열(지나가는 적 낙인 상한) · ③ 보스는 찢을 때 낙인을 두 배로 센다.</summary>
public sealed class LBetrayerBrandEffect : LancelotFragment
{
    private const float Execute = 0.2f, CrackRadius = 2f, CrackLife = 4f;
    private const int   MaxCracks = 4;
    private struct Crack { public Vector3 at; public float until; public float nextVfx; }
    private readonly List<Crack> _cracks = new(MaxCracks);
    private readonly HashSet<int> _cracked = new();
    private float _tick;
    public LBetrayerBrandEffect() : base("l_betrayer_brand") { }
    protected override void Bind()   => Hub.PreTear += OnPreTear;
    protected override void Unbind() => Hub.PreTear -= OnPreTear;
    public override void OnHit(in HitInfo hit, PlayerController player)
    {
        if (hit.ActionType != WeaponActionType.QSkill || Hub == null) return;
        var mb = AsMonster(hit.Target);
        if (mb == null || mb.IsDead || mb.Grade == MonsterGrade.Boss || mb.EffectiveMaxHp <= 0) return;
        if ((float)mb.CurrentHp / mb.EffectiveMaxHp > Execute) return;
        var st = RelicMarkStatus.Of(mb.gameObject, false);
        int b = st != null ? st.ConsumeBrand() : 0;
        Vector3 at = mb.transform.position;
        ElementVfxPlayer.PlayBurst(RuneElement.Dark, at, 1.5f);
        mb.TakeDamage(mb.CurrentHp * 10f, player.gameObject, 0.3f);   // 처형(오버킬)
        if (b > 0) Hub.SaveGrudge(b);
        Say(at, "처형");
        Debug.Log($"[LancelotMemory] 배신자의 낙인 — 처형 {mb.name} · 낙인 {b} → 원한");
        if (Line2 && _cracks.Count < MaxCracks)
        {
            _cracks.Add(new Crack { at = at, until = Time.time + CrackLife, nextVfx = Time.time + 1f });
            ElementVfxPlayer.PlayBurst(RuneElement.Fire, at, CrackRadius);
        }
    }
    public override void Tick(float dt, PlayerController player)
    {
        if (_cracks.Count == 0 || Hub == null) return;
        _tick -= dt;
        if (_tick > 0f) return;
        _tick = 0.25f;
        for (int i = _cracks.Count - 1; i >= 0; i--)
        {
            var c = _cracks[i];
            if (Time.time >= c.until) { _cracks.RemoveAt(i); continue; }
            if (Time.time >= c.nextVfx)
            {
                c.nextVfx = Time.time + 1f;
                _cracks[i] = c;
                ElementVfxPlayer.PlayBurst(RuneElement.Fire, c.at, CrackRadius * 0.6f);
            }
            int n = CombatQuery.GetNearbyEnemies(c.at, CrackRadius, null, 8, Buf);
            for (int k = 0; k < n; k++)
            {
                var mb = Buf[k];
                if (mb == null || mb.IsDead || !_cracked.Add(mb.GetInstanceID())) continue;
                Hub.FillBrand(mb);
            }
        }
        if (_cracks.Count == 0) _cracked.Clear();
    }
    private void OnPreTear(LancelotMemoryHub.TearContext ctx)
    {
        if (Line3 && ctx.Cause == LancelotMemoryHub.TearCause.Judgment && ctx.Target != null && ctx.Target.Grade == MonsterGrade.Boss)
            ctx.Stacks *= 2;
    }
}

/// <summary>
/// L16 두 번째 심판 — 심판 한 번에 낙인 6개 이상 찢으면 0.35초 뒤 <b>곧바로 한 방 심판</b>이 한 번 더 떨어진다(광란당 1번) · ② 두 번째 심판은 원한을 쓰지 않는다 · ③ 두 번째 심판 뒤 광란 +2초.
/// 처음엔 심판 횟수 +1(3.6초 시전 한 번 더)이었는데, 낙인 · 원한을 막 다 쓴 직후라 약하고 그동안 평타를 못 쳐 오히려 DPS가 줄었다
/// (10-03 예산 실측: 단독 허브보다 −17%). 가져서 손해인 조각은 만들지 않는다(설계 §8) → 시전 없는 막타 몫 한 방으로.
/// </summary>
public sealed class LSecondJudgmentEffect : LancelotFragment
{
    private const int   Need = 6;
    private const float FrenzyBonus = 2f;
    private const float FollowDelay = 0.35f;
    private bool  _granted, _pendingSecond, _inSecond;
    private float _fireAt = -1f;
    public LSecondJudgmentEffect() : base("l_second_judgment") { }
    protected override void Bind()   { Hub.FrenzyStarted += OnFrenzy; Hub.JudgmentBegan += OnBegan; Hub.JudgmentFinished += OnFinished; }
    protected override void Unbind() { Hub.FrenzyStarted -= OnFrenzy; Hub.JudgmentBegan -= OnBegan; Hub.JudgmentFinished -= OnFinished; }
    private void OnFrenzy() { _granted = _pendingSecond = _inSecond = false; _fireAt = -1f; }
    private void OnBegan()
    {
        _inSecond = _pendingSecond;
        _pendingSecond = false;
        if (_inSecond && Line2) Hub.SkipGrudgeThisJudgment = true;
    }
    private void OnFinished(Vector3 pos, Vector3 fwd, int torn)
    {
        if (_inSecond)
        {
            _inSecond = false;
            if (Line3 && Hub.IsFrenzy)
            {
                Hub.Madness.ExtendFrenzy(FrenzyBonus);
                Say(pos, "광란 +2초");
            }
            return;
        }
        if (_granted || torn < Need || !Hub.IsFrenzy || Hub.Relic == null) return;
        _granted = true;
        _fireAt = Time.time + FollowDelay;   // 막 끝난 심판의 사건 안에서 다시 심판을 부르지 않는다(원한 쓰기 순서가 꼬인다)
        Say(pos, "두 번째 심판");
        Debug.Log($"[LancelotMemory] 두 번째 심판 — 찢은 낙인 {torn} ≥ {Need} → {FollowDelay}초 뒤 한 방 심판");
    }
    public override void Tick(float dt, PlayerController player)
    {
        if (_fireAt < 0f || Time.time < _fireAt) return;
        _fireAt = -1f;
        if (Hub == null || Hub.Relic == null || !Hub.IsFrenzy || player == null) return;
        _pendingSecond = true;
        Hub.Relic.PerformJudgmentStrike(player.transform, 0, 1, LancelotMadnessRelic.InstantJudgmentShare);
    }
}
