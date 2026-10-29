using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// Phase 2 진입 시 DeathKnightBossMonster에서 시작하는 독립 공격 루프.
///
/// FSM 상태 전환 없이 SoulSpear(5~8s 쿨)와 PhantomRush(8~12s 쿨)를
/// 각자 별도 UniTask 루프로 발동해 기존 콤보 패턴과 동시에 나온다.
/// </summary>
public class DKP2PassiveAttackRunner
{
    private const float KnightHitGrace = 0.3f;   // 기사에게 이 시간 안에 맞았으면 수동 공격 판정은 버린다 — 본 패턴과 겹친 이중 피격(10-08 점검)

    private readonly MonsterContext         _ctx;
    private readonly DKSoulSpearPatternSO   _spearSO;
    private readonly DKPhantomRushPatternSO _phantomSO;
    private float _lastKnightHitAt = float.NegativeInfinity;   // 기사에게 실제로 맞은 마지막 시각(본 패턴 · 수동 공격 모두)
    private bool  _spearNeighbours;                            // 지금 영혼 창 예고가 이웃 줄까지 깔려 있는가

    public DKP2PassiveAttackRunner(MonsterContext ctx,
                                   DKSoulSpearPatternSO spearSO,
                                   DKPhantomRushPatternSO phantomSO)
    {
        _ctx       = ctx;
        _spearSO   = spearSO;
        _phantomSO = phantomSO;
    }

    public void Start(CancellationToken ct)
    {
        // 기사에게 맞은 시각을 센다 — 루프가 끊길 때(페이지 전환 · 처치 · 비활성) 함께 푼다(10-08 점검)
        var player = _ctx.Runtime?.CachedPlayer;
        if (player != null)
        {
            player.OnDamageResolved += OnPlayerDamageResolved;
            ct.Register(() => player.OnDamageResolved -= OnPlayerDamageResolved);
        }

        if (_spearSO   != null) RunSoulSpearLoop(ct).Forget();
        if (_phantomSO != null) RunPhantomRushLoop(ct).Forget();
    }

    // ── SoulSpear 루프 ────────────────────────────────────────

