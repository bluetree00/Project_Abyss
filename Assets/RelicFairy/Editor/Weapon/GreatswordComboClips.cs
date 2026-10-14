#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 대검 지상 3연타 = 사무라이 팩 「Attack02_3 · 02_4 · 03_5」(사용자 결정 10-07 오후).
/// - Grruzam 7연속 콤보(오전 결정)는 UE 골격 리타깃이라 이 캐릭터에서 뼈가 깨졌다 → 사무라이 대검으로 되돌리되
///   「무형검 · 카타나와 겹치면 안 된다」: 기본 ESkill_01 클립(무형검 · 카타나 E 스킬이 쓴다) 내용이 Attack02_2라서 02_2는 빼고
///   이어지는 02_3 · 02_4를 1 · 2타로 쓴다(카타나 = Attack01_1~3 · 무형검 = Attack01_1 · 01_2 · 01_4).
/// - 3타: 02_5는 찌르기였다 — 사용자 「마지막 타는 휘두르는 동작」 → 03_5(칼을 머리 뒤로 들었다가 몸을 낮추며 크게 내려벤다).
///   비교: 02_6 · 03_2 · 03_3 · 03_4 · 03_5 미리보기(실측 메뉴 9) — 03_3 · 03_4는 칼을 앞으로 뻗은 자세가 길어 찌르기에 가깝고 02_6은 이동이 크다.
/// - _ThirdParty(추적 제외)에 있던 클립은 _Imported로 옮긴다(AssetDatabase.MoveAsset — GUID 보존). 쓰지 않게 된 02_5는 주소를 지우고 되돌려 놓는다.
/// - 슬래시 이벤트는 단계 번호로 다시 매긴다(02_3 → SpawnSlashEffect0 · 02_4 → 1 · 03_5 → 2). 시점은 SlashNt(클립 초).
/// - 슬래시 회전(Z) = 칼끝 이동 각 − 180°(실측 8 · 8b 규칙 — 프리팹은 회전 0°에서 오른쪽 → 왼쪽으로 쓴다).
/// </summary>
public static class GreatswordComboClips
{
    private const string Grruzam  = "Assets/RelicFairy/_Imported/Grruzam Powerful Sword Animation(Great Sword, Katana)/Animation/M_Big_Sword/2_Attacks/2__7Combos/";
    private const string Imported = "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/";
    private const string Third    = "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/";
    private const string Ability  = "Assets/RelicFairy/Weapon/Greatsword/Data/GreatswordBase/GreatswordAbility.asset";
    private static readonly string[] Combo = { "GhostSamurai_APose_Attack02_3_Inplace.FBX", "GhostSamurai_APose_Attack02_4_Inplace.FBX", "GhostSamurai_APose_Attack03_5_Inplace.FBX" };
    private const string Dropped = "GhostSamurai_APose_Attack02_5_Inplace.FBX";   // 찌르기라 뺀 3타(10-07)
    // 슬래시 이벤트 자리(정규화) — 음수면 클립에 있던 슬래시 시점을 그대로(없으면 0.2). 실측 8로 칼끝 휘두름 구간을 잰 뒤 채운다.
    // 실측 8c 2회차: 클립 속 실제 휘두름(진행) 02_3 = 0.08~0.10 · 02_4 = 0.11~0.13 — 이벤트(0.15)가 늦었다. 이벤트 time은 클립 초(진행 × 길이):
    // 02_3 0.31초 → 진행 0.15, 02_4 0.29초 → 0.15로 쟀다(길이 2.07 · 1.93초). 02_5(마지막 타 · 회수까지 봄) 휘두름 = 0.30~0.35(이 클립은 이벤트 초 = 진행).
    // 3회차: 1타 이벤트 0.09 ↔ 휘두름 0.08~0.09 · 2타 0.12 ↔ 0.11~0.13 — 일치.
    // 3타 03_5 큰 움직임(실측 8c 6회차): 0.10 앞으로 내밀기 · 0.14 뒤로 당기기(예비) · 0.34 왼쪽 가로베기 2.4 m = 베기. 이 클립은 이벤트 time = 진행.
    private static readonly float[] SlashNt   = { 0.19f, 0.23f, 0.34f };
    // 칼끝 이동 각(몸 기준, 등 뒤 시점) 1타 −110° · 2타 −57° · 3타 −157° → 회전 = 각 − 180°
    private static readonly float[] SlashRoll = { 70f, 123f, -16f };   // 3타 칼끝 164° − 180°
    // 3타 연결 지점: 베기(0.34)가 기본 연결(0.35)과 붙어 연타하면 휘두르다 끊긴다 → 0.50으로 미룬다(창 0.40~0.50). 1 · 2타는 베기 0.09 · 0.12라 그대로.
    private static readonly (int comboIndex, float open, float close, float end)[] Timings = { (2, 0.40f, 0.50f, 0.50f) };
    private const string AnimSet = "Assets/RelicFairy/Weapon/Greatsword/Data/GreatswordBase/GreatswordAnimation.asset";

