using System;
using System.Collections.Generic;
using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 죽음의 기사 악몽 특성 「지휘」의 환영 기수(10-02 설계 승인) — 플레이어가 고르는 것은 「무엇부터 벨까」.
///
/// · 악몽 모드 2페이지에서만 선다 — <see cref="DKCommandTrait"/>가 세우고 거둔다. 플레이어 구역 가장자리 칸(가운데 · 플레이어 칸 아님).
/// · 서 있는 동안 기사의 패턴 사이 쉬는 시간 ×<see cref="RestMult"/>(<see cref="DKComboRunner"/>) · 기사 공격 피해 ×<see cref="DamageMult"/>
///   (<see cref="DKPage2Zone.HitPlayer"/> · DKGridPatternHelper 피격). 예고 · 판정 고정 시간은 건드리지 않는다.
/// · 플레이어 타격 <see cref="HitsToBreak"/>번에 쓰러진다 — 피해량과 무관. 주 피해 파이프라인이 타격마다 부르는 <see cref="OnReceiveHit"/>로 센다
///   (CombatDamage.Deal → TakeDamage → HitFeedbackService.RaiseHit). 그래서 화상 같은 지속 피해 · 아이템 추가타 · 서약 광역 같은
///   2차 피해(TakeDamage만 부른다)는 세지 않는다. 한 번 휘두름 · 한 번 일제 사격이 여러 번 세지지 않게 <see cref="HitGap"/>초 안의 타격은 하나로 친다.
/// · 판정 = 무덤 기둥(<see cref="DKTombPillar"/>)과 같은 길 — 루트에 IDamageable + MonsterHit 레이어 캡슐이라 근접 판정 · 투사체 · 조준 보정이 그대로 잡는다.
///   캡슐은 트리거다(환영 — 플레이어 길을 막지 않는다). 기사 자식이 아니다 — 피해 경로가 GetComponentInParent&lt;MonsterBase&gt;로
///   몬스터를 찾으므로 자식이면 기사에게 넘어간다. 받은 피해는 어디에도 넘기지 않는다.
/// · 몸 = 기사 애니메이터 자식 복제(<see cref="DKPhantomClone"/> — 반역의 환영과 같은 방식) · 어두운 금빛. 발밑 청록 원 = 칠 수 있음(예고 색 규약).
///
/// 시뮬레이터: <see cref="Active"/>로 찾아 <see cref="SimulatePlayerHit"/>로 친다(실제 타격과 같은 길). TakeDamage는 아무것도 하지 않으므로
/// 피해만 넣어서는(예: 소환물 정리) 쓰러지지 않는다.
/// </summary>
public sealed class DKStandardBearer : MonoBehaviour, IDamageable, IHitReceiver
{
    // ── Constants ─────────────────────────────────────────────────
    public const int   HitsToBreak = 4;
    public const float RestMult    = 0.85f;   // 서 있는 동안 기사 패턴 사이 쉬는 시간
    public const float DamageMult  = 1.15f;   // 서 있는 동안 기사 공격 피해
    public const float HitGap      = 0.1f;    // 이 안의 타격은 하나로 친다(초)

    private const string HitLayerName   = "MonsterHit";
    private const string StandAnim      = "Idle2";   // 기사 채널링 자세 — 기를 든 채 버틴다
    private const float  Radius         = 0.6f;
    private const float  Height         = 2.4f;
    private const float  RingPad        = 0.4f;
    private const float  FadeIn         = 0.6f;
    private const float  BreakFade      = 0.5f;
    private const float  DismissFade    = 0.3f;
    private const float  FlashSeconds   = 0.08f;
    private const float  WornGlow       = 0.4f;    // 한 대 남았을 때 발광(처음 = 1) — 맞을수록 빛이 꺼져 간다
    private const float  BurstScale     = 0.6f;
    private const float  FallbackRadius = 0.45f;

    private static readonly Color GoldBase      = new Color(0.35f, 0.24f, 0.05f, 1f);
    private static readonly Color GoldEmission  = new Color(1.3f, 0.85f, 0.2f, 1f);
    private static readonly Color GoldTint      = new Color(1f, 0.75f, 0.25f, 1f);   // 터짐 이펙트 · 대체 기둥
    private static readonly Color FlashEmission = new Color(2.4f, 2.1f, 1.4f, 1f);

