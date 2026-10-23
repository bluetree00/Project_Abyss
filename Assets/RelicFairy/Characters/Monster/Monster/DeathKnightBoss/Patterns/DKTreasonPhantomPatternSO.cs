using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 죽음의 기사 2페이지 KL3 「반역의 환영」(09-28 설계 확정 §5).
///
/// 흐름:
///  Enter  → 플레이어가 선 칸을 가운데로 잡고, 네 방향(기사 쪽 · 오른쪽 · 뒤 · 왼쪽) 이웃 칸 바깥에 환영 넷이 선다(페이드인)
///  차례로 → 시작 방향은 무작위, 시계 방향으로 strikeInterval(0.45)초마다 환영 하나가 제 앞 칸을 벤다
///  마지막 → 환영 넷이 한꺼번에 가운데 칸을 벤다(기사도 같은 박자에 검을 내린다)
///  흑백 규칙(10-03 S3) → 환영 넷은 흰 / 검 번갈아 선다. 지금 검 색과 같은 환영만 제 앞 칸을 벤다 — 다른 색 환영 앞 칸이 안전.
///  앞 줄(10-22 E5 페이지2) → 같은 색 환영은 제 앞 칸과 그 양옆(환영 쪽 3칸 줄, 구역 안만)을 벤다 — 3×3 안 안전은 다른 색 환영 칸 둘뿐
///                       (+ 이미 베인 줄). 예전엔 아홉 칸 중 여섯이 안전해 비키는 느낌이 없었다.
///
/// 칸 예고: 각 칸은 베기 cellTelegraph초 전에 회색으로 차오르고, lockLead(0.4)초 전에 판정 색으로 굳는다.
/// 칸은 시작 때 한 번 정해지고 따라오지 않는다(R3 — 판정 0.6초 전부터 고정).
/// 환영: 기사 애니메이터 자식 복제(환영 돌진과 같은 방식). 애니메이터가 루트에 붙어 있으면 환영 없이 예고 · 판정만.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/DeathKnight/Page2/DK_KL3_TreasonPhantom", fileName = "DK_KL3_TreasonPhantom")]
public class DKTreasonPhantomPatternSO : BossPatternSO
{
    [Header("환영")]
    public float  phantomFadeIn    = 0.4f;
    public float  phantomFadeOut   = 0.3f;
    [Tooltip("환영 공격 모션")]
    public string phantomAnim      = "Attack2";
    [Tooltip("공격 모션에서 칼이 내려오는 순간(초, 속도 1) — 베기보다 이만큼 먼저 모션을 건다")]
    public float  swingLead        = 0.35f;

    [Header("타이밍 (초)")]
    [Tooltip("첫 베기 시각(환영이 다 드러난 뒤)")]
    public float firstStrikeTime = 1.0f;
    [Tooltip("베기 간격 — 설계 0.45")]
    public float strikeInterval  = 0.45f;
    [Tooltip("각 칸 예고 시작(베기 전 초)")]
    public float cellTelegraph   = 0.6f;
    [Tooltip("각 칸이 판정 색으로 굳는 시점(베기 전 초). R3: 0.4 이상")]
    public float lockLead        = 0.4f;
    public float recoveryTime    = 0.5f;

    [Header("VFX · 사운드")]
    [Tooltip("칸 베기 이펙트 (Sword Slash mirror)")]
    public GameObject slashVfxPrefab;
    public float      slashVfxScale = 1.5f;
    public AudioClip  slashSfx;

    [Header("데미지")]
    public float damageMultiplier       = 0.9f;
    public float centerDamageMultiplier = 1.2f;
    public float knockbackMultiplier    = 1f;

    private DKTreasonPhantomState _state;

    public override void Initialize(BossPatternContext ctx) => _state = new DKTreasonPhantomState(this);
    public override void OnRecycled()                      => _state = new DKTreasonPhantomState(this);

    public override bool CanExecute(BossPatternContext ctx)
        => ctx.Ctx.Runtime.PlayerTarget != null && ctx.Ctx.Monster is DeathKnightBossMonster;

    public override SpecialStateBase GetRuntimeState() => _state;
}

