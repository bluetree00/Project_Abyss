using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 봉인된 리치의 끝 — <b>봉인 의식</b>(플레이어가 봉인석 넷을 깨운다) → <b>붕괴 컷신</b>(봉인 역류 · 외곽 두 링 붕괴 · 리치 해방 · 퇴장).
///
/// 결계(SealBreaker)를 뒤집어 쓴다: 리치가 결계를 치던 구조 → 플레이어가 봉인을 치는 구조.
/// 결계 해골 대신 봉인석, 처치 대신 점화. 주기 공격·방해 해골은 결계 패턴 설정값을 빌리고 피해는 절반.
/// <b>실패 조건·제한 시간 없음</b> — 마지막 순간을 좌절로 만들지 않는다.
///
/// 붕괴(세계 기록)는 봉인이 완성되는 순간 남긴다 — 의식 도중 쓰러지면 리치는 봉인되지 않은 채로 남는다.
/// 설계: 기획 「리치보스_완전설계」 §3-5 · §3-6.
/// </summary>
public sealed class LichSealRitual
{
    // ── 봉인 의식 ─────────────────────────────────────────────────
    private const int   StoneCount              = 4;
    private const int   StoneRing               = 2;       // 링 2 동·서·남·북
    private const float FallbackStoneDistance   = 12.5f;   // 붕괴형 아레나가 아닐 때
    private const float KneelHeight             = 1f;
    private const float KneelSeconds            = 0.8f;
    private const float BossBarClearance        = 70f;     // 목표 문구를 보스 체력바 아래로(1080 기준 px) — F4와 같은 값. 0이면 이름·체력바와 겹쳤다
    private const float InterferenceDamageScale = 0.5f;
    private const float DefaultAttackInterval   = 2.5f;
    private const int   InterferenceSkeletons   = 2;
    private const float SkeletonDistance        = 6f;
    private const float SealFreezeSeconds       = 1f;
    private const float DoneLineSeconds         = 2.5f;
    private const float StoneShotMove           = 0.4f;    // 봉인석 차례 비추기(10-03) — 다음 돌로 옮겨 가는 시간
    private const float StoneShotHold           = 0.25f;   //   · 머무는 시간(돌 하나 약 0.65초)
    private const float StoneShotBack           = 7f;      //   · 돌 바깥으로 물러선 거리(m) — 낮게 비춰 돌 너머로 무릎 꿇은 리치가 함께 잡힌다
    private const float StoneShotHeight         = 3f;
    private const float StoneShotLook           = 1.2f;    //   · 돌 몸통 높이를 본다
    private const float StoneRevealVfxScale     = 0.6f;    // 솟는 돌 발밑 청록 원(TileRestore 칸)
    private const float RevealSafety            = 1.5f;    // 카메라가 돌아오는 동안까지 무적

    // ── 붕괴 컷신 ─────────────────────────────────────────────────
    private const float PullSeconds        = 0.8f;
    private const float CoreSouthOffset    = 7.5f;   // 코어(링 0~1) 남쪽 줄 중심
    private const float ChainReverseSecs   = 1.5f;
    private const float RiseHeight         = 6f;
    private const float RiseSeconds        = 1.5f;
    private const float VanishHeight       = 25f;
    private const float VanishSeconds      = 0.6f;
    private const float OuterRingWarn      = 0.6f;
    private const float InnerRingWarn      = 1.0f;

    // ── 컷신 카메라 (리치 기준, 제단 남쪽 = 플레이어 쪽) ─────────────
    private static readonly Vector3 CloseShot = new Vector3(0f, 4f, -9f);
    private static readonly Vector3 WideShot  = new Vector3(0f, 22f, -26f);
    private static readonly Vector3 LowShot   = new Vector3(0f, 0.8f, -5f);

    private const string RitualKey      = "Seal_Ritual";
    private const string RitualDoneKey  = "Seal_Ritual_Done";
    private const string CollapseKey    = "Seal_Collapse";   // 0 리치 · 1 멀린 · 2 리치 · 3 그림자
    private const string CollapseNotice = "봉인 붕괴 — 숲 · 불 · 검 · 성소";

    // ── Private ───────────────────────────────────────────────────
    private readonly LichMonster              _lich;
    private readonly MonsterContext           _ctx;
    private readonly LichSealBreakerPatternSO _ward;   // 주기 공격·방해 해골 설정(없으면 기본값)
    private readonly List<LichSealStone>      _stones = new(StoneCount);