    private async UniTaskVoid RunSoulSpearLoop(CancellationToken ct)
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(UnityEngine.Random.Range(3f, 5f)),
                cancellationToken: ct);
            while (true)
            {
                int burst = UnityEngine.Random.Range(1, 4);
                for (int i = 0; i < burst; i++)
                {
                    try { await FireSoulSpearAsync(ct); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) { Debug.LogError($"[DKP2] SoulSpear error: {e}"); }

                    if (i < burst - 1)
                    {
                        float interval = UnityEngine.Random.Range(_spearSO.passiveBurstIntervalMin,
                                                                  _spearSO.passiveBurstIntervalMax);
                        await UniTask.Delay(TimeSpan.FromSeconds(interval), cancellationToken: ct);
                    }
                }

                float cd = UnityEngine.Random.Range(_spearSO.passiveCooldownMin,
                                                     _spearSO.passiveCooldownMax);
                await UniTask.Delay(TimeSpan.FromSeconds(cd), cancellationToken: ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async UniTask FireSoulSpearAsync(CancellationToken ct)
    {
        if (_ctx.Runtime?.PlayerTarget == null) return;

        var          playerCell   = DKBossRoomContext.WorldToCell(_ctx.Runtime.PlayerTarget.position);
        int          rowZ         = playerCell.y;
        DKSwordColor sc           = GetSwordColor();
        Vector3      grid         = DKBossRoomContext.WorldCenter;   // 예고를 그린 격자 — Strike가 중심을 옮기면 줄 번호가 다른 줄을 가리킨다

        // 줄 예고(이웃 줄 반대 색 + 경계 테두리)는 판정 순간까지 남긴다 — 예전엔 빔이 뜰 때 지워 회피 창 0.4초를 기억에 맡겼다(10-03)
        var tiles = new List<DKTileInfo>();
        var edges = new List<GameObject>();
        _spearNeighbours = !_ctx.Monster.IsInSpecialState;   // 본 패턴 중엔 노리는 줄만 — 반대 색 이웃 줄이 본 패턴의 위험 칸을 덮어 안전해 보였다(10-08 점검)
        DKSoulSpearState.SpawnRowGuide(_spearSO, rowZ, sc, tiles, edges, _spearNeighbours);
        float startedAt = Time.time;
        float hitAt     = _spearSO.passiveWarningDuration + _spearSO.passiveBeamHitDelay;

        try
        {
            await WaitKeepingRowGuideAsync(_spearSO.passiveWarningDuration, rowZ, sc, tiles, edges, startedAt, hitAt, ct);

            DKSoulSpearState.SpawnRowBeam(_spearSO.impactVfxPrefab, rowZ, sc);
            Managers.Sound?.PlayEffectAt(_spearSO.beamSfx,
                DKBossRoomContext.CellToWorld(DKBossRoomContext.Width / 2, rowZ, 0.1f));

            await WaitKeepingRowGuideAsync(_spearSO.passiveBeamHitDelay, rowZ, sc, tiles, edges, startedAt, hitAt, ct);
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);   // 같은 프레임 본 패턴 피격을 먼저 센다(10-08 점검)

            if (!IsPlayerInGuardianShield() && DKBossRoomContext.WorldCenter == grid   // 격자가 바뀌었으면 그린 줄과 판정 줄이 다르다(10-08 점검) — 버린다
                && !HitByKnightRecently())                                             // 기사에게 막 맞았으면 버린다(10-08 점검)
                DKGridPatternHelper.TriggerSingleRowDamage(
                    _ctx, rowZ,
                    _spearSO.damageMultiplier * 0.9f,
                    _spearSO.knockbackMultiplier,
                    HitWeight.Light);   // 수동 공격 — 약(환영과 같게: 포이즈를 쌓지 않아 콤보와 겹쳐도 날아가는 연쇄가 없다, 10-08 점검)
        }
        finally
        {
            DKGridPatternHelper.DestroyEdges(edges);
            DKGridPatternHelper.DestroyTiles(tiles);
        }
    }

    // ── PhantomRush 루프 ──────────────────────────────────────

    private async UniTaskVoid RunPhantomRushLoop(CancellationToken ct)
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(UnityEngine.Random.Range(6f, 10f)),
                cancellationToken: ct);
            while (true)
            {
                int burst = UnityEngine.Random.Range(1, 4);
                for (int i = 0; i < burst; i++)
                {
                    try { await FirePhantomRushAsync(ct); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) { Debug.LogError($"[DKP2] PhantomRush error: {e}"); }

                    if (i < burst - 1)
                    {
                        float interval = UnityEngine.Random.Range(_phantomSO.passiveBurstIntervalMin,
                                                                  _phantomSO.passiveBurstIntervalMax);
                        await UniTask.Delay(TimeSpan.FromSeconds(interval), cancellationToken: ct);
                    }
                }

                float cd = UnityEngine.Random.Range(_phantomSO.passiveCooldownMin,
                                                     _phantomSO.passiveCooldownMax);
                await UniTask.Delay(TimeSpan.FromSeconds(cd), cancellationToken: ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async UniTask FirePhantomRushAsync(CancellationToken ct)
    {
        if (_ctx.Runtime?.PlayerTarget == null) return;

        Vector2Int pc = DKBossRoomContext.WorldToCell(_ctx.Runtime.PlayerTarget.position);
        if (!DKBossRoomContext.IsInterior(pc.x, pc.y)) return;

        DKSwordColor sc         = GetSwordColor();
        Vector3      cellCenter = DKBossRoomContext.CellToWorld(pc.x, pc.y, 0.1f);
        Vector3      grid       = DKBossRoomContext.WorldCenter;   // 예고를 그린 격자(영혼 창과 같은 이유)

        // 경고 타일
        GameObject tileGo      = null;
        var        tilePrefab  = sc == DKSwordColor.White ? _phantomSO.whiteTilePrefab : _phantomSO.blackTilePrefab;
        if (tilePrefab != null)
        {
            tileGo = BossEffectPool.Spawn(tilePrefab,
                DKBossRoomContext.CellToWorld(pc.x, pc.y, 0.2f),   // 본 패턴 칸(0.05) 위 — 같은 높이면 겹쳐 안 보였다(10-13 검증 · 영혼 창 줄과 같은 높이)
                Quaternion.Euler(-90f, 0f, 0f));
            if (tileGo != null)
            {
                tileGo.transform.localScale = Vector3.one * DKBossRoomContext.CellSize;
                DKGridPatternHelper.ChargeTile(tileGo);   // (10-23 가이드 색 통일) 맞을 칸 = 처음부터 공격 색 → 판정 직전 빨강
            }
        }

        // 분신 스폰 (Animator child가 별도 오브젝트일 때만)
        GameObject    phantomGo    = null;
        Animator      phantomAnim  = null;
        DKPhantomClone phantomClone = null;
        bool canSpawn = _ctx.Animator != null
            && _ctx.Animator.gameObject != _ctx.Monster.gameObject;
        if (canSpawn)
        {
            float      angle     = UnityEngine.Random.Range(0f, 360f);
            Vector3    randomDir = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
            Vector3    spawnPos  = cellCenter + randomDir * (DKBossRoomContext.CellSize * 0.5f);
            spawnPos.y           = _ctx.Transform.position.y - 1f;
            Quaternion spawnRot  = Quaternion.LookRotation(-randomDir);

            phantomGo    = UnityEngine.Object.Instantiate(_ctx.Animator.gameObject, spawnPos, spawnRot);
            phantomAnim  = phantomGo.GetComponent<Animator>();
            phantomClone = phantomGo.AddComponent<DKPhantomClone>();
            phantomClone.Initialize();
            phantomClone.StartFadeIn(_phantomSO.phantomFadeInDuration);
            if (phantomAnim != null)
            {
                phantomAnim.speed = _phantomSO.phantomAnimSpeed;
                phantomAnim.CrossFade("Attack1", 0.05f, 0, 0f);
            }
        }

        try
        {
            // 판정(경고 + 0.15초) 0.25초 전 붉게 — 붉은 신호가 없었다(10-13 검증)
            float armIn = Mathf.Max(0f, _phantomSO.passiveWarningDuration - (DKGridPatternHelper.ArmLead - 0.15f));
            await UniTask.Delay(TimeSpan.FromSeconds(armIn), cancellationToken: ct);
            if (tileGo != null) DKGridPatternHelper.ArmTile(tileGo);
            await UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0f, _phantomSO.passiveWarningDuration - armIn)),
                cancellationToken: ct);

            // 경고 칸은 판정까지 남긴다 — 이펙트 · 0.15초 대기 동안 칸이 없어 판정이 예고 없이 들어왔다(10-08 점검)

            // VFX — 분신이 바라보는 방향으로 회전
            if (_phantomSO.phantomVfxPrefab != null)
            {
                Quaternion vfxRot = phantomGo != null ? phantomGo.transform.rotation : Quaternion.identity;
                var vfxGo = BossEffectPool.SpawnOneShot(_phantomSO.phantomVfxPrefab,
                    cellCenter, vfxRot, fallbackLifetime: 2.5f);
                if (vfxGo != null)
                    vfxGo.transform.localScale = Vector3.one * _phantomSO.phantomVfxScale;
            }
            Managers.Sound?.PlayEffectAt(_phantomSO.slashSfx, cellCenter);

            await UniTask.Delay(TimeSpan.FromSeconds(0.15f), cancellationToken: ct);
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, ct);   // 같은 프레임 본 패턴 피격을 먼저 센다(10-08 점검)

            // 피격 판정 — GuardianShield 안에 있으면 면제 · 기사에게 막 맞았으면 버린다(본 패턴과 겹친 이중 피격, 10-08 점검)
            if (_ctx.Runtime?.PlayerTarget != null && !IsPlayerInGuardianShield() && DKBossRoomContext.WorldCenter == grid
                && !HitByKnightRecently())
            {
                Vector2Int playerCell = DKBossRoomContext.WorldToCell(_ctx.Runtime.PlayerTarget.position);
                Vector2Int tileCell   = DKBossRoomContext.WorldToCell(cellCenter);
                if (playerCell == tileCell)
                {
                    var player = _ctx.Runtime.PlayerTarget.GetComponent<PlayerController>();
                    if (player != null)
                    {
                        int dmg = Mathf.Max(1,
                            (int)(_ctx.Config.stat.attackPower * _phantomSO.damageMultiplier));
                        // 수동 공격 — 약. 예고 0.45초짜리 상시 견제라 날리지 않는다(약은 포이즈를 안 쌓음 — 10-01, 콤보와 겹쳐 연쇄 피격이 되지 않게)
                        player.TakeDamage(dmg, _ctx.Monster.gameObject, false, HitWeight.Light);
                        Vector3 kb = (_ctx.Runtime.PlayerTarget.position - cellCenter).normalized;
                        kb.y = 0.2f;
                        player.ApplyKnockback(
                            kb * _ctx.Config.stat.knockbackForce * _phantomSO.knockbackMultiplier);
                    }
                }
            }

            if (tileGo != null) { DKGridPatternHelper.Disarm(tileGo); BossEffectPool.Release(tileGo); tileGo = null; }   // 판정이 끝났다 — 칠한 색을 지우고 걷는다

            // 분신 페이드아웃
            if (phantomGo != null)
            {
                var go = phantomGo; phantomGo = null;
                if (phantomClone != null)
                    phantomClone.StartFadeOut(_phantomSO.phantomFadeOutDuration,
                        () => { if (go != null) UnityEngine.Object.Destroy(go); });
                else
                    UnityEngine.Object.Destroy(go);
            }
        }
        finally
        {
            // 취소·예외 발생 시 잔여 오브젝트 즉시 정리
            if (tileGo    != null) { DKGridPatternHelper.Disarm(tileGo); BossEffectPool.Release(tileGo); }
            if (phantomGo != null) UnityEngine.Object.Destroy(phantomGo);
        }
    }

    private DKSwordColor GetSwordColor()
        => (_ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.SwordColor ?? DKSwordColor.White;

    /// <summary>
    /// 줄 예고를 둔 채 기다린다 — 그사이 본 패턴이 시작되면 이웃 줄을 걷고 노리는 줄만 다시 깐다(10-08 점검).
    /// </summary>
    private async UniTask WaitKeepingRowGuideAsync(float seconds, int rowZ, DKSwordColor sc,
                                                   List<DKTileInfo> tiles, List<GameObject> edges,
                                                   float startedAt, float hitAt, CancellationToken ct)
    {
        float end = Time.time + seconds;
        while (Time.time < end)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            // 노린 줄을 공격 색 → 판정 직전 신호 빨강으로 — 예전엔 흑/백 그대로라 검은 검이면 어두운 바닥 위 검은 줄이었다(10-24)
            DKGridPatternHelper.ArmBefore(Time.time - startedAt, hitAt, tiles, sc);
            if (!_spearNeighbours || !_ctx.Monster.IsInSpecialState) continue;
            _spearNeighbours = false;
            DKGridPatternHelper.DestroyEdges(edges);
            DKGridPatternHelper.DestroyTiles(tiles);
            DKSoulSpearState.SpawnRowGuide(_spearSO, rowZ, sc, tiles, edges, neighbours: false);
        }
    }

    /// <summary>기사에게 방금(<see cref="KnightHitGrace"/>초 안) 맞았는가 — 본 패턴 · 다른 수동 공격 모두(10-08 점검).</summary>
    private bool HitByKnightRecently() => Time.time - _lastKnightHitAt < KnightHitGrace;

    /// <summary>플레이어가 GuardianShield(같은 색 검 파괴 후 생성) 범위 안에 있으면 true.</summary>
    private bool IsPlayerInGuardianShield()
    {
        var dkBoss = _ctx.Monster as DeathKnightBossMonster;
        if (dkBoss == null || _ctx.Runtime?.PlayerTarget == null) return false;
        var bb = dkBoss.DKBlackboard;
        if (!bb.GuardianShieldActive) return false;
        Vector3 playerXZ = new Vector3(_ctx.Runtime.PlayerTarget.position.x, 0f, _ctx.Runtime.PlayerTarget.position.z);
        Vector3 shieldXZ = new Vector3(bb.GuardianShieldCenter.x, 0f, bb.GuardianShieldCenter.z);
        return Vector3.Distance(playerXZ, shieldXZ) <= bb.GuardianShieldRadius;
    }

    // ── Event Handlers ────────────────────────────────────────

    private void OnPlayerDamageResolved(GameObject attacker, int raw, int final, PlayerController.DamageOutcome outcome)
    {
        if (outcome == PlayerController.DamageOutcome.Applied && attacker != null && attacker == _ctx.Monster.gameObject)
            _lastKnightHitAt = Time.time;
    }
}
}
