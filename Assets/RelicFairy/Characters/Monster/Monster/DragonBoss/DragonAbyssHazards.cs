using System;
using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 화룡 2페이지 「심연의 화룡」 패턴(DL1~DL4 · DL-S)이 같이 쓰는 작은 도구 — 판정 · 피해 · 이펙트 · 모션 · 복귀.
/// 기존 화룡 패턴은 파일마다 같은 헬퍼를 복사해 두었는데, 새 패턴 다섯이 또 복사하지 않게 한곳에 모았다.
/// </summary>
internal static class DragonAbyssFx
{
    // 원 깔기(PackDiscs · 10-22 E5 페이지2) — 격자점을 겨눈 자리에서 가까운 순으로(정렬 비교는 한 번만 만든다)
    private static readonly List<Vector3> s_lattice = new(64);
    private static Vector3 s_latticeOrigin;
    private static readonly Comparison<Vector3> s_byLatticeDistance =
        (a, b) => FlatSq(a, s_latticeOrigin).CompareTo(FlatSq(b, s_latticeOrigin));

    // ── 색 ────────────────────────────────────────────────────
    /// <summary>
    /// 심연 패턴 바닥 예고 색 — (10-23 가이드 색 통일) 검보라 → 공용 공격 색(<see cref="PatternGuideHelper.AttackFill"/>).
    /// 이름은 예고 호출 자리를 그대로 두려고 남겼다 — 심연 원소 색(이펙트 · 화면 번쩍임)은 <see cref="AbyssVfx"/>.
    /// </summary>
    public static Color Abyss => PatternGuideHelper.AttackFill;
    /// <summary>심연 원소 색(검보라) — 이펙트 · 화면 번쩍임만.</summary>
    public static Color AbyssVfx => DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Abyss);

    public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

    // ── 바닥 · 아레나 ─────────────────────────────────────────
    public static float FloorY(MonsterContext ctx, Vector3 at) => DragonPatternFloorUtils.GetFloorY(at, ctx.Runtime.SpawnPosition.y);

    public static Vector3 OnFloor(MonsterContext ctx, Vector3 at)
    {
        at.y = FloorY(ctx, at);
        return at;
    }

    /// <summary>아레나 바닥 안쪽(가장자리에서 <paramref name="margin"/> m)으로 XZ를 끌어들인다.</summary>
    public static Vector3 ClampToArena(Vector3 p, float margin)
    {
        Bounds b  = DragonPatternFloorUtils.GetRoomFloorBoundsXZ();
        float  mx = Mathf.Min(margin, b.extents.x * 0.9f);
        float  mz = Mathf.Min(margin, b.extents.z * 0.9f);
        p.x = Mathf.Clamp(p.x, b.min.x + mx, b.max.x - mx);
        p.z = Mathf.Clamp(p.z, b.min.z + mz, b.max.z - mz);
        return p;
    }

    /// <summary>아레나를 다 덮는 반경(중심 → 모서리).</summary>
    public static float ArenaHalfDiagonal()
    {
        Bounds b = DragonPatternFloorUtils.GetRoomFloorBoundsXZ();
        return new Vector2(b.extents.x, b.extents.z).magnitude;
    }

    public static Vector3 FlatDir(Vector3 from, Vector3 to, Vector3 fallback)
    {
        Vector3 d = to - from;
        d.y = 0f;
        if (d.sqrMagnitude < 0.0001f) d = fallback;
        d.y = 0f;
        return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.forward;
    }

    /// <summary>방위(도, +Z 기준 시계 방향) — <see cref="PatternGuideHelper.Sector"/>의 yaw.</summary>
    public static float Yaw(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

    // ── 판정 (XZ) ─────────────────────────────────────────────
    /// <summary>origin에서 dir로 length · 반폭 halfWidth 직사각 띠 안인가.</summary>
    public static bool InStrip(Vector3 p, Vector3 origin, Vector3 dir, float length, float halfWidth)
    {
        Vector3 d = p - origin;
        d.y = 0f;
        float along = Vector3.Dot(d, dir);
        if (along < 0f || along > length) return false;
        return (d - dir * along).sqrMagnitude <= halfWidth * halfWidth;
    }

    public static bool InDisc(Vector3 p, Vector3 center, float radius)
    {
        float dx = p.x - center.x;
        float dz = p.z - center.z;
        return dx * dx + dz * dz <= radius * radius;
    }

    /// <summary>
    /// 반경 <paramref name="radius"/> 원을 빽빽이 깐다(10-22 E5 페이지2 · 리치 LichPatternUtil.PackDiscs와 같은 방식) — 흔든 육각 격자:
    /// 격자 한 점 = <paramref name="aim"/>(없으면 <paramref name="area"/>), 방향 · 간격은 시전마다 무작위, 원마다 조금씩 흔들어 이웃 원 가장자리 사이
    /// 빈틈이 <paramref name="gap"/>(최소 · 최대) 안에서 매번 다르다(빈자리를 무작위로 찾으면 지름 5 m 넘는 빈자리가 남았다 — 오프라인 실측).
    /// <paramref name="area"/> 둘레 <paramref name="areaRadius"/> 안 · 아레나 바닥 안 격자점을 겨눈 자리에서 가까운 순으로 <paramref name="maxCount"/>개까지 ·
    /// <paramref name="blocked"/>가 true인 자리는 비운다. <paramref name="result"/>에 이미 든 원과는 최소 빈틈을 지킨다.
    /// </summary>
    public static void PackDiscs(List<Vector3> result, Vector3 area, float areaRadius, float radius, Vector2 gap, int maxCount,
                                 Vector3? aim, Predicate<Vector3> blocked = null)
    {
        float   g      = UnityEngine.Random.Range(Mathf.Lerp(gap.x, gap.y, 0.3f), Mathf.Lerp(gap.x, gap.y, 0.7f));   // 이번 시전 간격
        float   jitter = Mathf.Min(g - gap.x, gap.y - g) * 0.5f;                                                   // 원마다 흔들림 — 이웃 틈이 gap 안에 남는다
        float   step   = radius * 2f + g;
        Vector3 origin = aim ?? area;
        float   yaw    = UnityEngine.Random.Range(0f, 60f);
        Vector3 u      = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * step;
        Vector3 v      = Quaternion.Euler(0f, yaw + 60f, 0f) * Vector3.forward * step;
        int     n      = Mathf.CeilToInt((areaRadius + Mathf.Sqrt(FlatSq(origin, area))) / (step * 0.866f)) + 1;

        s_lattice.Clear();
        for (int a = -n; a <= n; a++)
        for (int b = -n; b <= n; b++)
        {
            Vector3 p = origin + u * a + v * b;
            if (InDisc(p, area, areaRadius)) s_lattice.Add(p);
        }
        s_latticeOrigin = origin;
        s_lattice.Sort(s_byLatticeDistance);

        int first = result.Count;
        for (int i = 0; i < s_lattice.Count && result.Count - first < maxCount; i++)
        {
            Vector3 p = s_lattice[i];
            if (!aim.HasValue || FlatSq(p, origin) > 0.0001f)   // 겨눈 자리는 그대로
            {
                Vector2 o = UnityEngine.Random.insideUnitCircle * jitter;
                p += new Vector3(o.x, 0f, o.y);
            }
            if ((ClampToArena(p, radius) - p).sqrMagnitude > 0.01f || (blocked != null && blocked(p))) continue;   // 바닥 밖 · 비울 자리
            bool crowded = false;
            for (int k = 0; k < first && !crowded; k++) crowded = InDisc(p, result[k], radius * 2f + gap.x);
            if (!crowded) result.Add(p);
        }
    }

    private static float FlatSq(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    // ── 플레이어 ──────────────────────────────────────────────
    public static PlayerController Player(MonsterContext ctx)
    {
        var player = ctx.Runtime.CachedPlayer;
        if (player == null && ctx.Runtime.PlayerTarget != null)
            ctx.Runtime.PlayerTarget.TryGetComponent(out player);
        return player;
    }

    /// <summary>
    /// 공격력 × <paramref name="damageMult"/> 피해. 실제로 들어갔을 때만(회피 · 무적 아님) 넉백을 건다 — 리치 판정과 같은 규칙.
    /// </summary>
    /// <returns>피해가 들어갔으면 true.</returns>
    public static bool HitPlayer(MonsterContext ctx, float damageMult, HitWeight weight, bool ignorePoise = false,
                                 float knockbackMult = 0f, Vector3 knockFrom = default)
    {
        var player = Player(ctx);
        if (player == null) return false;

        var  stats  = player.RuntimeStats;
        bool dodged = player.IsInvincible;
        int  hp     = stats != null ? stats.Hp : 0;
        int  shield = stats != null ? stats.Shield : 0;

        player.TakeDamage(Mathf.Max(1, Mathf.RoundToInt(ctx.Config.stat.attackPower * damageMult)),
                          ctx.Monster.gameObject, ignorePoise, weight);

        bool landed = !dodged && stats != null && (stats.Hp < hp || stats.Shield < shield);
        if (landed && knockbackMult > 0f) Knockback(ctx, player, knockFrom, knockbackMult);
        return landed;
    }

    public static void Knockback(MonsterContext ctx, PlayerController player, Vector3 from, float mult)
    {
        if (player == null || mult <= 0f) return;
        Vector3 dir = player.transform.position - from;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.001f ? dir.normalized : ctx.Transform.forward;
        dir.y = 0.3f;
        player.ApplyKnockback(dir.normalized * (ctx.Config.stat.knockbackForce * mult));
    }

    // ── 이펙트 ────────────────────────────────────────────────
    /// <summary>한 번 터지는 이펙트(보스 이펙트 풀). 프리팹이 없으면 아무것도 안 한다.</summary>
    public static GameObject Burst(GameObject prefab, Vector3 pos, Quaternion rot, float scale, float fallbackLifetime = 2f)
    {
        if (prefab == null) return null;
        var go = BossEffectPool.SpawnOneShot(prefab, pos, rot, fallbackLifetime: fallbackLifetime);
        if (go != null) go.transform.localScale = Vector3.one * scale;
        return go;
    }

    /// <summary>
    /// 시각 전용 인스턴스 — 딸린 스크립트(데모 팩의 이동 · 자폭 스크립트 등)와 콜라이더를 끈다. 풀에 넣지 않으니 호출자가 Destroy한다.
    /// <paramref name="forceLoop"/>면 한 번 터지고 끝나는 이펙트도 계속 돌게 한다(불타는 띠 · 기둥).
    /// </summary>
    public static GameObject SpawnVisual(GameObject prefab, Vector3 pos, Quaternion rot, float scale,
                                         Transform parent = null, bool forceLoop = false)
    {
        if (prefab == null) return null;
        var go = UnityEngine.Object.Instantiate(prefab, pos, rot, parent);
        go.transform.localScale = Vector3.one * scale;
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null) mb.enabled = false;
        foreach (var col in go.GetComponentsInChildren<Collider>(true))
            col.enabled = false;
        if (forceLoop)
        {
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = true;
                if (!ps.isPlaying) ps.Play();
            }
        }
        return go;
    }

    /// <summary>겹친 바닥 가이드 중 이것을 위에 그린다(안전 부채꼴 · 약점 원이 큰 예고 원에 묻히지 않게).</summary>
    public static void DrawOnTop(GameObject guide, int order = 1)
    {
        if (guide != null && guide.TryGetComponent<MeshRenderer>(out var mr)) mr.sortingOrder = order;
    }

    // ── 모션 · 이동 ───────────────────────────────────────────
    public static void PlayAnim(MonsterContext ctx, string stateName, float fade)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        int hash = Animator.StringToHash(stateName);
        if (!ctx.Animator.HasState(0, hash)) return;
        ctx.Animator.speed = 1f;
        ctx.Animator.CrossFade(hash, fade, 0, 0f);
    }

    public static bool IsAnimNearEnd(MonsterContext ctx, string stateName, float normalized = 0.9f)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return true;
        int hash = Animator.StringToHash(stateName);
        if (!ctx.Animator.HasState(0, hash)) return true;
        if (ctx.Animator.IsInTransition(0)) return false;
        var info = ctx.Animator.GetCurrentAnimatorStateInfo(0);
        if (info.shortNameHash != hash) return true;
        return info.normalizedTime >= normalized;
    }

    /// <summary>수평으로 <paramref name="dir"/>을 향해 초당 <paramref name="degPerSec"/>도까지 돈다.</summary>
    public static void Face(MonsterContext ctx, Vector3 dir, float degPerSec)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        ctx.Transform.rotation = Quaternion.RotateTowards(
            ctx.Transform.rotation, Quaternion.LookRotation(dir), degPerSec * Time.deltaTime);
    }

    public static DragonBossBlackboard Blackboard(MonsterContext ctx) => (ctx.Monster as DragonBossMonster)?.DragonBlackboard;

    public static bool IsAirborne(MonsterContext ctx)
    {
        var bb = Blackboard(ctx);
        return bb != null && bb.BodyState == BodyState.Airborne;
    }

    /// <summary>땅에 내려앉는다 — 바닥 스냅 · 에이전트 복구 · 지상 상태(패턴이 끝날 때까지 에이전트는 멈춰 둔다).</summary>
    public static void Land(MonsterContext ctx)
    {
        Vector3 euler = ctx.Transform.eulerAngles;
        ctx.Transform.rotation = Quaternion.Euler(0f, euler.y, 0f);   // 급강하 · 추락 자세의 기울기를 푼다
        DragonPatternFloorUtils.SnapToFloorAndRestoreAgent(ctx);
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh) ctx.Agent.isStopped = true;
        var bb = Blackboard(ctx);
        if (bb != null) bb.BodyState = BodyState.Grounded;
    }

    /// <summary>패턴 뒤 복귀 — 공중이면 선회, 지상이면 거리 따라 추격(<see cref="DragonBossMonster.ReturnToCombat"/>).</summary>
    public static void ReturnToCombat(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.enabled && ctx.Agent.isOnNavMesh) ctx.Agent.isStopped = false;
        if (ctx.Monster is DragonBossMonster dragon) dragon.ReturnToCombat();
        else ctx.Monster.ChangeState<ChaseState>();
    }
}

