using UnityEditor;
using UnityEngine;

/// <summary>
/// 보상 오브젝트 등급 연출 점검 도구(에디터 전용, 10-01 이벤트방 설계 §11).
///   · Self Check - World Spec : 등급 · 모드별 수치가 설계표대로인지 찍는다(PASS/FAIL).
///   · Mode - Full / Brief / Off : 연출 강도를 바꾼다(PlayerPrefs — 끝나면 Full로 되돌릴 것).
///   · Preview … (Play) : 플레이어 앞에 그 등급의 예고 → 등장 → 놓여 있는 모습을 세우고 찍는다(RewardPresentationPreview.cs).
/// </summary>
public static partial class RewardPresentationDebugMenu
{
    private const string Root = "RelicFairy/Reward/";

    [MenuItem(Root + "Mode - Full")]  public static void ModeFull()  => SetMode(RewardPresentationMode.Full);
    [MenuItem(Root + "Mode - Brief")] public static void ModeBrief() => SetMode(RewardPresentationMode.Brief);
    [MenuItem(Root + "Mode - Off")]   public static void ModeOff()   => SetMode(RewardPresentationMode.Off);

    private static void SetMode(RewardPresentationMode mode)
    {
        RewardPresentation.Mode = mode;
        Debug.Log($"[RewardFx] 연출 모드 = {mode}");
    }

    [MenuItem(Root + "Self Check - World Spec")]
    public static void SelfCheckWorldSpec()
    {
        var saved = RewardPresentation.Mode;
        int fail = 0;
        try
        {
            RewardPresentation.Mode = RewardPresentationMode.Full;
            fail += Expect("전체 · 일반 예고 0.6",       RewardPresentation.World(ItemRarity.Common).Foretell,     0.6f);
            fail += Expect("전체 · 레어 예고 1.0",       RewardPresentation.World(ItemRarity.Rare).Foretell,       1.0f);
            fail += Expect("전체 · 에픽 예고 1.5",       RewardPresentation.World(ItemRarity.Epic).Foretell,       1.5f);
            fail += Expect("전체 · 전설 예고 2.0",       RewardPresentation.World(ItemRarity.Legendary).Foretell,  2.0f);
            fail += Expect("전체 · 일반 빛기둥 없음",    RewardPresentation.World(ItemRarity.Common).PillarScale,  0f);
            fail += Expect("전체 · 에픽 고리 1",         RewardPresentation.World(ItemRarity.Epic).Rings,          1);
            fail += Expect("전체 · 전설 고리 2",         RewardPresentation.World(ItemRarity.Legendary).Rings,     2);
            fail += Expect("전체 · 전설 슬로모 0.5",     RewardPresentation.World(ItemRarity.Legendary).SlowScale, 0.5f);
            fail += Expect("전체 · 에픽 색수차 없음",    RewardPresentation.World(ItemRarity.Epic).PulsePeak,      0f);
            fail += Expect("전체 · 전설 색수차 약하게",  RewardPresentation.World(ItemRarity.Legendary).PulsePeak, 0.15f);
            fail += Expect("전체 · 에픽 슬로모 없음",    RewardPresentation.World(ItemRarity.Epic).SlowScale,      1f);
            fail += Expect("전체 · 전설 음 3",           RewardPresentation.World(ItemRarity.Legendary).Notes,     3);
            fail += Expect("전체 · 일반 등급 기둥 없음", RewardPresentation.World(ItemRarity.Common).IdlePillar ? 1 : 0,    0);
            fail += Expect("전체 · 레어 등급 기둥",      RewardPresentation.World(ItemRarity.Rare).IdlePillar ? 1 : 0,      1);
            fail += Expect("전체 · 에픽 등급 기둥",      RewardPresentation.World(ItemRarity.Epic).IdlePillar ? 1 : 0,      1);
            fail += Expect("전체 · 전설 등급 기둥",      RewardPresentation.World(ItemRarity.Legendary).IdlePillar ? 1 : 0, 1);
            fail += Expect("전체 · 일반 빛 흩어짐",      RewardPresentation.World(ItemRarity.Common).IdleAura ? 1 : 0, 0);
            fail += Expect("전체 · 레어 빛 남음",        RewardPresentation.World(ItemRarity.Rare).IdleAura ? 1 : 0,   1);

            RewardPresentation.Mode = RewardPresentationMode.Brief;
            fail += Expect("축약 · 전설 예고 1.0",       RewardPresentation.World(ItemRarity.Legendary).Foretell,  1.0f);
            fail += Expect("축약 · 전설 슬로모 없음",    RewardPresentation.World(ItemRarity.Legendary).SlowScale, 1f);
            fail += Expect("축약 · 전설 고리 1",         RewardPresentation.World(ItemRarity.Legendary).Rings,     1);
            fail += Expect("축약 · 전설 음 1",           RewardPresentation.World(ItemRarity.Legendary).Notes,     1);

            RewardPresentation.Mode = RewardPresentationMode.Off;
            fail += Expect("끔 · 전설 대기 0.4",         RewardPresentation.World(ItemRarity.Legendary).Foretell,  RewardPresentation.WorldOffDelay);
            fail += Expect("끔 · 전설 소용돌이 없음",    RewardPresentation.World(ItemRarity.Legendary).SwirlScale, 0f);
            fail += Expect("끔 · 전설 음 0",             RewardPresentation.World(ItemRarity.Legendary).Notes,     0);
        }
        finally
        {
            RewardPresentation.Mode = saved;
        }
        Debug.Log(fail == 0 ? "[RewardFx] 등급표 점검 PASS" : $"[RewardFx] 등급표 점검 FAIL {fail}건");
    }

    private static int Expect(string label, float actual, float expected)
    {
        if (Mathf.Abs(actual - expected) < 0.0001f) return 0;
        Debug.LogError($"[RewardFx] FAIL {label} — 실제 {actual} · 기대 {expected}");
        return 1;
    }

    private static int Expect(string label, int actual, int expected)
    {
        if (actual == expected) return 0;
        Debug.LogError($"[RewardFx] FAIL {label} — 실제 {actual} · 기대 {expected}");
        return 1;
    }
}
