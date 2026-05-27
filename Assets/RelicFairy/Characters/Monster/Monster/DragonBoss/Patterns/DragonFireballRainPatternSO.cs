using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 드래곤이 카메라 위로 사라진 뒤 맵 전체에 무작위 3×3 경고 타일이 등장하고
/// 하늘에서 화염구가 낙하하는 패턴. 마지막 화염구 착지 시 종료.
/// </summary>
[CreateAssetMenu(fileName = "DragonFireballRainPattern",
    menuName = "RelicFairy/Boss/Dragon/FireballRainPattern")]
public class DragonFireballRainPatternSO : BossPatternSO
{
    [Header("비행")]
    [SerializeField] private string _takeoffStateName  = "Takeoff";
    [SerializeField] private string _hoverStateName    = "URFlyStand";
    [SerializeField] private float  _hideHeight        = 40f;
    [SerializeField] private float  _riseSpeed         = 20f;

    [Header("화염구 비")]
    [SerializeField] private int    _fireballCount     = 6;
    [SerializeField] private float  _spawnInterval     = 0.7f;
    [SerializeField] private float  _warningDuration   = 1.5f;
    [SerializeField] private float  _fallHeight        = 35f;
    [SerializeField] private float  _fallSpeed         = 18f;
    [SerializeField] private int    _attackDamage      = 30;
    [SerializeField] private float  _damageRadius      = 1.5f;
    [SerializeField] private Color  _warningColor      = new Color(1f, 0.25f, 0f, 0.5f);
    [SerializeField] private GameObject _fireballPrefab;
    [SerializeField] private GameObject _fireballHitPrefab;

    [Header("쿨다운")]
    [SerializeField] private float  _cooldown          = 20f;

    public string TakeoffStateName => _takeoffStateName;
    public string HoverStateName   => _hoverStateName;
    public float  HideHeight       => _hideHeight;
    public float  RiseSpeed        => _riseSpeed;
    public int    FireballCount    => _fireballCount;
    public float  SpawnInterval    => _spawnInterval;
    public float  WarningDuration  => _warningDuration;
    public float  FallHeight       => _fallHeight;
    public float  FallSpeed        => _fallSpeed;
    public int    AttackDamage     => _attackDamage;
    public float  DamageRadius     => _damageRadius;
    public Color  WarningColor     => _warningColor;
    public GameObject FireballPrefab    => _fireballPrefab;
    public GameObject FireballHitPrefab => _fireballHitPrefab;
    public float  Cooldown         => _cooldown;

    private DragonFireballRainState _runtimeState;

    public override void Initialize(BossPatternContext ctx)
        => _runtimeState = new DragonFireballRainState(this);

    public override void OnRecycled() => _runtimeState?.Reset();

    public override bool CanExecute(BossPatternContext ctx)
    {
        if (ctx.Ctx.Runtime.PlayerTarget == null) return false;
        return ctx.Blackboard is DragonBossBlackboard bb
               && bb.BodyState == BodyState.Airborne
               && bb.LeapCooldown <= 0f;
    }

    public override SpecialStateBase GetRuntimeState() => _runtimeState;
}

