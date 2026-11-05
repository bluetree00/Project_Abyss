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
/// E 회전 베기만 판정 원 전체에 속성 장판(2초 · <see cref="SpawnField"/>)을 깐다. 베기 이펙트도 속성 색(<see cref="ElementSlashTint"/>). 지속 = 6초 + 재련 n × 0.5초 + 「속성 검 연장」(<see cref="DurationFor"/>) · 재사용 9초는
/// 속성 검이 끝난 뒤부터 센다 — 켜져 있는 동안 R은 다시 쓸 수 없고(<see cref="LocksRecast"/>) 시전 끝의 재사용은 꺼지는 순간으로 미룬다(<see cref="DeferCooldown"/>)
/// (11-05 사용자 「대검 쿨타임이 짧다」 — 시전 끝에서 흐르던 9초가 버프와 겹쳐 실제 대기 3초 · 「이점이 좋아야」 → 켜진 동안 평타 피해 +<see cref="BasicBonus"/>).
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
    /// <summary>켜진 동안 평타 피해 +30% — 재사용이 버프 뒤로 밀려 가동률이 준 만큼 켜진 동안을 세게(11-05 사용자 「이점이 좋아야」 · <see cref="CombatDamage"/>가 읽는다).</summary>
    public const float BasicBonus = 0.3f;
    private const float ModelScale = 1.6f, GrowSeconds = 0.15f, ShrinkSeconds = 0.3f, BladeGlow = 1.8f;   // 칼날 속성 빛 = 속성 대표 색 × 1.8(형 빛 0.6보다 또렷하게 · 10-30 게임 화면에서 읽히게 1.2 → 1.8)
    private const float ZoneSeconds = 2f, ZoneTickGap = 0.45f, SameSpot = 1f, HitBurstRadius = 0.8f;   // 장판 수명 · 몬스터마다 틱 간격 · 이만큼 가까우면 같은 자리 · 맞은 적 점 타격 크기
    private const int   MaxZones = 3;
    // 속성별 역할(11-05 사용자 「속성검의 이점을 잘 구성해야 해 속성별로」 — 재사용이 버프 뒤로 밀려 가동률이 준 만큼 켜진 동안 속성마다 뚜렷하게)
    private const float BurnRatio = 0.45f, BurnSpread = 2.5f;                                            // 불 — 화상 공격력 45%/초 · 3초 · 타는 적을 치면 주위로 번짐
    private const int   BurnTicks = 3;
    private const float FrostSlow = 0.4f, FrostSeconds = 1.5f, FreezeSeconds = 1f, FrostHitWindow = 2f,   // 얼음 — 둔화 → 두 번째 적중에 빙결 → 언 적을 치면 깨짐
                        ShatterRatio = 1.2f, BossShatterRatio = 0.8f;                                     //   보스는 얼지 않고 두 번째 적중에 바로 깨짐(약하게)
    private const float ShockRatio = 0.2f, ShockAmp = 0.1f, ShockSeconds = 1.5f, ChainRange = 4f;         // 전기 — 감전(공격력 20% · 받는 피해 +10%)이 4 m 안 적 둘에게 번짐
    private const int   ChainCount = 2;
    private const float PoisonRatio = 0.08f, PoisonBurstMult = 1.5f, PoisonSplash = 2f, PoisonSplashShare = 0.5f;   // 풀 — 독 8%/중첩 · 8중첩이면 남은 독 × 1.5가 터짐(주위 2 m에 절반)
    private const int   PoisonMax = 8, PoisonTicks = 4;
    private const float LightCritChance = 0.8f, LightSeconds = 2f;                                        // 빛 — 표식 2초 · 표식 위 평타 80%가 치명 피해를 더 준다
    private const float DarkStackAmp = 0.05f, DarkSeconds = 3f, DarkBurstPerStack = 0.3f, DarkBurstRadius = 2.5f;  // 어둠 — 받는 피해 +5%씩(최대 5) · 쓰러지면 30% × 쌓인 수로 터짐
    private const int   DarkMaxStacks = 5, MaxDarkNotes = 64;
    private const string ZoneTick = "giant_zone", Burn = "giant_burn", Frost = "giant_frost", FrostHit = "giant_frost_hit",
                         Shock = "giant_shock", Poison = "item_poison", LightMark = "giant_light", Dark = "giant_dark", Freeze = "freeze";

    // ── Static ────────────────────────────────────────────────────
    private static readonly List<MonsterBase> s_near = new(16);
