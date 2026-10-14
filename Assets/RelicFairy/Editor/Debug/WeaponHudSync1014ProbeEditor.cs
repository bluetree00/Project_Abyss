#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-14 사용자 「가끔 사용 중인 장비가 아닌 장비가 사용 중 UI로 보인다 — 근접을 들고 있는데 UI는 원거리」 재현 · 검증(런 중).
/// ⓪ 런 시작 때 든 칸(근접 = 0이어야 — 거점과 같은 원칙) ① 연달아 두 프레임에 바꾸기(입력과 같은 PreUpdate) 8회 — 큰 원(78) = 든 칸이어야
/// ② 시간 정지 중 바꾸기 키 — 무시돼야(포션과 같다). 결과: Temp/weapon_hud_sync_1014_probe.txt · 로그 「[WeaponHudSync] 끝」.
/// </summary>
public static class WeaponHudSync1014ProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static StringBuilder _sb;
    private static int _pass, _fail;

    /// <summary>테스트 허브 원거리 고르기(첫 원거리) — 런 시작 때 두 칸이 다 찬 채로 근접을 드는지 보려고(허브 기본은 원거리 없음).</summary>
    [MenuItem("RelicFairy/Debug/10-14 무기 HUD — 허브 원거리 고르기 (테스트 허브, 플레이 중)")]
    private static void PickRanged()
    {
        var launcher = Object.FindFirstObjectByType<TestHubLauncher>();
        var f = typeof(TestHubLauncher).GetField("_rangedIndex", Inst);
        if (launcher == null || f == null) { Debug.LogWarning("[WeaponHudSync] 테스트 허브가 아니다"); return; }
        f.SetValue(launcher, 0);
        Debug.Log("[WeaponHudSync] 허브 원거리 = 첫 원거리");
    }

    [MenuItem("RelicFairy/Debug/10-14 무기 HUD 어긋남 재현 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || GameRunBootstrapper.Instance?.Run?.Player == null) { Debug.LogWarning("[WeaponHudSync] 런 안에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        _sb = new StringBuilder("10-14 무기 HUD 어긋남\n");
        _pass = _fail = 0;
        var player = GameRunBootstrapper.Instance.Run.Player;
        var wm = player.WeaponManager;
        var hud = Object.FindFirstObjectByType<CombatPanelView>(FindObjectsInactive.Include);
        object owner = new object();
        try
        {
            if (wm == null || hud == null) { _sb.AppendLine("무기 관리자 · HUD 없음"); _fail++; return; }
            bool twoAtStart = wm.slots[1] != null && !wm.slots[1].IsEmpty;
            Check($"⓪ 런 시작 때 든 칸 = 근접(0){(twoAtStart ? " · 원거리도 찬 채" : " · 원거리 없이 시작(이 확인은 약함)")}", wm.CurrentSlotIndex == 0, $"든 칸 {wm.CurrentSlotIndex}");
            if (!twoAtStart)
            {
                // 원거리 칸이 비었으면 활을 끈 채 끼운다(8e WeaponFormProbe와 같은 방법)
                var bow = AssetDatabase.LoadAssetAtPath<WeaponSO>("Assets/RelicFairy/Weapon/Bow/Data/T1_Bow.asset");
                if (bow != null) await wm.AcquireWeaponToSlotAsync(WeaponData.FromSO(bow), PlayerWeaponManager.Slot1, setActive: false, playAppear: false);
            }
            if (wm.slots[0] == null || wm.slots[0].IsEmpty || wm.slots[1] == null || wm.slots[1].IsEmpty)
            { _sb.AppendLine("두 칸이 다 차 있지 않다"); _fail++; return; }

            // 두 칸 인스턴스를 한 번씩 만들어 둔다(첫 바꾸기의 불러오기 프레임을 빼려고)
            await SwitchUntil(wm, 1); await UniTask.Delay(500, ignoreTimeScale: true);
            await SwitchUntil(wm, 0); await UniTask.Delay(500, ignoreTimeScale: true);

            // ① 연달아 두 프레임 — 입력(PreUpdate)과 같은 때 1 → 0
            int bad = 0, cond = 0;
            for (int trial = 0; trial < 8; trial++)
            {
                await UniTask.Yield(PlayerLoopTiming.PreUpdate);
                wm.SwitchToSlotAsync(1).Forget();
                if (trial == 0) _sb.AppendLine($"  진단 — 첫 바꾸기 직후: 패널 켜짐 {hud.isActiveAndEnabled} · 원 크기 {Sizes(hud)} · HUD 든 칸 {typeof(CombatPanelView).GetField("_activeWeaponIndex", Inst)?.GetValue(hud)}");
                bool back = false;
                for (int f = 0; f < 6 && !back; f++)
                {
                    await UniTask.Yield(PlayerLoopTiming.PreUpdate);
                    // 재현 조건 — 둘째 바꾸기 순간 원이 아직 제자리(근접 = 큰 원)면, 옛 코드는 「이미 제자리」로 건너뛰어 첫 바꿈이 끝까지 돌았다
                    if (trial == 0 && f == 0) _sb.AppendLine($"  진단 — 다음 프레임 PreUpdate: 원 크기 {Sizes(hud)} · 든 칸 {wm.CurrentSlotIndex}");
                    if (wm.CurrentSlotIndex == 1 && BigSlot(hud) == 0) cond++;
                    if (wm.CurrentSlotIndex == 1) wm.SwitchToSlotAsync(0).Forget();
                    back = wm.CurrentSlotIndex == 0;
                    if (back) _sb.AppendLine($"  시도 {trial + 1}: {f + 1}프레임 뒤 되돌림");
                }
                await UniTask.Delay(500, ignoreTimeScale: true);
                int big = BigSlot(hud);
                if (big != wm.CurrentSlotIndex) bad++;
                _sb.AppendLine($"    든 칸 {wm.CurrentSlotIndex} · 큰 원 {big}{(big != wm.CurrentSlotIndex ? "  ← 어긋남" : "")}");
                if (wm.CurrentSlotIndex != 0) { await SwitchUntil(wm, 0); await UniTask.Delay(400, ignoreTimeScale: true); }
            }
            Check("① 연달아 바꾸기 8회 — 큰 원 = 든 칸", bad == 0, $"어긋남 {bad}/8 · 재현 조건(둘째 바꾸기 때 원이 아직 제자리) {cond}/8");

            // ② 시간 정지 중 바꾸기 키(PlayerController.ChangeWeapon) — 무시돼야
            TimeScaleArbiter.Acquire(owner, 0f, TimeScaleArbiter.Priority.Pause);
            await UniTask.Yield();
            typeof(PlayerController).GetMethod("ChangeWeapon", Inst)?.Invoke(player, new object[] { 1 });
            await UniTask.Delay(300, ignoreTimeScale: true);
            Check("② 시간 정지 중 바꾸기 무시", wm.CurrentSlotIndex == 0, $"든 칸 {wm.CurrentSlotIndex}");
        }
        catch (System.Exception e) { _sb.AppendLine("예외: " + e); _fail++; }
        finally
        {
            TimeScaleArbiter.Release(owner);
            if (wm != null && wm.CurrentSlotIndex != 0) await SwitchUntil(wm, 0);
            _sb.AppendLine($"합계 통과 {_pass} · 실패 {_fail}");
            File.WriteAllText("Temp/weapon_hud_sync_1014_probe.txt", _sb.ToString());
            Debug.Log("[WeaponHudSync] 끝\n" + _sb);
        }
    }

    /// <summary>바꾸기가 받아들여질 때까지 몇 프레임 다시 부른다(바꾸는 중이면 무시되므로).</summary>
    private static async UniTask SwitchUntil(PlayerWeaponManager wm, int slot)
    {
        for (int f = 0; f < 30 && wm.CurrentSlotIndex != slot; f++)
        {
            wm.SwitchToSlotAsync(slot).Forget();
            await UniTask.Yield();
        }
    }

    private static string Sizes(CombatPanelView hud)
    {
        var roots = typeof(CombatPanelView).GetField("_weaponSlotRoot", Inst)?.GetValue(hud) as RectTransform[];
        if (roots == null) return "?";
        var sb = new StringBuilder();
        foreach (var r in roots) sb.Append(r != null ? r.sizeDelta.x.ToString("0.#") : "-").Append(' ');
        return sb.ToString().Trim();
    }

    /// <summary>큰 원(78)인 무기 칸 번호(-1 = 없음).</summary>
    private static int BigSlot(CombatPanelView hud)
    {
        var roots = typeof(CombatPanelView).GetField("_weaponSlotRoot", Inst)?.GetValue(hud) as RectTransform[];
        if (roots == null) return -1;
        for (int i = 0; i < roots.Length; i++)
            if (roots[i] != null && roots[i].sizeDelta.x > 60f) return i;
        return -1;
    }

    private static void Check(string name, bool ok, string note)
    {
        if (ok) _pass++; else _fail++;
        _sb.AppendLine($"  {(ok ? "통과" : "실패")} — {name}{(string.IsNullOrEmpty(note) ? "" : " · " + note)}");
    }
}
#endif