public class DKTreasonPhantomState : FullLockState<DKTreasonPhantomPatternSO>
{
    private const string ChannelAnim = "Idle2";
    private const string KnightSwing = "Attack2";
    private const int    Sides       = 4;
    private const float  GuideLinger = 0.15f;
    private const float  SquareY     = 0.15f;   // 판정 칸 사각 — 원 예고(0.2) 아래 · 바닥 잔해(≈0.1) 위(10-08 점검)
    private const float  SquareAlpha = 0.35f;
    private static readonly Color ArmedSquare = new Color(1f, 0.1f, 0.1f, 0.5f);   // 굳은 판정 색(PatternGuideHelper.Active)

    // 한 번의 베기(0~3 = 네 이웃 칸, 4 = 가운데)
    private struct Cut
    {
        public bool          Active;
        public Vector2Int    Cell;
        public Vector2Int    Side;        // 줄 방향(환영 방향에 수직) — 가운데 베기는 0(한 칸) · 10-22 E5 페이지2
        public int           Lo, Hi;      // 줄 범위: Cell + Side × [Lo, Hi] (구역 밖 칸은 뺀다)
        public float         HitTime;
        public bool          Shown, Locked, Swung, Fired;
        public GameObject    Guide;
        public float         KillAt;
        public GameObject    Square;      // 판정 칸 그대로의 사각(10-08 점검)
        public MeshRenderer  SquareMr;
        public Material      SquareMat;
    }

    private readonly Cut[]            _cuts        = new Cut[Sides + 1];
    private readonly GameObject[]     _phantoms    = new GameObject[Sides];
    private readonly Animator[]       _phantomAnims = new Animator[Sides];
    private readonly DKPhantomClone[] _phantomFx   = new DKPhantomClone[Sides];
    private readonly int[]            _cutOfPhantom = new int[Sides];
    private DKSwordColor _sword;   // 이번 시전의 검 색 — 같은 색 환영만 벤다
    private DKPage2Zone _zone;
    private float       _timer;
    private float       _endTime;

    public DKTreasonPhantomState(DKTreasonPhantomPatternSO data) : base(data) { }