/// <summary>
/// 흑염 폭풍(DL1)이 지나간 줄 — 몇 초간 타오르는 띠. 들어가 있으면 0.5초마다 약한 피해.
/// 패턴이 끝나도 제 수명을 산다. 동시에 최대 2줄 — 새 줄이 생기면 가장 오래된 줄이 꺼진다.
/// 보스가 죽거나 풀로 돌아가면(세대가 바뀌면) 함께 사라진다.
/// </summary>
public sealed class DragonAbyssFlameStrip : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────
    private const int   MaxActive   = 2;
    private const float TickSeconds = 0.5f;
    private const float FadeSeconds = 0.6f;

    private static readonly List<DragonAbyssFlameStrip> s_active = new();

    // ── Private ───────────────────────────────────────────────
    private MonsterBase _boss;
    private int         _generation;
    private Vector3     _origin;
    private Vector3     _dir;
    private float       _length;
    private float       _halfWidth;
    private float       _damageMult;
    private float       _life;
    private float       _tick;
    private GameObject  _guide;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_active.Clear();

    // ── Lifecycle ─────────────────────────────────────────────
    private void Update()
    {
        if (_boss == null || _boss.IsDead || !_boss.isActiveAndEnabled || _boss.GenerationId != _generation)
        {
            Destroy(gameObject);
            return;
        }

        _life -= Time.deltaTime;
        if (_life <= 0f)
        {
            Destroy(gameObject);
            return;
        }
        if (_life < FadeSeconds) PatternGuideHelper.SetIntensity(_guide, _life / FadeSeconds);

        _tick -= Time.deltaTime;
        if (_tick > 0f) return;
        _tick = TickSeconds;

        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player == null || !DragonAbyssFx.InStrip(player.transform.position, _origin, _dir, _length, _halfWidth)) return;
        int dmg = Mathf.Max(1, Mathf.RoundToInt(_boss.EffectiveAttackPower * _damageMult));
        player.TakeDamage(dmg, _boss.gameObject, false, HitWeight.Light);   // 장판 틱 — 약
    }

    private void OnDestroy() => s_active.Remove(this);

    // ── Public Methods ────────────────────────────────────────
    /// <param name="origin">띠 시작점(바닥).</param>
    /// <param name="damageMultPerTick">0.5초마다 공격력 × 이 값.</param>
    public static DragonAbyssFlameStrip Create(MonsterBase boss, Vector3 origin, Vector3 dir, float length, float width,
                                               float seconds, float damageMultPerTick, Color color,
                                               GameObject vfxPrefab, float vfxScale, float vfxSpacing)
    {
        while (s_active.Count >= MaxActive)
        {
            var oldest = s_active[0];
            s_active.RemoveAt(0);
            if (oldest != null) Destroy(oldest.gameObject);
        }

        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;

        var go    = new GameObject("~DragonAbyssFlameStrip");
        var strip = go.AddComponent<DragonAbyssFlameStrip>();
        strip._boss       = boss;
        strip._generation = boss != null ? boss.GenerationId : 0;
        strip._origin     = origin;
        strip._dir        = dir;
        strip._length     = length;
        strip._halfWidth  = width * 0.5f;
        strip._damageMult = damageMultPerTick;
        strip._life       = Mathf.Max(0.1f, seconds);
        strip._tick       = TickSeconds;   // 발사 판정과 같은 순간에 겹쳐 맞지 않게 첫 틱은 한 박자 뒤

        strip._guide = PatternGuideHelper.Beam(origin, dir, length, width, color);
        PatternGuideHelper.SetFlow(strip._guide, color);
        PatternGuideHelper.SetProgress(strip._guide, 1f);
        strip._guide.transform.SetParent(go.transform, true);

        if (vfxPrefab != null)
        {
            float      step = Mathf.Max(1f, vfxSpacing);
            Quaternion rot  = Quaternion.LookRotation(dir, Vector3.up);
            for (float d = step * 0.5f; d < length; d += step)
                DragonAbyssFx.SpawnVisual(vfxPrefab, origin + dir * d, rot, vfxScale, go.transform, forceLoop: true);
        }

        s_active.Add(strip);
        return strip;
    }
}

