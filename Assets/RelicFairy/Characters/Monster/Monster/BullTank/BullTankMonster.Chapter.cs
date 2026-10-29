using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 중간 보스 챕터별 개성(10-16 사용자 「겉모습은 챕터 톤 · 패턴마다 강화 하나씩 · 동작에 맞는 이펙트 · 제대로 된 이름」 —
/// 기획/RelicFairy_중간보스_챕터별개성_설계_20261016.md). 챕터 = 프리팹 이름(BullTank_ChN) — 없으면 런의 현재 챕터.
///   1 숲   — 내려찍은 부채꼴에 가시 · 착지 둘레로 더 넓게 뿌리 · 지나간 줄에 가시(예고 뒤 한 번)
///   2 불   — 내려찍은 부채꼴 · 착지 원 · 지나간 줄에 불길이 남는다(약 틱)
///   3 흑백 — 그림자가 같은 부채꼴을 한 번 더 · 플레이어 자리에 그림자 착지 · 같은 줄로 그림자 돌진
///   4 공허 — 부채꼴 끝 공허 구슬이 터짐 · 착지 자리 공허가 더 넓게 터짐 · 돌진 끝 공허 폭발
/// 규칙(보스와 같음): 예고 = 판정(모양 · 크기 · 시점) · 앞섬 0.45초 이상 · 남는 장판은 약 틱 · 흡혈 없음.
/// </summary>
public partial class BullTankMonster
{
    // ── Constants ──────────────────────────────────────────────
    public enum Theme { Forest = 1, Fire = 2, Shadow = 3, Void = 4 }

    private const float FollowLead      = 0.6f;   // 예고 뒤 한 번 치는 강화 — 예고 → 판정
    private const float ShadowLead      = 0.75f;  // 그림자 일격
    private const float VoidLead        = 0.9f;   // 공허 터짐
    private const float ThornDamageMul  = 0.6f;
    private const float FireTick        = 0.5f;
    private const float FireDamageMul   = 0.12f;  // 약 틱(서 있으면 아프고 지나가면 거의 안 아프다)
    private const float FireArm         = 0.5f;   // 불이 붙고 첫 틱까지(지나가는 순간엔 안 맞게)
    private const float FireSeconds     = 3f;
    private const float ShadowLateralAtOne = 2.4f;   // 거울 베기(Sword Slash mirror) 배율 1의 옆 폭(10-18 FX Fit 2.39) — 줄 판정 반폭에 맞출 때
    private const float FirePointRadius = 0.8f;   // _fireVfxScale이 맞춘 불 한 점의 보이는 반경(LineStep의 절반 · 10-18 FX Fit: FlameTsunami ×1 = 16 m → ×0.05)
    private const float ShadowDamageMul = 0.8f;   // 원래 공격 배율에 곱
    private const float VoidDamageMul   = 0.9f;
    private const float VoidSfxVolume   = 0.55f;  // 공허 후속타 소리 — 착지음을 그 자리에서 작게(10-29 Ch4 사운드 점검 — 무음이었다)
    private const float LineStep        = 1.6f;   // 줄 위 이펙트 간격(m)

    // ── Serialized ─────────────────────────────────────────────
    [Header("챕터 개성 — 동작 이펙트(10-16)")]
    [Tooltip("내려찍기 궤적 — 휘두르는 순간 부채꼴을 따라(모든 챕터 · 색만 챕터)")]
    [SerializeField] private GameObject _slashVfx;
    [SerializeField] private float      _slashVfxScale = 1f;
    [Header("챕터 개성 — 강화 이펙트(10-16)")]
    [SerializeField] private GameObject _thornVfx;
    [SerializeField] private float      _thornVfxScale = 1f;
    [SerializeField] private GameObject _fireVfx;
    [SerializeField] private float      _fireVfxScale = 1f;
    [SerializeField] private GameObject _shadowVfx;
    [SerializeField] private float      _shadowVfxScale = 1f;
    [Tooltip("공허 터짐 — 배율은 반경 2 m 기준(코드가 반경에 맞춰 곱한다)")]
    [SerializeField] private GameObject _voidVfx;
    [SerializeField] private float      _voidVfxScale = 1f;
    [Tooltip("공허 구슬(터지기 전 부채꼴 끝에 떠 있는 것)")]
    [SerializeField] private GameObject _voidOrbVfx;
    [SerializeField] private float      _voidOrbVfxScale = 1f;

    // ── Private ────────────────────────────────────────────────
    private Theme _theme = Theme.Forest;
    private CancellationTokenSource _kitCts;
    private static readonly Dictionary<(Material, Theme), Material> s_tinted = new();

    // ── Properties ─────────────────────────────────────────────
    public Theme ChapterTheme => _theme;

    private static string ThemeNameKo(Theme t) => t switch
    {
        Theme.Fire   => "잿불 황소",
        Theme.Shadow => "망령 황소",
        Theme.Void   => "공허뿔 황소",
        _            => "가시뿔 황소",
    };

    private static string ThemeNameEn(Theme t) => t switch
    {
        Theme.Fire   => "Cinder Bull",
        Theme.Shadow => "Wraith Bull",
        Theme.Void   => "Voidhorn Bull",
        _            => "Thornhorn Bull",
    };

    /// <summary>챕터 톤 — 몸 색 · 이펙트에 쓴다. 바닥 예고는 공용 공격 색(10-23 가이드 색 통일).</summary>
    private static Color ThemeColor(Theme t) => t switch
    {
        Theme.Fire   => new Color(1f, 0.5f, 0.28f, 1f),
        Theme.Shadow => new Color(0.78f, 0.84f, 1f, 1f),
        Theme.Void   => new Color(0.68f, 0.42f, 1f, 1f),
        _            => new Color(0.55f, 0.85f, 0.42f, 1f),
    };

    /// <summary>몸 색에 섞는 비율 — 원래 털빛을 남기고 톤만(이펙트 절제).</summary>
    private static Color BodyTint(Theme t) => Color.Lerp(Color.white, ThemeColor(t), t == Theme.Shadow ? 0.55f : 0.4f);

    // ── 챕터 · 겉모습 ─────────────────────────────────────────
    private void ResolveTheme()
    {
        _theme = Theme.Forest;
        int at = name.IndexOf("_Ch", StringComparison.Ordinal);
        if (at >= 0 && at + 3 < name.Length && char.IsDigit(name[at + 3]))
            _theme = (Theme)Mathf.Clamp(name[at + 3] - '0', 1, 4);
        else if (GameRunBootstrapper.Instance?.Run != null)
            _theme = (Theme)Mathf.Clamp((int)GameRunBootstrapper.Instance.Run.CurrentChapter, 1, 4);   // ChapterId는 1부터
    }

    /// <summary>몸 색 재질의 챕터 톤 사본(재질 · 테마마다 한 번 만든다).</summary>
    private static Material Tinted(Material src, Theme t)
    {
        if (src == null) return null;
        if (s_tinted.TryGetValue((src, t), out var m) && m != null) return m;
        m = new Material(src) { name = src.name + "_" + t };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.GetColor("_BaseColor") * BodyTint(t));
        else if (m.HasProperty("_Color")) m.SetColor("_Color", m.GetColor("_Color") * BodyTint(t));
        s_tinted[(src, t)] = m;
        return m;
    }

    private void BeginKit()
    {
        _kitCts?.Cancel();
        _kitCts?.Dispose();
        _kitCts = new CancellationTokenSource();
    }

    private void EndKit()
    {
        _kitCts?.Cancel();
        _kitCts?.Dispose();
        _kitCts = null;
    }

    private CancellationToken KitToken => _kitCts != null ? _kitCts.Token : CancellationToken.None;

    // ── 동작 이펙트 ───────────────────────────────────────────
    /// <summary>내려찍기 궤적 — 부채꼴 방향 · 크기에 맞춰 휘두르는 순간 한 번.</summary>
    private void SlashFx(Vector3 origin, Vector3 forward)
    {
        if (_slashVfx == null) return;
        Spawn(_slashVfx, origin + Vector3.up * 0.9f, Quaternion.LookRotation(forward), _slashVfxScale, ThemeColor(_theme), 1.5f);
    }

    /// <summary>돌진 흙먼지 자국 — 달리는 동안 발밑에(작게).</summary>
    private void TrailFx(Vector3 at) => RunFx.Play(RunFxSlot.Burst, at + Vector3.up * 0.1f, 0.35f, Color.Lerp(DustTint, ThemeColor(_theme), 0.25f));

    // ── 패턴 강화 ─────────────────────────────────────────────
    /// <summary>① 내려찍기 판정 직후.</summary>
    private void KitAfterSwing(Vector3 origin, Vector3 forward, float radius, float halfAngle)
    {
        var ct = KitToken;
        Color c = ThemeColor(_theme);
        Debug.Log($"[BullTank] 강화 {_theme} · 내려찍기 t={Time.time:F2}");
        switch (_theme)
        {
            case Theme.Forest:
                MonsterTelegraph.Cone(origin, forward, radius, halfAngle, FollowLead, MonsterTelegraph.Normal);   // (10-23 가이드 색 통일)
                StrikeAfter(FollowLead, p => InCone(p, origin, forward, radius, halfAngle), ThornDamageMul, HitWeight.Medium,
                            () => FxAlongCone(_thornVfx, _thornVfxScale, origin, forward, radius, halfAngle, 1.6f), ct);
                break;
            case Theme.Fire:
                MonsterTelegraph.Cone(origin, forward, radius, halfAngle, FireArm, MonsterTelegraph.Normal);   // 불이 붙는다 — 다 차면 첫 틱(10-18 「불에 판정이 없다」)
                var fx = FxAlongCone(_fireVfx, _fireVfxScale, origin, forward, radius, halfAngle, FireSeconds + 0.5f);
                Linger(p => InCone(p, origin, forward, radius, halfAngle), fx, ct);
                break;
            case Theme.Shadow:
                MonsterTelegraph.Cone(origin, forward, radius, halfAngle, ShadowLead, MonsterTelegraph.Normal);
                StrikeAfter(ShadowLead, p => InCone(p, origin, forward, radius, halfAngle), _swingDamageMul * ShadowDamageMul, HitWeight.Medium,
                            () => { if (_shadowVfx != null) Spawn(_shadowVfx, origin + Vector3.up * 0.9f, Quaternion.LookRotation(forward), _shadowVfxScale, c, 1.5f); }, ct);
                break;
            case Theme.Void:
            {
                Vector3 tip = origin + forward.normalized * radius;
                const float r = 1.8f;
                if (_voidOrbVfx != null) Spawn(_voidOrbVfx, tip + Vector3.up * 0.6f, Quaternion.identity, _voidOrbVfxScale, c, VoidLead + 0.1f);
                MonsterTelegraph.Circle(tip, r, VoidLead, MonsterTelegraph.Normal);
                StrikeAfter(VoidLead, p => Flat(p - tip).magnitude <= r, VoidDamageMul, HitWeight.Medium,
                            () => { if (_voidVfx != null) Spawn(_voidVfx, tip, Quaternion.identity, _voidVfxScale * r / 2f, c, 1.5f); SfxAt(_landSfx, tip, VoidSfxVolume); }, ct);
                break;
            }
        }
    }

    /// <summary>② 도약 착지 직후(_to = 착지 자리).</summary>
    private void KitAfterLeap(Vector3 at, float radius)
    {
        var ct = KitToken;
        Color c = ThemeColor(_theme);
        Debug.Log($"[BullTank] 강화 {_theme} · 도약 t={Time.time:F2}");
        switch (_theme)
        {
            case Theme.Forest:
            {
                float r = radius + 2.5f;   // 착지 원보다 넓게 한 번 더
                MonsterTelegraph.Circle(at, r, FollowLead, MonsterTelegraph.Normal);
                StrikeAfter(FollowLead, p => Flat(p - at).magnitude <= r, ThornDamageMul, HitWeight.Medium,
                            () => FxRing(_thornVfx, _thornVfxScale, at, r * 0.75f, 7, 1.6f), ct);
                break;
            }
            case Theme.Fire:
            {
                const float r = 2.5f;
                MonsterTelegraph.Circle(at, r, FireArm, MonsterTelegraph.Normal);
                var fx = FxDisc(_fireVfx, _fireVfxScale, at, r, FireSeconds + 0.5f);
                Linger(p => Flat(p - at).magnitude <= r, fx, ct);
                break;
            }
            case Theme.Shadow:
            {
                var pl = PlayerPos(out bool ok);
                if (!ok) break;
                Vector3 spot = new(pl.x, at.y, pl.z);
                const float r = 2.5f;
                MonsterTelegraph.Circle(spot, r, ShadowLead, MonsterTelegraph.Heavy);   // 강 타격
                StrikeAfter(ShadowLead, p => Flat(p - spot).magnitude <= r, _leapDamageMul * ShadowDamageMul * 0.75f, HitWeight.Heavy,
                            () =>
                            {
                                // 원 판정 — 베기 궤적만으론 모양이 안 맞았다: 착지 고리(판정 원까지 퍼지게 맞춘 RunFx Ring)를 그림자 색으로(10-18)
                                RunFx.Play(RunFxSlot.Ring, spot + Vector3.up * 0.1f, r / 1.36f, c);
                                if (_shadowVfx != null) Spawn(_shadowVfx, spot, Quaternion.identity, _shadowVfxScale, c, 1.5f);
                            }, ct);
                break;
            }
            case Theme.Void:
            {
                float r = radius + 1.5f;
                MonsterTelegraph.Circle(at, r, VoidLead, MonsterTelegraph.Heavy);   // 강 타격
                StrikeAfter(VoidLead, p => Flat(p - at).magnitude <= r, VoidDamageMul, HitWeight.Heavy,
                            () => { if (_voidVfx != null) Spawn(_voidVfx, at, Quaternion.identity, _voidVfxScale * r / 2f, c, 1.5f); SfxAt(_landSfx, at, VoidSfxVolume); }, ct);
                break;
            }
        }
    }

    /// <summary>③ 돌진이 끝난 순간(from → to · 폭).</summary>
    private void KitAfterCharge(Vector3 from, Vector3 to, float width)
    {
        var ct = KitToken;
        Color c = ThemeColor(_theme);
        Vector3 d = Flat(to - from);
        float len = d.magnitude;
        if (len < 0.5f) return;
        Vector3 dir = d / len;
        Debug.Log($"[BullTank] 강화 {_theme} · 돌진 길이 {len:F1} m t={Time.time:F2}");
        switch (_theme)
        {
            case Theme.Forest:
                MonsterTelegraph.Line(from, dir, len, width, FollowLead, MonsterTelegraph.Normal);
                StrikeAfter(FollowLead, p => InLine(p, from, to, width), ThornDamageMul, HitWeight.Medium,
                            () => FxAlongLine(_thornVfx, _thornVfxScale, from, to, 1.6f), ct);
                break;
            case Theme.Fire:
                MonsterTelegraph.Line(from, dir, len, width, FireArm, MonsterTelegraph.Normal);
                Linger(p => InLine(p, from, to, width),   // 불 한 점은 반경 FirePointRadius에 맞춘 배율 — 줄은 판정 반폭까지 넓힌다
                       FxAlongLine(_fireVfx, _fireVfxScale * (width * 0.5f + _chargeHitPad) / FirePointRadius, from, to, FireSeconds + 0.5f), ct);
                break;
            case Theme.Shadow:
                MonsterTelegraph.Line(from, dir, len, width, ShadowLead, MonsterTelegraph.Heavy);   // 강 타격
                StrikeAfter(ShadowLead, p => InLine(p, from, to, width), _chargeDamageMul * ShadowDamageMul * 0.75f, HitWeight.Heavy,   // 거울 베기 옆 폭을 판정 반폭에(10-18)
                            () => FxAlongLine(_shadowVfx, _shadowVfxScale * (width * 0.5f + _chargeHitPad) / ShadowLateralAtOne, from, to, 1.2f), ct);
                break;
            case Theme.Void:
            {
                const float r = 3f;
                MonsterTelegraph.Circle(to, r, VoidLead, MonsterTelegraph.Heavy);   // 강 타격
                StrikeAfter(VoidLead, p => Flat(p - to).magnitude <= r, VoidDamageMul * 1.1f, HitWeight.Heavy,
                            () => { if (_voidVfx != null) Spawn(_voidVfx, to, Quaternion.identity, _voidVfxScale * r / 2f, c, 1.5f); SfxAt(_wallSfx, to, VoidSfxVolume); }, ct);
                break;
            }
        }
    }

    // ── 강화 도우미 ───────────────────────────────────────────
    /// <summary>강화 후속타 소리 — 그 자리에서 작게.</summary>
    private void SfxAt(AudioClip clip, Vector3 at, float volume)
    {
        if (clip != null) Managers.Sound?.PlayEffectAt(clip, at, volume, _sfxPitch * UnityEngine.Random.Range(0.96f, 1.04f));
    }

    /// <summary>예고 뒤 한 번 — 예고(MonsterTelegraph)가 다 차는 순간 판정 · 이펙트.</summary>
    private void StrikeAfter(float delay, Func<Vector3, bool> inside, float dmgMul, HitWeight weight, Action fx, CancellationToken ct)
        => StrikeAfterAsync(delay, inside, dmgMul, weight, fx, ct).Forget();

    private async UniTaskVoid StrikeAfterAsync(float delay, Func<Vector3, bool> inside, float dmgMul, HitWeight weight, Action fx, CancellationToken ct)
    {
        try { await UniTask.Delay(TimeSpan.FromSeconds(delay), cancellationToken: ct); }
        catch (OperationCanceledException) { return; }
        if (_runtime == null || _runtime.IsDead) return;
        fx?.Invoke();
        var p = PlayerPos(out bool ok);
        bool hit = ok && inside(p);
        if (hit) HitPlayer(Damage(dmgMul), weight);
        Debug.Log($"[BullTank] 강화 판정 {_theme} t={Time.time:F2}{(hit ? " — 명중" : "")}");
    }

    /// <summary>남는 불길 — FireArm 뒤부터 FireTick마다 약 틱, FireSeconds 동안.</summary>
    private void Linger(Func<Vector3, bool> inside, List<GameObject> fx, CancellationToken ct) => LingerAsync(inside, fx, ct).Forget();

    private async UniTaskVoid LingerAsync(Func<Vector3, bool> inside, List<GameObject> fx, CancellationToken ct)
    {
        float t0 = Time.time, next = t0 + FireArm;
        try
        {
            while (Time.time - t0 < FireSeconds)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                if (Time.time < next) continue;
                next += FireTick;
                var p = PlayerPos(out bool ok);
                if (ok && inside(p)) HitPlayer(Damage(FireDamageMul), HitWeight.Light);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            foreach (var go in fx)
                if (go != null) Destroy(go);
        }
    }

    private static bool InCone(Vector3 p, Vector3 origin, Vector3 forward, float radius, float halfAngle)
    {
        Vector3 to = Flat(p - origin);
        return to.magnitude <= radius && Vector3.Angle(Flat(forward), to) <= halfAngle;
    }

    private bool InLine(Vector3 p, Vector3 from, Vector3 to, float width)
    {
        Vector3 a = Flat(from), b = Flat(to), q = Flat(p);
        Vector3 ab = b - a;
        float k = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector3.Dot(q - a, ab) / ab.sqrMagnitude) : 0f;
        return (q - (a + ab * k)).magnitude <= width * 0.5f + _chargeHitPad;
    }

    private GameObject Spawn(GameObject prefab, Vector3 at, Quaternion rot, float scale, Color tint, float life)
    {
        if (prefab == null) return null;
        var go = Instantiate(prefab, at, rot);
        go.transform.localScale = Vector3.one * scale;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var col = main.startColor;
            col.colorMin *= tint; col.colorMax *= tint; col.color *= tint;
            main.startColor = col;
        }
        // 불꽃 타일(FlameTsunami)은 저 혼자 앞으로 흘러간다 — 화룡 휩쓸기처럼 움직임을 끈다
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && mb.GetType().Name == "VariousTranslateMove") mb.enabled = false;
        if (life > 0f) Destroy(go, life);
        return go;
    }

    /// <summary>이펙트 물들임 — 불은 제 색 그대로(챕터 색을 곱하면 검붉게 탄다 · 10-18 실측), 나머지는 챕터 색.</summary>
    private Color FxTint(GameObject prefab) => prefab == _fireVfx ? Color.white : ThemeColor(_theme);

    /// <summary>원을 덮는 이펙트 — 가운데 하나 + 둘레 여섯(반경의 0.6).</summary>
    private List<GameObject> FxDisc(GameObject prefab, float scale, Vector3 center, float r, float life)
    {
        var list = new List<GameObject>();
        if (prefab == null) return list;
        list.Add(Spawn(prefab, center, Quaternion.identity, scale, FxTint(prefab), life));
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3f;
            list.Add(Spawn(prefab, center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (r * 0.6f), Quaternion.identity, scale, FxTint(prefab), life));
        }
        return list;
    }

    private List<GameObject> FxAlongLine(GameObject prefab, float scale, Vector3 from, Vector3 to, float life)
    {
        var list = new List<GameObject>();
        if (prefab == null) return list;
        Vector3 d = Flat(to - from);
        int n = Mathf.Max(1, Mathf.CeilToInt(d.magnitude / LineStep));
        Quaternion rot = d.sqrMagnitude > 0.01f ? Quaternion.LookRotation(d) : Quaternion.identity;
        for (int i = 0; i <= n; i++)
            list.Add(Spawn(prefab, Vector3.Lerp(from, to, i / (float)n), rot, scale, FxTint(prefab), life));
        return list;
    }

    private List<GameObject> FxAlongCone(GameObject prefab, float scale, Vector3 origin, Vector3 forward, float radius, float halfAngle, float life)
    {
        var list = new List<GameObject>();
        if (prefab == null) return list;
        Vector3 f = Flat(forward).normalized;
        foreach (float a in new[] { -halfAngle * 0.6f, 0f, halfAngle * 0.6f })
            foreach (float k in new[] { 0.45f, 0.85f })
                list.Add(Spawn(prefab, origin + Quaternion.Euler(0f, a, 0f) * f * (radius * k), Quaternion.LookRotation(f), scale, FxTint(prefab), life));
        return list;
    }

    private void FxRing(GameObject prefab, float scale, Vector3 center, float r, int count, float life)
    {
        if (prefab == null) return;
        for (int i = 0; i < count; i++)
        {
            float a = i * Mathf.PI * 2f / count;
            Spawn(prefab, center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r, Quaternion.identity, scale, FxTint(prefab), life);
        }
    }
}
}