    // ── Static ────────────────────────────────────────────────────
    private static readonly List<DKStandardBearer> s_active = new();

    // 도메인 리로드 OFF — 지난 플레이의 기수가 남지 않게
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_active.Clear();

    // ── Private ───────────────────────────────────────────────────
    private DeathKnightBossMonster   _boss;
    private Action<DKStandardBearer> _onBroken;
    private Collider                 _collider;
    private DKPhantomClone           _visual;
    private GameObject               _fallback;
    private GameObject               _baseRing;
    private float                    _spawnTime;
    private float                    _lastHitTime = float.NegativeInfinity;
    private float                    _flashUntil;

    // ── Properties ────────────────────────────────────────────────
    /// <summary>지금 서 있는 기수(쓰러지거나 거두는 중인 것은 빠진다) — 보통 0~1개. 시뮬레이터 · 디버그가 읽는다.</summary>
    public static IReadOnlyList<DKStandardBearer> Active => s_active;

    public bool                   IsStanding   { get; private set; }
    public int                    HitsTaken    { get; private set; }
    public int                    HitsLeft     => Mathf.Max(0, HitsToBreak - HitsTaken);
    public Vector2Int             Cell         { get; private set; }
    public Vector3                Position     => transform.position;
    public float                  StoodSeconds => Time.time - _spawnTime;
    public DeathKnightBossMonster Boss         => _boss;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Update()
    {
        if (!IsStanding) return;
        // 기사가 죽거나 사라지면 함께 사라진다(기사 쪽 정리가 먼저 돌지만 풀 반환 · 씬 정리 순서와 무관하게)
        if (_boss == null || _boss.IsDead || !_boss.isActiveAndEnabled)
        {
            Dismiss(false);
            return;
        }
        if (_flashUntil > 0f && Time.time >= _flashUntil)
        {
            _flashUntil = 0f;
            ApplyLook(false);
        }
    }

    private void OnDestroy() => s_active.Remove(this);

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>이 기사의 기수가 서 있는가.</summary>
    public static bool IsStandingFor(MonsterBase boss)
    {
        if (boss == null) return false;
        for (int i = 0; i < s_active.Count; i++)
            if (s_active[i] != null && s_active[i]._boss == boss) return true;
        return false;
    }

    /// <summary>기사 러너가 패턴 사이 쉬는 시간에 곱한다 — 기수가 서 있으면 <see cref="RestMult"/>, 아니면 1.</summary>
    public static float RestScale(MonsterBase boss) => IsStandingFor(boss) ? RestMult : 1f;

    /// <summary>기사 공격 피해에 곱한다 — 기수가 서 있으면 <see cref="DamageMult"/>, 아니면 1.</summary>
    public static float DamageScale(MonsterBase boss) => IsStandingFor(boss) ? DamageMult : 1f;