/// <summary>
/// 검은 태양(DL-S)의 날개 약점 — 날개 높이의 청록 구체 · 발밑 청록 원 · 바닥까지 닿는 세로 판정 기둥.
/// 결계 해골처럼 몬스터가 아니다 — <see cref="IDamageable"/> + MonsterHit 레이어라 근접 · 원거리 무기 판정에 그대로 잡힌다
/// (리치 봉인석 · 기사 영혼 기둥과 같은 방식). 피해량은 보지 않고 <b>타격 횟수</b>만 센다. 다 깨지면 알리고 사라진다.
/// 보스 몸을 따라 날개 쪽(오른쪽 축 ± 거리)에 붙어 있다.
/// </summary>
public sealed class DragonWingWeakPoint : MonoBehaviour, IDamageable
{
    // ── Constants ─────────────────────────────────────────────
    private const string HitLayerName = "MonsterHit";
    private const float  HitInterval  = 0.15f;   // 한 번의 휘두름이 여러 번 맞아도 1타로 센다
    private const float  FlashSeconds = 0.08f;
    private const float  OrbRadius    = 0.9f;
    private const float  LinkWidth    = 0.12f;

    // ── Private ───────────────────────────────────────────────
    private Transform  _anchor;
    private float      _side;
    private float      _offset;
    private float      _floorY;
    private int        _hitsLeft;
    private int        _hitsTotal;
    private float      _lastHitTime = float.NegativeInfinity;
    private float      _flashUntil;
    private bool       _flashing;
    private GameObject _orb;
    private GameObject _disc;
    private GameObject _link;
    private GameObject _hitVfx;
    private float      _hitVfxScale;
    private GameObject _breakVfx;
    private float      _breakVfxScale;
    private Action<DragonWingWeakPoint> _onBroken;