    // 10-09: 대검 콤보는 03_1_ALL → 03_2 → 03_3으로 바뀌었다(WeaponAnimChains — 02_4 → 03_5가 52 cm 튀었다).
    // 이 메뉴를 다시 돌리면 예전 배정으로 되돌리고 02_5(이제 랜슬롯 Q 베기)를 _ThirdParty로 돌려 빌드에서 빠진다 — 막는다.
    private static readonly bool Replaced = true;

    [MenuItem("RelicFairy/Weapon/Greatsword Ground Combo = Samurai 02_3 · 02_4 · 03_5 (10-09 대체됨)")]
    public static void Apply()
    {
        if (Replaced) { Debug.LogWarning("[GreatswordComboClips] 대체됨 — RelicFairy/Weapon/무기 동작 연속기 배정 (10-09)을 쓴다"); return; }
        foreach (var name in Combo)
        {
            if (AssetDatabase.LoadMainAssetAtPath(Imported + name) != null || AssetDatabase.LoadMainAssetAtPath(Third + name) == null) continue;
            string err = AssetDatabase.MoveAsset(Third + name, Imported + name);
            if (!string.IsNullOrEmpty(err)) { Debug.LogWarning($"[GreatswordComboClips] {name} 옮기기 실패: " + err); return; }
        }

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        // 뺀 02_5: 주소를 지우고 원래 자리(_ThirdParty)로 되돌린다 — 추적 폴더에 쓰지 않는 원본을 남기지 않는다
        string droppedGuid = AssetDatabase.AssetPathToGUID(Imported + Dropped);
        if (!string.IsNullOrEmpty(droppedGuid))
        {
            settings.RemoveAssetEntry(droppedGuid);
            string err = AssetDatabase.MoveAsset(Imported + Dropped, Third + Dropped);
            if (!string.IsNullOrEmpty(err)) Debug.LogWarning("[GreatswordComboClips] 02_5 되돌리기 실패: " + err);
        }
        var group = settings.FindGroup("WeaponAnimation") ?? settings.DefaultGroup;
        for (int i = 1; i <= 3; i++)   // Grruzam은 원래 주소(경로)로 — 키를 먼저 비운다
            Readdress(settings, group, Grruzam + $"M_Big_Sword@Attack_7Combo_{i}.FBX", Grruzam + $"M_Big_Sword@Attack_7Combo_{i}.FBX");
        for (int i = 0; i < Combo.Length; i++)
        {
            Readdress(settings, group, Imported + Combo[i], $"GreatswordAttack_0{i + 1}");
            SetSlashEvent(Imported + Combo[i], i, SlashNt[i]);
        }
        SetSlashRolls();
        SetTimings();
        AssetDatabase.SaveAssets();
        Debug.Log("[GreatswordComboClips] 대검 지상 3연타 = 사무라이 Attack02_3 · 02_4 · 03_5");
    }

