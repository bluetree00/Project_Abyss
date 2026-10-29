#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 10-13 힉스필드 그림 반입 — 사용자 「힉스필드로 필수 UI들을 제작해서 넣고 검증하자」.
/// <c>_Imported/Higgsfield/</c>에 둔 그림의 가져오기 설정을 프롤로그 그림과 같게 맞추고(스프라이트 · 밉맵 끔 · 2048 · 보통 압축),
/// 대사 · 장면이 부르는 주소를 달고(기본 로컬 그룹 — 프롤로그 그림과 같은 곳), 유물 초상 · 문장을 유물 데이터에 넣는다.
/// 여러 번 눌러도 같다. 로그 「[HiggsfieldArt] 끝」.
/// </summary>
public static class HiggsfieldArtImportEditor
{
    private const string Root      = "Assets/RelicFairy/_Imported/Higgsfield/";
    private const string GroupName = "Default Local Group";
    private const string RelicDir  = "Assets/RelicFairy/Systems/Relic/Data/";

    // 파일 → 주소(대사 · 장면 코드가 부르는 키)
    private static readonly (string file, string address)[] Addressed =
    {
        ("Story/Illust_Story_MissionSeal.png",     "Illust_Story_MissionSeal"),     // 봉인기 사명 — 넷을 묶어라
        ("Story/Illust_Story_MorganaReveal.png",   "Illust_Story_MorganaReveal"),   // 엔딩 — 모르가나의 정체
        ("Story/Illust_Story_EndingCard.png",      "Illust_Story_EndingCard"),      // 엔딩 카드 배경
        ("Portrait/Lich_Portrait.png",             "Lich_Portrait"),                // 대사 표가 이미 부르던 키(그림이 없어 빈칸이었다)
        ("Portrait/Lich_Portrait_Calm.png",        "Lich_Portrait_Calm"),
        ("Portrait/Lich_Portrait_Rage.png",        "Lich_Portrait_Rage"),
        ("Portrait/Lich_Portrait_Dying.png",       "Lich_Portrait_Dying"),
        ("Portrait/Portrait_Boss_ForestGuardian.png", "Portrait_Boss_ForestGuardian"),   // 보스 말풍선 초상
        ("Portrait/Portrait_Boss_Dragon.png",      "Portrait_Boss_Dragon"),
        ("Portrait/Portrait_Boss_Mordred.png",     "Portrait_Boss_Mordred"),
    };

    // 유물 데이터에 직접 넣는 것(주소 없음 — 데이터가 참조로 들고 간다)
    private static readonly (string relic, string portrait, string crest)[] Relics =
    {
        ("RelicClass_Gawain",   "Relic/Relic_Gawain_Portrait.png",   "Relic/Relic_Gawain_Crest.png"),
        ("RelicClass_Lancelot", "Relic/Relic_Lancelot_Portrait.png", "Relic/Relic_Lancelot_Crest.png"),
    };

