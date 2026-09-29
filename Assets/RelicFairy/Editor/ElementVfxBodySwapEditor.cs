using UnityEditor;
using UnityEngine;

/// <summary>
/// 속성 상태이상 "몸 이펙트"(<see cref="ElementVfxRegistry"/> statusBody)를 파티클형 _Imported 프리팹으로 바꾼다.
///
/// 원래 값은 INab Character Effects(VFX Graph) 5종이었다(불 Burning 1 · 얼음 Ice · 독 Poison · 빛 Holy · 어둠 Dark).
/// 이 이펙트는 캐릭터에 CharacterEffect 컴포넌트를 붙여 메시를 굽고 재생 이벤트를 보내야 돈다.
/// ElementVfxPlayer는 프리팹을 띄워 위치만 따라가게 하므로 게임에서 아무것도 보이지 않았다(09-17 근접 촬영).
/// 전기는 칸이 비어 바닥 룬 원(Aura_Storm)으로 대체돼, 감전된 적이 몰리면 플레이어 주변이 격자 원통처럼 보였다.
///
/// 되돌리기: 메뉴 「…/원래 값(INab)으로 되돌리기」.
/// </summary>
public static class ElementVfxBodySwapEditor
{
    private const string RegistryPath = "Assets/RelicFairy/Systems/Combat/ElementVfxRegistry.asset";
    private const string E  = "Assets/RelicFairy/_Imported/EffectSource/";
    private const string MP = E + "Magic Pig Games (Infinity PBR)/Shared Files/Magic Spells & Particles/";
    private const string SB = E + "Spells Pack/LWRP(URP)/Particles_LWRP/Prefabs/Buffs/";
    private const string IN = E + "INab Studio/Vfx Assets/Character Effects/Effect Prefabs/";
    private const string HR = E + "Hovl Studio/RPG VFX Bundle/Prefabs/Magic buffs and hits/";

    private static readonly (RuneElement element, string path)[] Particle =
    {
        (RuneElement.Fire,     MP + "Particles/Particle Flame.prefab"),
        (RuneElement.Ice,      SB + "Buff_Ice_LWRP.prefab"),
        (RuneElement.Electric, MP + "Particles/Particle Electric Ball.prefab"),
        (RuneElement.Grass,    SB + "Buff_Nature_LWRP.prefab"),
        (RuneElement.Light,    SB + "Buff_Light_LWRP.prefab"),
        // 어둠: Rising Skull은 짧게 끊겨 나와 대부분의 순간 비어 있었다(0.8초 시점 0개) → 계속 도는 어두운 연기 소용돌이.
        (RuneElement.Dark,     HR + "Debuff 1.prefab"),
    };

    private static readonly (RuneElement element, string path)[] Original =
    {
        (RuneElement.Fire,     IN + "Burning 1.prefab"),
        (RuneElement.Ice,      IN + "Ice.prefab"),
        (RuneElement.Electric, null),
        (RuneElement.Grass,    IN + "Poison.prefab"),
        (RuneElement.Light,    IN + "Holy.prefab"),
        (RuneElement.Dark,     IN + "Dark.prefab"),
    };

    [MenuItem("RelicFairy/Combat/속성 몸 이펙트/파티클형으로 교체")]
    private static void SwapToParticles() => Apply(Particle);

    [MenuItem("RelicFairy/Combat/속성 몸 이펙트/원래 값(INab)으로 되돌리기")]
    private static void RevertToOriginal() => Apply(Original);

    private static void Apply((RuneElement element, string path)[] table)
    {
        var registry = AssetDatabase.LoadAssetAtPath<ElementVfxRegistry>(RegistryPath);
        if (registry == null) { Debug.LogError("[ElementVfxBody] 레지스트리 없음: " + RegistryPath); return; }

        var so      = new SerializedObject(registry);
        var entries = so.FindProperty("entries");
        int changed = 0;
        foreach (var (element, path) in table)
        {
            var prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!string.IsNullOrEmpty(path) && prefab == null) { Debug.LogError("[ElementVfxBody] 프리팹 없음: " + path); continue; }

            for (int i = 0; i < entries.arraySize; i++)
            {
                var e = entries.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("element").enumValueIndex != (int)element) continue;
                e.FindPropertyRelative("statusBody").objectReferenceValue = prefab;
                changed++;
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ElementVfxBody] 몸 이펙트 {changed}칸 갱신");
    }
}
