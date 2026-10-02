using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using RelicFairy.Monster;

/// <summary>체력바 왼쪽 특성 배지 한 칸 — 아이콘 키 + 분류색 테.</summary>
public readonly struct MonsterTraitBadge
{
    public readonly string IconKey;
    public readonly Color  Ring;
    public MonsterTraitBadge(string iconKey, Color ring) { IconKey = iconKey; Ring = ring; }
}

/// <summary>
/// 몬스터 한 마리의 시기 특성(10-02 레벨디자인 설계서 §4) — 스포너가 굴려서 붙이고, 풀로 돌아가면(꺼지면) 되돌리고 떨어진다.
/// 몬스터 본체(<see cref="MonsterBase"/>)엔 훅 셋만 있다: 받는 피해(<see cref="ModifyIncoming"/>) · 공격 배율(지휘) · 체력 배율(분열 분신).
/// 나머지(질주 · 폭발 · 흔적 · 잠복 · 분열)는 여기서 몬스터를 바깥에서 다룬다.
/// </summary>
[DisallowMultipleComponent]
public sealed class MonsterTraits : MonoBehaviour
{
    // ── Constants ──────────────────────────────────────────────
    private const float SwiftMoveMul      = 1.3f;   // 질주 — 이동 속도(공격 속도 배율은 몬스터 쪽에 쓰는 곳이 없어 이동만)
    private const float WardShare         = 0.2f;   // 수호막 — 최대 체력의 이만큼을 흡수하기 전까지
    private const float WardCut           = 0.5f;
    private const float ResistCut         = 0.5f;
    private const float CommanderRadius   = 8f;
    private const float CommanderAtkMul   = 1.2f;
    private const float CommanderTick     = 0.5f;
    private const float LurkerRevealMin   = 5f;     // 이 거리(또는 공격 사거리 + 1.5 m) 안에 들면 드러난다
    private const float VolatileDelay     = 1.2f;
    private const float VolatileRadius    = 3f;
    private const float VolatileDmgMul    = 1.5f;
    private const float TrailInterval     = 0.6f;
    private const float TrailLife         = 3f;
    private const float TrailRadius       = 1.1f;
    private const float TrailTick         = 0.5f;
    private const float TrailDmgMul       = 0.25f;
    private const float TrailMinMove      = 0.6f;   // 이만큼 움직였을 때만 흔적을 남긴다(제자리 공격 중엔 안 깐다)
    private const float SplitAt           = 0.5f;
    private const float SplitChildHp      = 0.3f;
    private const float SplitChildScale   = 0.72f;
    private const float SplitSpread       = 0.9f;
    private const string SeenPrefsPrefix  = "MonsterTraitSeen_";

    private static readonly Color VolatileColor = new(1f, 0.25f, 0.18f, 1f);
    private static readonly Color TrailColor    = new(1f, 0.45f, 0.15f, 1f);

    // ── Static ─────────────────────────────────────────────────
    private static readonly List<MonsterTraits> s_commanders = new();
    private static readonly List<MonsterBase>   s_scan       = new();
    private static readonly HashSet<MonsterTraitKind> s_noticedThisSession = new();

    // ── Private ────────────────────────────────────────────────
    private readonly List<MonsterTraitKind>  _kinds  = new(2);
    private readonly List<MonsterTraitBadge> _badges = new(2);
    private readonly HashSet<MonsterBase>    _aura   = new();
    private MonsterBase  _monster;
    private NavMeshAgent _agent;
    private Func<Vector3, UniTask> _spawnChild;
    private RuneElement  _resist;
    private float        _wardLeft;
    private float        _swiftApplied = -1f;
    private float        _nextCommanderTick;
    private Vector3      _lastTrailPos;
    private float        _nextTrailAt;
    private bool         _split;
    private bool         _hidden;
    private Renderer[]   _hiddenRenderers;
    private bool         _isChild;
    private Vector3      _childBaseScale;
    private bool         _reverted;

    // ── Properties ─────────────────────────────────────────────
    public IReadOnlyList<MonsterTraitKind>  Kinds  => _kinds;
    public IReadOnlyList<MonsterTraitBadge> Badges => _badges;
    public bool Has(MonsterTraitKind k) => !_reverted && _kinds.Contains(k);

    // ── Lifecycle ──────────────────────────────────────────────
    private void LateUpdate()
    {
        if (_reverted || _monster == null || _monster.IsDead) return;

        if (Has(MonsterTraitKind.Swift)) TickSwift();
        if (Has(MonsterTraitKind.Lurker) && _hidden) TickLurker();
        if (Has(MonsterTraitKind.Commander) && Time.time >= _nextCommanderTick) TickCommander();
        if (Has(MonsterTraitKind.Trail)) TickTrail();
    }