// ─────────────────────────────────────────────────────────────────────────────
// Runtime state
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class DragonFireballRainState : FullLockState<DragonFireballRainPatternSO>
{
    private enum Phase { Rise, Rain, Done }

    private Phase _phase;
    private float _timer;
    private float _spawnTimer;
    private int   _spawnedCount;
    private int   _landedCount;
    private bool  _allSpawned;

    // 경고 타일: 각 화염구마다 3×3 경고 영역 + 낙하 오브젝트
    private struct FireballEntry
    {
        public Vector2Int CenterCell;
        public List<GameObject> WarnTiles;
        public List<Material>   WarnMats;
        public GameObject Projectile;
        public Vector3 LandPos;
        public float WarnTimer;
        public bool  Falling;
        public bool  Landed;
    }

    private readonly List<FireballEntry> _entries = new();

    internal DragonFireballRainState(DragonFireballRainPatternSO data) : base(data) { }

    internal void Reset()
    {
        CleanupAll();
        _phase        = Phase.Done;
        _timer        = 0f;
        _spawnTimer   = 0f;
        _spawnedCount = 0;
        _landedCount  = 0;
        _allSpawned   = false;
    }

    // ── FSM ──────────────────────────────────────────────────────────────────

    public override void Enter(MonsterContext ctx)
    {
        _phase        = Phase.Rise;
        _timer        = 0f;
        _spawnTimer   = 0f;
        _spawnedCount = 0;
        _landedCount  = 0;
        _allSpawned   = false;
        _entries.Clear();

        if (ctx.Agent != null) ctx.Agent.enabled = false;

        var bb = (ctx.Monster as IBoss)?.Blackboard;
        if (bb != null) bb.LeapCooldown = Data.Cooldown;

        PlayAnim(ctx, Data.HoverStateName);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;
        switch (_phase)
        {
            case Phase.Rise: UpdateRise(ctx); break;
            case Phase.Rain: UpdateRain(ctx); break;
        }
    }

    public override void Exit(MonsterContext ctx) => CleanupAll();

    // ── Rise ─────────────────────────────────────────────────────────────────

    private void UpdateRise(MonsterContext ctx)
    {
        float targetY = ctx.Runtime.SpawnPosition.y + Data.HideHeight;
        Vector3 pos = ctx.Transform.position;
        pos.y = Mathf.MoveTowards(pos.y, targetY, Data.RiseSpeed * Time.deltaTime);
        ctx.Transform.position = pos;

        if (pos.y >= targetY - 0.5f)
        {
            _phase = Phase.Rain;
            _timer = 0f;
            _spawnTimer = 0f;
        }
    }

    // ── Rain ─────────────────────────────────────────────────────────────────

    private void UpdateRain(MonsterContext ctx)
    {
        // 새 화염구 스폰
        if (!_allSpawned)
        {
            _spawnTimer += Time.deltaTime;
            if (_spawnTimer >= Data.SpawnInterval)
            {
                _spawnTimer = 0f;
                SpawnFireball(ctx);
                _spawnedCount++;
                if (_spawnedCount >= Data.FireballCount)
                    _allSpawned = true;
            }
        }

        // 진행 중인 화염구 업데이트
        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            if (e.Landed) continue;

            if (!e.Falling)
            {
                e.WarnTimer += Time.deltaTime;
                float t = Mathf.Clamp01(e.WarnTimer / Data.WarningDuration);
                foreach (var mat in e.WarnMats)
                {
                    if (mat == null) continue;
                    Color c = mat.color;
                    c.a = Mathf.Lerp(0f, Data.WarningColor.a, t);
                    mat.color = c;
                }

                if (e.WarnTimer >= Data.WarningDuration)
                {
                    e.Falling = true;
                    DestroyWarnTiles(ref e);
                    if (Data.FireballPrefab != null && e.Projectile == null)
                    {
                        Vector3 spawnPos = e.LandPos + Vector3.up * Data.FallHeight;
                        e.Projectile = Object.Instantiate(Data.FireballPrefab, spawnPos, Quaternion.identity);
                    }
                }
                _entries[i] = e;
            }
            else
            {
                // 낙하
                if (e.Projectile != null)
                {
                    Vector3 p = e.Projectile.transform.position;
                    p = Vector3.MoveTowards(p, e.LandPos, Data.FallSpeed * Time.deltaTime);
                    e.Projectile.transform.position = p;

                    if (Vector3.Distance(p, e.LandPos) < 0.2f)
                    {
                        LandFireball(ctx, ref e);
                        _entries[i] = e;
                    }
                    else
                    {
                        _entries[i] = e;
                    }
                }
                else
                {
                    e.Landed = true;
                    _landedCount++;
                    _entries[i] = e;
                }
            }
        }

        // 모두 착지했으면 종료
        if (_allSpawned && _landedCount >= Data.FireballCount)
        {
            _phase = Phase.Done;
            RestoreAgent(ctx);
            ctx.Monster.ChangeState<AttackReadyState>();
        }
    }

    // ── 화염구 스폰 ──────────────────────────────────────────────────────────

    private void SpawnFireball(MonsterContext ctx)
    {
        // 랜덤 중심 셀 (경계 2칸 안쪽: 3×3 영역이 맵 안에 들어오게)
        int minX = 2, maxX = DKBossRoomContext.Width  - 3;
        int minZ = 2, maxZ = DKBossRoomContext.Height - 3;
        int cx = Random.Range(minX, maxX + 1);
        int cz = Random.Range(minZ, maxZ + 1);

        Vector3 landPos = DKBossRoomContext.CellToWorld(cx, cz, 0.05f);

        var entry = new FireballEntry
        {
            CenterCell = new Vector2Int(cx, cz),
            WarnTiles  = new List<GameObject>(),
            WarnMats   = new List<Material>(),
            LandPos    = landPos,
            WarnTimer  = 0f,
            Falling    = false,
            Landed     = false,
        };

        // 3×3 경고 타일 생성
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                int tx = cx + dx, tz = cz + dz;
                if (!DKBossRoomContext.IsInterior(tx, tz)) continue;

                var go = CreateWarnTile(DKBossRoomContext.CellToWorld(tx, tz, 0.1f));
                if (go == null) continue;

                entry.WarnTiles.Add(go);
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null) entry.WarnMats.Add(mr.material);
            }
        }

        _entries.Add(entry);
    }

    private static GameObject CreateWarnTile(Vector3 worldPos)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "FireballWarn";
        Object.Destroy(go.GetComponent<MeshCollider>());
        go.transform.position = worldPos;
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        go.transform.localScale = Vector3.one;

        var mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var mat = new Material(shader);
        mat.color = new Color(1f, 0.25f, 0f, 0f);
        mr.material = mat;
        return go;
    }

    // ── 착지 처리 ────────────────────────────────────────────────────────────

    private void LandFireball(MonsterContext ctx, ref FireballEntry e)
    {
        if (e.Projectile != null)
        {
            Object.Destroy(e.Projectile);
            e.Projectile = null;
        }

        if (Data.FireballHitPrefab != null)
            BossEffectPool.SpawnOneShot(Data.FireballHitPrefab, e.LandPos, Quaternion.identity, fallbackLifetime: 2f);

        // 피격 판정
        if (ctx?.Runtime?.PlayerTarget != null)
        {
            Vector3 playerPos = ctx.Runtime.PlayerTarget.position;
            playerPos.y = e.LandPos.y;
            if (Vector3.Distance(playerPos, e.LandPos) <= Data.DamageRadius)
            {
                var player = ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
                player?.TakeDamage(Data.AttackDamage);
            }
        }

        e.Landed = true;
        _landedCount++;
    }

    // ── 정리 ─────────────────────────────────────────────────────────────────

    private static void DestroyWarnTiles(ref FireballEntry e)
    {
        foreach (var go in e.WarnTiles)
            if (go != null) Object.Destroy(go);
        foreach (var mat in e.WarnMats)
            if (mat != null) Object.Destroy(mat);
        e.WarnTiles.Clear();
        e.WarnMats.Clear();
    }

    private void CleanupAll()
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            DestroyWarnTiles(ref e);
            if (e.Projectile != null) Object.Destroy(e.Projectile);
            _entries[i] = e;
        }
        _entries.Clear();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null || string.IsNullOrEmpty(stateName)) return;
        int hash = Animator.StringToHash(stateName);
        if (ctx.Animator.HasState(0, hash))
            ctx.Animator.CrossFade(stateName, 0.12f, 0, 0f);
    }

    private static void RestoreAgent(MonsterContext ctx)
    {
        if (ctx.Agent == null || ctx.Agent.enabled) return;
        ctx.Agent.enabled = true;
        if (UnityEngine.AI.NavMesh.SamplePosition(
            ctx.Transform.position, out var hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
            ctx.Agent.Warp(hit.position);
    }
}
}
