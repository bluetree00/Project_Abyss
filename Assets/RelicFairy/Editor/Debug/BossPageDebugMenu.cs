using System.Reflection;
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 2페이지 보스 검증 도구(에디터 전용) — 악몽기 오버라이드(Test Run/Story/Override - Nightmare)와 함께 쓴다.
///   · Jump To Page 2 Boundary : 보스 체력을 2페이지 경계 바로 위로 — 다음 한 방에 전환 연출이 나온다.
///   · Jump To Signature       : 2페이지에서 체력을 간판 경계(2페이지 50%) 바로 위로.
///   · Log Pages               : 페이지 · 체력 · 전환/간판 여부.
/// 시뮬(Fight Sim)로 긴 1페이지를 다 깎지 않고 2페이지 패턴을 재기 위한 것이다.
/// </summary>
public static class BossPageDebugMenu
{
    private const string Root = "RelicFairy/Boss/Page2/";
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem(Root + "Jump To Page 2 Boundary (Play)")]
    public static void JumpToPage2()
    {
        var boss = FindPagedBoss(out var pages);
        if (boss == null) return;
        if (pages.IsPage2) { Debug.Log("[Page2Debug] 이미 2페이지"); return; }
        SetHp(boss, pages.Page2Hp + Mathf.Max(20, Mathf.RoundToInt(boss.EffectiveMaxHp * 0.01f)));
        Debug.Log($"[Page2Debug] 2페이지 경계로 — HP {boss.CurrentHp}/{boss.EffectiveMaxHp} (경계 {pages.Page2Hp})");
    }

    [MenuItem(Root + "Jump To Signature (Play)")]
    public static void JumpToSignature()
    {
        var boss = FindPagedBoss(out var pages);
        if (boss == null) return;
        if (!pages.IsPage2) { Debug.LogWarning("[Page2Debug] 2페이지가 아니다"); return; }
        SetHp(boss, Mathf.CeilToInt(pages.Page2Hp * BossPages.SignatureAt) + 20);
        Debug.Log($"[Page2Debug] 간판 경계로 — HP {boss.CurrentHp}/{boss.EffectiveMaxHp}");
    }

    [MenuItem(Root + "Force Late - Skip Signature (Play)")]
    public static void ForceLate()
    {
        var boss = FindPagedBoss(out var pages);
        if (boss == null) return;
        if (!pages.IsPage2) { Debug.LogWarning("[Page2Debug] 2페이지가 아니다"); return; }
        pages.MarkSignatureDone();
        SetHp(boss, pages.Page2Hp);
        Debug.Log($"[Page2Debug] 후반으로 — 간판 건너뜀 · HP {boss.CurrentHp}/{boss.EffectiveMaxHp} (연계기 · 쉬는 시간 −25% 확인용)");
        BossCombatLog.Note("[후반] 강제 — 간판 건너뜀");
    }

    [MenuItem(Root + "Log Pages (Play)")]
    public static void LogPages()
    {
        var boss = FindPagedBoss(out var pages);
        if (boss == null) return;
        Debug.Log($"[Page2Debug] {boss.name} · 켜짐 {pages.Enabled} · 페이지 {pages.Page} · 전환 중 {pages.Transitioning} · 간판 {pages.SignatureDone} · " +
                  $"HP {boss.CurrentHp}/{boss.EffectiveMaxHp} · 2페이지 몫 {pages.Page2Hp} · 전환 {pages.TransitionDue(boss.CurrentHp)} · 간판 때 {pages.SignatureDue(boss.CurrentHp)}");
    }

    private static MonsterBase FindPagedBoss(out BossPages pages)
    {
        pages = null;
        if (!Application.isPlaying) { Debug.LogWarning("[Page2Debug] 플레이 모드에서만."); return null; }
        foreach (var m in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
        {
            if (m is not IPagedBoss paged || paged.Pages == null || m.IsDead) continue;
            pages = paged.Pages;
            if (!pages.Enabled) { Debug.LogWarning($"[Page2Debug] {m.name}: 2페이지 꺼짐(봉인기) — Test Run/Story/Override - Nightmare 후 보스방에 다시 들어가라"); return null; }
            return m;
        }
        Debug.LogWarning("[Page2Debug] 2페이지 보스를 못 찾음");
        return null;
    }

    private static void SetHp(MonsterBase boss, int hp)
    {
        var runtime = typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(boss);
        var field   = runtime?.GetType().GetField("CurrentHp", Inst);
        if (field == null) { Debug.LogWarning("[Page2Debug] 체력 필드를 못 찾음"); return; }
        field.SetValue(runtime, Mathf.Clamp(hp, 1, boss.EffectiveMaxHp));
    }
}
