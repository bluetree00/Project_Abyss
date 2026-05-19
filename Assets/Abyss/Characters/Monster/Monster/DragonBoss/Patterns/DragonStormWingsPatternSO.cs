using UnityEngine;
using UnityEngine.Rendering;

namespace Abyss.Monster
{
/// <summary>
/// Boss pattern: hover in place → rectangle warning fills (border first, then color) →
/// wing-strike animation + spawn WindBlast in zone → landing.
/// </summary>
[CreateAssetMenu(fileName = "DragonStormWingsPattern",
    menuName = "Abyss/Boss/Dragon/StormWingsPattern")]
public class DragonStormWingsPatternSO : BossPatternSO
{
    [Header("비행")]
    [SerializeField] private float  _hoverHeight         = 5f;
    [SerializeField] private string _takeoffStateName    = "Takeoff";
    [SerializeField] private string _hoverStateName      = "URFlyStand";
    [SerializeField] private string _attackStateName     = "UAttackWindHighStart";
    [SerializeField] private string _landingStateName    = "Landing";

    [Header("경고 장판")]
    [SerializeField] private float  _warningDuration     = 2f;
    [SerializeField] private float  _warningWidth        = 8f;
    [SerializeField] private float  _warningLength       = 14f;
    [SerializeField] private float  _borderLineWidth     = 0.15f;
    [SerializeField] private Color  _warningColor        = new Color(0.3f, 0.8f, 1f, 0.45f);

    [Header("공격")]
    [SerializeField] private float  _attackAnimDuration  = 1.8f;
    [SerializeField] private int    _attackDamage        = 25;
    [SerializeField] private GameObject _windBlastPrefab;

    [Header("상태이상")]
    [SerializeField] private PlayerStatusEffectSO _statusEffect;

    [Header("쿨다운")]
    [SerializeField] private float  _cooldown            = 18f;

    public float  HoverHeight        => _hoverHeight;
    public string TakeoffStateName   => _takeoffStateName;
    public string HoverStateName     => _hoverStateName;
    public string AttackStateName    => _attackStateName;
    public string LandingStateName   => _landingStateName;
    public float  WarningDuration    => _warningDuration;
    public float  WarningWidth       => _warningWidth;
    public float  WarningLength      => _warningLength;
    public float  BorderLineWidth    => _borderLineWidth;
    public Color  WarningColor       => _warningColor;
    public float  AttackAnimDuration => _attackAnimDuration;
    public int    AttackDamage       => _attackDamage;
    public GameObject WindBlastPrefab => _windBlastPrefab;
    public PlayerStatusEffectSO StatusEffect => _statusEffect;
    public float  Cooldown           => _cooldown;

    private DragonStormWingsState _runtimeState;

    public override void Initialize(BossPatternContext ctx)
        => _runtimeState = new DragonStormWingsState(this);

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

// ────────────────────────────────────────────────────────────────────────────
// Runtime state
// ────────────────────────────────────────────────────────────────────────────

internal sealed class DragonStormWingsState : FullLockState<DragonStormWingsPatternSO>
{
    private enum Phase { Takeoff, Hover, Warning, Attack, Landing, Done }

    private Phase   _phase;
    private float   _timer;
    private Vector3 _hoverPos;
    private int     _takeoffHash;
    private int     _landingHash;

    // 경고 장판
    private GameObject _fillGo;
    private GameObject _borderGo;
    private Material   _fillMat;
    private Material   _borderMat;
    private Vector3    _warnCenter;
    private Quaternion _warnRotation;
    private Vector3    _toPlayer;
    private float      _targetAlpha;
    private bool       _windBlastSpawned;

    internal DragonStormWingsState(DragonStormWingsPatternSO data) : base(data) { }

    internal void Reset()
    {
        _phase = Phase.Done;
        _timer = 0f;
        DestroyWarning();
    }

    // ── FSM ──────────────────────────────────────────────────────────────────

    public override void Enter(MonsterContext ctx)
    {
        _phase            = Phase.Hover;
        _timer            = 0f;
        _windBlastSpawned = false;
        _takeoffHash      = Animator.StringToHash(Data.TakeoffStateName);
        _landingHash      = Animator.StringToHash(Data.LandingStateName);
        _hoverPos         = ctx.Transform.position;
        _hoverPos.y       = Mathf.Max(ctx.Transform.position.y, ctx.Runtime.SpawnPosition.y + Data.HoverHeight);

        PlayAnim(ctx, Data.HoverStateName);

        var bb = (ctx.Monster as IBoss)?.Blackboard;
        if (bb != null) bb.LeapCooldown = Data.Cooldown;
    }

    public override void Update(MonsterContext ctx)
    {
        _timer += Time.deltaTime;

        switch (_phase)
        {
            case Phase.Takeoff:  UpdateTakeoff(ctx);  break;
            case Phase.Hover:    UpdateHover(ctx);    break;
            case Phase.Warning:  UpdateWarning(ctx);  break;
            case Phase.Attack:   UpdateAttack(ctx);   break;
            case Phase.Landing:  UpdateLanding(ctx);  break;
        }
    }

