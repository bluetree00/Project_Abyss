using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 속성 검(10-26 대검 R 개편 · 설계 기획/RelicFairy_대검R_속성검버프_설계_20261026.md §7) — R에서 검을 뽑으면 칼이 커지고(×1.6) 룬판에 가장 많은 속성(없으면 불)이 붙는다.
/// 지속 동안 평타 범위 ×1.3(<see cref="GreatswordReach"/>)이고, 평타 · R에 맞은 적은 모두 그 자리에서 속성 효과를 받는다(<see cref="HitElement"/> · 속성 점 타격 이펙트) —
/// 범위 안 전체가 속성 피해를 받는 느낌(10-26 사용자 「범위 안 전체에 피해를 준다는 느낌 · 지금은 끝 가장자리만」 · 「장판이 휘두르는 건 톤이 안 맞네 · e는 좋은 것 같아」).
/// E 회전 베기만 판정 원 전체에 속성 장판(2초 · <see cref="SpawnField"/>)을 깐다. 베기 이펙트도 속성 색(<see cref="ElementSlashTint"/>). 지속 = 6초 + 재련 n × 0.5초 + 「속성 검 연장」(<see cref="DurationFor"/>) · 재사용 9초 그대로.
/// 지속 동안 칼 궤적은 속성별 트레일(<see cref="ElementVfxRegistry"/> 칼날 트레일 칸 · 커진 칼의 칼끝 · 칼밑에 묶인다).
/// 「휩쓸기」 증강이 여럿을 맞힌 만큼 늘린다(<see cref="Extend"/> · 한 번 시전에 최대 3초).
/// 남은 시간은 게임 시간이라 멈춘 화면에서 줄지 않는다. 무기를 바꾸거나 쓰러지면 바로 끝난다(자국은 남은 수명대로).
/// 칼날에는 속성 이펙트를 두른다(<see cref="BladeWrapFx"/> — 칼 메시의 자식이라 칼과 같이 커진다 · 10-26 사용자 「검에 두르는 이펙트」).
/// </summary>
[DisallowMultipleComponent]
public sealed class GiantSwordBuff : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    /// <summary>대검 평타 판정 반경(배율 1 — 베기 이펙트 안의 구 2.2 × 단계 크기 1.15). 자국 호의 바깥 반지름 = 이것 × 평타 배율.</summary>
    public const float BaseSwingRadius = 2.5f;
    /// <summary>3타(마지막 타) 판정 반경(배율 1 — 구 2.2 × 단계 크기 1.4) — 자국 호가 그 휘두름 판정에 맞게.</summary>
    public const float FinisherSwingRadius = 3.08f;
    /// <summary>지속 = 이것 + 재련 n × <see cref="PerReforgeSeconds"/> + 「속성 검 연장」 · 「휩쓸기」가 한 번 시전에 늘릴 수 있는 최대.</summary>
    public const float BaseSeconds = 6f, PerReforgeSeconds = 0.5f, MaxExtendSeconds = 3f;   // 10-26 사용자 「r 상태의 평타가 메인 재미 — 시간을 늘리고」 4 → 6초
    /// <summary>속성 검 동안 평타 범위 배율 — R 3단계(+12)면 더 넓다(10-26 「R까지 키면 더 좋은 범위에 효율」).</summary>
    public const float RangeMult = 1.3f, RangeMultTier3 = 1.45f;
    private const float ModelScale = 1.6f, GrowSeconds = 0.15f, ShrinkSeconds = 0.3f, BladeGlow = 1.2f;   // 칼날 속성 빛 = 속성 대표 색 × 1.2(형 빛 0.6보다 또렷하게)
    private const float ZoneSeconds = 2f, ZoneTickGap = 0.45f, SameSpot = 1f, HitBurstRadius = 0.8f;   // 장판 수명 · 몬스터마다 틱 간격 · 이만큼 가까우면 같은 자리 · 맞은 적 점 타격 크기
    private const int   MaxZones = 3;
    private const float BurnRatio = 0.2f, FrostSlow = 0.4f, ShockRatio = 0.12f, ShockAmp = 0.1f, PoisonRatio = 0.04f,
                        DarkAmp = 0.12f, LightCritChance = 0.2f, StatusSeconds = 0.6f, FreezeSeconds = 1f, FrostHitWindow = 2f;
    private const string ZoneTick = "giant_zone", Burn = "giant_burn", Frost = "giant_frost", FrostHit = "giant_frost_hit",
                         Shock = "giant_shock", Poison = "item_poison", LightMark = "giant_light", Dark = "giant_dark";

    // ── Static ────────────────────────────────────────────────────