    public override void Enter(MonsterContext ctx)
    {
        _zone  = (ctx.Monster as DeathKnightBossMonster)?.Page2Zone;
        _timer = 0f;
        for (int i = 0; i < _cuts.Length; i++) _cuts[i] = default;

        DKPage2Zone.StopAgent(ctx);
        DKPage2Zone.FacePlayer(ctx);
        DKPage2Zone.PlayAnim(ctx, ChannelAnim);
        if (_zone == null) { _endTime = 0f; return; }

        Vector2Int center = DKPage2Zone.TryPlayerCell(ctx, out var pc) && _zone.Contains(pc) ? pc : _zone.Center;
        var dk = ctx.Monster as DeathKnightBossMonster;
        _sword = dk?.DKBlackboard != null ? dk.DKBlackboard.SwordColor : DKSwordColor.White;
        dk?.HintTreasonColor();
        int start = Random.Range(0, Sides);
        int first = Random.Range(0, 2);               // 첫 환영 색 — 이웃끼리 번갈아 선다(검 색 2 · 다른 색 2)
        int slot  = 0;
        for (int k = 0; k < Sides; k++)
        {
            int side = (start + k) % Sides;            // 시계 방향(기사 쪽 → 오른쪽 → 뒤 → 왼쪽)
            Vector2Int d = SideDir(side);
            Vector2Int target = center + d;
            _cutOfPhantom[k] = -1;
            if (!_zone.Contains(target)) continue;     // 구역 밖(유리벽 너머 등)은 건너뛴다

            DKSwordColor color = ((side + first) % 2 == 0) ? _sword : Opposite(_sword);
            SpawnPhantom(ctx, k, target, d, color);
            if (color != _sword) continue;             // 다른 색 환영은 서 있기만 한다 — 그 칸이 안전

            _cuts[slot] = StripCut(target, d, Data.firstStrikeTime + slot * Data.strikeInterval);   // 앞 줄 3칸(10-22 E5 페이지2)
            _cutOfPhantom[k] = slot;
            slot++;
        }
        _cuts[Sides] = new Cut { Active = true, Cell = center, HitTime = Data.firstStrikeTime + slot * Data.strikeInterval };
        _endTime = _cuts[Sides].HitTime + Data.recoveryTime;
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        for (int i = 0; i < _cuts.Length; i++)
        {
            ref Cut cut = ref _cuts[i];
            if (!cut.Active) continue;

            if (!cut.Shown && _timer >= cut.HitTime - Data.cellTelegraph)
            {
                cut.Shown = true;
                Color c = PatternGuideHelper.AttackFill;   // (10-23 가이드 색 통일) 예고는 공용 공격 색 — 「같은 색이 벤다」는 환영 몸 · 베기 이펙트의 검 색이 맡는다
                // 판정은 칸 전체(2 m 사각)다 — 예전 원(반지름 1.1)은 칸 모서리를 못 덮고 이웃 칸으로 0.1 m 삐져나갔다.
                // 사각 = 판정 칸 그대로, 원은 그 안에서 차오르는 박자만 보인다(10-08 점검)
                cut.Guide = PatternGuideHelper.Prepare(CutGuide(cut, c), c);   // 한 칸 = 원 · 줄 = 줄 그대로의 직선(10-22 E5 페이지2)
                ShowSquare(ref cut, new Color(c.r, c.g, c.b, SquareAlpha));
            }
            if (cut.Shown && !cut.Locked)
            {
                float open = Mathf.Max(0.01f, Data.cellTelegraph - Data.lockLead);
                PatternGuideHelper.SetProgress(cut.Guide, (_timer - (cut.HitTime - Data.cellTelegraph)) / open);
                if (_timer >= cut.HitTime - Data.lockLead)
                {
                    cut.Locked = true;
                    PatternGuideHelper.Arm(cut.Guide);
                    if (cut.SquareMat != null) cut.SquareMat.color = ArmedSquare;   // 사각도 원과 같이 판정 색으로 굳는다
                }
            }
            if (!cut.Swung && _timer >= cut.HitTime - Data.swingLead)
            {
                cut.Swung = true;
                Swing(ctx, i);
            }
            if (!cut.Fired && _timer >= cut.HitTime) Fire(ctx, ref cut, i == Sides);
            if (cut.Fired && _timer >= cut.KillAt)
            {
                PatternGuideHelper.SafeDestroy(ref cut.Guide);
                HideSquare(ref cut);
            }
        }

        if (_timer >= _endTime)
            ctx.Monster.ChangeState<AttackReadyState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        for (int i = 0; i < _cuts.Length; i++)
        {
            PatternGuideHelper.SafeDestroy(ref _cuts[i].Guide);
            HideSquare(ref _cuts[i]);
        }
        for (int k = 0; k < Sides; k++) DismissPhantom(k);
        DKPage2Zone.RestoreAgent(ctx);
    }

    // ── 베기 ───────────────────────────────────────────────

    /// <summary>0 = 기사 쪽, 1 = 오른쪽, 2 = 뒤, 3 = 왼쪽 (위에서 볼 때 시계 방향).</summary>
    private Vector2Int SideDir(int side)
    {
        int n = _zone.TowardKnight;
        return side switch
        {
            0 => new Vector2Int(0,  n),
            1 => new Vector2Int(n,  0),
            2 => new Vector2Int(0, -n),
            _ => new Vector2Int(-n, 0),
        };
    }

    /// <summary>이 베기의 칼을 내린다 — 이웃 칸은 그 칸의 환영, 가운데는 환영 넷 모두와 기사.</summary>
    private void Swing(MonsterContext ctx, int cutIndex)
    {
        bool center = cutIndex == Sides;
        for (int k = 0; k < Sides; k++)
        {
            if (_phantomAnims[k] == null) continue;
            if (!center && _cutOfPhantom[k] != cutIndex) continue;
            _phantomAnims[k].speed = 1f;
            _phantomAnims[k].CrossFade(Data.phantomAnim, 0.05f, 0, 0f);
        }
        if (center) DKPage2Zone.PlayAnim(ctx, KnightSwing);
    }