    public override void Exit(MonsterContext ctx)
        => DestroyWarning();

    // ── Takeoff ───────────────────────────────────────────────────────────────

    private void UpdateTakeoff(MonsterContext ctx)
    {
        FacePlayer(ctx);

        // Takeoff 클립이 제자리로 변경됨 → 코드에서 Y 상승 처리
        float targetY = ctx.Runtime.SpawnPosition.y + Data.HoverHeight;
        Vector3 p = ctx.Transform.position;
        p.y = Mathf.MoveTowards(p.y, targetY, ctx.Stat.moveSpeed * 2f * Time.deltaTime);
        ctx.Transform.position = p;

        if (!IsAnimNearEnd(ctx, _takeoffHash)) return;

        Vector3 pos = ctx.Transform.position;
        pos.y = targetY;
        ctx.Transform.position = pos;
        _hoverPos = pos;

        var bb = GetDragonBB(ctx);
        if (bb != null) bb.IsAirborne = true;

        _phase = Phase.Hover;
        _timer = 0f;
        PlayAnim(ctx, Data.HoverStateName);
    }

    // ── Hover ─────────────────────────────────────────────────────────────────

    private void UpdateHover(MonsterContext ctx)
    {
        ctx.Transform.position = _hoverPos;
        FacePlayer(ctx);

        // 1프레임 호버 후 경고 장판 생성
        _phase = Phase.Warning;
        _timer = 0f;
        CreateWarning(ctx);
    }

    // ── Warning ───────────────────────────────────────────────────────────────

    private void CreateWarning(MonsterContext ctx)
    {
        Color c = Data.WarningColor;
        _targetAlpha = c.a;

        // 바닥 기준 위치 — 지형 z-fighting 방지용 0.3f 오프셋
        float groundY = ctx.Runtime.SpawnPosition.y + 0.3f;
        Vector3 bossFloor = new Vector3(
            ctx.Transform.position.x, groundY, ctx.Transform.position.z);

        // 플레이어 방향 (수평 고정)
        if (ctx.Runtime.PlayerTarget != null)
        {
            _toPlayer = ctx.Runtime.PlayerTarget.position - bossFloor;
            _toPlayer.y = 0f;
        }
        else
        {
            _toPlayer = ctx.Transform.forward;
            _toPlayer.y = 0f;
        }
        if (_toPlayer.sqrMagnitude < 0.01f) _toPlayer = ctx.Transform.forward;
        _toPlayer.Normalize();

        _warnCenter   = bossFloor + _toPlayer * (Data.WarningLength * 0.5f);
        _warnRotation = Quaternion.LookRotation(_toPlayer, Vector3.up);

        // ── 채움 Plane (PrimitiveType.Plane = 기본 +Y 방향, 바닥에 눕힘, alpha 0→target) ──
        _fillGo = GameObject.CreatePrimitive(PrimitiveType.Plane);
        _fillGo.name = "StormWingsFill";
        Object.Destroy(_fillGo.GetComponent<MeshCollider>());
        var mr = _fillGo.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows    = false;

        _fillMat = CreateTransparentMat(new Color(c.r, c.g, c.b, 0f));
        mr.material = _fillMat;

        // Plane 기본 normal = +Y. LookRotation(_toPlayer, Vector3.up) → 방향만 회전, 면은 유지
        // Plane 기본 크기 = 10×10 → WarningWidth/10, WarningLength/10 으로 스케일
        _fillGo.transform.position   = _warnCenter;
        _fillGo.transform.rotation   = Quaternion.LookRotation(_toPlayer, Vector3.up);
        _fillGo.transform.localScale = new Vector3(Data.WarningWidth / 10f, 1f, Data.WarningLength / 10f);

        // ── 테두리 LineRenderer (즉시 완전 불투명) ────────────────────
        _borderGo = new GameObject("StormWingsBorder");
        var lr    = _borderGo.AddComponent<LineRenderer>();
        _borderMat = CreateTransparentMat(new Color(c.r, c.g, c.b, 1f));
        lr.material          = _borderMat;
        lr.useWorldSpace     = true;
        lr.loop              = true;
        lr.positionCount     = 4;
        lr.startWidth        = Data.BorderLineWidth;
        lr.endWidth          = Data.BorderLineWidth;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows    = false;

        Vector3 right  = Vector3.Cross(Vector3.up, _toPlayer).normalized * (Data.WarningWidth * 0.5f);
        Vector3 fwdVec = _toPlayer * Data.WarningLength;
        lr.SetPositions(new[]
        {
            bossFloor - right,
            bossFloor + right,
            bossFloor + right + fwdVec,
            bossFloor - right + fwdVec,
        });
    }

    private void UpdateWarning(MonsterContext ctx)
    {
        ctx.Transform.position = _hoverPos;

        // fill alpha 0 → target
        float t = Mathf.Clamp01(_timer / Data.WarningDuration);
        if (_fillMat != null)
        {
            Color col = _fillMat.color;
            col.a = Mathf.Lerp(0f, _targetAlpha, t);
            _fillMat.color = col;
        }

        if (_timer < Data.WarningDuration) return;

        _phase = Phase.Attack;
        _timer = 0f;
        PlayAnim(ctx, Data.AttackStateName);
    }

