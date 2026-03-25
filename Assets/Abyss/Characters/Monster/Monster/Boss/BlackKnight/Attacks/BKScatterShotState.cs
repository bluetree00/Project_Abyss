using Abyss.Monster;
using UnityEngine;

/// <summary>
/// BT 리프 노드 — ScatterShot (부채꼴 투사체).
/// FullLockState: 실행 중 피격 차단.
///
/// CanExecute: ScatterCooldown ≤ 0
/// 흐름:
///   ① 경고 — 1·2차 모든 투사체 궤적을 선(Line)으로 표시 + 바람업 (scatterShotDelay 초)
///   ② 1차 발사 — 부채꼴 scatterCount 개 투사체
///   ③ 딜레이 (scatterShotDelay 초)
///   ④ 2차 발사 — scatterAngleOffset 만큼 회전한 부채꼴
///   ⑤ ChaseState 복귀
/// </summary>
public class BKScatterShotState : FullLockState<BlackKnightPatternData>
{
    private readonly BossAttackBlackboard _bb;
    private          BKProjectilePool     _pool1; // 1차 발사 전용
    private          BKProjectilePool     _pool2; // 2차 발사 전용

    public void SetPools(BKProjectilePool pool1, BKProjectilePool pool2)
    {
        _pool1 = pool1;
        _pool2 = pool2;
    }

    private enum Phase { WindUp, Delay, Done }

    private Phase  _phase;
    private float  _timer;
    private int    _shotsFired; // 0=미발사, 1=1차 완료, 2=2차 완료

    private BossWarningIndicator _indicator;
    private Vector3              _aimDir;
    private Vector3              _spawnPos;

    public BKScatterShotState(BlackKnightPatternData data, BossAttackBlackboard bb) : base(data)
    {
        _bb = bb;
    }

    // ── BT 조건 ──────────────────────────────────────────
    public bool CanExecute(MonsterContext ctx) => _bb.ScatterCooldown <= 0f
                                               && ctx.Runtime.PlayerTarget != null;

    // ── FSM Enter/Update/Exit ────────────────────────────
    public override void Enter(MonsterContext ctx)
    {
        _indicator  = ctx.Transform.GetComponent<BossWarningIndicator>();
        _shotsFired = 0;
        _phase      = Phase.WindUp;
        _timer      = Data.scatterShotDelay;

        ctx.Agent.ResetPath();
        FacePlayer(ctx);

        // 조준 방향 저장
        if (ctx.Runtime.PlayerTarget != null)
        {
            _aimDir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
            _aimDir.y = 0f;
            _aimDir = _aimDir.sqrMagnitude > 0.001f ? _aimDir.normalized : ctx.Transform.forward;
        }
        else
        {
            _aimDir = ctx.Transform.forward;
        }

        _spawnPos = ctx.Transform.position + Vector3.up * 1.2f;

        // 경고: 1차 투사체 궤적만 표시 (startOffset=1.5f: 투사체 스폰 오프셋과 끝점 일치)
        _indicator?.ShowScatterLines(_spawnPos, ComputeDirections(0f), Data.scatterRange, Data.scatterShotDelay, 1.5f);

        ctx.Animator?.CrossFade(Data.scatterAnimState, 0.1f);
    }

    public override void Update(MonsterContext ctx)
    {
        _timer -= Time.deltaTime;

        switch (_phase)
        {
            case Phase.WindUp:
                if (_timer <= 0f)
                {
                    FireShot(ctx, 0f, _pool1);
                    _shotsFired = 1;
                    // 2차 경고 라인으로 교체
                    _indicator?.ShowScatterLines(_spawnPos, ComputeDirections(Data.scatterAngleOffset),
                                                 Data.scatterRange, Data.scatterShotDelay, 1.5f);
                    _phase = Phase.Delay;
                    _timer = Data.scatterShotDelay;
                }
                break;

            case Phase.Delay:
                if (_timer <= 0f && _shotsFired == 1)
                {
                    FireShot(ctx, Data.scatterAngleOffset, _pool2);
                    _shotsFired = 2;
                    _indicator?.HideScatterLines();
                    _phase = Phase.Done;
                }
                break;

            case Phase.Done:
                ctx.Monster.ChangeState<ChaseState>();
                break;
        }
    }

