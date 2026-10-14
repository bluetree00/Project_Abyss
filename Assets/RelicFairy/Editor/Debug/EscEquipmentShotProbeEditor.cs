#if UNITY_EDITOR
using System.Reflection;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-13 일시정지 「장비」 쪽 촬영(4b UI 요청 8) — 런 안에서 ESC 메뉴를 열고 「장비」를 눌러 한 장, 메뉴 판 한 장(버튼 6개 배치).
/// 결과: Temp/ui_shots/EscEquip_*.png · 로그 「[EscEquipShot] 끝」(무기 칸 글 첫 줄 · 영어 enum 남음 여부).
/// </summary>
public static class EscEquipmentShotProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/10-13 ESC 장비 쪽 촬영 (런 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("[EscEquipShot] 플레이 모드(런 안)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new System.Text.StringBuilder("ESC 장비 쪽 촬영\n");
        UI_EscMenu menu = null;
        try
        {
            menu = Object.FindFirstObjectByType<UI_EscMenu>(FindObjectsInactive.Include);
            if (menu == null) menu = new GameObject("ProbeEscMenu").AddComponent<UI_EscMenu>();
            menu.Open();
            await UniTask.Delay(500, ignoreTimeScale: true);
            await Shot("EscEquip_1_Menu");
            typeof(UI_EscMenu).GetMethod("OnEquipment", Inst)?.Invoke(menu, null);
            await UniTask.Delay(600, ignoreTimeScale: true);
            await Shot("EscEquip_2_Page");
            var page = Object.FindFirstObjectByType<UI_EquipmentPage>();
            for (int i = 0; i < 2 && page != null; i++)
            {
                var t = page.transform.Find($"Panel/Weapon{i}")?.GetComponent<TMP_Text>();
                string plain = t != null ? System.Text.RegularExpressions.Regex.Replace(t.text, "<[^>]+>", "") : "(없음)";
                string[] lines = plain.Split('\n');
                sb.AppendLine($"무기 {i + 1}: {(lines.Length > 1 ? lines[1] : plain)} · 줄 {lines.Length} · 넘침 {(t != null && t.isTextOverflowing)}");
            }
        }
        catch (System.Exception e) { sb.AppendLine("예외: " + e); }
        finally
        {
            if (menu != null) menu.Close();
            Debug.Log("[EscEquipShot] 끝\n" + sb);
        }
    }

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 300 }) : UniTask.CompletedTask;
    }
}
#endif
