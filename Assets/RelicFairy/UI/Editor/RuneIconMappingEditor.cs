using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 룬 문양(<c>Sprites/Runes/Effects</c>)을 <b>기능(effect_type)</b>에 이어 붙인다.
///
/// <para><b>왜 기능 기준인가</b> — 룬은 120종인데 효과는 54종이고, 납품 문양도 정확히 54장이다.
/// 룬마다 다른 그림을 주면 120장이 필요하고 외울 수도 없다. 같은 일을 하는 룬이 같은 문양을 쓰면
/// <b>그림만 보고 무슨 룬인지</b> 알게 된다 — 등급은 테두리·광휘가 따로 말한다.</para>
///
/// <para>아래 표가 그 대응이다. 형태·색이 기능을 연상시키도록 골랐다:
/// 방패=방어, 하트=체력, 날개=이동, 번개=전기, 눈결정=빙결처럼.
/// 전설 18종은 속성색이 뚜렷한 보석 문양을 6속성 × 3역할로 나눠 준다.</para>
/// </summary>
public static class RuneIconMappingEditor
{
    private const string IconFolder  = "Assets/RelicFairy/UI/PuzzleGrid/Sprites/Runes/Effects";
    private const string LibraryPath = "Assets/RelicFairy/UI/PuzzleGrid/RuneArtLibrary.asset";

    /// <summary>effect_type → 문양 파일 이름(확장자 제외).</summary>
    private static readonly (string Effect, string Icon)[] Map =
    {
        // ── 기본 스탯 (Common~Epic) — 형태가 곧 뜻이다 ──
        ("AllDamage",              "송곳니룬"),      // 공격력 — 물어뜯는 이빨
        ("MaxHP",                  "하트룬"),        // 최대 체력
        ("Defense",                "방패룬"),        // 방어력
        ("CritChance",             "네잎클로버룬"),  // 치명타 확률 — 운
        ("CritDamage",             "십자표창룬"),    // 치명타 피해 — 꽂히는 날
        ("SkillDamage",            "마름모룬"),
        ("MaxHPPercent",           "동글룬"),
        ("MoveSpeed",              "날개룬"),        // 이동속도
        ("DefensePercent",         "방패동글룬"),
        ("AttackSpeed",            "갈퀴룬"),        // 연타
        ("SkillCooldownReduction", "도넛룬"),        // 순환 = 쿨타임

        // ── 조건부 (Common~Epic) ──
        ("CondAllDamage",          "뾰족 룬"),
        ("CondDefensePercent",     "사각사각룬"),
        ("CondAttackSpeed",        "동글뾰족룬"),
        ("CondCritChance",         "세잎클로버"),
        ("CondMoveSpeed",          "반달돌칼 룬"),
        ("CondMaxHpPercent",       "호떡룬"),
        ("FirstHitBonus",          "삼각룬"),        // 첫 일격
        ("CondCritDamage",         "뾰족마름모룬"),
        ("CondSkillDamage",        "오각룬"),

        // ── Epic 특수 효과 ──
        ("PoisonOnHit",            "꽃룬"),          // 독/화상 — 피어나는 것
        ("DamageReflect",          "결정룬"),        // 되돌려 보냄
        ("ExtraAttack",            "도끼머리룬"),    // 추가타
        ("Freeze",                 "눈결정룬"),      // 빙결
        ("DamageReduction",        "동글마름 룬"),
        ("ProjectilePierce",       "달칼날룬"),      // 관통
        ("Stun",                   "고슴도치룬"),    // 기절 — 충격
        ("DefenseOnHit",           "동글뱅이룬"),
        ("ProjectileCount",        "피라미드 룬"),
        ("HPRegenOnHit",           "핑크눈물 룬"),   // 회복 — 눈물
        ("DebuffDuration",         "소용돌이룬"),
        ("HealingReceived",        "골든눈물 룬"),   // 받는 회복
        ("HPRegenOnClear",         "눈룬"),
        ("FirstAttackAfterRoll",   "나침반룬"),      // 구르기 직후 — 방향
        ("DebuffResistance",       "에메랄드 룬"),
        ("RollDistance",           "무지개룬"),      // 도약

        // ── Legendary 18 — 속성 6 × 역할 3(광역/단일/투사체) ──
        // 색이 속성을 말하는 보석 문양을 우선 배정한다.
        ("FireLegendAoe",          "불룬"),
        ("FireLegendSingle",       "레이어 6"),      // 빨강 각진 보석
        ("FireLegendProjectile",   "레이어 14"),     // 빨강 오각 보석

        ("IceLegendAoe",           "레이어 8"),      // 하늘 물방울
        ("IceLegendSingle",        "레이어 5"),      // 파랑 팔각별
        ("IceLegendField",         "하늘룬"),

        ("ElecLegendAoe",          "번개룬"),
        ("ElecLegendSingle",       "레이어 10"),     // 노랑 팔각
        ("ElecLegendProjectile",   "레이어 7"),      // 금색 태양

        ("GrassLegendAoe",         "레이어 12"),     // 초록 네잎
        ("GrassLegendSingle",      "하투룬"),
        ("GrassLegendProjectile",  "주황룬"),

        ("LightLegendAoe",         "태양룬"),
        ("LightLegendSingle",      "레이어 11"),     // 흰 육각 기둥
        ("LightLegendProjectile",  "흰룬"),

        ("DarkLegendAoe",          "흑색룬"),
        ("DarkLegendSingle",       "레이어 4"),      // 보라 초승달
        ("DarkLegendProjectile",   "방패 룬"),
    };