    private void OnDisable() => Revert();

    // ── Public Methods ─────────────────────────────────────────

    /// <summary>
    /// 스포너가 굴린 특성을 붙인다. <paramref name="spawnChild"/> = 분열 분신 하나를 그 자리에 세우는 스포너 쪽 길(방 생존 수에 들어가게).
    /// </summary>
    public static MonsterTraits Attach(MonsterBase monster, IReadOnlyList<MonsterTraitKind> kinds, Func<Vector3, UniTask> spawnChild)
    {
        if (monster == null || kinds == null || kinds.Count == 0) return null;
        var t = monster.gameObject.AddComponent<MonsterTraits>();
        t.Init(monster, kinds, spawnChild);
        return t;
    }

    /// <summary>분열 분신 — 작은 체력 · 작은 몸. 특성은 없다(다시 갈라지지 않는다).</summary>
    public static void MakeSplitChild(MonsterBase child)
    {
        if (child == null) return;
        var t = child.gameObject.AddComponent<MonsterTraits>();
        t._monster        = child;
        t._isChild        = true;
        t._childBaseScale = child.transform.localScale;
        child.transform.localScale = t._childBaseScale * SplitChildScale;
        child.MakeTraitSplitChild(SplitChildHp);
        child.BindTraits(t);
    }

    /// <summary>받는 피해 — 수호막(남은 몫 동안 −50%) · 속성 저항(그 속성 −50%). 몬스터 본체의 피해 계산 끝에서 부른다.</summary>
    public float ModifyIncoming(float actual, RuneElement? element)
    {
        if (_reverted) return actual;
        if (element.HasValue && Has(MonsterTraitKind.Resist) && element.Value == _resist)
            actual *= ResistCut;
        if (_wardLeft > 0f && Has(MonsterTraitKind.Ward))
        {
            actual *= WardCut;
            _wardLeft -= actual;
            if (_wardLeft <= 0f)
            {
                _wardLeft = 0f;
                RebuildBadges();   // 깨진 막은 배지에서 뺀다
                PushBadges();
            }
        }
        return Mathf.Max(1f, actual);
    }

    // ── Private Methods ────────────────────────────────────────

    private void Init(MonsterBase monster, IReadOnlyList<MonsterTraitKind> kinds, Func<Vector3, UniTask> spawnChild)
    {
        _monster    = monster;
        _agent      = monster.GetComponent<NavMeshAgent>();
        _spawnChild = spawnChild;
        _kinds.AddRange(kinds);
        monster.BindTraits(this);
        RunFx.LoadAsync().Forget();   // 폭발 · 흔적 장판의 바닥 원 재질

        if (Has(MonsterTraitKind.Resist)) _resist = (RuneElement)UnityEngine.Random.Range(0, 6);
        if (Has(MonsterTraitKind.Ward))   _wardLeft = Mathf.Max(1f, monster.EffectiveMaxHp * WardShare);
        if (Has(MonsterTraitKind.Commander)) s_commanders.Add(this);
        if (Has(MonsterTraitKind.Volatile))  monster.OnDied += HandleDiedVolatile;
        if (Has(MonsterTraitKind.Splitter))  monster.OnHPChanged += HandleHpSplitter;
        if (Has(MonsterTraitKind.Lurker))    Conceal();
        _lastTrailPos = monster.transform.position;

        RebuildBadges();
        PushBadges();
        NoticeFirstEncounter(_kinds);
        Debug.Log($"[MonsterTrait] {monster.name}: {string.Join(", ", _kinds)}{(Has(MonsterTraitKind.Resist) ? $" (저항 {_resist})" : "")}");
    }

    /// <summary>되돌린다 — 풀로 돌아가거나 꺼질 때. 남의 몸에 건 지휘 배율 · 감춘 그림 · 분신 크기를 원래대로.</summary>
    private void Revert()
    {
        if (_reverted) return;
        _reverted = true;

        if (_monster != null)
        {
            _monster.OnDied      -= HandleDiedVolatile;
            _monster.OnHPChanged -= HandleHpSplitter;
            _monster.BindTraits(null);
        }
        s_commanders.Remove(this);
        foreach (var m in _aura)
            if (m != null && !CoveredByOtherCommander(m)) m.SetTraitAttackMul(1f);
        _aura.Clear();
        if (_hidden) Reveal();
        if (_isChild && _monster != null) _monster.transform.localScale = _childBaseScale;
        _kinds.Clear();
        _badges.Clear();   // 떨어지기 전(프레임 끝) 같은 몸에 바가 다시 달려도 배지가 남지 않게

        Destroy(this);
    }