    /// <summary>
    /// 기수를 세운다. <paramref name="source"/> = 기사 애니메이터(자식) — 없거나 보스 루트에 붙어 있으면 금빛 기둥으로 대신한다.
    /// <paramref name="facing"/> = 바라볼 방향(수평으로 편다).
    /// </summary>
    public static DKStandardBearer Create(DeathKnightBossMonster boss, Animator source, Vector3 groundPos, Vector3 facing,
                                          Vector2Int cell, Action<DKStandardBearer> onBroken)
    {
        facing.y = 0f;
        Quaternion rot = facing.sqrMagnitude > 0.001f ? Quaternion.LookRotation(facing) : Quaternion.identity;

        var go = new GameObject("DKStandardBearer");
        go.transform.SetPositionAndRotation(groundPos, rot);
        int hitLayer = LayerMask.NameToLayer(HitLayerName);
        if (hitLayer >= 0) go.layer = hitLayer;

        var col       = go.AddComponent<CapsuleCollider>();
        col.radius    = Radius;
        col.height    = Height;
        col.center    = Vector3.up * (Height * 0.5f);
        col.isTrigger = true;

        var bearer = go.AddComponent<DKStandardBearer>();
        bearer._boss      = boss;
        bearer._onBroken  = onBroken;
        bearer._collider  = col;
        bearer._spawnTime = Time.time;
        bearer.Cell       = cell;
        bearer.IsStanding = true;

        if (source != null && boss != null && source.gameObject != boss.gameObject)
        {
            var body = Instantiate(source.gameObject, groundPos, rot, go.transform);
            foreach (var c in body.GetComponentsInChildren<Collider>()) c.enabled = false;   // 판정은 루트 캡슐 하나로

            bearer._visual = body.AddComponent<DKPhantomClone>();
            bearer._visual.Initialize();
            bearer._visual.SetTint(GoldBase, GoldEmission);
            bearer._visual.StartFadeIn(FadeIn);

            if (body.TryGetComponent<Animator>(out var anim))
            {
                anim.speed = 1f;
                int stand = Animator.StringToHash(StandAnim);
                if (anim.HasState(0, stand)) anim.CrossFade(stand, 0.05f, 0, 0f);
            }
        }
        else
        {
            bearer._fallback = PatternGuideHelper.Pillar(groundPos, FallbackRadius, Height, GoldTint);
            bearer._fallback.transform.SetParent(go.transform, true);
        }
        bearer._baseRing = PatternGuideHelper.Disc(groundPos, Radius + RingPad, PatternGuideHelper.Breakable);
        bearer._baseRing.transform.SetParent(go.transform, true);

        RunFx.Play(RunFxSlot.Burst, groundPos + Vector3.up * (Height * 0.5f), BurstScale, GoldTint);
        s_active.Add(bearer);
        return bearer;
    }

    /// <summary>피해는 받지 않는다 — 타격 수만 센다(<see cref="OnReceiveHit"/>). 판정 대상이 되려고 IDamageable을 단다.</summary>
    public void TakeDamage(float amount, GameObject instigator, float knockbackMultiplier = 1f, bool isCrit = false) { }

    /// <summary>
    /// 시뮬레이터용 — 플레이어 타격 한 번을 실제와 같은 길로 넣는다(CombatDamage.Deal → TakeDamage → HitFeedbackService.RaiseHit → <see cref="OnReceiveHit"/>).
    /// 아이템 · 패시브 · 서약 적중 효과도 실제처럼 돈다. <see cref="HitGap"/>초(게임 시간) 안에 다시 치면 세지 않는다.
    /// 반환 = 이번 타격이 세어졌는가.
    /// </summary>
    public bool SimulatePlayerHit(GameObject player, float baseDamage = 1f)
    {
        if (!IsStanding || player == null) return false;
        int before = HitsTaken;
        CombatDamage.Deal(new CombatDamage.Request
        {
            Target         = gameObject,
            BaseDamage     = baseDamage,
            Owner          = player,
            ActionType     = WeaponActionType.GroundLight,
            HitPoint       = transform.position + Vector3.up * (Height * 0.5f),
            SourcePosition = player.transform.position,
        });
        return HitsTaken > before;
    }

    /// <summary>쓰러뜨리지 않고 거둔다(기사 사망 · 2페이지가 아님 · 전투 초기화). <paramref name="immediate"/>면 페이드 없이 바로 지운다(비활성 · 씬 정리).</summary>
    public void Dismiss(bool immediate)
    {
        if (this == null || !IsStanding) return;
        _onBroken = null;
        Vanish(immediate ? 0f : DismissFade);
    }

    // ── Private Methods ───────────────────────────────────────────
    private void Break()
    {
        var onBroken = _onBroken;
        _onBroken = null;
        RunFx.Play(RunFxSlot.Burst, transform.position + Vector3.up * (Height * 0.5f), BurstScale, GoldTint);
        Vanish(BreakFade);
        onBroken?.Invoke(this);
    }

    /// <summary>판정을 끄고 페이드아웃 뒤 지운다(<paramref name="fade"/> 0 = 바로).</summary>
    private void Vanish(float fade)
    {
        IsStanding = false;
        s_active.Remove(this);
        if (_collider != null) _collider.enabled = false;   // 사라지는 동안엔 맞지 않는다
        PatternGuideHelper.SafeDestroy(ref _baseRing);
        if (fade <= 0f || _visual == null)
        {
            Destroy(gameObject);
            return;
        }
        _visual.StartFadeOut(fade);
        Destroy(gameObject, fade);
    }

