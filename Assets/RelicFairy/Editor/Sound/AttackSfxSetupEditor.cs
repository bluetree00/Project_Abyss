#if UNITY_EDITOR
using RelicFairy.Monster;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 10-29 사용자 「박쥐 · 흑기사 공격음은 있는 내용 중 최선으로」 — 있는 클립 중에서 고른 공격음을 두 몬스터 SO에 넣는다(편집 모드 · 한 번).
///   흑기사 = 기사 보스 Slash2(정점 0.59초 — 차오르는 휘두름이 맞는 순간에 터진다) · 베기 1.0 · 찌르기 1.1 · 내려치기 0.85(무겁게) · 볼륨 0.6.
///     Slash1은 플레이어 평타 휘두름(sfx_player_swing)과 파형 상관 0.43 · 대역 세기 3 dB 안 — 내 휘두름과 헷갈려 뺐다.
///   박쥐 = Malbers 「Wing Flap 02」(작은 날개 — 300 Hz 아래가 주가 아닌 유일한 날갯짓 · 나머지는 저음 쿵) · 피치 1.15 · 볼륨 1.
/// </summary>
public static class AttackSfxSetupEditor
{
    private const string Mon        = "Assets/RelicFairy/Characters/Monster/Monster/";
    private const string KnightSO   = Mon + "BlackKnight/SO/BlackKnightPatternAttackOverride.asset";
    private const string BatSO      = Mon + "FairyBat/SO/FairyBatDualAttackOverride.asset";
    private const string KnightClip = Mon + "DeathKnightBoss/Art/Sound/Slash2.mp3";
    private const string BatClip    = "Assets/RelicFairy/_Imported/Malbers Animations/Common/Audio/Wings/Wing Flap 02.wav";
    private const float  Slash2Peak = 0.59f;

    [MenuItem("RelicFairy/Monster/10-29 박쥐 · 흑기사 공격음 넣기 (Edit)")]
    private static void Apply()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[AttackSfx] 편집 모드에서"); return; }
        var slash = AssetDatabase.LoadAssetAtPath<AudioClip>(KnightClip);
        var flap  = AssetDatabase.LoadAssetAtPath<AudioClip>(BatClip);
        var knight = AssetDatabase.LoadAssetAtPath<PatternAttackOverrideSO>(KnightSO);
        var bat    = AssetDatabase.LoadAssetAtPath<FairyBatDualAttackOverrideSO>(BatSO);
        if (slash == null || flap == null || knight == null || bat == null)
        {
            Debug.LogError($"[AttackSfx] 없음 — Slash2 {slash != null} · 날갯짓 {flap != null} · 흑기사 SO {knight != null} · 박쥐 SO {bat != null}");
            return;
        }

        var so = new SerializedObject(knight);
        var arr = so.FindProperty("patterns");
        var sb = new System.Text.StringBuilder("[AttackSfx] 넣음\n");
        for (int i = 0; i < arr.arraySize; i++)
        {
            var el = arr.GetArrayElementAtIndex(i);
            string name = el.FindPropertyRelative("name").stringValue;
            float pitch = name == "Thrust" ? 1.1f : name == "Overhead" ? 0.85f : 1f;
            el.FindPropertyRelative("castSfx").objectReferenceValue = slash;
            el.FindPropertyRelative("castSfxVolume").floatValue     = 0.6f;
            el.FindPropertyRelative("castSfxPitch").floatValue      = pitch;
            el.FindPropertyRelative("castSfxPeakTime").floatValue   = Slash2Peak;
            sb.AppendLine($"  흑기사 {name} — Slash2 · 피치 {pitch} · 볼륨 0.6 · 정점 {Slash2Peak}");
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        var sb2 = new SerializedObject(bat);
        sb2.FindProperty("windupSfx").objectReferenceValue = flap;
        sb2.FindProperty("windupSfxVolume").floatValue     = 1f;
        sb2.FindProperty("windupSfxPitch").floatValue      = 1.15f;
        sb2.ApplyModifiedPropertiesWithoutUndo();
        sb.AppendLine("  박쥐 — Wing Flap 02 · 피치 1.15 · 볼륨 1");

        AssetDatabase.SaveAssets();
        Debug.Log(sb.ToString());
    }
}
#endif