    public override void Exit(MonsterContext ctx)
    {
        _indicator?.HideScatterLines();
        _bb.ScatterCooldown = Data.scatterCooldown;
    }

    // ── 발사 ─────────────────────────────────────────────
    private void FireShot(MonsterContext ctx, float rotateOffsetDeg, BKProjectilePool pool)
    {
        if (Data.scatterSfx != null)
            _bb.AudioPool?.Play(ctx.Transform.position, Data.scatterSfx, 0.5f);

        int   count = Mathf.Max(1, Data.scatterCount);
        int   dmg   = Mathf.RoundToInt(ctx.Config.stat.attackPower
                                       * Data.scatterDamageMul
                                       * ctx.Runtime.AttackMultiplier);

        Quaternion offsetRot = Quaternion.AngleAxis(rotateOffsetDeg, Vector3.up);
        Vector3    baseDir   = offsetRot * _aimDir;
        Vector3    right     = Vector3.Cross(Vector3.up, baseDir).normalized;
        Vector3    spawnPos  = ctx.Transform.position + Vector3.up * 1.2f;

        for (int i = 0; i < count; i++)
        {
            float t      = count == 1 ? 0.5f : (float)i / (count - 1);
            float angle  = (-Data.scatterFanAngle * 0.5f + Data.scatterFanAngle * t) * Mathf.Deg2Rad;
            Vector3 dir  = (Mathf.Cos(angle) * baseDir + Mathf.Sin(angle) * right).normalized;
            SpawnProjectile(ctx, spawnPos, dir, dmg, pool);
        }
    }

    private void SpawnProjectile(MonsterContext ctx, Vector3 pos, Vector3 dir, int dmg, BKProjectilePool pool)
    {
        Vector3 spawnPos = pos + dir * 1.5f;
        Quaternion rot   = Quaternion.LookRotation(dir);

        BKBossProjectile proj;
        if (pool != null)
        {
            proj = pool.Get(spawnPos, rot);
        }
        else if (Data.scatterProjectilePrefab != null)
        {
            var go = Object.Instantiate(Data.scatterProjectilePrefab, spawnPos, rot);
            var hovlMover = go.GetComponent<HS_ProjectileMover>();
            if (hovlMover != null)
            {
                hovlMover.enabled = false;
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null) rb.isKinematic = true;
            }
            foreach (var src in go.GetComponentsInChildren<AudioSource>(true))
                src.volume *= 0.5f;
            proj = go.GetComponent<BKBossProjectile>() ?? go.AddComponent<BKBossProjectile>();
        }
        else
        {
            var go = CreateFallbackSphere(spawnPos, dir);
            proj = go.GetComponent<BKBossProjectile>() ?? go.AddComponent<BKBossProjectile>();
        }

        proj.Init(Data.scatterSpeed, Data.scatterRange, dmg, Data.scatterHitRadius);
    }

    private static GameObject CreateFallbackSphere(Vector3 pos, Vector3 dir)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.transform.position   = pos;
        go.transform.rotation   = Quaternion.LookRotation(dir);
        go.transform.localScale = Vector3.one * 0.3f;
        Object.Destroy(go.GetComponent<Collider>());
        return go;
    }

    // ── 한 번의 샷에 대한 방향 배열 계산 ────────────────
    private Vector3[] ComputeDirections(float rotateOffsetDeg)
    {
        int count   = Mathf.Max(1, Data.scatterCount);
        var result  = new Vector3[count];
        Vector3 baseDir = Quaternion.AngleAxis(rotateOffsetDeg, Vector3.up) * _aimDir;
        Vector3 right   = Vector3.Cross(Vector3.up, baseDir).normalized;

        for (int i = 0; i < count; i++)
        {
            float t     = count == 1 ? 0.5f : (float)i / (count - 1);
            float angle = (-Data.scatterFanAngle * 0.5f + Data.scatterFanAngle * t) * Mathf.Deg2Rad;
            result[i]   = (Mathf.Cos(angle) * baseDir + Mathf.Sin(angle) * right).normalized;
        }
        return result;
    }

    private static void FacePlayer(MonsterContext ctx)
    {
        if (ctx.Runtime.PlayerTarget == null) return;
        Vector3 dir = ctx.Runtime.PlayerTarget.position - ctx.Transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            ctx.Transform.rotation = Quaternion.LookRotation(dir);
    }
}