    /// <summary>번쩍(흰빛) 또는 제 빛 — 제 빛은 맞을수록 어두워진다.</summary>
    private void ApplyLook(bool flash)
    {
        if (_fallback != null) PatternGuideHelper.SetColor(_fallback, flash ? Color.white : GoldTint);
        if (_visual == null) return;
        if (flash)
        {
            _visual.SetTint(Color.white, FlashEmission);
        }
        else
        {
            float glow = Mathf.Lerp(1f, WornGlow, (float)HitsTaken / (HitsToBreak - 1));
            _visual.SetTint(GoldBase, GoldEmission * glow);
        }
        _visual.Repaint();
    }

    // ── Event Handlers ────────────────────────────────────────────
    /// <summary>주 피해 파이프라인의 타격 통지(HitFeedbackService.RaiseHit → 피격자 IHitReceiver) — 플레이어 타격만 센다.</summary>
    public void OnReceiveHit(in HitInfo info)
    {
        if (!IsStanding || info.Attacker == null || !info.Attacker.TryGetComponent<PlayerController>(out _)) return;
        float now = Time.time;
        if (now - _lastHitTime < HitGap) return;
        _lastHitTime = now;
        HitsTaken++;

        _flashUntil = now + FlashSeconds;
        ApplyLook(true);   // 마지막 타격이면 흰빛에서 흩어진다
        if (HitsTaken >= HitsToBreak) Break();
    }
}

/// <summary>
/// 죽음의 기사 악몽 특성 「지휘」(10-02 설계 승인) — 기사(<see cref="DeathKnightBossMonster"/>)가 들고 Update마다 <see cref="Tick"/>한다.
///
/// 악몽 모드 2페이지(전환이 끝난 뒤)에서 개막 패턴을 보여 주고 <see cref="FirstDelay"/>초가 지나면, 패턴 사이에 환영 기수
/// (<see cref="DKStandardBearer"/>) 하나를 플레이어 구역 가장자리 칸에 세운다. 기수가 쓰러지면 <see cref="RespawnDelay"/>초 뒤
/// 다시 선다(여전히 악몽 2페이지 · 기사 생존 · 전환 아님 · 패턴 사이일 때). 기사 사망 · 2페이지가 아님 · 전투 초기화면 바로 거둔다.
/// 이번 전투 첫 기수 때 규칙 자막 한 번(<see cref="DeathKnightBossMonster.HintTreasonColor"/>와 같은 방식).
/// </summary>
public sealed class DKCommandTrait
{
    // ── Constants ─────────────────────────────────────────────────
    private const float  FirstDelay    = 4f;    // 2페이지가 열리고 첫 기수까지(개막 패턴을 보여 준 뒤 · 패턴 사이)
    private const float  RespawnDelay  = 25f;   // 쓰러지고 다시 서기까지
    private const float  RetryDelay    = 1f;    // 설 칸이 없을 때 다시 볼 때까지
    private const int    MinPlayerGap  = 2;     // 플레이어 칸과 이만큼(칸, 체비셰프) 떨어진 칸 — 바로 옆에 서지 않게
    private const int    FrontRowsKept = 2;     // 유리 앞 두 줄은 비운다 — 공명 성흔석 · 왕좌의 인력 자리
    private const string Hint          = "기수가 서 있는 동안 기사가 거세진다 — 기수부터 베어라";

    // ── Static ────────────────────────────────────────────────────
    private static readonly List<Vector2Int> s_cells = new();

    // ── Private ───────────────────────────────────────────────────
    private readonly DeathKnightBossMonster   _dk;
    private readonly Action<DKStandardBearer> _onBroken;
    private DKStandardBearer _bearer;
    private float            _nextSpawnAt = -1f;   // < 0 = 아직 악몽 2페이지가 아니다
    private bool             _hintShown;

    // ── Properties ────────────────────────────────────────────────
    /// <summary>지금 서 있는 기수(없으면 null).</summary>
    public DKStandardBearer Bearer => _bearer;

    // ── Constructor ───────────────────────────────────────────────
    public DKCommandTrait(DeathKnightBossMonster dk)
    {
        _dk       = dk;
        _onBroken = OnBearerBroken;
    }

