#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 무기 동작 연속기 배정(10-09 사용자 「애니메이션은 겹치는건 줄여야해」 · 「동작이 연결되는게 중요해」 · 「크로스페이드도 한계는있으니」).
/// 사무라이 팩 공격은 연속기 세 줄(Attack01 · 02 · 03)이고, 각 조각은 앞 조각의 「끊는 지점」 자세에서 시작한다.
/// _ALL = 연속기 처음부터 그 조각까지의 누적본이라 대기 자세에서 바로 시작할 수 있는 건 *_1_ALL뿐이다(02_4_ALL = 네 번 베기).
/// 장비 형/27 실측(앞 조각 끝 ↔ 다음 조각 첫 자세 거리 · 0.02 간격)으로 고른 배정 — 한 무기에 한 줄:
///   무형검 = 01_1_ALL → 01_2 → 01_3 (이음 3.6 · 4.6점 — 예전 3타 01_4는 01_3을 건너뛰어 53 cm · 77° 튐) · 무형검 E = 01_4
///   카타나 = 02_1_ALL → 02_2 → 02_3 (4.9 · 0.6 — 예전엔 무형검과 같은 Attack01 계열) · 카타나 E = SPAttack06(예전 = 무형검 R과 같은 QSkill_Iasen)
///   대검   = 03_1_ALL → 03_2 → 03_3 (2.4 · 1.1 — 예전 02_3 → 02_4 → 03_5는 대기→1타 62 · 2→3타 79점, 사용자 10-09 결정)
///   랜슬롯 Q = Attack07 → 02_4 ↔ 02_5 → 03_5 마무리(<see cref="RelicQSlashClipGenerator"/>) · 활 · 석궁 이동 = 활 팩 Bow_Common
/// 연결(attackEndAt) = 끊는 지점. 늦은 입력 허용(comboWindowClose) = 자세 거리 15점 이하가 이어지는 끝 — 그 사이에 누르면
/// ActAttackState가 끊는 지점 자세로 기다렸다가 잇는다. 타격 · 트레일 이벤트는 오른손 속도 봉우리(휘두름)에 다시 넣는다(정규화 시간).
/// 무형검 클립 이벤트는 예전에 초 단위로 들어가(AE_AttackEnd 2.17) 타격이 휘두름보다 늦고 트레일 끄기가 발동하지 않았다 — 함께 바로잡는다.
/// </summary>
public static class WeaponAnimChains
{
    private const string Third    = "Assets/_ThirdParty/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/";
    private const string Imported = "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/APose/Attack/Inplace/";
    private const string Nameless = "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/katana/Nameless/";
    private const string BowThird    = "Assets/_ThirdParty/GhostSamurai_Animset/Animation/Bow/Common/Inplace/";
    private const string BowAttack   = "Assets/RelicFairy/Animations/Player/Test_01/Attack/GhostSamurai_Bow_";
    private const string BowImported = "Assets/RelicFairy/_Imported/GhostSamurai_Animset/Animation/Bow/Common/Inplace/";
    private const string WeaponData  = "Assets/RelicFairy/Weapon/";
    private const string GroupName   = "WeaponAnimation";
    private const string LancelotRelic = "Assets/RelicFairy/Systems/Relic/Data/RelicClass_Lancelot.asset";

    private static string Clip(string n) => "GhostSamurai_APose_" + n + "_Inplace.FBX";

    // _ThirdParty(추적 제외) → _Imported로 옮길 사무라이 클립
    private static readonly string[] MoveIn = { "Attack01_3", "Attack02_1_ALL", "Attack02_5", "Attack03_1_ALL", "Attack03_2", "Attack03_3", "Attack07", "SPAttack06" };

    // 한 칸: 클립 경로 · 주소 · 콤보 단계 · 휘두름(타격 이벤트) · 연결 · 늦은 입력 끝 · 창 열림 · 회수 끝 · 전진 끝
    private readonly struct Step
    {
        public readonly string Path, Address; public readonly int Combo;
        public readonly float Slash, End, Close, Open, Recovery, StepEnd;
        public Step(string path, string address, int combo, float slash, float end, float close, float open, float recovery, float stepEnd)
        { Path = path; Address = address; Combo = combo; Slash = slash; End = end; Close = close; Open = open; Recovery = recovery; StepEnd = stepEnd; }
    }

