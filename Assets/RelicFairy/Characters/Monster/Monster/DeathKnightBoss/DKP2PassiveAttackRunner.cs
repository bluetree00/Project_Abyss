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
    private readonly MonsterContext         _ctx;
    private readonly DKSoulSpearPatternSO   _spearSO;
    private readonly DKPhantomRushPatternSO _phantomSO;

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
        float        spawnWorldX  = _ctx.Runtime.PlayerTarget.position.x - DKBossRoomContext.CellSize;
        DKSwordColor sc           = GetSwordColor();

        var tileGos   = new List<GameObject>();
        var tilePrefab = sc == DKSwordColor.White ? _spearSO.whiteTilePrefab : _spearSO.blackTilePrefab;
        if (tilePrefab != null)
        {
            for (int x = 1; x <= DKBossRoomContext.Width - 2; x++)
            {
                if (!DKBossRoomContext.IsInterior(x, rowZ)) continue;
                var go = BossEffectPool.Spawn(tilePrefab,
                    DKBossRoomContext.CellToWorld(x, rowZ, 0.05f),
                    Quaternion.Euler(-90f, 0f, 0f));
                if (go == null) continue;
                go.transform.localScale = Vector3.one * DKBossRoomContext.CellSize;
                tileGos.Add(go);
            }
        }

        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(_spearSO.passiveWarningDuration),
                cancellationToken: ct);
        }
        finally
        {
            foreach (var go in tileGos) BossEffectPool.Release(go);
            tileGos.Clear();
        }

        DKSoulSpearState.SpawnRowBeam(_spearSO.impactVfxPrefab, rowZ, sc, spawnWorldX);
        Managers.Sound?.PlayEffectAt(_spearSO.beamSfx,
            DKBossRoomContext.CellToWorld(DKBossRoomContext.Width / 2, rowZ, 0.1f));

        await UniTask.Delay(TimeSpan.FromSeconds(_spearSO.passiveBeamHitDelay), cancellationToken: ct);

        if (!IsPlayerInGuardianShield())
            DKGridPatternHelper.TriggerSingleRowDamage(
                _ctx, rowZ,
                _spearSO.damageMultiplier * 0.9f,
                _spearSO.knockbackMultiplier);
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

        // 경고 타일
        GameObject tileGo      = null;
        var        tilePrefab  = sc == DKSwordColor.White ? _phantomSO.whiteTilePrefab : _phantomSO.blackTilePrefab;
        if (tilePrefab != null)
        {
            tileGo = BossEffectPool.Spawn(tilePrefab,
                DKBossRoomContext.CellToWorld(pc.x, pc.y, 0.05f),
                Quaternion.Euler(-90f, 0f, 0f));
            if (tileGo != null)
                tileGo.transform.localScale = Vector3.one * DKBossRoomContext.CellSize;
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
            await UniTask.Delay(TimeSpan.FromSeconds(_phantomSO.passiveWarningDuration),
                cancellationToken: ct);

            if (tileGo != null) { BossEffectPool.Release(tileGo); tileGo = null; }

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

            // 피격 판정 — GuardianShield 안에 있으면 면제
            if (_ctx.Runtime?.PlayerTarget != null && !IsPlayerInGuardianShield())
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
                        player.TakeDamage(dmg);
                        Vector3 kb = (_ctx.Runtime.PlayerTarget.position - cellCenter).normalized;
                        kb.y = 0.2f;
                        player.ApplyKnockback(
                            kb * _ctx.Config.stat.knockbackForce * _phantomSO.knockbackMultiplier);
                    }
                }
            }

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
            if (tileGo    != null) BossEffectPool.Release(tileGo);
            if (phantomGo != null) UnityEngine.Object.Destroy(phantomGo);
        }
    }

    private DKSwordColor GetSwordColor()
        => (_ctx.Monster as DeathKnightBossMonster)?.DKBlackboard.SwordColor ?? DKSwordColor.White;

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
}
}