    // ── Public Methods ────────────────────────────────────────────
    public void Tick(MonsterContext ctx)
    {
        var pages = _dk.Pages;
        if (!pages.NightmareMode) return;   // 악몽 모드 전투만
        if (!pages.IsPage2 || pages.Transitioning || _dk.IsDead)
        {
            Dismiss("2페이지 아님", false);
            _nextSpawnAt = -1f;
            return;
        }
        if (_nextSpawnAt < 0f) _nextSpawnAt = Time.time + FirstDelay;   // 2페이지가 열린 첫 프레임
        if (_bearer != null || Time.time < _nextSpawnAt) return;
        if (pages.OpenerDue || _dk.IsInSpecialState) return;              // 개막 패턴을 보여 준 뒤 · 패턴 사이에만 선다
        Spawn(ctx);
    }

    /// <summary>전투 정리 — 기수를 거두고 시계 · 자막 기억을 비운다. <paramref name="immediate"/>면 페이드 없이(비활성 · 씬 정리).</summary>
    public void Reset(string reason, bool immediate)
    {
        Dismiss(reason, immediate);
        _nextSpawnAt = -1f;
        _hintShown   = false;
    }

    // ── Private Methods ───────────────────────────────────────────
    private void Spawn(MonsterContext ctx)
    {
        var zone = _dk.Page2Zone;
        bool hasPlayer = DKPage2Zone.TryPlayerCell(ctx, out var playerCell);
        if (zone == null || !TryPickCell(zone, playerCell, hasPlayer, out var cell))
        {
            _nextSpawnAt = Time.time + RetryDelay;
            return;
        }

        Vector3 pos = zone.CellCenter(cell);
        _bearer = DKStandardBearer.Create(_dk, ctx.Animator, pos, zone.CenterWorld - pos, cell, _onBroken);   // 구역 안쪽(전장)을 본다
        Debug.Log($"[BossTrait] 기사 지휘 — 기수 등장 (칸 {cell.x},{cell.y})", _dk);

        if (_hintShown) return;
        _hintShown = true;
        UI_BossBark.Show(Hint, BossBarkType.PatternAnnounce);
    }

    private void Dismiss(string reason, bool immediate)
    {
        if (_bearer != null)
        {
            _bearer.Dismiss(immediate);
            Debug.Log($"[BossTrait] 기사 지휘 — 기수 거둠 ({reason})", _dk);
        }
        _bearer = null;
    }

    /// <summary>
    /// 가장자리 칸 하나(무작위). 1차 — 유리 앞 두 줄 밖 · 플레이어와 이웃하지 않는 칸. 2차 — 플레이어가 선 칸만 아니면.
    /// 가운데(무너진 3×3)는 늘 뺀다.
    /// </summary>
    private static bool TryPickCell(DKPage2Zone zone, Vector2Int player, bool hasPlayer, out Vector2Int cell)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            s_cells.Clear();
            for (int z = zone.MinZ; z <= zone.MaxZ; z++)
            for (int x = zone.MinX; x <= zone.MaxX; x++)
            {
                if (x != zone.MinX && x != zone.MaxX && z != zone.MinZ && z != zone.MaxZ) continue;   // 가장자리만
                if (zone.IsCollapsed(x, z)) continue;
                int gap = hasPlayer ? Mathf.Max(Mathf.Abs(x - player.x), Mathf.Abs(z - player.y)) : int.MaxValue;
                bool skip = pass == 0 ? gap < MinPlayerGap || zone.InFrontRows(z, FrontRowsKept) : gap < 1;
                if (!skip) s_cells.Add(new Vector2Int(x, z));
            }
            if (s_cells.Count == 0) continue;
            cell = s_cells[UnityEngine.Random.Range(0, s_cells.Count)];
            return true;
        }
        cell = default;
        return false;
    }

    // ── Event Handlers ────────────────────────────────────────────
    private void OnBearerBroken(DKStandardBearer bearer)
    {
        if (bearer != _bearer) return;
        _bearer      = null;
        _nextSpawnAt = Time.time + RespawnDelay;
        Debug.Log($"[BossTrait] 기사 지휘 — 기수 쓰러짐 ({bearer.StoodSeconds:0.0}초 버팀)", _dk);
    }
}
}
