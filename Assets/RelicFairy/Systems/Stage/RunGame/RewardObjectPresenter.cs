using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 방 클리어 보상 오브젝트의 등급 연출(10-01 이벤트방 설계 §11) — 예고(빛이 모임) · 등장 · 놓여 있는 모습.
/// 수치는 <see cref="RewardPresentation.World"/> 한 표, 빛깔은 <see cref="RarityColorTable"/>.
///
/// 여기 있는 것은 보여 주기뿐이다 — 후보 · 등급은 RoomClearGate가 이미 정했고, 보상 오브젝트 자체는 건드리지 않는다.
/// 이펙트 목록이 없거나 연출에서 예외가 나도 보상은 제때 선다(예고의 대기 길이는 그대로 지킨다).
/// </summary>
public static class RewardObjectPresenter
{
    // ── Constants ──────────────────────────────────────────────
    private const float GlyphScale   = 0.35f;   // 바닥 문양 — 소용돌이 크기에 곱한다
    private const float BeaconScale  = 0.6f;    // 표지 기둥 — 등장 기둥 배율에 곱한다
    private const float BeaconHold   = 2.5f;    // 표지 기둥이 등장 뒤 제 높이로 서 있는 시간(초, 실시간)
    private const float BeaconSettle = 0.45f;   // 그 뒤 낮아지는 크기 — 금빛 고리 + 짧은 기둥(반대편 시야를 가리지 않게, 10-01 f5)
    private const float RingStagger  = 0.15f;   // 빛 박자 · 음 사이 간격(초, 실시간)
    private const float RingGrowth   = 0.35f;   // 박자마다 커지는 비율
    private const float PulseSeconds = 0.25f;
    private const float FloorLift    = 0.05f;   // 바닥 이펙트를 살짝 띄운다(바닥과 겹쳐 깜빡이지 않게)

    private static readonly float[] NotePitches = { 1.0f, 1.15f, 1.3f };
    private static readonly Color   CommonLight = new Color(0.85f, 0.87f, 0.92f);
    private static readonly object  s_slowOwner = new object();

    // ── Public Methods ─────────────────────────────────────────

    /// <summary>등급 빛깔 — 일반은 옅은 흰빛, 나머지는 룬 카드와 같은 색.</summary>
    public static Color LightColor(ItemRarity rarity)
        => rarity == ItemRarity.Common ? CommonLight : RarityColorTable.Get(rarity);

    /// <summary>
    /// 예고 — 보상이 설 자리에 등급 빛이 모인다. 등급의 예고 길이가 지나면 돌아온다(목록을 읽는 시간도 그 안에 든다).
    /// 돌려주는 빛(<see cref="RewardAura"/>)은 <see cref="Arrive"/>에 넘긴다. 빛이 없으면 null.
    /// 취소(<see cref="OperationCanceledException"/>) 말고는 예외를 내지 않는다.
    /// </summary>
    public static async UniTask<RewardAura> ForetellAsync(Vector3 spot, float floorY, ItemRarity rarity, CancellationToken ct)
    {
        var   spec  = RewardPresentation.World(rarity);
        float start = Time.time;
        RewardAura aura = null;
        try
        {
            if (spec.SwirlScale > 0f)
            {
                try
                {
                    await RunFx.LoadAsync();
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // 예고는 연출이다 — 목록을 못 읽어도 대기 길이는 지키고 보상은 선다
                    Debug.LogWarning($"[RewardFx] 이펙트 목록 읽기 실패(빛 없이 진행) — {e.Message}");
                }
                ct.ThrowIfCancellationRequested();
                aura = BuildAura(spot, floorY, rarity, spec);
            }

            float left = spec.Foretell - (Time.time - start);
            if (left > 0f) await UniTask.Delay(TimeSpan.FromSeconds(left), cancellationToken: ct);
            return aura;
        }
        catch (OperationCanceledException)
        {
            if (aura != null) aura.Dissolve();
            throw;
        }
    }