    private static readonly (string animSet, Step[] steps)[] Chains =
    {
        ("Nameless/Data/NamelessBase/NamelessAnimation.asset", new[]
        {
            new Step(Nameless + Clip("Attack01_1_ALL"), "Nameless_GroundLight_01", 0, 0.08f, 0.24f, 0.30f, 0.04f, 0.75f, 0.08f),
            new Step(Nameless + Clip("Attack01_2"),     "Nameless_GroundLight_02", 1, 0.10f, 0.20f, 0.28f, 0.04f, 0.75f, 0.10f),
            // 3타는 두 번 벤다 — 0.11 · 0.20(10-19 ④ · 둘째 베기는 NamelessFinisherSecondSlash 「두 베기를 초반 두 휘두름으로」가 넣는다 · 이 도구를 다시 돌리면 그것도 다시)
            new Step(Imported + Clip("Attack01_3"),     "Nameless_GroundLight_03", 2, 0.11f, 0.24f, 0.24f, 0.06f, 0.80f, 0.12f),
        }),
        ("Katana/Data/KatanaBase/KatanaAnimation.asset", new[]
        {
            new Step(Imported + Clip("Attack02_1_ALL"), "Katana_GroundLight_01", 0, 0.22f, 0.24f, 0.56f, 0.04f, 0.85f, 0.20f),
            new Step(Imported + Clip("Attack02_2"),     "Katana_GroundLight_02", 1, 0.16f, 0.22f, 0.36f, 0.04f, 0.85f, 0.15f),
            new Step(Imported + Clip("Attack02_3"),     "Katana_GroundLight_03", 2, 0.10f, 0.34f, 0.34f, 0.04f, 0.80f, 0.10f),
        }),
        ("Greatsword/Data/GreatswordBase/GreatswordAnimation.asset", new[]
        {
            new Step(Imported + Clip("Attack03_1_ALL"), "GreatswordAttack_01", 0, 0.18f, 0.22f, 0.62f, 0.06f, 0.80f, 0.16f),
            new Step(Imported + Clip("Attack03_2"),     "GreatswordAttack_02", 1, 0.22f, 0.30f, 0.56f, 0.06f, 0.80f, 0.20f),
            new Step(Imported + Clip("Attack03_3"),     "GreatswordAttack_03", 2, 0.20f, 0.40f, 0.40f, 0.06f, 0.85f, 0.08f),
        }),
    };

    // 석궁 평타(10-10 · 활과 겹치던 평타 1 · 2를 활 팩의 다른 조각으로) — SP01(들어 올려 겨눔) → SP_Enhance(넓게 버티고 겨눔) · 이음 1.5점.
    // 사진(장비 형/30): SP01은 0.15부터 석궁이 수평 — 방아쇠라 0.18에 쏜다. 추천했던 SP02는 쏘고 뒤로 구르는 동작이라 제자리 클립에선 제자리 구르기로 보여 뺐다.
    // 석궁 능력은 0단계 하나뿐이라 두 타 모두 SpawnSlashEffect0(활과 같은 규칙) · 트레일 없음.
    private static readonly Step[] CrossbowSteps =
    {
        new Step(BowAttack + "Shoot_SP01_Inplace.FBX",       "Crossbow_GroundLight_01", 0, 0.18f, 0.38f, 0.50f, 0.04f, 0.70f, 0f),
        new Step(BowAttack + "Shoot_SP_Enhance_Inplace.FBX", "Crossbow_GroundLight_02", 1, 0.05f, 0.25f, 0.25f, 0.04f, 0.50f, 0f),
    };

