using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 죽음의 기사 2페이지 「왕을 벤 검」 — 플레이어 쪽 격자 구역(09-28 설계 확정 §5).
///
/// 기사 격자(<see cref="DKBossRoomContext"/>)는 바닥 전체(유리벽 너머 기사 쪽 포함)를 덮는다.
/// 2페이지 패턴은 플레이어가 설 수 있는 칸만 쓴다:
///   · 가운데 칸 = 플레이어 구역 중심(피라미드 앵커) — 무대 붕괴 3×3의 중심
///   · 앞줄 = 유리벽 바로 앞 줄(기사에 가장 가까운 줄) — 앵커에서 기사 쪽으로 유리 콜라이더까지 레이로 잰다.
///     유리를 못 찾으면 앵커와 기사 칸의 가운데를 쓴다(Ch3 아레나: 앵커 z=−11 · 기사 z=14 · 유리 z≈1 → 둘 다 같은 줄).
/// 줄(z)은 기사 쪽으로 <see cref="TowardKnight"/>만큼 늘어난다. 칸(x)은 격자 안쪽 전체.
/// 전환 연출 순간(격자 중심 덮어쓰기 없음) 한 번 재서 기사가 들고 있다(<see cref="DeathKnightBossMonster.Page2Zone"/>).
/// </summary>
public sealed class DKPage2Zone
{
    // ── Constants ──────────────────────────────────────────────
    /// <summary>2페이지 예고색 — 흑백이 섞인 회색 검(차오르는 예고 = 보스 색).</summary>
    public static readonly Color Grey    = new Color(0.55f, 0.57f, 0.66f);
    /// <summary>회색 검 이펙트 틴트(파티클 · 슬래시).</summary>
    public static readonly Color GreyVfx = new Color(0.72f, 0.72f, 0.78f, 1f);
    /// <summary>안전 = 흰색(예고 색 규약).</summary>
    public static readonly Color SafeWhite = Color.white;

    private const float GlassProbeHeight = 1f;
    private const float GlassProbeRange  = 60f;
    private const float GlassFrontGap    = 0.9f;   // 끌려가는 목표 — 유리 바로 앞

    // ── Properties ─────────────────────────────────────────────
    public int        MinX         { get; private set; }
    public int        MaxX         { get; private set; }
    public int        MinZ         { get; private set; }
    public int        MaxZ         { get; private set; }
    public Vector2Int Center       { get; private set; }
    /// <summary>플레이어 구역 중심 월드 위치(바닥) — 앵커 그대로(칸 중심은 격자 폭이 짝수라 1 m 어긋난다).</summary>
    public Vector3    CenterWorld  { get; private set; }
    public int        FrontRow     { get; private set; }
    public int        TowardKnight { get; private set; }
    public float      FloorY       { get; private set; }
    /// <summary>유리 바로 앞 z — 왕좌의 인력이 끌어당기는 목표 줄.</summary>
    public float      FrontLimitZ  { get; private set; }
    /// <summary>무대 붕괴(가운데 3×3) XZ 경계.</summary>
    public Bounds     CollapseRect { get; private set; }

    public int Columns => MaxX - MinX + 1;
    public int Rows    => MaxZ - MinZ + 1;

    // ── Public Methods ─────────────────────────────────────────

    /// <summary>기사 위치 · 피라미드 앵커 · 유리벽으로 플레이어 구역을 잰다.</summary>
    public static DKPage2Zone Resolve(DeathKnightBossMonster dk)
    {
        var z = new DKPage2Zone();
        int w = DKBossRoomContext.Width, h = DKBossRoomContext.Height;

        Transform anchor   = dk.PyramidStrikeAnchor;
        Vector3   knight   = dk.transform.position;
        Vector3   centerW  = anchor != null ? anchor.position : DKBossRoomContext.CellToWorld(w / 2, h / 2, 0f);
        Vector2Int center  = DKBossRoomContext.WorldToCell(centerW);
        int knightRow      = DKBossRoomContext.WorldToCell(knight).y;

        z.FloorY       = anchor != null ? anchor.position.y : DKBossRoomContext.WorldCenter.y;
        z.TowardKnight = knightRow >= center.y ? 1 : -1;
        z.Center       = center;
        z.CenterWorld  = new Vector3(centerW.x, z.FloorY, centerW.z);

        // 앞줄 — 유리벽 바로 앞(플레이어가 유리에 붙었을 때 서는 줄)
        Vector3 dir = new Vector3(0f, 0f, z.TowardKnight);
        Vector3 probe = new Vector3(centerW.x, z.FloorY + GlassProbeHeight, centerW.z);
        if (dk.TryFindGlass(probe, dir, GlassProbeRange, out float glassDist))
        {
            Vector3 glass = probe + dir * glassDist;
            z.FrontRow    = DKBossRoomContext.WorldToCell(glass - dir * 0.35f).y;
            z.FrontLimitZ = glass.z - dir.z * GlassFrontGap;
        }
        else
        {
            z.FrontRow    = center.y + z.TowardKnight * (Mathf.Abs(knightRow - center.y) / 2);
            z.FrontLimitZ = DKBossRoomContext.CellToWorld(center.x, z.FrontRow, 0f).z;
        }
        z.FrontRow = Mathf.Clamp(z.FrontRow, 1, h - 2);

        z.MinX = 1;
        z.MaxX = w - 2;
        z.MinZ = z.TowardKnight > 0 ? 1 : z.FrontRow;
        z.MaxZ = z.TowardKnight > 0 ? z.FrontRow : h - 2;

        z.CollapseRect = z.CellRect(center.x - 1, center.y - 1, center.x + 1, center.y + 1);
        return z;
    }