    private void Fire(MonsterContext ctx, ref Cut cut, bool center)
    {
        cut.Fired  = true;
        cut.KillAt = _timer + GuideLinger;

        Vector3 pos = CutCenter(cut);   // 줄이면 줄 가운데(이펙트는 하나 그대로 · 10-22 E5 페이지2)
        if (Data.slashVfxPrefab != null)
        {
            var fx = BossEffectPool.SpawnOneShot(Data.slashVfxPrefab, pos + Vector3.up * 0.1f,
                                                 Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), fallbackLifetime: 2.5f);
            if (fx != null)
            {
                fx.transform.localScale = Vector3.one * Data.slashVfxScale;
                DKGridPatternHelper.TintVfx(fx, center ? DKPage2Zone.GreyVfx : SwordTint(_sword));
            }
        }
        if (Data.slashSfx != null) Managers.Sound?.PlayEffectAt(Data.slashSfx, pos);

        if (DKPage2Zone.TryPlayerCell(ctx, out var pc) && InCut(cut, pc))
            DKPage2Zone.HitPlayer(ctx, center ? Data.centerDamageMultiplier : Data.damageMultiplier,
                                  Data.knockbackMultiplier, center ? HitWeight.Auto : HitWeight.Light, pos);

        if (center)
        {
            BossImpactFeedback.TriggerHitStop(0.08f);
            BossImpactFeedback.TriggerCameraShake(0.12f, 0.25f);
            for (int k = 0; k < Sides; k++) DismissPhantom(k);   // 마지막 베기 뒤 흩어진다
        }
    }

    /// <summary>판정 칸 그대로의 사각을 깐다 — 판정(<see cref="DKPage2Zone.TryPlayerCell"/> == 칸)과 같은 넓이(10-08 점검).</summary>
    private void ShowSquare(ref Cut cut, Color color)
    {
        var (go, mr, mat) = QuadTilePool.Rent();
        go.name = "DK_TreasonCell";
        float cs  = DKBossRoomContext.CellSize;
        float len = (cut.Hi - cut.Lo + 1) * cs;   // 줄이면 줄 길이 — 판정 칸 그대로(10-22 E5 페이지2)
        go.transform.SetPositionAndRotation(CutCenter(cut) + Vector3.up * SquareY, Quaternion.Euler(90f, 0f, 0f));
        go.transform.localScale = new Vector3(cut.Side.x != 0 ? len : cs, cut.Side.y != 0 ? len : cs, cs);   // 눕힌 쿼드: x = 월드 x · y = 월드 z
        mat.color     = color;
        cut.Square    = go;
        cut.SquareMr  = mr;
        cut.SquareMat = mat;
    }

    private static void HideSquare(ref Cut cut)
    {
        if (cut.Square == null) return;
        QuadTilePool.Return(cut.Square, cut.SquareMr, cut.SquareMat);
        cut.Square    = null;
        cut.SquareMr  = null;
        cut.SquareMat = null;
    }

    // ── 앞 줄(10-22 E5 페이지2) ──────────────────────────────

    /// <summary>같은 색 환영의 베기 — 제 앞 칸 <paramref name="cell"/>과 그 양옆(환영 방향 <paramref name="outward"/>에 수직), 구역 안 칸만.</summary>
    private Cut StripCut(Vector2Int cell, Vector2Int outward, float hitTime)
    {
        var side = new Vector2Int(Mathf.Abs(outward.y), Mathf.Abs(outward.x));
        return new Cut
        {
            Active = true, Cell = cell, HitTime = hitTime, Side = side,
            Lo = _zone.Contains(cell - side) ? -1 : 0,
            Hi = _zone.Contains(cell + side) ?  1 : 0,
        };
    }

    /// <summary>이 칸이 베기 범위 안인가 — 한 칸이면 그 칸, 줄이면 줄 위 [Lo, Hi].</summary>
    private static bool InCut(in Cut cut, Vector2Int c)
    {
        if (cut.Side == Vector2Int.zero) return c == cut.Cell;
        Vector2Int d   = c - cut.Cell;
        int        k   = cut.Side.x != 0 ? d.x : d.y;   // 줄을 따라
        int        off = cut.Side.x != 0 ? d.y : d.x;   // 줄에서 벗어남
        return off == 0 && k >= cut.Lo && k <= cut.Hi;
    }

    /// <summary>베기 범위 가운데(바닥) — 줄이면 양 끝 칸의 가운데.</summary>
    private Vector3 CutCenter(in Cut cut)
        => (_zone.CellCenter(cut.Cell + cut.Side * cut.Lo) + _zone.CellCenter(cut.Cell + cut.Side * cut.Hi)) * 0.5f;

    /// <summary>차오르는 예고 — 한 칸이면 원(박자만), 줄이면 줄 그대로의 직선(줄을 따라 차오른다).</summary>
    private GameObject CutGuide(in Cut cut, Color c)
    {
        float cs  = DKBossRoomContext.CellSize;
        int   len = cut.Hi - cut.Lo + 1;
        if (len <= 1) return PatternGuideHelper.Disc(_zone.CellCenter(cut.Cell), cs * 0.5f, c);
        Vector3 dir = new Vector3(cut.Side.x, 0f, cut.Side.y);
        return PatternGuideHelper.Beam(_zone.CellCenter(cut.Cell + cut.Side * cut.Lo) - dir * (cs * 0.5f), dir, len * cs, cs, c);
    }

    // ── 환영 ───────────────────────────────────────────────

    /// <summary>이웃 칸 바깥쪽 가장자리에 서서 그 칸(가운데 쪽)을 본다. 충돌체는 끈다.</summary>
    private static DKSwordColor Opposite(DKSwordColor c) => c == DKSwordColor.White ? DKSwordColor.Black : DKSwordColor.White;

    /// <summary>흰 = 흰빛, 검 = 읽히는 보라(순수 검정은 바닥에 묻힌다).</summary>
    private static Color SwordTint(DKSwordColor c) => c == DKSwordColor.White ? Color.white : DKGridPatternHelper.DarkReadableTint;

    private void SpawnPhantom(MonsterContext ctx, int k, Vector2Int target, Vector2Int outward, DKSwordColor color)
    {
        if (ctx.Animator == null || ctx.Animator.gameObject == ctx.Monster.gameObject) return;

        // 칸 중심은 플레이어 구역 바닥 높이 — 기사는 약 1 m 단 위에 서 있다(환영 돌진의 「루트 − 1 m」와 같은 자리)
        Vector3 facing = new Vector3(-outward.x, 0f, -outward.y);
        Vector3 pos    = _zone.CellCenter(target) - facing * (DKBossRoomContext.CellSize * 0.6f);

        var go = Object.Instantiate(ctx.Animator.gameObject, pos, Quaternion.LookRotation(facing));
        foreach (var col in go.GetComponentsInChildren<Collider>()) col.enabled = false;

        var fx = go.AddComponent<DKPhantomClone>();
        fx.Initialize();
        // 제 색으로 빛난다 — 흰 환영 / 검은(보라) 환영
        if (color == DKSwordColor.White) fx.SetTint(new Color(0.95f, 0.95f, 1f, 1f), new Color(1.4f, 1.4f, 1.6f, 1f));
        else                             fx.SetTint(new Color(0.25f, 0.08f, 0.4f, 1f), new Color(0.9f, 0.25f, 1.5f, 1f));
        fx.StartFadeIn(Data.phantomFadeIn);

        _phantoms[k]     = go;
        _phantomAnims[k] = go.GetComponent<Animator>();
        _phantomFx[k]    = fx;
        if (_phantomAnims[k] != null)
        {
            _phantomAnims[k].speed = 1f;
            _phantomAnims[k].CrossFade(ChannelAnim, 0.05f, 0, 0f);
        }
    }

    private void DismissPhantom(int k)
    {
        var go = _phantoms[k];
        if (go == null) return;
        _phantoms[k]     = null;
        _phantomAnims[k] = null;

        var fx = _phantomFx[k];
        _phantomFx[k] = null;
        if (fx != null) fx.StartFadeOut(Data.phantomFadeOut, () => { if (go != null) Object.Destroy(go); });
        else            Object.Destroy(go);
    }
}
}