    // 스킬 칸(이벤트를 비운다 — 스킬 상태에서 평타 타격 · 공격 끝 이벤트가 터지면 안 된다)
    private static readonly (string animSet, string baseClip, string path, string address, int actionType)[] SkillSlots =
    {
        ("Nameless/Data/NamelessBase/NamelessAnimation.asset", "ESkill_01",    Nameless + Clip("Attack01_4"), "Nameless_ESkill", 4),
        ("Katana/Data/KatanaBase/KatanaAnimation.asset",       "QSkill_Iasen", Imported + Clip("SPAttack06"), "Katana_ESkill",   4),
        // 석궁 자기 강화 시전 — 연사(E) = 등에서 꺼내 겨누기 · 다중 사격(R) = 하늘로 겨누기(활 E · R 발사 동작과 겹치던 것)
        ("Crossbow/Data/CrossbowBase/CrossbowAnimation.asset", "ESkill_01", BowAttack + "Shoot_SP_Start_Inplace.FBX",  "Crossbow_ESkill", 4),
        ("Crossbow/Data/CrossbowBase/CrossbowAnimation.asset", "QSkill_01", BowAttack + "RainShoot_Burst_Inplace.FBX", "Crossbow_RSkill", 3),
    };

    // 활 · 석궁 이동 — 맨손 이동(GhostSamurai_Common_*)을 활 든 이동(GhostSamurai_Bow_Common_*)으로
    private static readonly string[] BowLoco =
    {
        "Idle", "StrafeWalkF", "StrafeWalkFL", "StrafeWalkFR", "StrafeWalkL", "StrafeWalkR", "StrafeWalkB", "StrafeWalkBL", "StrafeWalkBR",
        "StrafeRun_F", "StrafeRun_FL", "StrafeRun_FR", "StrafeRun_L", "StrafeRun_R", "StrafeRun_B", "StrafeRun_BL", "StrafeRun_BR",
    };
    private static readonly string[] BowSets = { "Bow/Data/BowBase/BowAnimation.asset", "Crossbow/Data/CrossbowBase/CrossbowAnimation.asset" };

    // 평타 슬래시 이펙트 회전(Z) = 칼끝 휘두름 각 − 180°(10-07 규칙) — 장비 형/8 실측(10-10, 무기마다 6~7회 중앙값).
    // 무형검 3타(01_3)는 두 번 휘두른다 — 10-19 ④에 두 베기를 두 휘두름(0.11 · 0.20)에 걸었다: 첫째 왼쪽 위(126° → −52) · 둘째 오른쪽 위(−142 ~ −150 → −146, 4단계).
    // 카타나 3타(02_3)도 갈렸다(23 / 64) — 23은 이벤트(0.10) 전 넘어오는 움직임이고, 슬래시가 그려지는 동안 칼은 왼쪽 아래로 내려벤다(−116° → 64, 사진 대조).
    private static readonly (string ability, float[] rolls)[] SlashRolls =
    {
        ("Nameless/Data/NamelessBase/NamelessAbility.asset",       new[] { 20f, -160f, -52f, -146f }),
        ("Katana/Data/KatanaBase/KatanaAbility.asset",             new[] { -44f, 79f, 64f }),
        ("Greatsword/Data/GreatswordBase/GreatswordAbility.asset", new[] { 62f, -132f, 0f }),
    };

    // 예전 배정이 쓰던 주소 — 다른 클립이 같은 주소를 갖지 않게 원래(경로)로 되돌린다
    private static readonly string[] Released = { Imported + Clip("Attack02_4"), Imported + Clip("Attack03_5") };

