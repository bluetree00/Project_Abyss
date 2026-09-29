using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 도구 · 플레이 중] 씬 안의 <b>모든 PlayerController</b>와 그 초기화 상태를 찍는다.
///
/// 「PlayerController.Update() NullReferenceException(Combo가 null)」의 주인을 찾으려는 것이다.
/// Update는 <c>_inputInitialized</c>가 참일 때만 지나가고 Combo는 그보다 <b>먼저</b> 만들어지므로,
/// 정상 초기화된 인스턴스에서는 날 수 없다 → 어떤 인스턴스가 어떤 상태인지 직접 본다(허브 씬에서 실행).
/// 결과: 콘솔 + Temp/player_instances.txt
/// </summary>
public static class PlayerInstanceProbeEditor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("RelicFairy/Debug/플레이어 인스턴스 상태 (플레이 중)")]
    private static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[플레이어인스턴스] 플레이 모드에서만 동작한다."); return; }

        var all = Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var sb = new StringBuilder();
        sb.AppendLine($"PlayerController {all.Length}개");

        foreach (var p in all)
        {
            string path = p.gameObject.name;
            for (var t = p.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;

            sb.AppendLine($"· {path}");
            sb.AppendLine($"    씬 {p.gameObject.scene.name} · 컴포넌트 enabled={p.enabled} · 오브젝트 active={p.gameObject.activeInHierarchy}");
            sb.AppendLine($"    입력초기화={Get<bool>(p, "_inputInitialized")} · Combo={(p.Combo != null)}" +
                          $" · Stamina={(p.Stamina != null)} · Rigid={(p.Rigid != null)}");
            sb.AppendLine($"    characterData={(p.CharacterData != null)} · 카메라={(p.CinemachineCamera != null)}" +
                          $" · 분신={p.IsShadowClone} · 등록된 플레이어={(Managers.Player != null && Managers.Player.PlayerTransform == p.transform)}");
        }

        string text = sb.ToString();
        System.IO.File.WriteAllText("Temp/player_instances.txt", text);
        Debug.Log("[플레이어인스턴스] 결과\n" + text);
    }

    private static T Get<T>(object target, string field)
    {
        var f = target.GetType().GetField(field, Inst);
        return f != null ? (T)f.GetValue(target) : default;
    }
}