    private void RebuildBadges()
    {
        _badges.Clear();
        foreach (var k in _kinds)
        {
            if (k == MonsterTraitKind.Ward && _wardLeft <= 0f) continue;
            var d = MonsterTraitTable.Of(k);
            string icon = k == MonsterTraitKind.Resist ? ElementIcon(_resist) : d.iconKey;
            _badges.Add(new MonsterTraitBadge(icon, MonsterTraitTable.CategoryColor(d.category)));
        }
    }

    private void PushBadges()
    {
        var bar = _monster != null ? _monster.WorldHPBar : null;
        if (bar != null) bar.SetTraitBadges(_badges);
    }

    private static string ElementIcon(RuneElement e) => e switch
    {
        RuneElement.Fire     => "fire",
        RuneElement.Ice      => "freeze",
        RuneElement.Electric => "lightning",
        RuneElement.Grass    => "poison",
        RuneElement.Light    => "light",
        _                    => "dark",
    };

    // 질주 — FSM · 둔화가 속도를 새로 정할 때마다(값이 바뀌었을 때만) 한 번 곱한다. 매 프레임 곱하면 쌓인다.
    private void TickSwift()
    {
        if (_agent == null || !_agent.isActiveAndEnabled) return;
        float s = _agent.speed;
        if (Mathf.Approximately(s, _swiftApplied)) return;
        _agent.speed  = s * SwiftMoveMul;
        _swiftApplied = _agent.speed;
    }

    // 지휘 — 반경 안 아군 공격 ×1.2. 반경을 벗어나면(다른 지휘관 반경도 아니면) 원래대로.
    private void TickCommander()
    {
        _nextCommanderTick = Time.time + CommanderTick;
        MonsterBase.CopyActive(s_scan);
        Vector3 me = _monster.transform.position;
        float r2 = CommanderRadius * CommanderRadius;
        foreach (var m in s_scan)
        {
            if (m == null || m == _monster || m.IsDead || m.Grade == MonsterGrade.Boss) continue;
            bool inside = (m.transform.position - me).sqrMagnitude <= r2;
            if (inside)
            {
                if (_aura.Add(m)) m.SetTraitAttackMul(CommanderAtkMul);
            }
            else if (_aura.Remove(m) && !CoveredByOtherCommander(m))
            {
                m.SetTraitAttackMul(1f);
            }
        }
        _aura.RemoveWhere(m => m == null || !m.isActiveAndEnabled);
    }

    private bool CoveredByOtherCommander(MonsterBase m)
    {
        float r2 = CommanderRadius * CommanderRadius;
        foreach (var c in s_commanders)
            if (c != this && c != null && c._monster != null && !c._monster.IsDead
                && (m.transform.position - c._monster.transform.position).sqrMagnitude <= r2)
                return true;
        return false;
    }

    // 잠복 — 가까이 들거나 맞으면 드러난다. 사거리가 긴 몹은 사거리 밖에서 보이지 않는 채 쏘지 못하게 드러나는 거리를 늘린다.
    private void Conceal()
    {
        _hiddenRenderers = _monster.GetComponentsInChildren<Renderer>(false);
        foreach (var r in _hiddenRenderers) if (r != null) r.enabled = false;
        _monster.HideWorldHPBar();
        _hidden = true;
    }

    private void TickLurker()
    {
        var player = Managers.Player != null ? Managers.Player.PlayerTransform : null;
        if (player == null) return;
        float reveal = Mathf.Max(LurkerRevealMin, _monster.AttackRange + 1.5f);
        bool near = (player.position - _monster.transform.position).sqrMagnitude <= reveal * reveal;
        if (near || _monster.CurrentHp < _monster.EffectiveMaxHp) Reveal();
    }

    private void Reveal()
    {
        _hidden = false;
        if (_hiddenRenderers != null)
            foreach (var r in _hiddenRenderers) if (r != null) r.enabled = true;
        _hiddenRenderers = null;
        if (_monster != null && !_reverted) _monster.ShowWorldHPBar();
    }

    // 흔적 — 움직인 자리에 짧게 남는 불길. 머무는 동안 조금씩 아프다.
    private void TickTrail()
    {
        if (Time.time < _nextTrailAt) return;
        Vector3 p = _monster.transform.position;
        if ((p - _lastTrailPos).sqrMagnitude < TrailMinMove * TrailMinMove) return;
        _nextTrailAt  = Time.time + TrailInterval;
        _lastTrailPos = p;
        int dmg = Mathf.Max(1, Mathf.RoundToInt(_monster.EffectiveAttackPower * _monster.AttackMultiplierNow * TrailDmgMul));
        TrailSpotAsync(p, dmg).Forget();
    }