    private ArenaTileGrid   _grid;
    private Vector3         _center;
    private UI_ChallengeHud _hud;
    private int             _ignited;
    private int             _attackCycle;
    private GameObject      _reversedChains;

    // ── Public Methods ────────────────────────────────────────────
    public LichSealRitual(LichMonster lich, MonsterContext ctx, LichSealBreakerPatternSO ward)
    {
        _lich = lich;
        _ctx  = ctx;
        _ward = ward;
    }

    public async UniTask RunAsync(CancellationToken ct)
    {
        try
        {
            ResolveArena();
            await RunRitualAsync(ct);

            // 봉인 완성 = 붕괴. 세계 기록은 여기서(보스방 클리어보다 먼저 — 클리어 알림이 처치로 적지 않게).
            StoryProgress.TriggerCollapse();
            await RunCollapseAsync(ct);
        }
        finally
        {
            Cleanup();
        }
    }

    // ── 봉인 의식 ─────────────────────────────────────────────────
    private void ResolveArena()
    {
        var lichPos = _lich.transform.position;
        _grid = ArenaTileGrid.Active;
        if (_grid != null && _grid.TryGetCell(lichPos, out _) && _grid.TryGetWorldCenter(out _center))
            return;

        _grid   = null;
        _center = new Vector3(lichPos.x, _lich.SpawnGroundY, lichPos.z);
    }

    private async UniTask RunRitualAsync(CancellationToken ct)
    {
        // 리치가 공중에서 코어 위로 떨어져 무릎 꿇는다.
        _ctx.Animator?.CrossFade("Die", 0.15f);
        await MoveAsync(_lich.transform, _center + Vector3.up * KneelHeight, KneelSeconds, ct);

        UI_BossBark.ShowDialogue(RitualKey);
        _lich.SetRitualCamera(true);
        LichSfx.Play(LichSfxSlot.ZoneHum, _center);

        // 순서(10-03 사용자: 봉인 행동 안내가 없었다 — 돌 넷 · 해골 · 목표가 한꺼번에 떴다):
        // 봉인석을 하나씩 세우며 카메라가 비춘다 → 목표 띠 → 그다음에 방해 해골 · 주기 공격.
        float distance = _grid != null ? _grid.RingCenterDistance(StoneRing) : FallbackStoneDistance;
        await RevealStonesAsync(distance, ct);

        _hud = UI_ChallengeHud.Create(BossBarClearance);
        _hud.SetObjective("봉인석을 깨워라 — 세 번씩 쳐서 점화");
        RefreshHud();

        SpawnInterferenceSkeletons();
        Debug.Log($"[LichSeal] 봉인 의식 시작 — 봉인석 {StoneCount}개 (중심에서 {distance:0.#}m)", _lich);

        // 첫 주기 공격은 목표 띠가 뜬 뒤 interval초 — 비추는 동안은 세지 않는다.
        float interval = _ward != null ? _ward.periodicAttackInterval : DefaultAttackInterval;
        float timer    = 0f;
        while (_ignited < StoneCount)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            if (interval <= 0f) continue;

            timer += Time.deltaTime;
            if (timer < interval) continue;
            timer = 0f;
            SpawnInterferenceAttack();
        }

        // 봉인 완성 — 금빛 고리와 빛기둥, 사슬이 리치를 감고 잠시 멈춘다.
        LichSkeletonMonster.DespawnAll();
        PlaySealComplete();
        _hud.SetStatus("봉인 완성");
        await UniTask.Delay(TimeSpan.FromSeconds(SealFreezeSeconds), cancellationToken: ct);

        UI_BossBark.ShowDialogue(RitualDoneKey);
        await UniTask.Delay(TimeSpan.FromSeconds(DoneLineSeconds), cancellationToken: ct);

