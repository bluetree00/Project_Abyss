using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 일회성 빌더 — LeeHPBar 프리팹에 ElementGauge 추가하고 MonsterHPBar 필드 와이어링.
/// 메뉴: Tools/Abyss/Build MonsterHPBar ElementGauge
/// </summary>
public static class MonsterHPBarGaugeBuilder
{
    private const string PrefabPath = "Assets/Abyss/UI/WorldSpace/LeeHPBar.prefab";

    [MenuItem("Tools/Abyss/Build MonsterHPBar ElementGauge")]
    public static void Build()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            Debug.LogError($"[Builder] {PrefabPath} 로드 실패");
            return;
        }

        try
        {
            // 기존 ElementGauge 제거 (재실행 대응)
            var existing = root.transform.Find("ElementGauge");
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            // Slider 위치/크기 참고
            var slider = root.transform.Find("Slider") as RectTransform;
            if (slider == null)
            {
                Debug.LogError("[Builder] Slider 찾을 수 없음");
                return;
            }

            // ── ElementGauge 컨테이너 ─────────────────────────────
            var gauge = new GameObject("ElementGauge", typeof(RectTransform));
            gauge.transform.SetParent(root.transform, false);
            var gaugeRT = (RectTransform)gauge.transform;
            gaugeRT.anchorMin        = slider.anchorMin;
            gaugeRT.anchorMax        = slider.anchorMax;
            gaugeRT.pivot            = slider.pivot;
            gaugeRT.sizeDelta        = new Vector2(slider.sizeDelta.x * 0.9f, slider.sizeDelta.y * 0.5f);
            // HP 위로 배치 — Slider 높이의 0.7배만큼 위로
            gaugeRT.anchoredPosition = slider.anchoredPosition + new Vector2(0, slider.sizeDelta.y * 0.9f);

            // ── BG ───────────────────────────────────────────────
            var bg = new GameObject("BG", typeof(Image));
            bg.transform.SetParent(gauge.transform, false);
            var bgRT = (RectTransform)bg.transform;
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.sizeDelta = Vector2.zero;
            var bgImg = bg.GetComponent<Image>();
            bgImg.color = new Color(0, 0, 0, 0.6f);
            bgImg.raycastTarget = false;

            // ── Fill ─────────────────────────────────────────────
            var fill = new GameObject("Fill", typeof(Image));
            fill.transform.SetParent(gauge.transform, false);
            var fillRT = (RectTransform)fill.transform;
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.sizeDelta = new Vector2(-2, -2);
            var fillImg = fill.GetComponent<Image>();
            fillImg.color = Color.gray;
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImg.fillAmount = 0f;
            fillImg.raycastTarget = false;

            // ── Label ────────────────────────────────────────────
            var label = new GameObject("Label", typeof(TextMeshProUGUI));
            label.transform.SetParent(gauge.transform, false);
            var labelRT = (RectTransform)label.transform;
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.sizeDelta = Vector2.zero;
            var labelTMP = label.GetComponent<TextMeshProUGUI>();
            labelTMP.text = "- 0/100";
            labelTMP.fontSize = Mathf.Max(6, slider.sizeDelta.y * 0.6f);
            labelTMP.alignment = TextAlignmentOptions.Center;
            labelTMP.color = Color.white;
            labelTMP.raycastTarget = false;

            // ── MonsterHPBar 와이어링 ─────────────────────────────
            var bar = root.GetComponent<MonsterHPBar>();
            if (bar != null)
            {
                var so = new SerializedObject(bar);
                so.FindProperty("_elementFill").objectReferenceValue  = fillImg;
                so.FindProperty("_elementLabel").objectReferenceValue = labelTMP;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning("[Builder] MonsterHPBar 컴포넌트 없음 — 와이어링 건너뜀");
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[Builder] LeeHPBar ElementGauge 빌드 완료");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
