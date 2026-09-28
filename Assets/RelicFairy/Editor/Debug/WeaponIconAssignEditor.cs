using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 무기 아이콘(진화 단계별) 임포트 + 연결. 한 번만 쓰는 정리 도구다.
///
/// 진화 단계(T1·T2·T3)는 실제로 <b>모델이 서로 다른데</b> 아이콘은 종류당 1장을 셋이 공유하고 있었다
/// → HUD에서 "장비가 바뀐다"가 전혀 안 보였다(09-21 사용자 지적). 단계별 렌더로 만든 그림을 각 WeaponSO에 붙인다.
/// 그림은 `UI/HUD/Sprites/WeaponIcons/T{n}_{무기}_Icon.png`(실제 무기 모델 렌더 + 옅은 단계 테두리).
/// </summary>
public static class WeaponIconAssignEditor
{
    private const string IconDir = "Assets/RelicFairy/UI/HUD/Sprites/WeaponIcons";
    private static readonly string[] Weapons = { "Bow", "Crossbow", "Katana", "Greatsword" };

    [MenuItem("RelicFairy/Debug/무기 아이콘 단계별 연결")]
    private static void Assign()
    {
        AssetDatabase.Refresh();

        int imported = 0, linked = 0, missing = 0;
        foreach (string w in Weapons)
        {
            for (int t = 1; t <= 3; t++)
            {
                string png = $"{IconDir}/T{t}_{w}_Icon.png";
                if (!File.Exists(png)) { Debug.LogWarning($"[무기아이콘] 그림 없음: {png}"); missing++; continue; }

                // PNG는 기본 임포트가 Sprite가 아닐 수 있다 — UI에 쓰려면 Sprite여야 한다.
                var imp = AssetImporter.GetAtPath(png) as TextureImporter;
                if (imp != null && (imp.textureType != TextureImporterType.Sprite || imp.mipmapEnabled))
                {
                    imp.textureType      = TextureImporterType.Sprite;
                    imp.spriteImportMode = SpriteImportMode.Single;
                    imp.mipmapEnabled    = false;
                    imp.wrapMode         = TextureWrapMode.Clamp;
                    imp.alphaIsTransparency = true;
                    imp.SaveAndReimport();
                    imported++;
                }

                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(png);
                string soPath = $"Assets/RelicFairy/Weapon/{w}/Data/T{t}_{w}.asset";
                var so = AssetDatabase.LoadAssetAtPath<WeaponSO>(soPath);
                if (sprite == null || so == null)
                {
                    Debug.LogWarning($"[무기아이콘] 연결 실패 — sprite={(sprite != null)} so={(so != null)} ({soPath})");
                    missing++;
                    continue;
                }

                var sp = new SerializedObject(so);
                sp.FindProperty("icon").objectReferenceValue = sprite;
                sp.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(so);
                linked++;
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[무기아이콘] 임포트 설정 {imported}건 · 연결 {linked}건 · 실패 {missing}건");
    }
}