#if UNITY_EDITOR
    /// <summary>실측 97 ⑨ — 룬판과 상관없이 이 속성으로(−1 = 룬판).</summary>
    public static int ProbeElement = -1;
#endif

    // ── Private ───────────────────────────────────────────────────
    private PlayerController _player;
    private PlayerAnimationEventReceiver _receiver;
    private PlayerWeaponTrailVfx _trail;
    private GameObject _trailPrefab;   // 지금 덮어쓴 속성 트레일(끝낼 때 내 것만 거둔다)
    private Transform _model;
    private Vector3 _modelScale0;
    private BladeWrapFx _blade;
    private float _extended;   // 이번 시전에 「휩쓸기」로 늘린 초
    private bool _running;     // 켜진 뒤 아직 안 끝났다 — 칼날 이펙트가 못 붙어도(목록 미로드 등) 끝낼 때 칼 크기 · 빛을 되돌린다(10-26 검토 I1)
    private CancellationTokenSource _scaleCts;
    private readonly List<LegendGroundField> _zones = new(MaxZones);
    private Action<MonsterBase> _tick;

    // ── Properties ────────────────────────────────────────────────
    public bool Active => Remaining > 0f;
    public float Remaining { get; private set; }
    public RuneElement Element { get; private set; }
    /// <summary>지금 평타 판정 반경(m) — 자국 호의 바깥 반지름.</summary>
    public float SwingRadius => BaseSwingRadius * GreatswordReach.Basic(_player);

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        TryGetComponent(out _player);
        TryGetComponent(out _trail);
        _receiver = GetComponentInChildren<PlayerAnimationEventReceiver>();
        _tick = Tick;
    }

    private void OnEnable()
    {
        if (_player != null && _player.WeaponManager != null) _player.WeaponManager.OnWeaponChanged += OnWeaponChanged;
        HitFeedbackService.OnHit += OnHit;
    }

    private void Update()
    {
        if (Remaining <= 0f) return;
        if (_player == null || _player.RuntimeStats == null || _player.RuntimeStats.Hp <= 0) { End(true); return; }
        Remaining -= Time.deltaTime;
        if (Remaining <= 0f) End(false);
    }

    private void OnDisable()
    {
        if (_player != null && _player.WeaponManager != null) _player.WeaponManager.OnWeaponChanged -= OnWeaponChanged;
        HitFeedbackService.OnHit -= OnHit;
        End(true);
    }

    private void OnDestroy()
    {
        _scaleCts?.Cancel();
        _scaleCts?.Dispose();
        _scaleCts = null;
    }

    // ── Public Methods ────────────────────────────────────────────
    public static bool IsActive(PlayerController p) => p != null && p.TryGetComponent<GiantSwordBuff>(out var b) && b.Active;

    /// <summary>이 무기의 속성 검 지속(초) — 6초 + 진화 뒤 재련 n × 0.5초 + 「속성 검 연장」(10-26 사용자 「재련 내용에 맞게 유지」 · 「시간을 늘리고」).</summary>
    public static float DurationFor(WeaponData w)
        => BaseSeconds + PerReforgeSeconds * MeleeAugmentCatalog.ReforgesSinceEvolution(w) + MeleeAugments.ValueOn(w, MeleeAugmentCatalog.GreatFinal);

    /// <summary>속성 검 동안 평타 범위 배율(R 3단계면 ×1.45).</summary>
    public static float RangeMultFor(WeaponData w) => SkillTierResolver.Resolve(w) >= 3 ? RangeMultTier3 : RangeMult;

    /// <summary>이 콤보 단계 평타의 판정 반경(m) — 3타는 더 크다 · 범위 배율(속성 검 ×1.3 · 「거대한 칼날」) 포함.</summary>
    public static float SwingRadiusFor(PlayerController p, int comboStep)
        => (comboStep >= 2 ? FinisherSwingRadius : BaseSwingRadius) * GreatswordReach.Basic(p);

    /// <summary>속성 검 지속을 늘린다(한 번 시전에 최대 <see cref="MaxExtendSeconds"/>) — 실제로 늘린 초. 꺼져 있으면 0.</summary>
    public static float Extend(PlayerController p, float seconds)
    {
        if (seconds <= 0f || p == null || !p.TryGetComponent<GiantSwordBuff>(out var b) || !b.Active) return 0f;
        float add = Mathf.Min(seconds, MaxExtendSeconds - b._extended);
        if (add <= 0f) return 0f;
        b._extended  += add;
        b.Remaining  += add;
        p.RaiseSkillBuffStarted(SkillType.R, b.Remaining);   // HUD 남은 시간 막대를 늘어난 시간으로 다시 채운다
        return add;
    }

    /// <summary>지금 속성 검의 속성(꺼져 있으면 null) — 베기 이펙트 색.</summary>
    public static RuneElement? ElementOf(PlayerController p)
        => p != null && p.TryGetComponent<GiantSwordBuff>(out var b) && b.Active ? b.Element : (RuneElement?)null;

    /// <summary>속성 검 휘두름에 맞은 적 — 그 자리에서 속성 효과(장판 틱과 같은 것 · 몬스터마다 0.45초에 한 번) + 속성 점 타격 이펙트.</summary>
    public static void HitElement(PlayerController p, GameObject target)
    {
        if (p == null || target == null || !p.TryGetComponent<GiantSwordBuff>(out var b) || !b.Active) return;
        var mb = target.GetComponentInParent<MonsterBase>();
        if (mb == null || mb.IsDead) return;
        if (b.TryTick(mb)) ElementVfxPlayer.PlayBurst(b.Element, mb.transform.position, HitBurstRadius);   // 그림 = 판정(속성 효과가 걸릴 때만 · 10-26 검토 M2)
    }

    /// <summary>E 회전 베기의 속성 장판 — 판정 원 전체(반경 = 판정 반경). 속성 검이 꺼져 있으면 안 깐다(10-26 「e스킬도 r에 맞게」 · 「e는 좋은 것 같아」).</summary>
    public static void SpawnField(PlayerController p, float radius)
    {
        if (p != null && p.TryGetComponent<GiantSwordBuff>(out var b) && b.Active) b.SpawnZone(radius);
    }

    /// <summary>R 착지 순간 — 칼을 키우고 속성을 붙인다(지속 중 다시 부르면 지속만 새로 · 배율은 겹치지 않는다).</summary>
    public static void Begin(PlayerController p, float seconds)
    {
        if (p == null || seconds <= 0f) return;
        if (!p.TryGetComponent<GiantSwordBuff>(out var b)) b = p.gameObject.AddComponent<GiantSwordBuff>();
        b.StartBuff(seconds);
    }

    // ── Private Methods ───────────────────────────────────────────
    private void StartBuff(float seconds)
    {
        Element   = ElementEdgeAugment.TopElement(out var el) ? el : RuneElement.Fire;
#if UNITY_EDITOR
        if (ProbeElement >= 0) Element = (RuneElement)ProbeElement;
#endif
        Remaining = seconds;
        _extended = 0f;
        _running  = true;
        var wi = _player.WeaponManager != null ? _player.WeaponManager.GetCurrentWeaponComponent<WeaponInstance>() : null;
        if (wi != null && _model != wi.transform)
        {
            RestoreModel();
            _model = wi.transform;
            _modelScale0 = _model.localScale;
        }
        if (_model != null) ScaleTo(_modelScale0 * ModelScale, GrowSeconds);
        ReleaseBlade();
        _blade = BladeWrapFx.Attach(wi, Element);   // 칼날에 속성 이펙트 — 칼 메시의 자식이라 칼과 같이 커지고, 끝날 때 직접 거둔다(버프는 게임 시간)
        WeaponFormRuntime.Of(_player)?.SetExtraGlow(ElementPalette.Core(Element) * BladeGlow);   // 칼날이 속성 색으로 빛난다
        BeginTrail(wi);   // 칼 궤적 = 속성 트레일 — 커진 칼의 칼끝 · 칼밑(10-26 사용자 「트레일도 큰 검에 맞게 · 속성별」)
        _player.RaiseSkillBuffStarted(SkillType.R, seconds);   // HUD 남은 시간 막대(석궁 강화와 같은 길)
        Log($"속성 검 — {Element} · {seconds:0.0}초 · 칼날 이펙트 {_blade != null}");
    }

    private void End(bool instant)
    {
        if (!_running) return;
        _running = false;
        if (instant && Remaining > 0.05f && _player != null) _player.RaiseSkillBuffStarted(SkillType.R, 0.01f);   // 일찍 끝남(무기 바꿈 · 쓰러짐) — HUD 남은 시간 막대도 바로 끈다
        Remaining = 0f;
        ReleaseBlade();
        if (instant) RestoreModel();
        else if (_model != null) ScaleTo(_modelScale0, ShrinkSeconds);
    }

    private void SpawnZone(float swingR)
    {
        var w = _player.WeaponManager != null ? _player.WeaponManager.CurrentWeaponData : null;
        float radius = swingR * (1f + MeleeAugments.Value2On(w, MeleeAugmentCatalog.GreatFinal) * 0.01f);   // 「속성 검 연장」 = 장판 반경 +%
        Vector3 c = _player.transform.position;
        _zones.RemoveAll(z => z == null);
        for (int i = 0; i < _zones.Count; i++)
        {
            var z = _zones[i];
            if (Vector3.Distance(z.Center, c) >= SameSpot) continue;
            if (z.Radius >= radius - 0.01f) { z.Refresh(ZoneSeconds); return; }   // 같은 자리 · 같거나 큰 장판 — 수명만
            Destroy(z.gameObject);   // 같은 자리의 작은 장판 — 큰 것으로 바꾼다(겹쳐 그리지 않게 · 10-26 검토 M3)
            _zones.RemoveAt(i);
            break;
        }
        Log($"속성 장판 — 반경 {radius:0.00}");
        while (_zones.Count >= MaxZones) { if (_zones[0] != null) Destroy(_zones[0].gameObject); _zones.RemoveAt(0); }
        _zones.Add(LegendGroundField.SpawnCircle(this, _player.gameObject, c, radius, ZoneSeconds, Element, _tick));
    }

    private void ReleaseBlade()
    {
        if (_blade != null) _blade.Release();
        _blade = null;
        if (_player != null) WeaponFormRuntime.Of(_player)?.SetExtraGlow(Color.clear);
        if (_trail == null && _player != null) TryGetComponent(out _trail);   // 플레이어가 트레일 구동기를 늦게 붙인다
        if (_trailPrefab != null && _trail != null && _trail.OverridePrefab == _trailPrefab) _trail.EndOverride();   // 남의 트레일(랜슬롯 Q 검 · 형)은 끄지 않는다
        _trailPrefab = null;
    }

    private void BeginTrail(WeaponInstance wi)
    {
        if (_trail == null) TryGetComponent(out _trail);
        var prefab = ElementVfxPlayer.GetBladeTrail(Element);
        if (_trail == null || prefab == null || wi == null || wi.tipPoint == null || wi.rootPoint == null) return;
        _trail.BeginOverride(wi.tipPoint, wi.rootPoint, prefab);
        _trailPrefab = prefab;
    }

    private void RestoreModel()
    {
        _scaleCts?.Cancel();
        if (_model != null) _model.localScale = _modelScale0;
    }

    private void ScaleTo(Vector3 to, float seconds)
    {
        _scaleCts?.Cancel();
        _scaleCts?.Dispose();
        _scaleCts = new CancellationTokenSource();
        ScaleAsync(_model, to, seconds, _scaleCts.Token).Forget();
    }

    private static async UniTaskVoid ScaleAsync(Transform t, Vector3 to, float seconds, CancellationToken ct)
    {
        try
        {
            Vector3 from = t.localScale;
            for (float e = 0f; e < seconds; e += Time.deltaTime)
            {
                if (t == null) return;
                t.localScale = Vector3.Lerp(from, to, e / seconds);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            if (t != null) t.localScale = to;
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>장판 틱 · 휘두름 적중의 속성 효과 — 몬스터마다 0.45초에 한 번(겹쳐도 한 번 · 93 판단).</summary>
    private void Tick(MonsterBase mb) => TryTick(mb);

    /// <summary><see cref="Tick"/> — 이번에 실제로 걸었으면 true(점 타격 그림을 그때만 낸다).</summary>
    private bool TryTick(MonsterBase mb)
    {
        if (mb == null || mb.IsDead || mb.Status.IsOnCooldown(ZoneTick) || _player == null) return false;
        mb.Status.SetCooldown(ZoneTick, ZoneTickGap);
        float atk = _player.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee);
        var self = _player.gameObject;
        switch (Element)
        {
            case RuneElement.Fire:     mb.Status.ApplyDot(Burn, atk * BurnRatio, 1f, 1, self); break;
            case RuneElement.Ice:      mb.Status.ApplySlow(Frost, FrostSlow, StatusSeconds); break;
            case RuneElement.Electric:
                CombatQuery.DealSynergyDamage(mb, atk * ShockRatio, self, element: RuneElement.Electric);
                mb.ApplyDamageTakenAmp(ShockAmp, StatusSeconds, Shock);
                Log("전류 틱");   // 실측 97 ⑦-b — 자국이 겹쳐도 몬스터마다 0.45초에 한 번
                break;
            case RuneElement.Grass:    mb.Status.ApplyStackingDot(Poison, Mathf.Max(1f, atk * PoisonRatio), 1f, 4, 8, self); break;
            case RuneElement.Light:    mb.Status.SetCooldown(LightMark, StatusSeconds); break;
            case RuneElement.Dark:     mb.ApplyDamageTakenAmp(DarkAmp, StatusSeconds, Dark); break;
        }
        return true;
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private static void Log(string msg) => Debug.Log("[GiantSword] " + msg);

    // ── Event Handlers ────────────────────────────────────────────

    /// <summary>대검 평타 적중 — 속성 검이면 그 적에게 속성 효과 · 점 타격(범위 안 전체) · 얼음이 걸린 적의 두 번째 적중이면 빙결(보스 제외) · 빛이 걸린 적이면 20% 확률로 치명 몫을 더 준다.</summary>
    private void OnHit(HitInfo h)
    {
        if (_player == null || h.Attacker != _player.gameObject || h.Target == null) return;
        if (!Active && _zones.Count == 0) return;   // 버프도 장판도 없다 — 몬스터 찾기 전에 빠진다
        if (h.ActionType != WeaponActionType.GroundLight && h.ActionType != WeaponActionType.AirLight) return;
        if (MeleeAugmentCatalog.KindOf(_player.WeaponManager != null ? _player.WeaponManager.CurrentWeaponData : null) != MeleeWeaponKind.Greatsword) return;
        var mb = h.Target.GetComponentInParent<MonsterBase>();
        if (mb == null || mb.IsDead) return;
        if (Active && TryTick(mb))
            ElementVfxPlayer.PlayBurst(Element, mb.transform.position, HitBurstRadius);   // 속성 점 타격(파란 대검 타격 대신 — 베기 쪽은 흰 섬광) · 속성 효과가 걸릴 때만
        if (mb.Status.GetSlowStacks(Frost) > 0)
        {
            if (mb.Status.IsOnCooldown(FrostHit))   // 서리 위 두 번째 적중
            {
                if (mb.Grade != MonsterGrade.Boss) mb.Status.ApplyCc("freeze", FreezeSeconds);
                mb.Status.SetCooldown(FrostHit, 0.01f);   // 셈을 비운다 — 다음 적중이 다시 첫 번째
            }
            else mb.Status.SetCooldown(FrostHit, FrostHitWindow);
        }
        if (mb.Status.IsOnCooldown(LightMark) && !h.IsCritical && UnityEngine.Random.value < LightCritChance)
        {
            var w = _player.WeaponManager != null ? _player.WeaponManager.CurrentWeaponData : null;
            float mult = (w != null ? w.critDamage : 1.25f) + _player.RuntimeStats.CritDamageBonus;
            float bonus = h.Damage * Mathf.Max(0f, mult - 1f);
            if (bonus > 0f) CombatQuery.DealSynergyDamage(mb, bonus, _player.gameObject, 1f, isCrit: true, element: RuneElement.Light);
        }
    }

    /// <summary>무기가 바뀌었다 — 바로 끝(칼 원래 크기 · 오라 끔). 자국은 남은 수명대로 사라진다.</summary>
    private void OnWeaponChanged(WeaponData w, GameObject instance)
    {
        End(true);
        _model = null;
    }
}
