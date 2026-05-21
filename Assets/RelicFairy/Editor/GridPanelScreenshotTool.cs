#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class GridPanelScreenshotTool
{
    private const string UIROOT_PATH = "Assets/Abyss/UI/RootUI/@UIRoot.prefab";
    private const string OUTPUT_DIR  = "Assets/Screenshots/GridPanel";

    [MenuItem("Tools/Grid Panel/Capture All States")]
    public static void CaptureAllStates()
    {
        if (!Directory.Exists(OUTPUT_DIR))
            Directory.CreateDirectory(OUTPUT_DIR);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UIROOT_PATH);
        if (prefab == null) { Debug.LogError("[GridCapture] UIRoot not found: " + UIROOT_PATH); return; }

        var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (root == null) { Debug.LogError("[GridCapture] Instantiate failed"); return; }

        // CanvasGroup alpha 강제
        foreach (var cg in root.GetComponentsInChildren<CanvasGroup>(true))
            cg.alpha = 1f;

        var gridPanel = FindNamed(root, "UI_GridPanel");
        if (gridPanel == null) { Debug.LogError("[GridCapture] UI_GridPanel not found"); Object.DestroyImmediate(root); return; }
        gridPanel.SetActive(true);

        // 캡처 카메라 생성
        var camGO = new GameObject("_CaptureCam");
        var cam   = camGO.AddComponent<Camera>();
        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.05f, 0.08f);
        cam.cullingMask     = ~0;
        cam.orthographic    = false;
        cam.fieldOfView     = 60f;

        // Canvas를 Screen Space Camera 로 전환하고 캡처 카메라 연결
        foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
        {
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                canvas.renderMode    = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera   = cam;
                canvas.planeDistance = 1f;
            }
        }

        Debug.Log("[GridCapture] 시작");

        ApplyState(gridPanel, gallery: true,  edit: false, info: false, board: false);
        CaptureRT("01_GalleryMode", cam);

        ApplyState(gridPanel, gallery: false, edit: true,  info: true,  board: true);
        CaptureRT("02_EditMode", cam);

        ApplyState(gridPanel, gallery: true,  edit: false, info: true,  board: false);
        CaptureRT("03_StagingSelected", cam);

        Object.DestroyImmediate(root);
        Object.DestroyImmediate(camGO);
        AssetDatabase.Refresh();
        Debug.Log("[GridCapture] 완료 → " + OUTPUT_DIR);
    }

    private static void ApplyState(GameObject panel, bool gallery, bool edit, bool info, bool board)
    {
        SetChild(panel, "LeftPanel/GalleryView_Grid", gallery);
        SetChild(panel, "LeftPanel/EditView_Grid",    edit);
        SetChild(panel, "ItemInfoPanel",              info);
        SetChild(panel, "BoardContainer",             board);
    }

    private static void SetChild(GameObject root, string path, bool active)
    {
        var t = root.transform.Find(path);
        if (t != null) t.gameObject.SetActive(active);
        else Debug.LogWarning("[GridCapture] path not found: " + path);
    }

    private static void CaptureRT(string name, Camera cam)
    {
        int w = 1920, h = 1080;
        var rt  = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        cam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        string path = OUTPUT_DIR + "/" + name + ".png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        Debug.Log("[GridCapture] saved: " + path);
    }

    private static GameObject FindNamed(GameObject root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t.gameObject;
        return null;
    }
}
#endif