    [MenuItem("RelicFairy/Weapon/무기 동작 연속기 배정 (10-09)")]
    public static void Apply()
    {
        if (!MoveAll()) return;
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var group = settings.FindGroup(GroupName) ?? settings.DefaultGroup;

        foreach (var path in Released) ResetAddress(settings, path);
        foreach (var (animSet, steps) in Chains)
        {
            foreach (var s in steps)
            {
                Readdress(settings, group, s.Path, s.Address);
                SetEvents(s.Path, s.Combo, s.Slash, s.End);
            }
            SetChainMappings(WeaponData + animSet, steps);
        }
        foreach (var s in CrossbowSteps)
        {
            Readdress(settings, group, s.Path, s.Address);
            SetShotEvent(s.Path, s.Slash);
        }
        SetChainMappings(WeaponData + "Crossbow/Data/CrossbowBase/CrossbowAnimation.asset", CrossbowSteps);
        foreach (var (animSet, baseClip, path, address, actionType) in SkillSlots)
        {
            Readdress(settings, group, path, address);
            StripEvents(path);
            SetSkillMapping(WeaponData + animSet, baseClip, address, actionType);
        }
        foreach (var n in BowLoco)
        {
            string path = BowImported + "GhostSamurai_Bow_Common_" + n + "_Inplace.FBX";
            SetLoop(path);
            Readdress(settings, group, path, "Bow_Common_" + n);
        }
        foreach (var set in BowSets) SetBowLocomotion(WeaponData + set);
        SetLancelot();
        ApplySlashRolls();

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, null, true);
        AssetDatabase.SaveAssets();
        RelicQSlashClipGenerator.Execute();   // 랜슬롯 Q 베기 · 마무리 클립과 상태 속도
        Debug.Log("[WeaponAnimChains] 무기 동작 연속기 배정 완료 — 무형검 01 · 카타나 02 · 대검 03 · 랜슬롯 Q 07→02_4↔02_5→03_5 · 석궁 SP01→SP_Enhance · 활 이동");
    }

    // ── 옮기기 ────────────────────────────────────────────────────
    private static bool MoveAll()
    {
        foreach (var n in MoveIn)
            if (!Move(Third + Clip(n), Imported + Clip(n))) return false;
        EnsureFolder(BowImported);
        foreach (var n in BowLoco)
        {
            string file = "GhostSamurai_Bow_Common_" + n + "_Inplace.FBX";
            if (!Move(BowThird + file, BowImported + file)) return false;
        }
        return true;
    }

    private static bool Move(string from, string to)
    {
        if (AssetDatabase.LoadMainAssetAtPath(to) != null) return true;   // 이미 옮겼다
        if (AssetDatabase.LoadMainAssetAtPath(from) == null) { Debug.LogError("[WeaponAnimChains] 원본 없음: " + from); return false; }
        string err = AssetDatabase.MoveAsset(from, to);   // GUID 보존
        if (!string.IsNullOrEmpty(err)) { Debug.LogError($"[WeaponAnimChains] 옮기기 실패 {from}: {err}"); return false; }
        return true;
    }

    private static void EnsureFolder(string folder)
    {
        folder = folder.TrimEnd('/');
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = folder.Substring(0, folder.LastIndexOf('/'));
        EnsureFolder(parent + "/");
        AssetDatabase.CreateFolder(parent, folder.Substring(folder.LastIndexOf('/') + 1));
    }

    // ── 주소 ──────────────────────────────────────────────────────
    private static void Readdress(AddressableAssetSettings settings, AddressableAssetGroup group, string path, string address)
    {
        string guid = AssetDatabase.AssetPathToGUID(path);
        if (string.IsNullOrEmpty(guid)) { Debug.LogWarning("[WeaponAnimChains] 없음: " + path); return; }
        // 같은 주소를 쥔 다른 항목은 경로 주소로 되돌린다(주소 중복 → 엉뚱한 클립이 잡힌다)
        foreach (var g in settings.groups)
        {
            if (g == null) continue;
            foreach (var e in new List<AddressableAssetEntry>(g.entries))
                if (e.address == address && e.guid != guid) { Debug.Log($"[WeaponAnimChains] 주소 풀기 「{address}」 ← {e.AssetPath}"); e.address = e.AssetPath; }
        }
        var entry = settings.FindAssetEntry(guid) ?? settings.CreateOrMoveEntry(guid, group, false, false);
        if (entry.address != address) Debug.Log($"[WeaponAnimChains] {System.IO.Path.GetFileName(path)}: 「{entry.address}」 → 「{address}」");
        entry.address = address;
    }

    private static void ResetAddress(AddressableAssetSettings settings, string path)
    {
        var entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(path));
        if (entry != null && entry.address != entry.AssetPath) { Debug.Log($"[WeaponAnimChains] 주소 풀기 「{entry.address}」 ← {path}"); entry.address = entry.AssetPath; }
    }

    // ── 임포터(이벤트 · 반복) ─────────────────────────────────────
    /// <summary>타격 · 트레일 이벤트를 다시 넣는다(정규화 시간). 트레일 끄기는 연결 지점 전에 — 이어 갈 때도 반드시 꺼진다.</summary>
    private static void SetEvents(string path, int step, float slash, float end)
    {
        if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) { Debug.LogWarning("[WeaponAnimChains] 임포터 없음: " + path); return; }
        var clips = imp.clipAnimations;
        if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;
        if (clips == null || clips.Length == 0) return;
        var c = clips[0];
        var kept = new List<AnimationEvent>();
        foreach (var e in c.events ?? System.Array.Empty<AnimationEvent>())
        {
            if (e.functionName.StartsWith("SpawnSlashEffect") || e.functionName == "AE_BeginTrail" || e.functionName == "AE_EndTrail"
                || e.functionName == "AE_AttackEnd" || e.functionName == "AE_EffectStep") continue;
            kept.Add(e);
        }
        kept.Add(new AnimationEvent { time = Mathf.Max(0f, slash - 0.06f), functionName = "AE_BeginTrail" });
        kept.Add(new AnimationEvent { time = slash, functionName = "SpawnSlashEffect" + step });
        kept.Add(new AnimationEvent { time = Mathf.Min(slash + 0.12f, end - 0.01f), functionName = "AE_EndTrail" });
        kept.Add(new AnimationEvent { time = 0.98f, functionName = "AE_AttackEnd" });
        kept.Sort((a, b) => a.time.CompareTo(b.time));
        c.events = kept.ToArray();
        c.loopTime = false;
        clips[0] = c;
        imp.clipAnimations = clips;
        imp.SaveAndReimport();
        Debug.Log($"[WeaponAnimChains] {System.IO.Path.GetFileName(path)}: SpawnSlashEffect{step} @ {slash:0.00} · 연결 {end:0.00}");
    }

    /// <summary>석궁 평타 — 화살이 나가는 이벤트(SpawnSlashEffect0)와 공격 끝만(활 클립과 같은 규칙 · 트레일 없음).</summary>
    private static void SetShotEvent(string path, float shot)
    {
        if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) { Debug.LogWarning("[WeaponAnimChains] 임포터 없음: " + path); return; }
        var clips = imp.clipAnimations;
        if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;
        if (clips == null || clips.Length == 0) return;
        var c = clips[0];
        c.events = new[]
        {
            new AnimationEvent { time = shot,  functionName = "SpawnSlashEffect0" },
            new AnimationEvent { time = 0.98f, functionName = "AE_AttackEnd" },
        };
        c.loopTime = false;
        clips[0] = c;
        imp.clipAnimations = clips;
        imp.SaveAndReimport();
        Debug.Log($"[WeaponAnimChains] {System.IO.Path.GetFileName(path)}: 쏘기 @ {shot:0.00}");
    }

    private static void StripEvents(string path)
    {
        if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) return;
        var clips = imp.clipAnimations;
        if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;
        if (clips == null || clips.Length == 0 || clips[0].events == null || clips[0].events.Length == 0) return;
        clips[0].events = System.Array.Empty<AnimationEvent>();
        imp.clipAnimations = clips;
        imp.SaveAndReimport();
    }

    private static void SetLoop(string path)
    {
        if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) { Debug.LogWarning("[WeaponAnimChains] 임포터 없음: " + path); return; }
        var clips = imp.clipAnimations;
        if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;
        if (clips == null || clips.Length == 0 || clips[0].loopTime) return;
        clips[0].loopTime = true;
        imp.clipAnimations = clips;
        imp.SaveAndReimport();
    }

    // ── 애니 세트 ─────────────────────────────────────────────────
    private static void SetChainMappings(string animSetPath, Step[] steps)
    {
        var so = AssetDatabase.LoadAssetAtPath<WeaponAnimationSetSO>(animSetPath);
        if (so == null) { Debug.LogWarning("[WeaponAnimChains] 애니 세트 없음: " + animSetPath); return; }
        var o = new SerializedObject(so);
        var groups = o.FindProperty("animGroups");
        for (int g = 0; g < groups.arraySize; g++)
        {
            var arr = groups.GetArrayElementAtIndex(g).FindPropertyRelative("clipMappings");
            for (int i = 0; i < arr.arraySize; i++)
            {
                var m = arr.GetArrayElementAtIndex(i);
                if (!m.FindPropertyRelative("baseClipName").stringValue.StartsWith("GroundLightAttack_")) continue;
                int ci = m.FindPropertyRelative("comboIndex").intValue;
                foreach (var s in steps)
                {
                    if (s.Combo != ci) continue;
                    m.FindPropertyRelative("addressableKey").stringValue    = s.Address;
                    m.FindPropertyRelative("comboWindowOpen").floatValue    = s.Open;
                    m.FindPropertyRelative("comboWindowClose").floatValue   = s.Close;
                    m.FindPropertyRelative("attackEndAt").floatValue        = s.End;
                    m.FindPropertyRelative("recoveryEndAt").floatValue      = s.Recovery;
                    m.FindPropertyRelative("attackStepStartNorm").floatValue = 0.02f;
                    m.FindPropertyRelative("attackStepEndNorm").floatValue  = s.StepEnd;
                }
            }
        }
        o.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(so);
    }

    private static void SetSkillMapping(string animSetPath, string baseClip, string address, int actionType)
    {
        var so = AssetDatabase.LoadAssetAtPath<WeaponAnimationSetSO>(animSetPath);
        if (so == null) { Debug.LogWarning("[WeaponAnimChains] 애니 세트 없음: " + animSetPath); return; }
        var o = new SerializedObject(so);
        var arr = o.FindProperty("animGroups").GetArrayElementAtIndex(0).FindPropertyRelative("clipMappings");
        SerializedProperty slot = null;
        for (int i = 0; i < arr.arraySize && slot == null; i++)
            if (arr.GetArrayElementAtIndex(i).FindPropertyRelative("baseClipName").stringValue == baseClip) slot = arr.GetArrayElementAtIndex(i);
        if (slot == null)
        {
            arr.InsertArrayElementAtIndex(arr.arraySize);
            slot = arr.GetArrayElementAtIndex(arr.arraySize - 1);
            slot.FindPropertyRelative("baseClipName").stringValue = baseClip;
            slot.FindPropertyRelative("comboIndex").intValue = 0;
        }
        slot.FindPropertyRelative("addressableKey").stringValue = address;
        slot.FindPropertyRelative("actionType").intValue = actionType;
        o.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(so);
    }

    private static void SetBowLocomotion(string animSetPath)
    {
        var so = AssetDatabase.LoadAssetAtPath<WeaponAnimationSetSO>(animSetPath);
        if (so == null) { Debug.LogWarning("[WeaponAnimChains] 애니 세트 없음: " + animSetPath); return; }
        var o = new SerializedObject(so);
        var arr = o.FindProperty("animGroups").GetArrayElementAtIndex(0).FindPropertyRelative("clipMappings");
        foreach (var n in BowLoco)
        {
            string baseClip = "GhostSamurai_Common_" + n + "_Inplace";
            SerializedProperty slot = null;
            for (int i = 0; i < arr.arraySize && slot == null; i++)
                if (arr.GetArrayElementAtIndex(i).FindPropertyRelative("baseClipName").stringValue == baseClip) slot = arr.GetArrayElementAtIndex(i);
            if (slot == null)
            {
                arr.InsertArrayElementAtIndex(arr.arraySize);
                slot = arr.GetArrayElementAtIndex(arr.arraySize - 1);
                slot.FindPropertyRelative("baseClipName").stringValue = baseClip;
                slot.FindPropertyRelative("actionType").intValue = 0;
                slot.FindPropertyRelative("comboIndex").intValue = 0;
            }
            slot.FindPropertyRelative("addressableKey").stringValue = "Bow_Common_" + n;
        }
        o.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(so);
    }

    // ── 무형검 이동(10-11 사용자 「기본 이동 애니가 뼈가 안 맞는 듯 부자연스럽다」 → 후보 중 A) ──────────
    // 근접 무기 이동은 사무라이 카타나 양손 자세(APose_*)라 한 손 검에선 왼손이 칼자루 없이 입가에 떴다.
    // 캐릭터 자기 팩(CombatGirls 검과 방패)의 대기 · 걷기 · 달리기로 바꾼다 — 같은 몸에 맞춰 만든 동작이다.
    // 팩에는 앞으로 걷기 · 달리기만 있다 — 앞 · 앞 대각만 바꾸고, 옆 · 뒤(조준 중 잠깐)는 지금 동작을 둔다(앞 동작으로 뒤로 가면 문워크).
    private const string CgAnim = "Assets/CombatGirlsCharacterPack/CombatGirl_Shield/Animations/Normal/";
    private static readonly (string baseClip, string path, string address)[] NamelessLoco =
    {
        ("GhostSamurai_Common_Idle_Inplace",         CgAnim + "SS_Idle.fbx", "SS_Idle"),
        ("GhostSamurai_Common_StrafeWalkF_Inplace",  CgAnim + "SS_Walk.fbx", "SS_Walk"),
        ("GhostSamurai_Common_StrafeWalkFL_Inplace", CgAnim + "SS_Walk.fbx", "SS_Walk"),
        ("GhostSamurai_Common_StrafeWalkFR_Inplace", CgAnim + "SS_Walk.fbx", "SS_Walk"),
        ("GhostSamurai_Common_StrafeRun_F_Inplace",  CgAnim + "SS_Run.fbx",  "SS_Run"),
        ("GhostSamurai_Common_StrafeRun_FL_Inplace", CgAnim + "SS_Run.fbx",  "SS_Run"),
        ("GhostSamurai_Common_StrafeRun_FR_Inplace", CgAnim + "SS_Run.fbx",  "SS_Run"),
    };

    [MenuItem("RelicFairy/Weapon/무형검 이동 = 캐릭터 팩 SS (10-11)")]
    public static void ApplyNamelessLocomotion()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var group = settings.FindGroup(GroupName) ?? settings.DefaultGroup;
        var so = AssetDatabase.LoadAssetAtPath<WeaponAnimationSetSO>(WeaponData + "Nameless/Data/NamelessBase/NamelessAnimation.asset");
        if (so == null) { Debug.LogWarning("[WeaponAnimChains] 무형검 애니 세트 없음"); return; }
        var o = new SerializedObject(so);
        var arr = o.FindProperty("animGroups").GetArrayElementAtIndex(0).FindPropertyRelative("clipMappings");
        foreach (var (baseClip, path, address) in NamelessLoco)
        {
            Readdress(settings, group, path, address);
            SetLoop(path);
            SerializedProperty slot = null;
            for (int i = 0; i < arr.arraySize && slot == null; i++)
                if (arr.GetArrayElementAtIndex(i).FindPropertyRelative("baseClipName").stringValue == baseClip) slot = arr.GetArrayElementAtIndex(i);
            if (slot == null) { Debug.LogWarning("[WeaponAnimChains] 무형검 이동 칸 없음: " + baseClip); continue; }
            slot.FindPropertyRelative("addressableKey").stringValue = address;
        }
        o.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(so);
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, null, true);
        AssetDatabase.SaveAssets();
        Debug.Log("[WeaponAnimChains] 무형검 이동 = SS_Idle · SS_Walk · SS_Run(앞 · 앞 대각) 배정");
    }

    // ── 근접 평타 속도(10-11 사용자 「검 공격이 제한 없이 빠르다」 → 「동작 느리게 + 피해 보전」) ──────────
    // 무형검 · 카타나는 상태 속도 2.5 × 애니 세트 1.2 × 무기 템포(1.6 · 1.5)로 실제 4.5~4.8배속이었다(3타 한 바퀴 0.4초).
    // 애니 세트 재생 보정을 ×0.64로 낮추고(1.2 → 0.77) 평타 칸 피해 계수를 그만큼 올린다(2 → 3.125) — 초당 피해 유지.
    // 대검(1.5배속)과 활 · 석궁은 빠르다는 지적이 없어 그대로. 값은 절대값으로 넣는다(다시 돌려도 같은 결과).
    private static readonly (string animSet, string ability, float animSpeed, float baseDamage)[] MeleeTempo =
    {
        ("Nameless/Data/NamelessBase/NamelessAnimation.asset", "Nameless/Data/NamelessBase/NamelessAbility.asset", 0.77f, 3.125f),
        ("Katana/Data/KatanaBase/KatanaAnimation.asset",       "Katana/Data/KatanaBase/KatanaAbility.asset",       0.77f, 3.125f),
    };

    [MenuItem("RelicFairy/Weapon/근접 평타 속도 — 무형검 · 카타나 ×0.64 · 피해 보전 (10-11)")]
    public static void ApplyMeleeTempo()
    {
        foreach (var (animSet, ability, speed, dmg) in MeleeTempo)
        {
            var set = AssetDatabase.LoadAssetAtPath<WeaponAnimationSetSO>(WeaponData + animSet);
            var ab  = AssetDatabase.LoadAssetAtPath<WeaponAbilitySO>(WeaponData + ability);
            if (set == null || ab == null) { Debug.LogWarning($"[WeaponAnimChains] 없음: {animSet} · {ability}"); continue; }
            var so = new SerializedObject(set);
            so.FindProperty("lightAttackAnimSpeed").floatValue = speed;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(set);

            var ao = new SerializedObject(ab);
            var steps = ao.FindProperty("steps");
            for (int i = 0; i < steps.arraySize; i++)
            {
                var st = steps.GetArrayElementAtIndex(i);
                st.FindPropertyRelative("baseDamage").floatValue = dmg;
                st.FindPropertyRelative("collider").FindPropertyRelative("damage").floatValue = dmg;
            }
            ao.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ab);
            Debug.Log($"[WeaponAnimChains] 근접 평타 속도 {System.IO.Path.GetFileNameWithoutExtension(animSet)}: 재생 {speed} · 피해 계수 {dmg} ({steps.arraySize}칸)");
        }
        AssetDatabase.SaveAssets();
    }

    [MenuItem("RelicFairy/Weapon/무기 동작 연속기 — 슬래시 회전만 (10-10)")]
    public static void ApplySlashRolls()
    {
        foreach (var (ability, rolls) in SlashRolls)
        {
            var so = AssetDatabase.LoadAssetAtPath<WeaponAbilitySO>(WeaponData + ability);
            if (so == null) { Debug.LogWarning("[WeaponAnimChains] 어빌리티 없음: " + ability); continue; }
            var o = new SerializedObject(so);
            var steps = o.FindProperty("steps");
            for (int i = 0; i < steps.arraySize; i++)
            {
                var st = steps.GetArrayElementAtIndex(i);
                int idx = st.FindPropertyRelative("stepIndex").intValue;
                if (idx < 0 || idx >= rolls.Length) continue;
                var rot = st.FindPropertyRelative("effect.rotationEuler");
                rot.vector3Value = new Vector3(rot.vector3Value.x, rot.vector3Value.y, rolls[idx]);
            }
            o.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(so);
            Debug.Log($"[WeaponAnimChains] 슬래시 회전 {System.IO.Path.GetFileNameWithoutExtension(ability)}: {string.Join(" / ", rolls)}");
        }
        AssetDatabase.SaveAssets();
    }

    // ── 랜슬롯 Q ──────────────────────────────────────────────────
    /// <summary>마무리 = 전용 상태(가웨인 Q의 RelicQ_Main과 다른 모션) · 둘째 베기(02_4)는 0.04부터(들어가기 Attack07 끝 자세와 맞는 곳).</summary>
    private static void SetLancelot()
    {
        var so = AssetDatabase.LoadAssetAtPath<RelicClassSO>(LancelotRelic);
        if (so == null) { Debug.LogWarning("[WeaponAnimChains] 랜슬롯 유물 없음"); return; }
        var o = new SerializedObject(so);
        o.FindProperty("qSkillMainState").stringValue = RelicQSlashClipGenerator.FinishState;
        var seq = o.FindProperty("qSkillClipSequence");
        for (int i = 0; i < seq.arraySize; i++)
            seq.GetArrayElementAtIndex(i).FindPropertyRelative("startNormalized").floatValue = i == 1 ? 0.04f : 0f;
        o.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(so);
    }
}
#endif