    // ── Attack ───────────────────────────────────────────────────────────────

    private void UpdateAttack(MonsterContext ctx)
    {
        ctx.Transform.position = _hoverPos;

        if (!_windBlastSpawned && _timer >= Data.AttackAnimDuration * 0.4f)
        {
            _windBlastSpawned = true;
            SpawnWindBlast(ctx);
            DestroyWarning();
        }

        if (_timer < Data.AttackAnimDuration) return;

        _phase = Phase.Done;
        _timer = 0f;
        ctx.Monster.ChangeState<AttackReadyState>();
    }

    // ── Landing ───────────────────────────────────────────────────────────────

    private void UpdateLanding(MonsterContext ctx)
    {
        float targetY = ctx.Runtime.SpawnPosition.y;
        Vector3 pos   = ctx.Transform.position;
        pos.y = Mathf.MoveTowards(pos.y, targetY, ctx.Stat.moveSpeed * 2f * Time.deltaTime);
        ctx.Transform.position = pos;

        if (!IsAnimNearEnd(ctx, _landingHash)) return;

        RestoreAgent(ctx);
        _phase = Phase.Done;
        ctx.Monster.ChangeState<ChaseState>();
    }

    // ── WindBlast 스폰 ────────────────────────────────────────────────────────

    private void SpawnWindBlast(MonsterContext ctx)
    {
        if (Data.WindBlastPrefab == null) return;

        // 날개에서 발사: 보스 호버 위치 앞에서 수평으로 발사
        Vector3 wingOrigin = _hoverPos + _toPlayer * 2f;
        Quaternion blastRot = Quaternion.LookRotation(_toPlayer, Vector3.up);

        var go = Object.Instantiate(Data.WindBlastPrefab, wingOrigin, blastRot);
        go.transform.localScale = new Vector3(Data.WarningWidth, Data.WarningWidth * 0.5f, Data.WarningLength);

        // 속성 색상 적용
        Color tint = new Color(Data.WarningColor.r, Data.WarningColor.g, Data.WarningColor.b, 1f);
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(tint);
        }
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var mat in r.materials)
            {
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
                if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     tint);
                if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", tint);
            }
        }

        // 경고 장판 전체 범위로 판정 (장판이 SpawnPosition.y 기준)
        Vector3 hitCenter = _warnCenter;
        hitCenter.y = ctx.Runtime.SpawnPosition.y + 1f;
        var hits = Physics.OverlapBox(
            hitCenter,
            new Vector3(Data.WarningWidth * 0.5f, 1.5f, Data.WarningLength * 0.5f),
            _warnRotation);
        foreach (var col in hits)
        {
            var player = col.GetComponent<PlayerController>()
                      ?? col.GetComponentInParent<PlayerController>();
            if (player == null) continue;
            player.TakeDamage(Data.AttackDamage);
            Data.StatusEffect?.Apply(player);
            break;
        }

        Object.Destroy(go, 3f);
    }

    // ── 경고 장판 정리 ────────────────────────────────────────────────────────

    private void DestroyWarning()
    {
        if (_fillGo   != null) { Object.Destroy(_fillGo);    _fillGo   = null; }
        if (_borderGo != null) { Object.Destroy(_borderGo);  _borderGo = null; }
        if (_fillMat  != null) { Object.Destroy(_fillMat);   _fillMat  = null; }
        if (_borderMat!= null) { Object.Destroy(_borderMat); _borderMat= null; }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Material CreateTransparentMat(Color color)
    {
        var shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var mat = new Material(shader) { color = color };
        return mat;
    }

    private static void PlayAnim(MonsterContext ctx, string stateName)
    {
        if (ctx.Animator == null) return;
        int hash = Animator.StringToHash(stateName);
        if (ctx.Animator.HasState(0, hash))
            ctx.Animator.CrossFade(stateName, 0.12f, 0, 0f);
    }

    private static bool IsAnimNearEnd(MonsterContext ctx, int hash)
    {
        if (ctx.Animator == null) return true;
        if (!ctx.Animator.HasState(0, hash)) return true;
        if (ctx.Animator.IsInTransition(0)) return false;
        var info = ctx.Animator.GetCurrentAnimatorStateInfo(0);
        if (info.shortNameHash != hash) return true;
        return info.normalizedTime >= 0.9f;
    }

    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.01f)
            ctx.Transform.rotation = Quaternion.Slerp(
                ctx.Transform.rotation, Quaternion.LookRotation(dir), 8f * Time.deltaTime);
    }

    private static void RestoreAgent(MonsterContext ctx)
    {
        if (ctx.Agent == null || ctx.Agent.enabled) return;
        ctx.Agent.enabled = true;
        ctx.Agent.Warp(ctx.Transform.position);
    }

    private static DragonBossBlackboard GetDragonBB(MonsterContext ctx)
        => (ctx.Monster as DragonBossMonster)?.DragonBlackboard;
}
}