    /// <summary>
    /// 등장 — 보상 오브젝트가 선 순간. 빛기둥 · 빛 박자 · 음 · (전설) 슬로모와 금빛 가장자리. 예고의 빛은 보상 곁에 남긴다.
    /// 보상 오브젝트는 건드리지 않는다. 예외를 내지 않는다.
    /// </summary>
    public static void Arrive(GameObject rewardGO, RewardAura aura, float floorY, ItemRarity rarity)
    {
        try
        {
            if (rewardGO == null)
            {
                if (aura != null) aura.Dissolve();
                return;
            }

            var     spec  = RewardPresentation.World(rarity);
            Color   color = LightColor(rarity);
            Vector3 floor = OnFloor(rewardGO.transform.position, floorY);
            Debug.Log($"[RewardFx] 보상 등장 — 최고 등급 {rarity} · 예고 {spec.Foretell:0.0}초 · 빛 {(aura != null ? "있음" : "없음")}");

            // 놓여 있는 빛은 레어 이상 — 매 방 보는 일반은 등장과 함께 흩어진다
            if (aura != null)
            {
                if (spec.IdleAura)
                {
                    aura.Follow(rewardGO, spec.IdlePulseHz);
                    aura.SettleBeaconAfter(BeaconHold, BeaconSettle);
                }
                else aura.Dissolve();
            }

            if (spec.Notes > 0)
                ArriveFxAsync(rewardGO, floor, rarity, spec, color).Forget();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[RewardFx] 등장 연출 실패(보상은 그대로 선다) — {e.Message}");
        }
    }

    // ── Private Methods ────────────────────────────────────────

    private static RewardAura BuildAura(Vector3 spot, float floorY, ItemRarity rarity, in RewardPresentation.WorldSpec spec)
    {
        try
        {
            if (!RunFx.IsReady) return null;

            Color color = LightColor(rarity);
            var   aura  = RewardAura.Create(spot);
            aura.AddLoop(RunFx.PlayLoop(RunFxSlot.Swirl, spot, spec.SwirlScale, color));
            if (spec.Glyph)
                aura.AddLoop(RunFx.PlayLoop(RunFxSlot.Glyph, OnFloor(spot, floorY), GlyphScale * spec.SwirlScale, color));
            // 전설 — 금빛 표지 기둥이 서기 전부터 솟는다(원래 금빛이라 색을 입히지 않는다)
            if (spec.IdlePillar)
                aura.SetBeacon(RunFx.PlayLoop(RunFxSlot.Beacon, OnFloor(spot, floorY), spec.PillarScale * BeaconScale, Color.clear));
            aura.Grow(spec.Foretell);

            Managers.Sound?.PlayUiAsync(SoundKey.Sfx.UiButton, 0.45f, RewardPresentation.For(rarity).SfxPitch).Forget();
            return aura;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[RewardFx] 예고 연출 실패(대기는 그대로) — {e.Message}");
            return null;
        }
    }

    private static async UniTaskVoid ArriveFxAsync(GameObject rewardGO, Vector3 floor, ItemRarity rarity,
                                                  RewardPresentation.WorldSpec spec, Color color)
    {
        var  ct     = rewardGO.GetCancellationTokenOnDestroy();
        bool slowed = false;
        try
        {
            if (spec.PillarScale > 0f) RunFx.Play(RunFxSlot.Pillar, floor, spec.PillarScale, color);
            if (spec.PulsePeak > 0f)   VolumePulseService.Pulse(spec.PulsePeak, PulseSeconds);
            if (spec.EdgeGlow)         FinisherEdgeService.Pulse();
            if (spec.SlowScale < 1f)
            {
                TimeScaleArbiter.Acquire(s_slowOwner, spec.SlowScale, TimeScaleArbiter.Priority.SlowMotion);
                slowed = true;
            }

            float elapsed = 0f;
            int   beats   = Mathf.Max(spec.Rings, spec.Notes);
            for (int i = 0; i < beats; i++)
            {
                if (i < spec.Rings) RunFx.Play(RunFxSlot.Ring, floor, 1f + RingGrowth * i, color);
                if (i < spec.Notes)
                {
                    float pitch = spec.Notes > 1 ? NotePitches[Mathf.Min(i, NotePitches.Length - 1)]
                                                 : RewardPresentation.For(rarity).SfxPitch;
                    Managers.Sound?.PlayUiAsync(SoundKey.Sfx.ItemPickup, 0.7f, pitch).Forget();
                }
                if (i < beats - 1)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(RingStagger), DelayType.UnscaledDeltaTime, cancellationToken: ct);
                    elapsed += RingStagger;
                }
            }

            if (slowed && spec.SlowSeconds > elapsed)
                await UniTask.Delay(TimeSpan.FromSeconds(spec.SlowSeconds - elapsed), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogWarning($"[RewardFx] 등장 연출 도중 예외 — {e.Message}");
        }
        finally
        {
            // 취소 · 예외 어느 길로 빠져도 시간 배율이 남지 않게
            if (slowed) TimeScaleArbiter.Release(s_slowOwner);
        }
    }

    private static Vector3 OnFloor(Vector3 p, float floorY)
    {
        p.y = floorY + FloorLift;
        return p;
    }
}