    [MenuItem("RelicFairy/Tools/10-13 힉스필드 그림 반입 — 가져오기 설정 · 주소 · 유물 초상 · 문장")]
    private static void Run()
    {
        int imported = 0, addressed = 0, relics = 0, missing = 0;

        foreach (var (file, _) in Addressed) { if (Configure(Root + file)) imported++; else missing++; }
        foreach (var (_, p, c) in Relics)
        {
            if (Configure(Root + p)) imported++; else missing++;
            if (Configure(Root + c)) imported++; else missing++;
        }

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings != null)
        {
            var group = settings.FindGroup(GroupName) ?? settings.DefaultGroup;
            foreach (var (file, address) in Addressed)
            {
                string guid = AssetDatabase.AssetPathToGUID(Root + file);
                if (string.IsNullOrEmpty(guid)) continue;
                var entry = settings.CreateOrMoveEntry(guid, group, false, false);
                entry.address = address;
                addressed++;
            }
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true);
        }
        else Debug.LogWarning("[HiggsfieldArt] Addressables 설정 없음");

        foreach (var (relic, p, c) in Relics)
        {
            var so = AssetDatabase.LoadAssetAtPath<RelicClassSO>(RelicDir + relic + ".asset");
            if (so == null) { Debug.LogWarning($"[HiggsfieldArt] 유물 없음 — {relic}"); continue; }
            var s = new SerializedObject(so);
            s.FindProperty("portrait").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(Root + p);
            var crest = s.FindProperty("crest");
            if (crest != null) crest.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(Root + c);
            s.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(so);
            relics++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[HiggsfieldArt] 끝 — 가져오기 {imported} · 주소 {addressed} · 유물 {relics} · 없음 {missing}");
    }

    // ── 10-18 증강 · 진화 고르기 틀(사용자 「증강 사용할 팝업 UI 테두리가 포함된 틀」 · 「뽑은 리소스 활용해야지」) ──
    private const string FrameDir         = Root + "UIFrame/";
    private const string CrucibleSkinPath = "Assets/RelicFairy/UI/Skin/Crucible/CrucibleSkin.asset";
    private const string DraftSkinPath    = "Assets/RelicFairy/UI/Skin/RelicPartDraft/RelicPartDraftSkin.asset";
    private const string RuneSkinPath     = "Assets/RelicFairy/UI/Skin/RuneSelect/RuneSelectSkin.asset";

    // 파일 → 9칸 자르기 경계(왼 · 아래 · 오른 · 위 — 그림 px). 0이면 자르지 않는다(비율 그대로 그리는 카드 · 문장).
    private static readonly (string file, Vector4 border)[] Frames =
    {
        ("Frame_CrucibleAugmentCard.png",    Vector4.zero),
        ("Frame_CrucibleAugmentPanel.png",   new Vector4(84f, 84f, 84f, 84f)),
        ("Frame_RelicMemoryCard.png",        new Vector4(56f, 56f, 56f, 56f)),
        ("Frame_EvolutionCard_Gawain.png",   new Vector4(140f, 150f, 140f, 150f)),
        ("Frame_EvolutionCard_Lancelot.png", new Vector4(104f, 124f, 104f, 124f)),
        ("Crest_EvolutionCard_Gawain.png",   Vector4.zero),
        ("Crest_EvolutionCard_Lancelot.png", Vector4.zero),
        ("Frame_CrucibleChooserCard.png",    new Vector4(84f, 84f, 84f, 84f)),   // 10-19 ⑯
        ("Frame_PartsWorkshopCard.png",      new Vector4(64f, 64f, 64f, 64f)),   // 10-19 ⑥
        ("Frame_PartsWorkshopPanel.png",     new Vector4(76f, 76f, 76f, 76f)),
        ("Frame_RuneSelectWindow.png",       new Vector4(72f, 72f, 72f, 72f)),   // 10-19 룬 획득 팝업 창(모서리 쐐기돌 ≤ 65 · 안쪽 빛 선 42)
        ("Frame_RuneCard_Common.png",        new Vector4(56f, 56f, 56f, 56f)),   // 10-19 룬 카드 등급 틀(쐐기돌 ≈ 55 · 안쪽 빛 선 38)
        ("Frame_RuneCard_Rare.png",          new Vector4(56f, 56f, 56f, 56f)),
        ("Frame_RuneCard_Epic.png",          new Vector4(58f, 58f, 58f, 58f)),
        ("Frame_RuneCard_Legendary.png",     new Vector4(136f, 136f, 136f, 136f)),   // 금 세공이 모서리에서 ≈ 130까지
        ("Frame_ShopWindow.png",             new Vector4(72f, 72f, 72f, 72f)),   // 10-21 상점 새 레이아웃 창(쐐기돌 ≈ 60 · 안쪽 빛 선 ≈ 48)
        ("Emblem_Blessing.png",              Vector4.zero),                      // 10-21 가호 문장 판(비율 그대로)
    };

    // ── 10-21 상점 새 레이아웃(사용자 「상점의 새로운 레이아웃부터 필요한 리소스를 제작해서 만들고 빌드」) ──
    private const string ShopSkinPath = "Assets/RelicFairy/UI/Skin/Shop/ShopSkin.asset";
    private const string IconDir      = "Assets/RelicFairy/_Imported/Artsystack/Sprites/flaticon/white/128/";

    // 가호 아이콘 — StatType 순서. 이미 반입된 Artsystack 흰 아이콘만 쓴다(새로 들이지 않음).
    private static readonly string[] BlessingIconFiles =
    {
        "power_1",       // AttackPower — 맹공
        "shield_2",      // Defense — 수호
        "Heart",         // MaxHp — 생명
        "wind_spell",    // MoveSpeed — 질풍
        "dagger",        // AttackSpeed — 연격
        "sand_glass_1",  // SkillCooldownReduction — 각성
        "sword_2",       // MeleeAttack
        "archery",       // RangedAttack
        "dice",          // Luck — 행운
        "sand_glass_1",  // ActiveItemCooldownReduction
        "quiver",        // Projectile — 분열
        "flame",         // InstantDamage
        "target_1",      // CritChance — 예리함
        "bomb",          // CritDamage — 파괴
    };

    [MenuItem("RelicFairy/Tools/10-21 상점 새 레이아웃 그림 반입 — 창 틀 · 행상 초상 · 가호 문장 · 아이콘 연결")]
    private static void RunShop()
    {
        int imported = 0;
        foreach (var (file, border) in Frames)
            if (file == "Frame_ShopWindow.png" || file == "Emblem_Blessing.png")
                if (Configure(FrameDir + file, border)) imported++;
        if (Configure(Root + "Portrait/Portrait_Peddler.png")) imported++;

        var shop = AssetDatabase.LoadAssetAtPath<ShopSkinSO>(ShopSkinPath);
        if (shop == null) { Debug.LogWarning($"[HiggsfieldArt] 상점 스킨 없음 — {ShopSkinPath}"); return; }
        var s = new SerializedObject(shop);
        s.FindProperty("windowFrame").objectReferenceValue     = FrameSprite("Frame_ShopWindow.png");
        s.FindProperty("blessingEmblem").objectReferenceValue  = FrameSprite("Emblem_Blessing.png");
        s.FindProperty("peddlerPortrait").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "Portrait/Portrait_Peddler.png");
        var icons = s.FindProperty("blessingIcons");
        icons.arraySize = BlessingIconFiles.Length;
        int missing = 0;
        for (int i = 0; i < BlessingIconFiles.Length; i++)
        {
            var sp = Icon(BlessingIconFiles[i]);
            if (sp == null) missing++;
            icons.GetArrayElementAtIndex(i).objectReferenceValue = sp;
        }
        s.FindProperty("lockClosed").objectReferenceValue  = Icon("lock_1");
        s.FindProperty("lockOpen").objectReferenceValue    = Icon("lock_3");
        s.FindProperty("rerollIcon").objectReferenceValue  = Icon("refresh");
        s.FindProperty("hpIcon").objectReferenceValue      = Icon("Heart");
        s.FindProperty("potionIcon").objectReferenceValue  = Icon("potion_1");
        s.FindProperty("storageIcon").objectReferenceValue = Icon("chest_1");
        s.FindProperty("boardIcon").objectReferenceValue   = Icon("crystal_1");
        s.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(shop);
        AssetDatabase.SaveAssets();
        Debug.Log($"[HiggsfieldArt] 상점 끝 — 가져오기 {imported}/3 · 창 틀 {shop.WindowFrame != null} · 초상 {shop.PeddlerPortrait != null} · 문장 {shop.BlessingEmblem != null} · 아이콘 없음 {missing}");
    }

    private static Sprite Icon(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(IconDir + name + "_128.png");

    [MenuItem("RelicFairy/Tools/10-22 인트로 모르드레드(타락 전) 초상 반입 — 가져오기 · 주소")]
    private static void RunMordredKnight()
    {
        const string file = "Portrait/Portrait_Mordred_Knight.png";
        bool imported = Configure(Root + file);
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        string guid = AssetDatabase.AssetPathToGUID(Root + file);
        if (settings == null || string.IsNullOrEmpty(guid)) { Debug.LogWarning($"[HiggsfieldArt] 모르드레드 초상 주소 실패 — 설정 {settings != null} · guid {guid}"); return; }
        var entry = settings.CreateOrMoveEntry(guid, settings.FindGroup(GroupName) ?? settings.DefaultGroup, false, false);
        entry.address = "Portrait_Mordred_Knight";   // UI_BossBark.PortraitKey(Mordred · 타락 전)
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[HiggsfieldArt] 모르드레드 초상 끝 — 가져오기 {imported} · 주소 {entry.address}");
    }

    [MenuItem("RelicFairy/Tools/10-22 상점 원석 환전소 그림 반입 — 저울 문장 · 원석 아이콘")]
    private static void RunExchangeArt()
    {
        bool imported = Configure(FrameDir + "Emblem_Exchange.png");
        var shop = AssetDatabase.LoadAssetAtPath<ShopSkinSO>(ShopSkinPath);
        if (shop == null) { Debug.LogWarning($"[HiggsfieldArt] 상점 스킨 없음 — {ShopSkinPath}"); return; }
        var s = new SerializedObject(shop);
        s.FindProperty("exchangeEmblem").objectReferenceValue = FrameSprite("Emblem_Exchange.png");
        s.FindProperty("oreIcon").objectReferenceValue        = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/RelicFairy/UI/HUD/Sprites/Currency/아이콘_원석.png");
        s.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(shop);
        AssetDatabase.SaveAssets();
        Debug.Log($"[HiggsfieldArt] 원석 환전소 그림 끝 — 가져오기 {imported} · 문장 {shop.ExchangeEmblem != null} · 원석 {shop.OreIcon != null}");
    }

    [MenuItem("RelicFairy/Tools/10-22 재련소 첫 화면 카드 그림 반입 — 무기 재련 · 파츠 재련")]
    private static void RunChooserArt()
    {
        int imported = 0;
        if (Configure(FrameDir + "Art_CrucibleChooseMelee.png"))  imported++;
        if (Configure(FrameDir + "Art_CrucibleChooseRanged.png")) imported++;
        var cru = AssetDatabase.LoadAssetAtPath<CrucibleSkinSO>(CrucibleSkinPath);
        if (cru == null) { Debug.LogWarning($"[HiggsfieldArt] 재련소 스킨 없음 — {CrucibleSkinPath}"); return; }
        var s = new SerializedObject(cru);
        s.FindProperty("chooserMeleeArt").objectReferenceValue  = FrameSprite("Art_CrucibleChooseMelee.png");
        s.FindProperty("chooserRangedArt").objectReferenceValue = FrameSprite("Art_CrucibleChooseRanged.png");
        s.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(cru);
        AssetDatabase.SaveAssets();
        Debug.Log($"[HiggsfieldArt] 재련소 첫 화면 그림 끝 — 가져오기 {imported}/2 · 무기 {cru.ChooserMeleeArt != null} · 파츠 {cru.ChooserRangedArt != null}");
    }

    // 10-26 사용자 「무형검 진화 때 무기 선택 · 형(능력) 고르는 카드 리소스가 너무 안 좋다 — 힉스필드로 실제 오브젝트 모양 살려서」
    [MenuItem("RelicFairy/Tools/10-26 진화 카드 그림 반입 — 배경 무형검 · 카타나 · 대검 머리 그림 · 형 문장")]
    private static void RunEvolveArt()
    {
        string[] files =
        {
            "Art_EvolveBackdrop_Nameless.png", "Art_EvolveBanner_Katana.png", "Art_EvolveBanner_Greatsword.png",
            "Art_EvolveForm_Windblade.png", "Art_EvolveForm_Weight.png",
        };
        int imported = 0;
        foreach (var f in files) if (Configure(FrameDir + f)) imported++;
        var cru = AssetDatabase.LoadAssetAtPath<CrucibleSkinSO>(CrucibleSkinPath);
        if (cru == null) { Debug.LogWarning($"[HiggsfieldArt] 재련소 스킨 없음 — {CrucibleSkinPath}"); return; }
        var s = new SerializedObject(cru);
        s.FindProperty("evolveBackdrop").objectReferenceValue         = FrameSprite(files[0]);
        s.FindProperty("evolveBannerKatana").objectReferenceValue     = FrameSprite(files[1]);
        s.FindProperty("evolveBannerGreatsword").objectReferenceValue = FrameSprite(files[2]);
        s.FindProperty("evolveFormWindblade").objectReferenceValue    = FrameSprite(files[3]);
        s.FindProperty("evolveFormWeight").objectReferenceValue       = FrameSprite(files[4]);
        s.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(cru);
        AssetDatabase.SaveAssets();
        Debug.Log($"[HiggsfieldArt] 진화 카드 그림 끝 — 가져오기 {imported}/5 · 배경 {cru.EvolveBackdrop != null} · 카타나 {cru.EvolveBanner(false) != null} · 대검 {cru.EvolveBanner(true) != null}"
                + $" · 칼바람 {cru.EvolveFormEmblem("windblade") != null} · 무게 {cru.EvolveFormEmblem("weight") != null}");
    }

    // ── 10-23 로비 세이브 창(사용자 「너무 튀는 레거시 리소스 — 톤에 맞게 · 세이브 데이터를 잘 보여 줄 · 배경이랑 틀」) ──
    private const string SaveSlotFolder   = "Assets/RelicFairy/UI/Skin/SaveSlot";
    private const string SaveSlotSkinPath = SaveSlotFolder + "/SaveSlotSkin.asset";

    /// <summary>배경 · 카드 틀을 들이고(틀은 9칸 경계 64 — 모서리 쐐기돌 ≈ 58까지) 세이브 창 스킨을 만들어(없으면) 잇고 주소 「UI/SaveSlotSkin」을 단다. 여러 번 눌러도 같다.</summary>
    [MenuItem("RelicFairy/Tools/10-23 로비 세이브 창 그림 반입 — 배경 · 틀")]
    private static void RunSaveSlot()
    {
        int imported = 0;
        if (Configure(FrameDir + "Art_SaveSlotBackground.png"))                             imported++;
        if (Configure(FrameDir + "Frame_SaveSlotCard.png", new Vector4(64f, 64f, 64f, 64f))) imported++;

        if (!AssetDatabase.IsValidFolder(SaveSlotFolder)) AssetDatabase.CreateFolder("Assets/RelicFairy/UI/Skin", "SaveSlot");
        var skin = AssetDatabase.LoadAssetAtPath<SaveSlotSkinSO>(SaveSlotSkinPath);
        if (skin == null)
        {
            skin = ScriptableObject.CreateInstance<SaveSlotSkinSO>();
            AssetDatabase.CreateAsset(skin, SaveSlotSkinPath);
        }
        var s = new SerializedObject(skin);
        s.FindProperty("background").objectReferenceValue = FrameSprite("Art_SaveSlotBackground.png");
        s.FindProperty("cardFrame").objectReferenceValue  = FrameSprite("Frame_SaveSlotCard.png");
        s.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(skin);
        AssetDatabase.SaveAssets();

        string address = "-";
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings != null)
        {
            var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(SaveSlotSkinPath), settings.FindGroup(GroupName) ?? settings.DefaultGroup, false, false);
            entry.address = UISkin.SaveSlotAddress;
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
            AssetDatabase.SaveAssets();
            address = entry.address;
        }
        else Debug.LogWarning("[HiggsfieldArt] AddressableAssetSettings 없음 — 세이브 창 스킨 주소를 달지 못했다");
        Debug.Log($"[HiggsfieldArt] 로비 세이브 창 끝 — 가져오기 {imported}/2 · 배경 {skin.Background != null} · 틀 {skin.CardFrame != null} · 주소 {address}");
    }

    [MenuItem("RelicFairy/Tools/10-18 힉스필드 증강 · 진화 틀 반입 — 가져오기 · 9칸 경계 · 스킨 연결")]
    private static void RunFrames()
    {
        int imported = 0;
        foreach (var (file, border) in Frames) if (Configure(FrameDir + file, border)) imported++;

        var cru = AssetDatabase.LoadAssetAtPath<CrucibleSkinSO>(CrucibleSkinPath);
        if (cru != null)
        {
            var s = new SerializedObject(cru);
            s.FindProperty("augmentCardFrame").objectReferenceValue  = FrameSprite("Frame_CrucibleAugmentCard.png");
            s.FindProperty("augmentPanelFrame").objectReferenceValue = FrameSprite("Frame_CrucibleAugmentPanel.png");
            s.FindProperty("chooserCardFrame").objectReferenceValue  = FrameSprite("Frame_CrucibleChooserCard.png");
            s.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(cru);
        }
        else Debug.LogWarning($"[HiggsfieldArt] 재련소 스킨 없음 — {CrucibleSkinPath}");

        var draft = AssetDatabase.LoadAssetAtPath<RelicPartDraftSkinSO>(DraftSkinPath);
        if (draft != null)
        {
            var s = new SerializedObject(draft);
            s.FindProperty("memoryCardFrame").objectReferenceValue = FrameSprite("Frame_RelicMemoryCard.png");
            s.FindProperty("workshopCardFrame").objectReferenceValue  = FrameSprite("Frame_PartsWorkshopCard.png");
            s.FindProperty("workshopPanelFrame").objectReferenceValue = FrameSprite("Frame_PartsWorkshopPanel.png");
            SetKeyArt(s.FindProperty("evolutionFrame"), ("Gawain", "Frame_EvolutionCard_Gawain.png"), ("Lancelot", "Frame_EvolutionCard_Lancelot.png"));
            SetKeyArt(s.FindProperty("evolutionCrest"), ("Gawain", "Crest_EvolutionCard_Gawain.png"), ("Lancelot", "Crest_EvolutionCard_Lancelot.png"));
            s.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(draft);
        }
        else Debug.LogWarning($"[HiggsfieldArt] 기억 스킨 없음 — {DraftSkinPath}");

        var rune = AssetDatabase.LoadAssetAtPath<RuneSelectSkinSO>(RuneSkinPath);
        if (rune != null)
        {
            var s = new SerializedObject(rune);
            s.FindProperty("windowFrame").objectReferenceValue = FrameSprite("Frame_RuneSelectWindow.png");
            var cards = s.FindProperty("rarityCardFrame");
            string[] rarity = { "Common", "Rare", "Epic", "Legendary" };   // ItemRarity 순서
            cards.arraySize = rarity.Length;
            for (int i = 0; i < rarity.Length; i++)
                cards.GetArrayElementAtIndex(i).objectReferenceValue = FrameSprite($"Frame_RuneCard_{rarity[i]}.png");
            s.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rune);
        }
        else Debug.LogWarning($"[HiggsfieldArt] 룬 획득 스킨 없음 — {RuneSkinPath}");

        AssetDatabase.SaveAssets();
        Debug.Log($"[HiggsfieldArt] 틀 끝 — 가져오기 {imported}/{Frames.Length} · 재련소 {cru != null} · 기억 {draft != null} · 룬 {rune != null}");
    }

    private static Sprite FrameSprite(string file) => AssetDatabase.LoadAssetAtPath<Sprite>(FrameDir + file);

    private static void SetKeyArt(SerializedProperty arr, params (string key, string file)[] items)
    {
        arr.arraySize = items.Length;
        for (int i = 0; i < items.Length; i++)
        {
            var e = arr.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("key").stringValue = items[i].key;
            e.FindPropertyRelative("sprite").objectReferenceValue = FrameSprite(items[i].file);
        }
    }

    /// <summary>프롤로그 그림(Illust_Intro_Page)과 같은 가져오기 설정. 파일이 없으면 false. <paramref name="border"/>가 있으면 9칸 경계도 단다.</summary>
    private static bool Configure(string path, Vector4 border = default)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogWarning($"[HiggsfieldArt] 그림 없음 — {path}"); return false; }
        ti.textureType         = TextureImporterType.Sprite;
        ti.spriteImportMode    = SpriteImportMode.Single;
        ti.mipmapEnabled       = false;
        ti.alphaIsTransparency = true;
        ti.sRGBTexture         = true;
        ti.maxTextureSize      = 2048;
        ti.textureCompression  = TextureImporterCompression.Compressed;
        if (border != Vector4.zero) ti.spriteBorder = border;
        ti.SaveAndReimport();
        return true;
    }
}
#endif