    // ── Properties ────────────────────────────────────────────
    public bool IsBroken { get; private set; }

    // ── Lifecycle ─────────────────────────────────────────────
    private void LateUpdate()
    {
        Follow();
        if (_flashing && Time.time >= _flashUntil)
        {
            _flashing = false;
            PatternGuideHelper.SetColor(_orb, PatternGuideHelper.Breakable);
        }
    }

    // ── Public Methods ────────────────────────────────────────
    /// <param name="side">-1 = 왼 날개, +1 = 오른 날개.</param>
    /// <param name="height">바닥에서 날개(구체)까지 높이 — 판정 기둥도 이만큼 선다.</param>
    public static DragonWingWeakPoint Create(Transform anchor, float side, float offset, float floorY, float height, float radius,
                                             int hits, GameObject hitVfx, float hitVfxScale,
                                             GameObject breakVfx, float breakVfxScale, Action<DragonWingWeakPoint> onBroken)
    {
        var go = new GameObject(side < 0f ? "DragonWingWeakPoint_L" : "DragonWingWeakPoint_R");
        int hitLayer = LayerMask.NameToLayer(HitLayerName);
        if (hitLayer >= 0) go.layer = hitLayer;

        var col    = go.AddComponent<CapsuleCollider>();
        col.radius = radius;
        col.height = Mathf.Max(height, radius * 2f);
        col.center = Vector3.up * (col.height * 0.5f);

        var wp = go.AddComponent<DragonWingWeakPoint>();
        wp._anchor        = anchor;
        wp._side          = side < 0f ? -1f : 1f;
        wp._offset        = offset;
        wp._floorY        = floorY;
        wp._hitsTotal     = Mathf.Max(1, hits);
        wp._hitsLeft      = wp._hitsTotal;
        wp._hitVfx        = hitVfx;
        wp._hitVfxScale   = hitVfxScale;
        wp._breakVfx      = breakVfx;
        wp._breakVfxScale = breakVfxScale;
        wp._onBroken      = onBroken;
        wp.Follow();

        // 시각물은 자식으로 — 루트(MonsterHit)만 판정이고 자식은 기본 레이어 · 콜라이더 꺼짐(가이드 헬퍼)
        Vector3 basePos = go.transform.position;
        Vector3 orbPos  = basePos + Vector3.up * height;
        wp._orb  = PatternGuideHelper.Sphere(orbPos, OrbRadius, PatternGuideHelper.Breakable);
        wp._orb.transform.SetParent(go.transform, true);
        wp._disc = PatternGuideHelper.Disc(basePos, radius + 0.6f, PatternGuideHelper.Breakable);
        PatternGuideHelper.SetFlow(wp._disc, PatternGuideHelper.Breakable);
        PatternGuideHelper.SetProgress(wp._disc, 1f);
        DragonAbyssFx.DrawOnTop(wp._disc, 2);
        wp._disc.transform.SetParent(go.transform, true);
        wp._link = PatternGuideHelper.Link(basePos, orbPos, LinkWidth, PatternGuideHelper.Breakable);
        wp._link.transform.SetParent(go.transform, true);
        return wp;
    }

