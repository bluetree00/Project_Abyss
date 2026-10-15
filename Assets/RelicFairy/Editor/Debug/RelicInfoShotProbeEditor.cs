#if UNITY_EDITOR
using System.Reflection;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-13 유물 정보 팝업 촬영 — 가웨인 · 랜슬롯을 차례로 열어 한 장씩(Artsystack 능력 카드 판 · 배지 · 초상 받침 · 임시 문장 · 태그 칩).
/// 결과: Temp/ui_shots/RelicInfo_*.png · 로그 「[RelicInfoShot] 끝」. 팝업만 열고 닫는다(선택은 하지 않는다).
/// </summary>
public static class RelicInfoShotProbeEditor
{
    private const string DataDir = "Assets/RelicFairy/Systems/Relic/Data/";

    [MenuItem("RelicFairy/Debug/10-13 유물 정보 촬영 — 가웨인 · 랜슬롯 (플레이 중)")]
    private static void Run()
    {
        if (!EditorApplication.isPlaying || Managers.UI == null) { Debug.LogWarning("[RelicInfoShot] 플레이 모드(부팅 뒤)에서"); return; }
        RunAsync().Forget();
    }

    private static async UniTaskVoid RunAsync()
    {
        var sb = new System.Text.StringBuilder("유물 정보 촬영\n");
        HubGui("HideTestHubGui");   // 테스트 허브 메뉴가 팝업을 덮지 않게(10-15)
        try
        {
            foreach (var id in new[] { "Gawain", "Lancelot" })
            {
                var relic = AssetDatabase.LoadAssetAtPath<RelicClassSO>($"{DataDir}RelicClass_{id}.asset");
                if (relic == null) { sb.AppendLine($"{id}: 에셋 없음"); continue; }
                var popup = await Managers.UI.ShowPopupUIAndGetAsync<UI_RelicInfoPopup>();
                if (popup == null) { sb.AppendLine($"{id}: 팝업을 못 열었다"); continue; }
                popup.Setup(relic, () => { });
                await UniTask.Delay(900, ignoreTimeScale: true);   // 카드 등장 연출
                await Shot($"RelicInfo_{id}");
                var box = ShopUIStyle.FindDeep(popup.transform, "PortraitFrame");
                sb.AppendLine($"{id}: 받침 고리 {(box != null && box.Find("PortraitRing") != null)} · 빛 {(box != null && box.Find("PortraitGlow") != null)} · 임시 문장 {(box != null && box.Find("PortraitEmblem") is Transform e && e.gameObject.activeSelf)}");
                Managers.UI.ClosePopupUI(popup);
                await UniTask.Delay(400, ignoreTimeScale: true);
            }
        }
        catch (System.Exception ex) { sb.AppendLine("예외: " + ex); }
        finally { HubGui("RestoreTestHubGui"); Debug.Log("[RelicInfoShot] 끝\n" + sb); }
    }

    private static void HubGui(string method)
        => typeof(UILayoutRuntimeProbeEditor).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);

    private static UniTask Shot(string name)
    {
        var m = typeof(UILayoutRuntimeProbeEditor).GetMethod("ShotAsync", BindingFlags.Static | BindingFlags.NonPublic);
        return m != null ? (UniTask)m.Invoke(null, new object[] { name, 300 }) : UniTask.CompletedTask;
    }
}
#endif