    /// <summary>이 칸이 플레이어 구역 안인가.</summary>
    public bool Contains(int x, int z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;

    public bool Contains(Vector2Int c) => Contains(c.x, c.y);

    /// <summary>무너진 가운데 3×3 칸인가(무대 변화 — 패턴 타일을 깔지 않는다).</summary>
    public bool IsCollapsed(int x, int z) => Mathf.Abs(x - Center.x) <= 1 && Mathf.Abs(z - Center.y) <= 1;

    /// <summary>칸 중심(바닥 높이).</summary>
    public Vector3 CellCenter(int x, int z)
    {
        Vector3 p = DKBossRoomContext.CellToWorld(x, z, 0f);
        p.y = FloorY;
        return p;
    }

    public Vector3 CellCenter(Vector2Int c) => CellCenter(c.x, c.y);

    /// <summary>앞줄에서 <paramref name="index"/>번째 줄(0 = 유리 바로 앞, 1 = 그 뒤 …).</summary>
    public int FrontRowAt(int index) => FrontRow - TowardKnight * index;

    /// <summary>이 줄이 앞줄 <paramref name="count"/>개 안인가.</summary>
    public bool InFrontRows(int row, int count)
    {
        int depth = (FrontRow - row) * TowardKnight;
        return depth >= 0 && depth < count;
    }

    /// <summary>칸 사각형(양 끝 포함)의 XZ 경계.</summary>
    public Bounds CellRect(int x0, int z0, int x1, int z1)
    {
        float half = DKBossRoomContext.CellSize * 0.5f;
        Vector3 a = CellCenter(Mathf.Min(x0, x1), Mathf.Min(z0, z1));
        Vector3 b = CellCenter(Mathf.Max(x0, x1), Mathf.Max(z0, z1));
        var bounds = new Bounds();
        bounds.SetMinMax(new Vector3(a.x - half, FloorY, a.z - half), new Vector3(b.x + half, FloorY, b.z + half));
        return bounds;
    }

    /// <summary>
    /// 기사 격자 피격 — 공격력 × 배율, 가해자 = 기사, 넉백은 <paramref name="from"/>에서 바깥으로.
    /// (<see cref="DKGridPatternHelper"/>의 피격과 같은 식 — 피격 등급만 패턴이 정한다)
    /// </summary>
    public static void HitPlayer(MonsterContext ctx, float damageMult, float knockbackMult, HitWeight weight, Vector3 from)
    {
        var player = ctx?.Runtime?.CachedPlayer;
        if (player == null || ctx.Config?.stat == null) return;

        // 악몽 「지휘」 — 환영 기수가 서 있는 동안 +15%
        int dmg = Mathf.Max(1, (int)(ctx.Config.stat.attackPower * damageMult * DKStandardBearer.DamageScale(ctx.Monster)));
        player.TakeDamage(dmg, ctx.Monster != null ? ctx.Monster.gameObject : null, false, weight);

        Vector3 away = player.transform.position - from;
        away.y = 0f;
        Vector3 dir = away.sqrMagnitude > 0.001f ? away.normalized : -ctx.Transform.forward;
        dir.y = 0.2f;
        player.ApplyKnockback(dir * ctx.Config.stat.knockbackForce * knockbackMult);
    }

    /// <summary>플레이어가 서 있는 칸. 플레이어가 없으면 false.</summary>
    public static bool TryPlayerCell(MonsterContext ctx, out Vector2Int cell)
    {
        cell = default;
        if (ctx?.Runtime?.PlayerTarget == null) return false;
        cell = DKBossRoomContext.WorldToCell(ctx.Runtime.PlayerTarget.position);
        return true;
    }

    /// <summary>줄 하나를 덮는 회색 슬래시 이펙트 — <paramref name="alongX"/>면 가로줄(행), 아니면 세로줄(열).</summary>
    public void SpawnLineVfx(GameObject prefab, Vector3 lineCenter, bool alongX, int cellCount)
    {
        if (prefab == null) return;
        float len = cellCount * DKBossRoomContext.CellSize * 0.1f;   // Sword Slash 15: 1칸 = scale 0.1 × CellSize
        Quaternion rot = alongX ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
        DKGridPatternHelper.SpawnStretchedVfx(prefab, lineCenter + Vector3.up * 0.1f, rot, len, GreyVfx);
    }

    // ── 2페이지 패턴 상태 공용 ─────────────────────────────────
    // 2페이지 패턴은 설계 타이밍(0.6 · 0.5 · 0.45 · 1.2초)을 그대로 쓴다 — 광폭 애니 배율(1.4)로 예고를 줄이지 않는다.

    /// <summary>애니메이터 상태 재생(없으면 건너뜀). 속도는 설계 타이밍과 맞게 기본 1.</summary>
    public static void PlayAnim(MonsterContext ctx, string state, float speed = 1f, float fade = 0.08f)
    {
        if (ctx?.Animator == null || string.IsNullOrEmpty(state)) return;
        if (!ctx.Animator.HasState(0, Animator.StringToHash(state))) return;
        ctx.Animator.speed = speed;
        ctx.Animator.CrossFade(state, fade, 0, 0f);
    }

    public static void StopAgent(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
        {
            ctx.Agent.isStopped = true;
            ctx.Agent.velocity  = Vector3.zero;
            ctx.Agent.ResetPath();
        }
    }

    public static void RestoreAgent(MonsterContext ctx)
    {
        if (ctx.Agent != null && ctx.Agent.isOnNavMesh)
            ctx.Agent.isStopped = false;
    }

    public static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }

    public static DKSwordColor SwordColor(MonsterContext ctx)
        => (ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.SwordColor ?? DKSwordColor.White;

    public static DKSwordColor Opposite(DKSwordColor c)
        => c == DKSwordColor.White ? DKSwordColor.Black : DKSwordColor.White;
}
}