#if UNITY_EDITOR
    /// <summary>실측 97 ⑨ — 룬판과 상관없이 이 속성으로(−1 = 룬판).</summary>
    public static int ProbeElement = -1;
    /// <summary>실측 99 W — 고치기 전(버프 중 잠금 · 재사용 미루기 · 평타 보너스 없이 · 시전 끝부터 흐름).</summary>
    public static bool ProbeOldCooldown;
    /// <summary>실측 99 W — 평타 보너스를 이 값으로(−1 = <see cref="BasicBonus"/>).</summary>
    public static float ProbeBasicBonus = -1f;
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
    private float _deferredCd; // 미뤄 둔 R 재사용(초 · 악몽 배율 포함 · 쿨감 전) — 버프가 꺼질 때 시작한다
    private bool _running;     // 켜진 뒤 아직 안 끝났다 — 칼날 이펙트가 못 붙어도(목록 미로드 등) 끝낼 때 칼 크기 · 빛을 되돌린다(10-26 검토 I1)
    private CancellationTokenSource _scaleCts;
    private readonly List<LegendGroundField> _zones = new(MaxZones);
    private Action<MonsterBase> _tick;
    private readonly Dictionary<int, (int n, float until)> _dark = new();   // 어둠 — 몬스터마다 쌓인 수 · 끝나는 때

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

    /// <summary>켜진 동안 평타 피해 배율에 더하는 몫(<see cref="BasicBonus"/>).</summary>
    public static float BasicDamageBonus
    {
        get
        {
#if UNITY_EDITOR
            if (ProbeOldCooldown) return 0f;
            if (ProbeBasicBonus >= 0f) return ProbeBasicBonus;
#endif
            return BasicBonus;
        }
    }

    /// <summary>R을 다시 쓸 수 없나 — 속성 검이 켜져 있는 동안(재사용은 꺼진 뒤부터 · 11-05). 입력 단계(PlayerController.CanUseSkillNow)가 읽는다.</summary>
    public static bool LocksRecast(PlayerController p) => !OldCooldownProbe && IsActive(p);

    /// <summary>
    /// R 시전이 끝날 때(ActSkillStateBase.Exit) — 속성 검이 켜져 있으면 재사용 <paramref name="seconds"/>초를 버프가 꺼지는 순간으로 미룬다(true · 그동안 추적기는 0 —
    /// HUD R 칸이 「켜짐」으로 그린다). 꺼져 있으면 false(바로 시작).
    /// </summary>
    public static bool DeferCooldown(PlayerController p, float seconds)
    {
        if (OldCooldownProbe || seconds <= 0f || p == null || !p.TryGetComponent<GiantSwordBuff>(out var b) || !b.Active) return false;
        b._deferredCd = seconds;
        return true;
    }

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
        // 빌드에서도 남긴다(10-30 「빌드에서 속성 검이 안 보임」 확인 — 시전마다 한 줄 · 재사용 대기 9초라 잦지 않다)
        Debug.Log($"[ElementSword] {Element} · 칼날 이펙트 {(_blade != null ? "붙음" : "없음")} · 재질 {(ElementVfxPlayer.GetBladeMaterial(Element) != null ? ElementVfxPlayer.GetBladeMaterial(Element).name : "없음")} · 칼 {(wi != null ? wi.name : "없음")}");
    }

    private void End(bool instant)
    {
        if (!_running) return;
        _running = false;
        if (_deferredCd > 0f && _player != null && _player.CooldownTracker != null)   // 미뤄 둔 R 재사용 — 버프가 꺼지는 지금부터(일찍 끝남 포함 · 쿨감은 지금 값)
            _player.CooldownTracker.StartCooldown(SkillType.R, _deferredCd, _player.RuntimeStats != null ? _player.RuntimeStats.SkillCooldownReduction : 0f);
        _deferredCd = 0f;
        if (instant && Remaining > 0.05f && _player != null) _player.RaiseSkillBuffStarted(SkillType.R, 0.01f);   // 일찍 끝남(무기 바꿈 · 쓰러짐) — HUD 남은 시간 막대도 바로 끈다
        Remaining = 0f;
        ReleaseBlade();
        if (instant) RestoreModel();
        else if (_model != null) ScaleTo(_modelScale0, ShrinkSeconds);
    }

    private static bool OldCooldownProbe
    {
        get
        {
#if UNITY_EDITOR
            return ProbeOldCooldown;
#else
            return false;
#endif
        }
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
            case RuneElement.Fire:     Ignite(mb, atk, self); break;
            case RuneElement.Ice:      mb.Status.ApplySlow(Frost, FrostSlow, FrostSeconds); break;
            case RuneElement.Electric: Chain(mb, atk, self); break;
            case RuneElement.Grass:    Poisoned(mb, atk, self); break;
            case RuneElement.Light:    mb.Status.SetCooldown(LightMark, LightSeconds); break;
            case RuneElement.Dark:     Darken(mb); break;
        }
        return true;
    }

    /// <summary>불 — 화상(공격력 45%/초 · 3초 · 다시 걸면 3초로). 이미 타는 적을 치면 주위 2.5 m로 번진다(번진 불은 거기서 다시 번지지 않는다).</summary>
    private void Ignite(MonsterBase mb, float atk, GameObject self)
    {
        bool burning = mb.Status.GetRemainingDotDamage(Burn) > 0f;
        mb.Status.ApplyDot(Burn, atk * BurnRatio, 1f, BurnTicks, self);
        if (!burning) return;
        int n = CombatQuery.GetNearbyEnemies(mb.transform.position, BurnSpread, self, 16, s_near);
        for (int i = 0; i < n; i++)
        {
            var e = s_near[i];
            if (e == null || e == mb || e.IsDead) continue;
            e.Status.ApplyDot(Burn, atk * BurnRatio, 1f, BurnTicks, self);
        }
        ElementVfxPlayer.PlayBurst(RuneElement.Fire, mb.transform.position, BurnSpread);   // 그림 = 번지는 반경
    }

    /// <summary>전기 — 맞은 적 감전(공격력 20% · 받는 피해 +10% 1.5초) → 4 m 안 다른 적 둘에게 같은 감전이 번진다.</summary>
    private void Chain(MonsterBase mb, float atk, GameObject self)
    {
        ShockOne(mb, atk, self);
        Log("전류 틱");   // 실측 97 ⑦-b — 자국이 겹쳐도 몬스터마다 0.45초에 한 번(맞은 적 기준)
        int n = CombatQuery.GetNearbyEnemies(mb.transform.position, ChainRange, self, 16, s_near);
        int chained = 0;
        for (int i = 0; i < n && chained < ChainCount; i++)
        {
            var e = s_near[i];
            if (e == null || e == mb || e.IsDead) continue;
            ShockOne(e, atk, self);
            ElementVfxPlayer.PlayBurst(RuneElement.Electric, e.transform.position, HitBurstRadius);   // 번진 자리
            chained++;
        }
        Log($"감전 — 번짐 {chained}");
    }

    private static void ShockOne(MonsterBase e, float atk, GameObject self)
    {
        CombatQuery.DealSynergyDamage(e, atk * ShockRatio, self, element: RuneElement.Electric);
        e.ApplyDamageTakenAmp(ShockAmp, ShockSeconds, Shock);
    }

    /// <summary>풀 — 독 중첩(공격력 8% × 중첩/초 · 4초 · 최대 8). 8중첩이 되면 터진다 — 남은 독 피해 × 1.5를 한 번에 · 2 m 안 다른 적에게 그 절반 · 중첩이 비워진다.</summary>
    private void Poisoned(MonsterBase mb, float atk, GameObject self)
    {
        mb.Status.ApplyStackingDot(Poison, Mathf.Max(1f, atk * PoisonRatio), 1f, PoisonTicks, PoisonMax, self);
        if (mb.Status.GetDotStacks(Poison) < PoisonMax) return;
        float burst = mb.Status.ConsumeDot(Poison, 1f) * PoisonBurstMult;
        if (burst <= 0f) return;
        CombatQuery.DealSynergyDamage(mb, burst, self, element: RuneElement.Grass);
        int n = CombatQuery.GetNearbyEnemies(mb.transform.position, PoisonSplash, self, 16, s_near);
        for (int i = 0; i < n; i++)
        {
            var e = s_near[i];
            if (e == null || e == mb || e.IsDead) continue;
            CombatQuery.DealSynergyDamage(e, burst * PoisonSplashShare, self, element: RuneElement.Grass);
        }
        ElementVfxPlayer.PlayBurst(RuneElement.Grass, mb.transform.position, PoisonSplash);
        Log($"독 터짐 — {burst:0}");
    }

    /// <summary>어둠 — 칠 때마다 받는 피해 +5%가 쌓인다(최대 5 = +25% · 3초 · 칠 때마다 다시 3초).</summary>
    private void Darken(MonsterBase mb)
    {
        if (_dark.Count >= MaxDarkNotes) _dark.Clear();
        int id = mb.GetInstanceID();
        int n = _dark.TryGetValue(id, out var d) && Time.time < d.until ? Mathf.Min(DarkMaxStacks, d.n + 1) : 1;
        _dark[id] = (n, Time.time + DarkSeconds);
        mb.ApplyDamageTakenAmp(DarkStackAmp * n, DarkSeconds, Dark);
    }

    /// <summary>어둠 — 쌓인 적이 평타에 쓰러지면 터진다(2.5 m · 공격력 30% × 쌓인 수).</summary>
    private void DarkBurst(MonsterBase mb)
    {
        int id = mb.GetInstanceID();
        if (!_dark.TryGetValue(id, out var d) || Time.time >= d.until || _player == null) return;
        _dark.Remove(id);
        float dmg = _player.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee) * DarkBurstPerStack * d.n;
        int n = CombatQuery.GetNearbyEnemies(mb.transform.position, DarkBurstRadius, _player.gameObject, 16, s_near);
        for (int i = 0; i < n; i++)
        {
            var e = s_near[i];
            if (e == null || e == mb || e.IsDead) continue;
            CombatQuery.DealSynergyDamage(e, dmg, _player.gameObject, element: RuneElement.Dark);
        }
        ElementVfxPlayer.PlayBurst(RuneElement.Dark, mb.transform.position, DarkBurstRadius);
        Log($"어둠 터짐 — {d.n}중첩 · {dmg:0}");
    }

    /// <summary>얼음 — 깨짐(공격력 × <paramref name="ratio"/>).</summary>
    private void Shatter(MonsterBase mb, float ratio)
    {
        if (_player == null) return;
        CombatQuery.DealSynergyDamage(mb, _player.RuntimeStats.GetEffectiveAttack(AttackStatKind.Melee) * ratio, _player.gameObject, element: RuneElement.Ice);
        ElementVfxPlayer.PlayBurst(RuneElement.Ice, mb.transform.position, HitBurstRadius);
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
        if (mb == null) return;
        if (mb.IsDead) { if (Active && Element == RuneElement.Dark) DarkBurst(mb); return; }   // 어둠 — 쌓인 적이 이 평타에 쓰러졌다
        if (Active && Element == RuneElement.Ice && mb.Status.HasCc(Freeze))   // 얼음 — 언 적을 치면 깨진다(빙결이 풀리고 셈은 처음부터)
        {
            mb.Status.ClearCc(Freeze);
            Shatter(mb, ShatterRatio);
            mb.Status.SetCooldown(FrostHit, 0.01f);
            return;
        }
        if (Active && TryTick(mb))
            ElementVfxPlayer.PlayBurst(Element, mb.transform.position, HitBurstRadius);   // 속성 점 타격(파란 대검 타격 대신 — 베기 쪽은 흰 섬광) · 속성 효과가 걸릴 때만
        if (mb.Status.GetSlowStacks(Frost) > 0)
        {
            if (mb.Status.IsOnCooldown(FrostHit))   // 서리 위 두 번째 적중
            {
                if (mb.Grade != MonsterGrade.Boss) mb.Status.ApplyCc(Freeze, FreezeSeconds);
                else if (Active && Element == RuneElement.Ice) Shatter(mb, BossShatterRatio);   // 보스는 얼지 않는다 — 바로 깨짐(약하게)
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
