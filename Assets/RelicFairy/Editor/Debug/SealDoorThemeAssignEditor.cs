#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-05 챕터 방 봉인 문 테마 배정 — 팔레트마다 실제 문 모델(Gothic_Interior)을 꽂는다(사용자 10-02 「문도 제대로 된 오브젝트로 — 챕터방들」).
/// 문 세우기는 <see cref="SealDoorFit"/>(돌려 세움 · 균등 배율 · 좁으면 쌍문). 나무문 = Swing(경첩 젖힘, 10-06) · 쇠창살 = Drop(내리닫이) — 숲의 Grow(뿌리 자람)는 문이라 맞지 않아 바꾼다.
/// 배정 표는 아래 한 곳. 결과: Temp/seal_door_assign.txt · 로그 「[DoorAssign] 끝」.
/// </summary>
public static class SealDoorThemeAssignEditor
{
    private const string Pal    = "Assets/RelicFairy/Systems/Stage/MapGen/Data/Palettes/";
    private const string Gothic = "Assets/RelicFairy/_Imported/Gothic_Interior/Environment/Asset/Prefabs/";

    // 팔레트 → 문(테마): 숲 = 아치 나무 쌍문 · 동굴(Ch2) = 쇠창살 문 · 성채(Ch3) = 장식 고딕 문(쌍) · 왕좌(Ch4) = 아치 쇠창살 문 · 심연 = 부서진 문
    private static readonly (string palette, string door, SealDoorMotion motion)[] Table =
    {
        ("BlockPalette_Forest", "SM_Door_01a",              SealDoorMotion.Swing),
        ("BlockPalette_Cave",   "SM_BarMetalDoor_01a",      SealDoorMotion.Drop),
        ("BlockPalette_Castle", "SM_Door_03b",              SealDoorMotion.Swing),
        ("BlockPalette_Throne", "SM_BarMetalFrameDoor_01a", SealDoorMotion.Drop),
        ("BlockPalette_Abyss",  "SM_Door_Broken_01a",       SealDoorMotion.Swing),
    };

    [MenuItem("RelicFairy/Setup/10-05 봉인 문 테마 배정")]
    private static void Run()
    {
        var sb = new StringBuilder("봉인 문 테마 배정\n");
        foreach (var (palette, doorName, motion) in Table)
        {
            var pal  = AssetDatabase.LoadAssetAtPath<BlockPalette>(Pal + palette + ".asset");
            var door = AssetDatabase.LoadAssetAtPath<GameObject>(Gothic + doorName + ".prefab");
            if (pal == null || door == null) { sb.AppendLine($"{palette}: 없음(팔레트 {pal != null} · 문 {door != null})"); continue; }

            var so = new SerializedObject(pal);
            var pDoor   = so.FindProperty("sealDoorPrefab");
            var pMotion = so.FindProperty("sealDoorMotion");
            string before = pDoor.objectReferenceValue != null ? pDoor.objectReferenceValue.name : "-";
            string beforeMotion = pMotion.enumNames[pMotion.enumValueIndex];
            pDoor.objectReferenceValue = door;
            pMotion.enumValueIndex = (int)motion;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pal);
            sb.AppendLine($"{palette}: {before}({beforeMotion}) → {door.name}({motion})");
        }
        AssetDatabase.SaveAssets();
        File.WriteAllText("Temp/seal_door_assign.txt", sb.ToString());
        Debug.Log("[DoorAssign] 끝\n" + sb);
    }
}
#endif
