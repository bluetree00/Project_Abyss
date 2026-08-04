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
    [SerializeField] private float  _damageMultiplier  = 1.5f;
    [SerializeField] private float  _damageRadius      = 1.5f;
    [SerializeField] private Color  _warningColor      = new Color(1.0f, 0.35f, 0.1f, 0.5f);
    [SerializeField] private GameObject _fireballPrefab;
    [SerializeField] private float  _fireballScale        = 3f;
    [SerializeField] private GameObject _explosionPrefab;
    [SerializeField] private float  _explosionScale       = 1f;
    [Tooltip("스폰 후 메테오가 시각적으로 바닥에 닿는 시간 (초) — 이 시점에 데미지·스코치·폭발 적용.")]
    [SerializeField] private float  _impactDelay          = 1.2f;
    [Tooltip("착지 후 Meteor 이펙트 파괴까지 대기 시간 (초). impactDelay보다 커야 함.")]
    [SerializeField] private float  _meteorHitDuration    = 2.5f;

    [Header("사운드")]
    [Tooltip("패턴 시작(드래곤이 비를 부르는 순간) 1회 재생할 사운드")]
    [SerializeField] private AudioClip _rainSfx;
    [Tooltip("화염구가 낙하를 시작할 때마다 해당 위치에서 재생할 사운드")]
    [SerializeField] private AudioClip _fireRainSfx;

    [Header("Scorch Marks")]
    [Tooltip("착지 지점에 남길 그을림 텍스처. 없으면 절차적 생성 사용.")]
    [SerializeField] private Texture2D _scorchTexture;
    [SerializeField] private int       _scorchClusterCount = 2;
    [SerializeField] private float     _scorchDuration     = 8f;
    [SerializeField] private float     _scorchScaleMin     = 1.5f;
    [SerializeField] private float     _scorchScaleMax     = 3.0f;

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
    public float  DamageMultiplier => _damageMultiplier;
    public float  DamageRadius     => _damageRadius;
    public Color  WarningColor     => _warningColor;
    public GameObject FireballPrefab    => _fireballPrefab;
    public float  FireballScale         => _fireballScale;
    public AudioClip  RainSfx            => _rainSfx;
    public AudioClip  FireRainSfx        => _fireRainSfx;
    public GameObject ExplosionPrefab   => _explosionPrefab;
    public float  ExplosionScale        => _explosionScale;
    public float  ImpactDelay           => _impactDelay;
    public float  MeteorHitDuration     => _meteorHitDuration;
    public Texture2D ScorchTexture      => _scorchTexture;
    public int       ScorchClusterCount => _scorchClusterCount;
    public float     ScorchDuration     => _scorchDuration;
    public float     ScorchScaleMin     => _scorchScaleMin;
    public float     ScorchScaleMax     => _scorchScaleMax;
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
        public float FallTimer;
        public bool  Falling;
        public bool  ImpactApplied;
        public bool  Landed;
    }

    private struct ScorchEntry { public GameObject Go; public MeshRenderer Mr; public Material Mat; public float Timer; public float MaxTimer; }

    private static Texture2D s_ScorchTex;
    private static Material  s_ScorchMat;

    private readonly List<FireballEntry> _entries = new();
    private readonly List<ScorchEntry>   _scorches = new();

    internal DragonFireballRainState(DragonFireballRainPatternSO data) : base(data) { }

    internal void Reset()
    {
        CleanupAll();
        CleanupAllScorches();
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
        GameCameraController.Instance?.DeactivateDragonTopDownView(0.8f);
        _phase        = Phase.Rise;
        _timer        = 0f;
        _spawnTimer   = 0f;
        _spawnedCount = 0;
        _landedCount  = 0;
        _allSpawned   = false;
        _entries.Clear();

        if (ctx.Agent != null) ctx.Agent.enabled = false;

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

    public override void Exit(MonsterContext ctx)
    {
        if ((ctx.Monster as IBoss)?.Blackboard is DragonBossBlackboard exitBb)
            exitBb.LeapCooldown = Data.Cooldown;
        CleanupAll();
        CleanupAllScorches();
    }

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
            Managers.Sound?.PlayEffectAt(Data.RainSfx, ctx.Transform.position);
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
                    e.Falling  = true;
                    e.FallTimer = 0f;
                    DestroyWarnTiles(ref e);
                    if (Data.FireballPrefab != null)
                    {
                        // LandPos에 직접 스폰 — 파티클 시뮬레이션이 낙하~폭발 전체를 재생
                        e.Projectile = BossEffectPool.Spawn(Data.FireballPrefab, e.LandPos, Quaternion.identity);
                        e.Projectile.transform.localScale = Vector3.one * Data.FireballScale;
                    }
                    Managers.Sound?.PlayEffectAt(Data.FireRainSfx, e.LandPos);
                }
                _entries[i] = e;
            }
            else
            {
                e.FallTimer += Time.deltaTime;

                // ImpactDelay 경과 시 데미지·스코치·폭발 적용
                if (!e.ImpactApplied && e.FallTimer >= Data.ImpactDelay)
                {
                    e.ImpactApplied = true;
                    ApplyMeteorImpact(ctx, ref e);
                    // 착지 후 메테오 파티클 방출 중단 — MeteorHitDuration 동안 루프되어 2번 낙하처럼 보이는 현상 방지
                    if (e.Projectile != null)
                        foreach (var ps in e.Projectile.GetComponentsInChildren<ParticleSystem>(true))
                            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }

                // MeteorHitDuration 경과 시 파티클 오브젝트 파괴 후 종료
                if (e.FallTimer >= Data.MeteorHitDuration)
                {
                    if (e.Projectile != null) { BossEffectPool.Release(e.Projectile); e.Projectile = null; }
                    e.Landed = true;
                    _landedCount++;
                }
                _entries[i] = e;
            }
        }

        UpdateLivingScorches();

        // 모두 착지했으면 종료
        if (_allSpawned && _landedCount >= Data.FireballCount)
        {
            _phase = Phase.Done;

            // Summon 패턴 공중 대기 루프에서 핸드오프된 경우 — 원래 상태로 복귀
            if ((ctx.Monster as IBoss)?.Blackboard is DragonBossBlackboard bb && bb.AirLoopReturnState != null)
            {
                var returnState = bb.AirLoopReturnState;
                bb.AirLoopReturnState = null;
                ctx.Monster.ChangeState(returnState);
                return;
            }

            RestoreAgent(ctx);
            ctx.Monster.ChangeState<AttackReadyState>();
        }
    }

    // ── 화염구 스폰 ──────────────────────────────────────────────────────────

    private void SpawnFireball(MonsterContext ctx)
    {
        // 랜덤 중심 셀 (경계 2칸 안쪽: 3×3 영역이 맵 안에 들어오게)
        int minX = 2, maxX = DragonBossRoomContext.Width  - 3;
        int minZ = 2, maxZ = DragonBossRoomContext.Height - 3;
        int cx = Random.Range(minX, maxX + 1);
        int cz = Random.Range(minZ, maxZ + 1);

        Vector3 landBase = DragonBossRoomContext.CellToWorld(cx, cz, 0f);
        Vector3 landPos;
        if (Physics.Raycast(new Vector3(landBase.x, landBase.y + 50f, landBase.z), Vector3.down, out RaycastHit groundHit, 100f))
            landPos = groundHit.point + Vector3.up * 0.05f;
        else
        {
            landPos = landBase;
            landPos.y = ctx.Runtime.SpawnPosition.y + 0.05f;
        }

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
        Color fireBase = DragonBossVisualHelper.GetElementColor(DragonBossBlackboard.DragonElement.Fire);
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
        {
            int tx = cx + dx, tz = cz + dz;
            if (!DragonBossRoomContext.IsInterior(tx, tz)) continue;

            var (go, _, mat) = QuadTilePool.Rent();
            go.name = "FireballWarn";
            go.transform.position   = DragonBossRoomContext.CellToWorld(tx, tz, 0.1f);
            go.transform.rotation   = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = Vector3.one;
            mat.color = new Color(fireBase.r, fireBase.g, fireBase.b, 0f);
            entry.WarnTiles.Add(go);
            entry.WarnMats.Add(mat);
        }

        _entries.Add(entry);
    }

    // ── 충돌 처리 (스폰 즉시 호출 — 데미지·스코치·폭발 이펙트) ─────────────

    private void ApplyMeteorImpact(MonsterContext ctx, ref FireballEntry e)
    {
        if (Data.ExplosionPrefab != null)
        {
            var expGo = BossEffectPool.SpawnOneShot(Data.ExplosionPrefab, e.LandPos, Quaternion.identity, fallbackLifetime: 3f);
            if (expGo != null) expGo.transform.localScale = Vector3.one * Data.ExplosionScale;
        }

        if (ctx?.Runtime?.PlayerTarget != null)
        {
            Vector3 playerPos = ctx.Runtime.PlayerTarget.position;
            playerPos.y = e.LandPos.y;
            if (Vector3.Distance(playerPos, e.LandPos) <= Data.DamageRadius)
                ctx.Runtime.PlayerTarget.GetComponent<PlayerController>()?.TakeDamage(Mathf.RoundToInt(ctx.Config.stat.attackPower * Data.DamageMultiplier));
        }

        SpawnScorchCluster(e.LandPos, ctx);
    }

    // ── 스코치 ───────────────────────────────────────────────────────────────

    private void SpawnScorchCluster(Vector3 landPos, MonsterContext ctx)
    {
        var scorchMat = GetOrCreateScorchMaterial();
        float cell = DragonBossRoomContext.CellSize;

        for (int i = 0; i < Mathf.Max(1, Data.ScorchClusterCount); i++)
        {
            float perpX = Random.Range(-1f, 1f) * cell * 0.35f;
            float perpZ = Random.Range(-1f, 1f) * cell * 0.35f;
            Vector3 pos = landPos + new Vector3(perpX, 0f, perpZ);
            Vector3 rayOrigin = new Vector3(pos.x, ctx.Runtime.SpawnPosition.y + 50f, pos.z);
            pos.y = Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 100f)
                ? hit.point.y + 0.02f
                : ctx.Runtime.SpawnPosition.y + 0.02f;

            float scaleBase = Random.Range(Data.ScorchScaleMin, Data.ScorchScaleMax) * cell;
            float scaleX    = scaleBase * Random.Range(0.7f, 1.3f);
            float scaleZ    = scaleBase * Random.Range(0.7f, 1.3f);
            float yRot      = Random.Range(0f, 360f);

            var (go, mr, ownedMat) = QuadTilePool.Rent();
            go.name = "ScorchMark";
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(90f, yRot, 0f));
            go.transform.localScale = new Vector3(scaleX, scaleZ, 1f);
            mr.sharedMaterial = scorchMat;

            _scorches.Add(new ScorchEntry { Go = go, Mr = mr, Mat = ownedMat, Timer = 0f, MaxTimer = Data.ScorchDuration });
        }
    }

    private void UpdateLivingScorches()
    {
        for (int i = _scorches.Count - 1; i >= 0; i--)
        {
            var e = _scorches[i];
            e.Timer += Time.deltaTime;
            if (e.Timer >= e.MaxTimer)
            {
                if (e.Go != null) QuadTilePool.Return(e.Go, e.Mr, e.Mat);
                _scorches.RemoveAt(i);
                continue;
            }
            _scorches[i] = e;
        }
    }

    private void CleanupAllScorches()
    {
        foreach (var e in _scorches) if (e.Go != null) QuadTilePool.Return(e.Go, e.Mr, e.Mat);
        _scorches.Clear();
    }

    private Material GetOrCreateScorchMaterial()
    {
        var tex = Data.ScorchTexture != null ? Data.ScorchTexture : GetOrCreateScorchTexture();

        if (s_ScorchMat != null && s_ScorchMat.GetTexture("_MainTex") == tex)
            return s_ScorchMat;

        var shader = Shader.Find("Sprites/Default")
                  ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");

        s_ScorchMat = new Material(shader);
        s_ScorchMat.SetTexture("_MainTex", tex);
        s_ScorchMat.SetTexture("_BaseMap", tex);
        s_ScorchMat.SetColor("_Color",     Color.white);
        s_ScorchMat.SetColor("_BaseColor", Color.white);

        if (shader != null && shader.name.Contains("Particles"))
        {
            s_ScorchMat.SetFloat("_Surface", 1f);
            s_ScorchMat.SetFloat("_Blend",   0f);
            s_ScorchMat.SetInt("_SrcBlend",  5);
            s_ScorchMat.SetInt("_DstBlend",  10);
            s_ScorchMat.SetInt("_ZWrite",    0);
            s_ScorchMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            s_ScorchMat.renderQueue = 3000;
        }

        return s_ScorchMat;
    }

    private static Texture2D GetOrCreateScorchTexture()
    {
        if (s_ScorchTex != null) return s_ScorchTex;

        const int size = 256;
        s_ScorchTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        s_ScorchTex.wrapMode = TextureWrapMode.Clamp;
        var center = new Vector2(0.5f, 0.5f);

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (float)x / (size - 1);
            float v = (float)y / (size - 1);
            float dist = Vector2.Distance(new Vector2(u, v), center);
            float n0 = Mathf.PerlinNoise(u * 3.7f + 0.10f, v * 3.7f + 0.30f);
            float n1 = Mathf.PerlinNoise(u * 7.3f + 1.40f, v * 7.3f + 2.10f);
            float n2 = Mathf.PerlinNoise(u * 14f  + 3.20f, v * 14f  + 0.70f);
            float noise = n0 * 0.55f + n1 * 0.30f + n2 * 0.15f;
            float pertDist = dist + (noise - 0.5f) * 0.28f;
            float alpha = 1f - Mathf.Clamp01((pertDist - 0.30f) / 0.18f);
            float inner = Mathf.Clamp01(1f - pertDist * 3.2f);
            float b = Mathf.Lerp(0.09f, 0.02f, inner);
            s_ScorchTex.SetPixel(x, y, new Color(b * 1.3f, b * 0.75f, b * 0.4f, alpha));
        }
        s_ScorchTex.Apply();
        return s_ScorchTex;
    }

    // ── 정리 ─────────────────────────────────────────────────────────────────

    private static void DestroyWarnTiles(ref FireballEntry e)
    {
        for (int i = 0; i < e.WarnTiles.Count; i++)
        {
            var go = e.WarnTiles[i];
            if (go == null) continue;
            QuadTilePool.Return(go, go.GetComponent<MeshRenderer>(), e.WarnMats[i]);
        }
        e.WarnTiles.Clear();
        e.WarnMats.Clear();
    }

    private void CleanupAll()
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            DestroyWarnTiles(ref e);
            if (e.Projectile != null) BossEffectPool.Release(e.Projectile);
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
        => DragonPatternFloorUtils.SnapToFloorAndRestoreAgent(ctx);
}
}