    /// <summary>슬래시 · 트레일 이벤트를 단계 번호로 다시 넣는다(정규화 시간 — ModelImporter 이벤트 time은 0~1). AE_AttackEnd 등 나머지는 둔다.</summary>
    private static void SetSlashEvent(string path, int step, float slashNt)
    {
        if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) { Debug.LogWarning("[GreatswordComboClips] 임포터 없음: " + path); return; }
        var clips = imp.clipAnimations;
        if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;
        if (clips == null || clips.Length == 0) return;
        var c = clips[0];
        var kept = new List<AnimationEvent>();
        float existing = -1f;
        foreach (var e in c.events ?? System.Array.Empty<AnimationEvent>())
        {
            if (e.functionName.StartsWith("SpawnSlashEffect")) { existing = e.time; continue; }
            if (e.functionName == "AE_BeginTrail" || e.functionName == "AE_EndTrail") continue;
            kept.Add(e);
        }
        float t = slashNt >= 0f ? slashNt : existing >= 0f ? existing : 0.2f;
        kept.Add(new AnimationEvent { time = Mathf.Max(0f, t - 0.06f), functionName = "AE_BeginTrail" });
        kept.Add(new AnimationEvent { time = t, functionName = "SpawnSlashEffect" + step });
        kept.Add(new AnimationEvent { time = Mathf.Min(0.99f, t + 0.25f), functionName = "AE_EndTrail" });
        if (!kept.Exists(e => e.functionName == "AE_AttackEnd")) kept.Add(new AnimationEvent { time = 0.98f, functionName = "AE_AttackEnd" });
        kept.Sort((a, b) => a.time.CompareTo(b.time));
        c.events = kept.ToArray();
        clips[0] = c;
        imp.clipAnimations = clips;
        imp.SaveAndReimport();
        Debug.Log($"[GreatswordComboClips] {System.IO.Path.GetFileName(path)}: SpawnSlashEffect{step} @ {t:0.00}");
    }

    private static void SetSlashRolls()
    {
        var so = AssetDatabase.LoadAssetAtPath<WeaponAbilitySO>(Ability);
        if (so == null) { Debug.LogWarning("[GreatswordComboClips] 어빌리티 없음: " + Ability); return; }
        var o = new SerializedObject(so);
        var steps = o.FindProperty("steps");
        for (int i = 0; i < steps.arraySize; i++)
        {
            var s = steps.GetArrayElementAtIndex(i);
            int idx = s.FindPropertyRelative("stepIndex").intValue;
            if (idx < 0 || idx >= SlashRoll.Length || float.IsNaN(SlashRoll[idx])) continue;
            s.FindPropertyRelative("effect.rotationEuler").vector3Value = new Vector3(0f, 0f, SlashRoll[idx]);
        }
        o.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(so);
    }

    private static void SetTimings()
    {
        var so = AssetDatabase.LoadAssetAtPath<WeaponAnimationSetSO>(AnimSet);
        if (so == null) { Debug.LogWarning("[GreatswordComboClips] 애니 세트 없음: " + AnimSet); return; }
        var o = new SerializedObject(so);
        // animGroups[].clipMappings[] 중 지상 평타(GroundLightAttack_0N) · comboIndex가 맞는 칸만 고친다
        var groups = o.FindProperty("animGroups");
        for (int g = 0; groups != null && g < groups.arraySize; g++)
        {
            var arr = groups.GetArrayElementAtIndex(g).FindPropertyRelative("clipMappings");
            for (int i = 0; arr != null && i < arr.arraySize; i++)
            {
                var m = arr.GetArrayElementAtIndex(i);
                var name = m.FindPropertyRelative("baseClipName");
                if (name == null || !name.stringValue.StartsWith("GroundLightAttack_")) continue;
                int ci = m.FindPropertyRelative("comboIndex").intValue;
                foreach (var t in Timings)
                {
                    if (t.comboIndex != ci) continue;
                    m.FindPropertyRelative("comboWindowOpen").floatValue  = t.open;
                    m.FindPropertyRelative("comboWindowClose").floatValue = t.close;
                    m.FindPropertyRelative("attackEndAt").floatValue      = t.end;
                    Debug.Log($"[GreatswordComboClips] {name.stringValue}: 창 {t.open:0.00}~{t.close:0.00} · 연결 {t.end:0.00}");
                }
            }
        }
        o.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(so);
    }

    private static void Readdress(AddressableAssetSettings settings, AddressableAssetGroup group, string path, string address)
    {
        string guid = AssetDatabase.AssetPathToGUID(path);
        if (string.IsNullOrEmpty(guid)) { Debug.LogWarning("[GreatswordComboClips] 없음: " + path); return; }
        var entry = settings.FindAssetEntry(guid) ?? settings.CreateOrMoveEntry(guid, group, false, false);
        if (entry.address == address) return;
        Debug.Log($"[GreatswordComboClips] {System.IO.Path.GetFileName(path)}: 「{entry.address}」 → 「{address}」");
        entry.address = address;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
    }
}
#endif