        _hud.Close();
        _hud = null;
    }

    /// <summary>
    /// 봉인석 넷을 북 → 동 → 남 → 서로 하나씩 세우며 카메라가 차례로 비춘다(돌 하나 약 0.65초, 10-03) —
    /// 돌은 디졸브로 솟고 발밑에 청록 원(칠 수 있음)이 핀다. 리치는 무릎 꿇은 채 · 방해 전이라 입력을 잠그고, 끝나면 플레이어 카메라로.
    /// </summary>
    private async UniTask RevealStonesAsync(float distance, CancellationToken ct)
    {
        var  player = _ctx.Runtime.CachedPlayer;
        bool camera = player != null;   // 플레이어가 없으면 비추지 않고 세우기만
        if (camera)
        {
            player.SetInputEnabled(false);
            player.SetInvincible(StoneCount * (StoneShotMove + StoneShotHold) + RevealSafety);
            LichCinematics.TakeCamera();
        }
        try
        {
            for (int i = 0; i < StoneCount; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, 90f * i, 0f) * Vector3.forward;
                Vector3 at  = _center + dir * distance;
                _stones.Add(LichSealStone.Create(at, _lich.transform, OnStoneIgnited));
                LichVfx.Play(LichVfxSlot.TileRestore, at + Vector3.up * 0.05f, Quaternion.identity, StoneRevealVfxScale);
                LichSfx.Play(LichSfxSlot.CircleSpawn, at);
                if (!camera) continue;

                // 돌 바깥 위에서 안쪽을 본다 — 돌 너머로 무릎 꿇은 리치(제단 중심)가 함께 잡혀 위치가 읽힌다.
                await LichCinematics.ShotAsync(at + dir * StoneShotBack + Vector3.up * StoneShotHeight,
                                               at + Vector3.up * StoneShotLook, StoneShotMove, ct);
                await Wait(StoneShotHold, ct);
            }
            if (camera)
            {
                await LichCinematics.ReturnToPlayerAsync(player.transform, ct);
                camera = false;
            }
        }
        finally
        {
            if (camera && player != null) LichCinematics.ReturnToPlayerAsync(player.transform, CancellationToken.None).Forget();
            if (player != null) player.SetInputEnabled(true);
        }
    }

    private void OnStoneIgnited(LichSealStone stone)
    {
        _ignited++;
        RefreshHud();
        LichSfx.Play(LichSfxSlot.CastCharge, stone.transform.position, 0.7f);
        LichPatternUtil.Impact(LichImpact.Heavy);
        // 최신 값만 — 빨리 치면 앞 값이 줄 서서 붕괴 컷신 위까지 늦게 나왔다(10-01 f5 전주기 시뮬). 다 켜지면 낡은 값은 걷는다.
        if (_ignited < StoneCount)
            UI_BossBark.ShowLatest($"봉인석 점화 ({_ignited} / {StoneCount})", BossBarkType.PatternAnnounce);
        else
            UI_BossBark.Dismiss(0f);
        Debug.Log($"[LichSeal] 봉인석 점화 {_ignited}/{StoneCount}", _lich);
    }

    private void RefreshHud() => _hud?.SetStatus($"봉인석 {_ignited} / {StoneCount}");

    /// <summary>봉인 완성 — 금 = 플레이어의 봉인(색 규약). 설계서 §3-5 「정지 1초」.</summary>
    private void PlaySealComplete()
    {
        Vector3 body = _lich.transform.position + Vector3.up * 1.2f;
        LichVfx.Play(LichVfxSlot.SealComplete, new Vector3(_center.x, _center.y + 0.05f, _center.z), Quaternion.identity);
        LichVfx.Play(LichVfxSlot.SealBurst, body, Quaternion.identity);
        LichVfx.PlayScreen(LichVfxSlot.ScreenMagicFlow, 2.5f);
        LichSfx.Play(LichSfxSlot.SealComplete, body);
        foreach (var stone in _stones)
            if (stone != null) stone.PulseChain();
        LichCinematics.Flash(new Color(1f, 0.9f, 0.6f), 0.3f, 0.2f);    // 금빛은 밝아 흰색보다 진하다 — 0.5는 화면 전체가 황토색(09-20 실측)
        LichCinematics.SlowMo(0.5f, 0.6f);
        LichPatternUtil.Impact(LichImpact.Transition);
    }

    private void SpawnInterferenceSkeletons()
    {
        var prefab = _ward != null ? _ward.additionalSkeletonPrefab : null;
        if (prefab == null) return;

        for (int i = 0; i < InterferenceSkeletons; i++)
        {
            // 봉인석 사이(대각선) — 봉인석 앞을 막지 않게
            Vector3 dir = Quaternion.Euler(0f, 45f + 180f * i, 0f) * Vector3.forward;
            UnityEngine.Object.Instantiate(prefab, _center + dir * SkeletonDistance, Quaternion.identity);
        }
    }

    /// <summary>결계 주기 공격과 같은 모양(조준 원 + 산개 원), 피해는 절반.</summary>
    private void SpawnInterferenceAttack()
    {
        var target = _ctx.Runtime.PlayerTarget;
        if (target == null || _ctx.Config?.stat == null) return;

        float attack      = _ctx.Config.stat.attackPower * InterferenceDamageScale;
        int   aimedCount  = _ward != null ? _ward.aimedZoneCount           : 1;
        float aimedRadius = _ward != null ? _ward.aimedZoneRadius          : 1.2f;
        int   aimedDamage = Mathf.Max(1, (int)(attack * (_ward != null ? _ward.aimedDamageMultiplier : 0.7f)));
        int   zoneCount   = _ward != null ? _ward.periodicZoneCount        : 3;
        float zoneRadius  = _ward != null ? _ward.periodicZoneRadius       : 1.5f;
        float scatter     = _ward != null ? _ward.periodicScatterRadius    : 4f;
        float telegraph   = _ward != null ? _ward.periodicTelegraphDuration : 0.8f;
        float active      = _ward != null ? _ward.periodicActiveDuration   : 0.5f;
        int   zoneDamage  = Mathf.Max(1, (int)(attack * (_ward != null ? _ward.periodicDamageMultiplier : 0.5f)));

        // 무릎 꿇은 채 발버둥 — 손만 든다
        _ctx.Animator?.CrossFade((_attackCycle++ & 1) == 0 ? "MagicBolt" : "ArcaneOrb", 0.1f);

        Vector3 basePos = target.position;
        for (int i = 0; i < aimedCount; i++)
            LichDarkRainZone.Spawn(basePos + new Vector3(0f, 0.05f, 0f), aimedRadius, telegraph * 0.6f, active, aimedDamage, 0.5f);

        for (int i = 0; i < zoneCount; i++)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * scatter;
            LichDarkRainZone.Spawn(basePos + new Vector3(offset.x, 0.05f, offset.y), zoneRadius, telegraph, active, zoneDamage, 0.5f);
        }
    }

    // ── 붕괴 컷신 (약 12초, 입력 잠금) ─────────────────────────────
    private async UniTask RunCollapseAsync(CancellationToken ct)
    {
        var player = _ctx.Runtime.CachedPlayer;
        player?.SetInputEnabled(false);
        UI_BossBark.Dismiss(0f);   // 의식 자막은 여기까지 — 레터박스 위로 따라오지 않게
        LichCinematics.CutsceneHud(true);
        LichCinematics.LetterboxInAsync(ct).Forget();
        LichCinematics.TakeCamera();
        try
        {
            // +0.0 플레이어를 코어 남쪽 끝으로 — 무너질 타일 위에 서 있지 않게
            PullPlayerAsync(player, _center + Vector3.back * CoreSouthOffset, ct).Forget();
            Shot(CloseShot, 1.5f, 1.0f, ct);
            await Wait(1.0f, ct);

            // +1.0 「멀린의 봉인술이라. …그 술식이 지금 어디 있는지, 잊었나.」
            UI_BossBark.ShowDialogueLine(CollapseKey, 0, interrupt: true);
            await Wait(1.5f, ct);

            // +2.5 금빛 사슬이 리치 쪽부터 보라로 물든다
            ReverseChainsAsync(ct).Forget();
            _reversedChains = LichVfx.PlayLoop(LichVfxSlot.ChainReversed, _lich.transform.position, Quaternion.identity);
            LichVfx.PlayScreen(LichVfxSlot.ScreenDebuff, 4f);
            LichSfx.Play(LichSfxSlot.PageTransition, _lich.transform.position);
            await Wait(1.5f, ct);

            // +4.0 봉인석의 빛이 꺼지고 네 봉인이 깨진다 · 「안 돼… 역류하고 있어!」
            foreach (var stone in _stones)
                if (stone != null) stone.Darken();
            UI_BossBark.ShowDialogueLine(CollapseKey, 1, interrupt: true);
            UnityEngine.Object.FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include)?.ShowBuffNotice(CollapseNotice);
            Shot(WideShot, 0f, 1.2f, ct);   // 제단 전체 — 무너지는 링이 보이게
            await Wait(1.5f, ct);

            // +5.5 봉인석 폭발 → 외곽 두 링 붕괴(컷신 전용 상한 예외 — 플레이어는 코어 위)
            foreach (var stone in _stones)
                if (stone != null) stone.Explode();
            _stones.Clear();
            if (_grid != null)
            {
                int outer = _grid.OuterRing;
                int fallen = _grid.CollapseRing(outer, OuterRingWarn, ignoreCap: true)
                           + _grid.CollapseRing(outer - 1, InnerRingWarn, ignoreCap: true);
                Debug.Log($"[LichSeal] 붕괴 — 링 {outer}·{outer - 1} {fallen}장", _lich);
            }
            LichSfx.Play(LichSfxSlot.Collapse, _center);
            LichPatternUtil.Impact(LichImpact.Transition);
            LichCinematics.SlowMo(0.6f, 0.8f);
            await Wait(1.5f, ct);

            // +7.0 리치가 사슬을 찢고 떠오른다 — 낫이 처음으로 온전히 · 「봉인은 끝났다. 다음엔 — 네가 끝날 차례다.」
            _ctx.Animator?.CrossFade("Phase2Entry", 0.1f);
            _lich.DissolveToForm(LichForm.Phase2);
            LichVfx.Stop(ref _reversedChains, 0.5f);
            LichVfx.Play(LichVfxSlot.PhaseBurst, _lich.transform.position + Vector3.up, Quaternion.identity);
            UI_BossBark.ShowDialogueLine(CollapseKey, 2, interrupt: true);
            Shot(LowShot, 3f, 1.0f, ct);   // 올려다보는 해방
            MoveAsync(_lich.transform, _lich.transform.position + Vector3.up * RiseHeight, RiseSeconds, ct).Forget();
            await Wait(2.0f, ct);

            // +9.0 하늘로 사라진다
            LichVfx.Play(LichVfxSlot.TeleportVanish, _lich.transform.position + Vector3.up, Quaternion.identity, 1.8f);
            LichSfx.Play(LichSfxSlot.Vanish, _lich.transform.position);
            await MoveAsync(_lich.transform, _lich.transform.position + Vector3.up * VanishHeight, VanishSeconds, ct);
            await Wait(0.4f, ct);

            // +10.0 그림자 「말했잖아. 버티지 못할 거라고.」
            UI_BossBark.ShowDialogueLine(CollapseKey, 3, interrupt: true);
            await Wait(1.5f, ct);
        }
        finally
        {
            LichVfx.Stop(ref _reversedChains);
            LichCinematics.LetterboxOutAsync(CancellationToken.None).Forget();
            if (player != null)
                LichCinematics.ReturnToPlayerAsync(player.transform, CancellationToken.None).Forget();
            LichCinematics.CutsceneHud(false);
            player?.SetInputEnabled(true);
        }
    }

    /// <summary>컷신 샷 — 리치 기준 <paramref name="offset"/>(제단 남쪽이 −Z)에서 리치 몸(+<paramref name="lookLift"/>)을 본다.</summary>
    private void Shot(Vector3 offset, float lookLift, float seconds, CancellationToken ct)
    {
        Vector3 lich = _lich.transform.position;
        LichCinematics.ShotAsync(lich + offset, lich + Vector3.up * (1.5f + lookLift), seconds, ct).Forget();
    }

    private async UniTaskVoid ReverseChainsAsync(CancellationToken ct)
    {
        float t = 0f;
        try
        {
            while (t < ChainReverseSecs)
            {
                t += Time.deltaTime;
                var color = Color.Lerp(PatternGuideHelper.PlayerSeal, PatternGuideHelper.Reversed, t / ChainReverseSecs);
                foreach (var stone in _stones)
                    if (stone != null) stone.SetChainColor(color);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static async UniTaskVoid PullPlayerAsync(PlayerController player, Vector3 target, CancellationToken ct)
    {
        if (player == null) return;

        var tf    = player.transform;
        var rb    = player.TryGetComponent<Rigidbody>(out var body) ? body : null;
        var start = tf.position;
        target.y  = start.y;

        float t = 0f;
        try
        {
            while (t < PullSeconds)
            {
                t += Time.deltaTime;
                var pos = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / PullSeconds));
                if (rb != null)
                {
                    rb.position       = pos;
                    rb.linearVelocity = Vector3.zero;
                }
                else
                {
                    tf.position = pos;
                }
                await UniTask.Yield(PlayerLoopTiming.FixedUpdate, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static async UniTask MoveAsync(Transform tf, Vector3 target, float seconds, CancellationToken ct)
    {
        if (tf == null) return;
        var start = tf.position;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            if (tf == null) return;
            tf.position = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / seconds));
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        if (tf != null) tf.position = target;
    }

    private static UniTask Wait(float seconds, CancellationToken ct)
        => UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: ct);

    private void Cleanup()
    {
        _hud?.Close();
        _hud = null;
        LichVfx.Stop(ref _reversedChains);

        foreach (var stone in _stones)
            if (stone != null) stone.Dismiss();   // 디졸브로 스러진다(10-03)
        _stones.Clear();

        LichSkeletonMonster.DespawnAll();
    }
}
}