    private static async UniTaskVoid TrailSpotAsync(Vector3 at, int dmg)
    {
        var disc = SpawnDisc(at, TrailRadius, TrailColor);
        if (disc == null) return;
        var ct = disc.GetCancellationTokenOnDestroy();
        try
        {
            float end = Time.time + TrailLife;
            while (Time.time < end)
            {
                PatternGuideHelper.SetProgress(disc, 1f - (end - Time.time) / TrailLife);
                HurtPlayerWithin(at, TrailRadius, dmg, HitWeight.Light);
                await UniTask.Delay(TimeSpan.FromSeconds(TrailTick), cancellationToken: ct);
            }
        }
        catch (OperationCanceledException) { return; }
        PatternGuideHelper.SafeDestroy(ref disc);
    }

    // 폭발 유해 — 죽은 자리에 붉은 원이 차오르고 1.2초 뒤 터진다.
    private void HandleDiedVolatile(MonsterBase m)
    {
        if (m == null) return;
        int dmg = Mathf.Max(1, Mathf.RoundToInt(m.EffectiveAttackPower * m.AttackMultiplierNow * VolatileDmgMul));
        VolatileBlastAsync(m.transform.position, dmg).Forget();
    }

    private static async UniTaskVoid VolatileBlastAsync(Vector3 at, int dmg)
    {
        var disc = SpawnDisc(at, VolatileRadius, VolatileColor);
        if (disc == null) return;
        var ct = disc.GetCancellationTokenOnDestroy();
        try
        {
            float start = Time.time;
            while (Time.time - start < VolatileDelay)
            {
                PatternGuideHelper.SetProgress(disc, (Time.time - start) / VolatileDelay);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { return; }
        RunFx.Play(RunFxSlot.Burst, at, 0.6f, VolatileColor);
        HurtPlayerWithin(at, VolatileRadius, dmg, HitWeight.Medium);
        PatternGuideHelper.SafeDestroy(ref disc);
    }

    // 분열 — 체력이 절반 아래로 처음 내려가는 순간 작은 분신 둘.
    private void HandleHpSplitter(int cur, int max)
    {
        if (_split || _reverted || max <= 0 || cur <= 0 || cur > max * SplitAt) return;
        _split = true;
        if (_spawnChild == null) return;
        Vector3 c = _monster.transform.position, side = _monster.transform.right * SplitSpread;
        _spawnChild(c + side).Forget();
        _spawnChild(c - side).Forget();
    }

    private static GameObject SpawnDisc(Vector3 at, float radius, Color color)
    {
        // 바닥 원 재질 — 보스 · 이벤트방이 쓰지 않을 땐 비어 있다. 런 공용 목록의 원 데칼을 잠깐 빌려 그린다.
        var prevCircle = PatternGuideHelper.CircleMaterial;
        var prevArrow  = PatternGuideHelper.ArrowMaterial;
        bool borrow = prevCircle == null && RunFx.GuideCircle != null;
        if (borrow) PatternGuideHelper.SetMaterials(RunFx.GuideCircle, RunFx.GuideArrow);
        var disc = PatternGuideHelper.Disc(at, radius, color);
        if (borrow) PatternGuideHelper.SetMaterials(prevCircle, prevArrow);
        if (disc != null) RoomScopedDrop.Mark(disc);   // 방을 떠나면 같이 정리
        return disc;
    }

    private static void HurtPlayerWithin(Vector3 at, float radius, int dmg, HitWeight weight)
    {
        var pt = Managers.Player != null ? Managers.Player.PlayerTransform : null;
        if (pt == null) return;
        Vector3 d = pt.position - at; d.y = 0f;
        if (d.sqrMagnitude > radius * radius) return;
        if (pt.TryGetComponent<PlayerController>(out var pc)) pc.TakeDamage(dmg, null, false, weight);
    }

    // 첫 조우 안내 — 특성마다 한 번(프로필 기준). 정예 둘이 처음이면 한 줄에 같이(알림 칸이 하나라 따로 띄우면 덮인다).
    private static void NoticeFirstEncounter(List<MonsterTraitKind> kinds)
    {
        string msg = null;
        foreach (var k in kinds)
        {
            if (!s_noticedThisSession.Add(k)) continue;
            string key = SeenPrefsPrefix + k;
            if (PlayerPrefs.GetInt(key, 0) != 0) continue;
            PlayerPrefs.SetInt(key, 1);
            var d = MonsterTraitTable.Of(k);
            msg = msg == null ? $"{d.name}: {d.desc}" : $"{msg}   ·   {d.name}: {d.desc}";
        }
        if (msg == null) return;
        var hud = FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
        hud?.ShowBuffNotice("새로운 적 특성 — " + msg);
    }
}
