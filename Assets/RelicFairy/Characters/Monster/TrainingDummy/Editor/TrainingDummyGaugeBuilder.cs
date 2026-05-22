using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 일회성 빌더 — TrainingDummy 프리팹에 단일 ElementGauge UI를 셋업하고 TrainingDummy의 필드를 와이어링한다.
/// 메뉴: Tools/Abyss/Build TrainingDummy ElementGauge
/// </summary>
public static class TrainingDummyGaugeBuilder
{
    private const string PrefabPath = "Assets/Abyss/Characters/Monster/TrainingDummy/TrainingDummy.prefab";

    [MenuItem("Tools/Abyss/Build TrainingDummy ElementGauge")]
    public static void Build()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[Builder] 프리팹을 찾을 수 없음: {PrefabPath}");
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var canvas = root.transform.Find("HPBarCanvas");
            if (canvas == null)
            {
                Debug.LogError("[Builder] HPBarCanvas 없음");
                return;
            }

            // 기존에 만들어진 빈 ElementGauge가 있다면 제거
            var existing = canvas.Find("ElementGauge");
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            // ── ElementGauge container ────────────────────────────────
            var gauge = new GameObject("ElementGauge", typeof(RectTransform));
            gauge.transform.SetParent(canvas, false);
            var gaugeRT = (RectTransform)gauge.transform;
            // HP 위에 띄우기 — HPBarCanvas는 200x60. 가로 200, 세로 16, y=+38 (HP 슬라이더 위)
            gaugeRT.anchorMin        = new Vector2(0.5f, 0.5f);
            gaugeRT.anchorMax        = new Vector2(0.5f, 0.5f);
            gaugeRT.pivot            = new Vector2(0.5f, 0.5f);
            gaugeRT.sizeDelta        = new Vector2(180, 16);
            gaugeRT.anchoredPosition = new Vector2(0, 38);

            // ── BG (어두운 회색 배경) ─────────────────────────────────
            var bg = new GameObject("BG", typeof(Image));
            bg.transform.SetParent(gauge.transform, false);
            var bgRT = (RectTransform)bg.transform;
            bgRT.anchorMin        = Vector2.zero;
            bgRT.anchorMax        = Vector2.one;
            bgRT.sizeDelta        = Vector2.zero;
            bgRT.anchoredPosition = Vector2.zero;
            var bgImg = bg.GetComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.6f);
            bgImg.raycastTarget = false;

            // ── Fill (원소 색, 가로 fill) ─────────────────────────────
            var fill = new GameObject("Fill", typeof(Image));
            fill.transform.SetParent(gauge.transform, false);
            var fillRT = (RectTransform)fill.transform;
            fillRT.anchorMin        = Vector2.zero;
            fillRT.anchorMax        = Vector2.one;
            fillRT.sizeDelta        = new Vector2(-2, -2); // 1px 패딩
            fillRT.anchoredPosition = Vector2.zero;
            var fillImg = fill.GetComponent<Image>();
            fillImg.color = Color.gray;
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImg.fillAmount = 0f;
            fillImg.raycastTarget = false;

            // ── Label (수치 텍스트, 게이지 위에 작게) ─────────────────
            var label = new GameObject("Label", typeof(TextMeshProUGUI));
            label.transform.SetParent(gauge.transform, false);
            var labelRT = (RectTransform)label.transform;
            labelRT.anchorMin        = Vector2.zero;
            labelRT.anchorMax        = Vector2.one;
            labelRT.sizeDelta        = Vector2.zero;
            labelRT.anchoredPosition = Vector2.zero;
            var labelTMP = label.GetComponent<TextMeshProUGUI>();
            labelTMP.text = "- 0/100";
            labelTMP.fontSize = 9;
            labelTMP.alignment = TextAlignmentOptions.Center;
            labelTMP.color = Color.white;
            labelTMP.raycastTarget = false;
            // 기본 폰트는 그대로 (HPText에서 쓰는 것과 동일하게 유지됨)

            // ── ElementBuildup 보장 ───────────────────────────────────
            if (root.GetComponent<ElementBuildup>() == null)
                root.AddComponent<ElementBuildup>();

            // ── TrainingDummy 와이어링 ────────────────────────────────
            var dummy = root.GetComponent<TrainingDummy>();
            if (dummy != null)
            {
                var so = new SerializedObject(dummy);
                var fillProp  = so.FindProperty("elementGaugeFill");
                var labelProp = so.FindProperty("elementGaugeLabel");
                if (fillProp != null)  fillProp.objectReferenceValue  = fillImg;
                if (labelProp != null) labelProp.objectReferenceValue = labelTMP;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning("[Builder] TrainingDummy 컴포넌트 없음 — 와이어링 건너뜀");
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[Builder] TrainingDummy ElementGauge 빌드 완료");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