    public void TakeDamage(float amount, GameObject instigator, float knockbackMultiplier = 1f, bool isCrit = false)
    {
        if (IsBroken) return;
        if (Time.time - _lastHitTime < HitInterval) return;
        _lastHitTime = Time.time;

        _hitsLeft--;
        if (_orb != null) DragonAbyssFx.Burst(_hitVfx, _orb.transform.position, Quaternion.identity, _hitVfxScale);
        if (_hitsLeft <= 0)
        {
            Break();
            return;
        }

        // 한 대마다 구체가 작아진다 — 남은 타수가 눈에 보이게
        if (_orb != null)
            _orb.transform.localScale = Vector3.one * (OrbRadius * 2f * Mathf.Lerp(0.55f, 1f, (float)_hitsLeft / _hitsTotal));
        PatternGuideHelper.SetColor(_orb, Color.white);
        _flashUntil = Time.time + FlashSeconds;
        _flashing   = true;
    }

    // ── Private Methods ───────────────────────────────────────
    private void Follow()
    {
        if (_anchor == null) return;
        Vector3 right = _anchor.right;
        right.y = 0f;
        if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
        right.Normalize();
        Vector3 p = _anchor.position + right * (_side * _offset);
        p.y = _floorY;
        transform.position = p;
    }

    private void Break()
    {
        IsBroken = true;
        if (TryGetComponent<Collider>(out var col)) col.enabled = false;
        Vector3 at = _orb != null ? _orb.transform.position : transform.position;
        DragonAbyssFx.Burst(_breakVfx, at, Quaternion.identity, _breakVfxScale);
        _onBroken?.Invoke(this);
        Destroy(gameObject);
    }
}
}