    [MenuItem("RelicFairy/UI/룬 문양 — 임포트 + 기능 매칭")]
    private static void Run()
    {
        int sprited = MakeSprites();
        int linked  = LinkToLibrary();
        Debug.Log($"[룬 문양] 스프라이트 전환 {sprited}장 · 기능 연결 {linked}/{Map.Length}");
    }

    /// <summary>폴더의 PNG를 전부 Sprite로 바꾼다. 이미 Sprite면 건너뛴다.</summary>
    private static int MakeSprites()
    {
        int n = 0;
        foreach (var path in Directory.GetFiles(IconFolder, "*.png"))
        {
            string p = path.Replace('\\', '/');
            if (AssetImporter.GetAtPath(p) is not TextureImporter ti) continue;
            if (ti.textureType == TextureImporterType.Sprite) continue;

            ti.textureType         = TextureImporterType.Sprite;
            ti.spriteImportMode    = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled       = false;
            ti.SaveAndReimport();
            n++;
        }
        return n;
    }

    /// <summary>표대로 라이브러리의 effectIcons를 채운다. 없는 파일은 건너뛰고 경고한다.</summary>
    private static int LinkToLibrary()
    {
        var lib = AssetDatabase.LoadAssetAtPath<RuneArtLibrarySO>(LibraryPath);
        if (lib == null) { Debug.LogError($"[룬 문양] 라이브러리 없음: {LibraryPath}"); return 0; }

        var list = new List<RuneArtLibrarySO.EffectIcon>(Map.Length);
        var missing = new List<string>();

        foreach (var (effect, icon) in Map)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{IconFolder}/{icon}.png");
            if (sprite == null) { missing.Add($"{effect} ← {icon}"); continue; }
            list.Add(new RuneArtLibrarySO.EffectIcon { effectType = effect, icon = sprite });
        }

        var so = new SerializedObject(lib);
        var arr = so.FindProperty("effectIcons");
        arr.arraySize = list.Count;
        for (int i = 0; i < list.Count; i++)
        {
            var e = arr.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("effectType").stringValue    = list[i].effectType;
            e.FindPropertyRelative("icon").objectReferenceValue = list[i].icon;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();

        if (missing.Count > 0)
            Debug.LogWarning($"[룬 문양] 파일을 못 찾은 {missing.Count}건:\n  " + string.Join("\n  ", missing));
        return list.Count;
    }
}
