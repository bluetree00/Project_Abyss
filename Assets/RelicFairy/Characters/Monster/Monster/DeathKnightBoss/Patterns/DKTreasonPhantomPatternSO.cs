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

    // 한 번의 베기(0~3 = 네 이웃 칸, 4 = 가운데)
    private struct Cut
    {
        public bool          Active;
        public Vector2Int    Cell;
        public float         HitTime;
        public bool          Shown, Locked, Swung, Fired;
        public GameObject    Guide;
        public float         KillAt;
    }

    private readonly Cut[]            _cuts        = new Cut[Sides + 1];
    private readonly GameObject[]     _phantoms    = new GameObject[Sides];
    private readonly Animator[]       _phantomAnims = new Animator[Sides];
    private readonly DKPhantomClone[] _phantomFx   = new DKPhantomClone[Sides];
    private readonly int[]            _cutOfPhantom = new int[Sides];
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
        int start = Random.Range(0, Sides);
        int slot  = 0;
        for (int k = 0; k < Sides; k++)
        {
            int side = (start + k) % Sides;            // 시계 방향(기사 쪽 → 오른쪽 → 뒤 → 왼쪽)
            Vector2Int d = SideDir(side);
            Vector2Int target = center + d;
            _cutOfPhantom[k] = -1;
            if (!_zone.Contains(target)) continue;     // 구역 밖(유리벽 너머 등)은 건너뛴다

            _cuts[slot] = new Cut { Active = true, Cell = target, HitTime = Data.firstStrikeTime + slot * Data.strikeInterval };
            _cutOfPhantom[k] = slot;
            SpawnPhantom(ctx, k, target, d);
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
                cut.Guide = PatternGuideHelper.Prepare(
                    PatternGuideHelper.Disc(_zone.CellCenter(cut.Cell), DKBossRoomContext.CellSize * 0.55f, DKPage2Zone.Grey),
                    DKPage2Zone.Grey);
            }
            if (cut.Shown && !cut.Locked)
            {
                float open = Mathf.Max(0.01f, Data.cellTelegraph - Data.lockLead);
                PatternGuideHelper.SetProgress(cut.Guide, (_timer - (cut.HitTime - Data.cellTelegraph)) / open);
                if (_timer >= cut.HitTime - Data.lockLead)
                {
                    cut.Locked = true;
                    PatternGuideHelper.Arm(cut.Guide);
                }
            }
            if (!cut.Swung && _timer >= cut.HitTime - Data.swingLead)
            {
                cut.Swung = true;
                Swing(ctx, i);
            }
            if (!cut.Fired && _timer >= cut.HitTime) Fire(ctx, ref cut, i == Sides);
            if (cut.Fired && cut.Guide != null && _timer >= cut.KillAt) PatternGuideHelper.SafeDestroy(ref cut.Guide);
        }

        if (_timer >= _endTime)
            ctx.Monster.ChangeState<AttackReadyState>();
    }

    public override void Exit(MonsterContext ctx)
    {
        for (int i = 0; i < _cuts.Length; i++) PatternGuideHelper.SafeDestroy(ref _cuts[i].Guide);
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

        Vector3 pos = _zone.CellCenter(cut.Cell);
        if (Data.slashVfxPrefab != null)
        {
            var fx = BossEffectPool.SpawnOneShot(Data.slashVfxPrefab, pos + Vector3.up * 0.1f,
                                                 Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), fallbackLifetime: 2.5f);
            if (fx != null)
            {
                fx.transform.localScale = Vector3.one * Data.slashVfxScale;
                DKGridPatternHelper.TintVfx(fx, DKPage2Zone.GreyVfx);
            }
        }
        if (Data.slashSfx != null) Managers.Sound?.PlayEffectAt(Data.slashSfx, pos);

        if (DKPage2Zone.TryPlayerCell(ctx, out var pc) && pc == cut.Cell)
            DKPage2Zone.HitPlayer(ctx, center ? Data.centerDamageMultiplier : Data.damageMultiplier,
                                  Data.knockbackMultiplier, center ? HitWeight.Auto : HitWeight.Light, pos);

        if (center)
        {
            BossImpactFeedback.TriggerHitStop(0.08f);
            BossImpactFeedback.TriggerCameraShake(0.12f, 0.25f);
            for (int k = 0; k < Sides; k++) DismissPhantom(k);   // 마지막 베기 뒤 흩어진다
        }
    }

    // ── 환영 ───────────────────────────────────────────────

    /// <summary>이웃 칸 바깥쪽 가장자리에 서서 그 칸(가운데 쪽)을 본다. 충돌체는 끈다.</summary>
    private void SpawnPhantom(MonsterContext ctx, int k, Vector2Int target, Vector2Int outward)
    {
        if (ctx.Animator == null || ctx.Animator.gameObject == ctx.Monster.gameObject) return;

        // 칸 중심은 플레이어 구역 바닥 높이 — 기사는 약 1 m 단 위에 서 있다(환영 돌진의 「루트 − 1 m」와 같은 자리)
        Vector3 facing = new Vector3(-outward.x, 0f, -outward.y);
        Vector3 pos    = _zone.CellCenter(target) - facing * (DKBossRoomContext.CellSize * 0.6f);

        var go = Object.Instantiate(ctx.Animator.gameObject, pos, Quaternion.LookRotation(facing));
        foreach (var col in go.GetComponentsInChildren<Collider>()) col.enabled = false;

        var fx = go.AddComponent<DKPhantomClone>();
        fx.Initialize();
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
