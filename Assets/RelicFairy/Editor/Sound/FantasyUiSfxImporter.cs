#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// [1회 · 편집 모드] Fantasy UI SFX Lite 반입(10-12 사운드 점검표 §6) — 쓰는 파일만 <c>_Imported/FantasyUISFX/</c>로,
/// 나머지는 폴더째 <c>_ThirdParty/</c>로 옮기고(둘 다 AssetDatabase.MoveAsset · GUID 유지), 가져오기 설정 · 주소(sfx_&lt;이벤트&gt;) ·
/// 효과음 표(SoundEventTable) 줄을 맞춘다. 표는 같은 eventId가 있으면 덮어쓰고 없으면 더한다 — 다른 줄은 건드리지 않는다.
///
/// <para>⚠️ <b>이 도구를 돌린 뒤에는 아래 두 메뉴를 돌리지 말 것</b> — 이 도구가 넣은 표 줄이 사라진다.
/// ① <c>RelicFairy/Debug/플레이어 리소스 연결 — 랜슬롯 검·효과음 표</c>(PlayerResourceHookupEditor): 실제 표를 13줄로 <b>잘라 덮어쓴다</b>.
/// ② <c>RelicFairy/Audio/Generate Sound Event Table</c>(SoundTableGenerator): 옛 CSV 11줄(흔들림 없음)로 표를 통째로 다시 만든다.
/// 표에 줄을 더할 땐 이 도구나 SkillSoundSetup처럼 「같은 eventId만 고치는」 방식을 쓴다.</para>
///
/// <para>주소 등록은 AddressableAssetSettings API로만 한다(그룹 .asset을 직접 고치지 않는다 — 다른 작업도 같은 그룹을 고친다).
/// 원본 폴더가 없으면 「이미 반입됨 — 검사만」으로 돈다(아무것도 쓰지 않는다). 다시 돌려도 안전하다.
/// 결과 <c>Logs/FantasyUISFX_반입결과_20261012.txt</c>.</para>
/// </summary>
public static class FantasyUiSfxImporter
{
    // ── Constants ────────────────────────────────────────────────
    private const string PackFolder   = "Assets/Fantasy UI SFX - Lite Edition";
    private const string ThirdParty   = "Assets/_ThirdParty";
    private const string PackParked   = ThirdParty + "/Fantasy UI SFX - Lite Edition";
    private const string ImportParent = "Assets/RelicFairy/_Imported";
    private const string DestFolder   = ImportParent + "/FantasyUISFX";
    private const string TablePath    = "Assets/RelicFairy/Systems/Sound/SoundEventTable.asset";
    private const string SoundGroup   = "Default Local Group";
    private const string ResultPath   = "Logs/FantasyUISFX_반입결과_20261012.txt";
    private const string Tag          = "[FantasyUISFX]";
    private const float  VorbisQuality = 0.7f;
    private const string UI = "UI", SFX = "SFX";   // 채널 — 표에는 채널 칸이 없다. UI = PlayUiEvent · SFX = PlayEvent(부르는 코드가 정한다)

    /// <summary>실제 소리가 긴 묶음 — Compressed In Memory + Vorbis 70%. 나머지는 Decompress On Load + ADPCM(§6-3 ⑤).</summary>
    private static readonly string[] LongPrefixes =
    {
        "Magical Interface 8-", "Magical Interface 9-", "Magical Interface 10-", "Special Interface", "Magical Texture Chimes",
    };

    /// <summary>
    /// 반입 목록 91줄(§6-4 · 새 파일 90 + 기존 소리 1). file = 팩 안 파일 이름(확장자 없음) · null이면 기존 소리(이동 · 주소 등록 없음, 표 줄만).
    /// keep = 표에 같은 eventId가 이미 있으면 두기(플레이어 담당이 코드와 함께 바꾼다). 볼륨 · 흔들림은 §3 · §4-1b 값 [추정 — 귀 확인 뒤 조정].
    /// </summary>
    internal static readonly (string file, string evt, string address, string channel, float volume, float jitter, bool keep)[] Rows =
    {
        ("Interface 1-2",                   "ui_click",           "sfx_ui_click",             UI,  0.45f, 0.04f, false),
        ("Interface 3-3",                   "ui_select",          "sfx_ui_select",            UI,  0.35f, 0.05f, false),
        ("Interface 6-4",                   "ui_open",            "sfx_ui_open",              UI,  0.40f, 0.00f, false),
        ("Interface 5-3",                   "ui_close",           "sfx_ui_close",             UI,  0.40f, 0.00f, false),
        ("Interface 1-1",                   "ui_reject",          "sfx_ui_reject",            UI,  0.50f, 0.00f, false),
        ("Building Interface 1-4",          "ui_confirm",         "sfx_ui_confirm",           UI,  0.55f, 0.00f, false),
        ("Interface 3-1",                   "ui_tick",            "sfx_ui_tick",              UI,  0.35f, 0.00f, false),
        ("Objects Small 1-3",               "shop_select",        "sfx_shop_select",          UI,  0.40f, 0.05f, false),
        ("Coins 2-2",                       "shop_buy",           "sfx_shop_buy",             UI,  0.50f, 0.05f, false),
        ("Magical Texture Chimes 1-1",      "shop_deal",          "sfx_shop_deal",            UI,  0.50f, 0.00f, false),
        ("Dice Single Drop 1-1",            "shop_reroll",        "sfx_shop_reroll",          UI,  0.45f, 0.05f, false),
        ("Blacksmithing 2-1",               "crucible_hit",       "sfx_crucible_hit",         UI,  0.55f, 0.06f, false),
        ("Blacksmithing 3-1",               "crucible_success",   "sfx_crucible_success",     UI,  0.60f, 0.00f, false),
        ("Special Interface 3-1",           "crucible_jackpot",   "sfx_crucible_jackpot",     UI,  0.65f, 0.00f, false),
        ("Armor 1-3",                       "crucible_fail",      "sfx_crucible_fail",        UI,  0.55f, 0.00f, false),
        ("Weapon 1-2",                      "enhance_success",    "sfx_enhance_success",      UI,  0.55f, 0.00f, false),
        ("Magical Interface 9-1",           "crucible_evolve",    "sfx_crucible_evolve",      UI,  0.65f, 0.00f, false),
        ("Magical Interface 8-1",           "crucible_promote",   "sfx_crucible_promote",     UI,  0.65f, 0.00f, false),
        ("Blacksmithing 4-2",               "smith_hammer",       "sfx_smith_hammer",         SFX, 0.25f, 0.08f, false),
        ("Alchemy 1-1",                     "refine_spin",        "sfx_refine_spin",          UI,  0.50f, 0.00f, false),
        ("Magical Interface 5-2",           "refine_reveal",      "sfx_refine_reveal",        UI,  0.55f, 0.00f, false),
        ("Magical Interface 6-3",           "refine_lit",         "sfx_refine_lit",           UI,  0.35f, 0.00f, false),
        ("Special Interface 7-1",           "refine_legend",      "sfx_refine_legend",        UI,  0.65f, 0.00f, false),
        ("Writting on Paper 1-1",           "covenant_pick",      "sfx_covenant_pick",        UI,  0.40f, 0.05f, false),
        ("Book Page 1-3",                   "covenant_reroll",    "sfx_covenant_reroll",      UI,  0.40f, 0.05f, false),
        ("Writting on Paper 1-3",           "covenant_fix",       "sfx_covenant_fix",         UI,  0.40f, 0.00f, false),
        ("Writting on Paper 2-2",           "covenant_forge",     "sfx_covenant_forge",       UI,  0.55f, 0.00f, false),
        ("Magical Interface 7-3",           "clause_silver",      "sfx_clause_silver",        UI,  0.30f, 0.00f, false),
        ("Magical Interface 1-2",           "clause_gold",        "sfx_clause_gold",          UI,  0.35f, 0.00f, false),
        ("Magical Interface 4-3",           "clause_ruby",        "sfx_clause_ruby",          UI,  0.35f, 0.00f, false),
        ("Card Draw 2-2",                   "rune_flip",          "sfx_rune_flip",            UI,  0.40f, 0.08f, false),
        ("Magical Interface 1-3",           "rune_legend",        "sfx_rune_legend",          UI,  0.60f, 0.00f, false),
        ("Card Place 2-1",                  "rune_take",          "sfx_rune_take",            UI,  0.50f, 0.00f, false),
        ("Bag Handle 3-6",                  "rune_lift",          "sfx_rune_lift",            UI,  0.35f, 0.08f, false),
        ("Building Interface 2-4",          "rune_place",         "sfx_rune_place",           UI,  0.45f, 0.06f, false),
        ("Bag Handle 4-1",                  "rune_return",        "sfx_rune_return",          UI,  0.40f, 0.00f, false),
        ("Objects Small 1-1",               "rune_salvage",       "sfx_rune_salvage",         UI,  0.45f, 0.00f, false),
        ("Bag Handle 2-3",                  "rune_reset",         "sfx_rune_reset",           UI,  0.45f, 0.00f, false),
        ("Bag Handle 2-7",                  "item_pickup",        "sfx_item_stash",           SFX, 0.45f, 0.05f, false),   // 갱신
        ("Weapon 1-4",                      "forge_take",         "sfx_forge_take",           UI,  0.50f, 0.00f, false),
        ("Blacksmithing 2-3",               "rest_forge",         "sfx_rest_forge",           SFX, 0.55f, 0.00f, false),
        ("Magical Texture Chimes 1-2",      "rest_heal",          "sfx_rest_heal",            SFX, 0.60f, 0.00f, false),
        ("Magical Interface 6-2",           "altar_buy",          "sfx_altar_buy",            UI,  0.50f, 0.00f, false),
        ("Magical Interface 2-1",           "altar_engrave",      "sfx_altar_engrave",        UI,  0.50f, 0.00f, false),
        ("Magical Texture Chimes 1-3",      "altar_grow",         "sfx_altar_grow",           UI,  0.50f, 0.00f, false),
        ("Magical Texture Chimes 1-4",      "altar_keystone",     "sfx_altar_keystone",       UI,  0.60f, 0.00f, false),
        ("Special Interface 1-1",           "altar_era",          "sfx_altar_era",            UI,  0.65f, 0.00f, false),
        ("Magical Interface 3-3",           "memory_absorb",      "sfx_memory_absorb",        UI,  0.50f, 0.00f, false),
        ("Magical Interface 7-2",           "memory_take",        "sfx_memory_take",          UI,  0.55f, 0.00f, false),
        ("Card Draw 1-2",                   "memory_redraw",      "sfx_memory_redraw",        UI,  0.40f, 0.00f, false),
        ("Book Page 1-1",                   "esc_memory_page",    "sfx_esc_memory_page",      UI,  0.40f, 0.00f, false),
        ("Magical Interface 9-2",           "result_reveal",      "sfx_result_reveal",        UI,  0.50f, 0.00f, false),
        ("Coins 1-9",                       "result_count",       "sfx_result_count",         UI,  0.40f, 0.00f, false),
        ("Writting on Paper 2-1",           "quest_done",         "sfx_quest_done",           UI,  0.45f, 0.00f, false),
        ("Blacksmithing 4-3",               "fuel_material",      "sfx_fuel_material",        SFX, 0.40f, 0.08f, false),
        ("Objects Small 1-2",               "fuel_ore",           "sfx_fuel_ore",             SFX, 0.40f, 0.08f, false),
        ("Interface 2-2",                   "essence_pickup",     "sfx_essence_pickup",       SFX, 0.30f, 0.10f, false),
        ("Rock Impact 37",                  "gate_seal",          "sfx_gate_seal",            SFX, 0.60f, 0.05f, false),
        ("Magical Interface 3-1",           "room_clear",         "sfx_room_clear",           SFX, 0.50f, 0.00f, false),
        ("Key & Lock 1-4",                  "gamble_open",        "sfx_gamble_open",          SFX, 0.55f, 0.00f, false),
        ("Key & Lock 2-2",                  "vault_open",         "sfx_vault_open",           SFX, 0.50f, 0.08f, false),
        ("Key & Lock 1-2",                  "buffbox_open",       "sfx_buffbox_open",         SFX, 0.50f, 0.05f, false),
        ("Potion Item 1-3",                 "sacrifice_offer",    "sfx_sacrifice_offer",      SFX, 0.55f, 0.00f, false),
        ("Magical Interface 10-4",          "oracle_choose",      "sfx_oracle_choose",        SFX, 0.50f, 0.00f, false),
        ("Potion Item 1-1",                 "spring_drink",       "sfx_spring_drink",         SFX, 0.55f, 0.00f, false),
        ("Weapon 1-5",                      "sword_pickup",       "sfx_sword_pickup",         SFX, 0.60f, 0.00f, false),
        ("Building Interface 4-1",          "stair_rise",         "sfx_stair_rise",           SFX, 0.30f, 0.15f, false),   // 갱신
        ("Magical Interface 10-1",          "portal_open",        "sfx_portal_open",          SFX, 0.50f, 0.00f, false),   // 갱신
        ("Magical Interface 10-2",          "portal_enter",       "sfx_portal_enter",         SFX, 0.60f, 0.00f, false),   // 갱신
        ("Magical Interface 8-2",           "reward_charge",      "sfx_reward_charge",        UI,  0.40f, 0.00f, false),
        ("Magical Interface 1-1",           "reward_grade",       "sfx_reward_grade",         UI,  0.50f, 0.00f, false),
        ("Magical Interface 6-1",           "reward_arrive",      "sfx_reward_arrive",        UI,  0.50f, 0.00f, false),
        ("Interface 2-3",                   "mg_success",         "sfx_mg_success",           UI,  0.50f, 0.00f, false),
        (null,                              "mg_fail",            "sfx_player_hit_medium",    UI,  0.55f, 0.00f, false),   // 기존 피격음 중간 단계 — 새 파일 · 새 주소 없음
        ("Building Interface 6-7",          "mg_start",           "sfx_mg_start",             UI,  0.50f, 0.00f, false),
        ("Book Page 1-4",                   "mg_card",            "sfx_mg_card",              UI,  0.45f, 0.00f, false),
        ("Interface 6-3",                   "mg_grade_up",        "sfx_mg_grade_up",          UI,  0.45f, 0.00f, false),
        ("Interface 6-5",                   "mg_grade_down",      "sfx_mg_grade_down",        UI,  0.45f, 0.00f, false),
        ("Building Interface 4-2",          "mg_stamp",           "sfx_mg_stamp",             UI,  0.55f, 0.00f, false),
        ("Magical Interface 5-1",           "mg_note",            "sfx_mg_note",              UI,  0.50f, 0.00f, false),
        ("Arrow & Bow 2-3",                 "player_shot",        "sfx_player_shot_bow",      SFX, 0.30f, 0.10f, true ),   // P1 — 표 줄은 플레이어 담당이 코드와 함께 바꾼다(기존 줄 유지 · 주소만)
        ("Potion Item 1-2",                 "potion_use",         "sfx_potion_use",           SFX, 0.50f, 0.05f, false),   // 볼륨 추정(문서 값 없음)
        ("Armor 1-1",                       "player_die",         "sfx_player_die",           SFX, 0.60f, 0.00f, false),   // 볼륨 추정(문서 값 없음)
        ("Armor 1-2",                       "costume_up",         "sfx_costume_up",           SFX, 0.35f, 0.00f, false),   // 볼륨 추정(문서 값 없음)
        ("Coins 3-3",                       "relic_harvest",      "sfx_relic_harvest",        SFX, 0.45f, 0.03f, false),
        ("Interface 5-1",                   "relic_bloom",        "sfx_relic_bloom",          SFX, 0.35f, 0.06f, false),
        ("Bag Handle 1-1",                  "relic_rend",         "sfx_relic_rend",           SFX, 0.45f, 0.06f, false),
        ("Magical Interface 4-1",           "relic_grudge_wave",  "sfx_relic_grudge_wave",    SFX, 0.45f, 0.04f, false),
        ("Arrow & Bow 2-7",                 "form_windblade",     "sfx_form_windblade",       SFX, 0.50f, 0.05f, false),
        ("Wood Impact 05",                  "form_weight",        "sfx_form_weight",          SFX, 0.60f, 0.04f, false),
        ("Building Interface 5-2",          "skill_tier_up",      "sfx_skill_tier_up",        SFX, 0.40f, 0.00f, false),
    };

    // ── Public Methods ───────────────────────────────────────────
    [MenuItem("RelicFairy/Sound/Fantasy UI SFX 반입 (1회 · 편집 모드)")]
    public static void Import()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning($"{Tag} 플레이 중에는 돌리지 않는다 — Assets/ 쓰기가 도메인 리로드로 런을 깬다. 편집 모드에서 다시.");
            return;
        }

        bool checkOnly = !AssetDatabase.IsValidFolder(PackFolder);
        var log = new StringBuilder($"Fantasy UI SFX 반입 — {(checkOnly ? "이미 반입됨 · 검사만" : "반입")} · {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
        int errors = 0;

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var group    = settings != null ? settings.FindGroup(SoundGroup) : null;
        var table    = AssetDatabase.LoadAssetAtPath<SoundEventTableSO>(TablePath);
        if (settings == null || group == null || table == null)
        {
            Debug.LogError($"{Tag} 주소 설정 · 그룹({SoundGroup}) · 표({TablePath}) 중 하나가 없다 — 중단");
            return;
        }

        if (!checkOnly && !AssetDatabase.IsValidFolder(DestFolder))
            AssetDatabase.CreateFolder(ImportParent, "FantasyUISFX");

        // 1) 이동 — 이동 전 GUID(.meta) → MoveAsset → 대상 존재 · GUID 대조
        int moved = 0, files = 0;
        var guids = new Dictionary<string, string>();   // file → guid(대상)
        foreach (var r in Rows)
        {
            if (r.file == null) continue;
            files++;
            string src = $"{PackFolder}/{r.file}.wav";
            string dst = $"{DestFolder}/{r.file}.wav";
            if (File.Exists(dst))
            {
                guids[r.file] = MetaGuid(dst);
                moved++;
                log.AppendLine($"[이동] 이미 있음 {dst} guid={guids[r.file]}");
                continue;
            }
            if (checkOnly || !File.Exists(src))
            {
                errors++;
                log.AppendLine($"[이동] 없음 {r.file} — 원본 · 대상 둘 다 없다");
                continue;
            }
            string before = MetaGuid(src);
            string err = AssetDatabase.MoveAsset(src, dst);
            if (!string.IsNullOrEmpty(err))
            {
                // 이동 API가 「실패」를 돌려줘도 실제로는 옮겨졌을 수 있다(09-18 기록) — 파일 시스템으로 다시 본다
                AssetDatabase.Refresh();
                if (!File.Exists(dst)) { errors++; log.AppendLine($"[이동] 실패 {src} — {err}"); continue; }
                log.AppendLine($"[이동] 실패 응답이었지만 파일은 옮겨짐 — {err}");
            }
            string after = MetaGuid(dst);
            bool same = !string.IsNullOrEmpty(before) && before == after;
            if (!same) errors++;
            guids[r.file] = after;
            moved++;
            log.AppendLine($"[이동] {src} → {dst} guid={after} {(same ? "일치" : $"불일치(전 {before})")}");
        }

        // 2) 남은 팩 — 폴더째 _ThirdParty로(대상 폴더를 미리 만들지 않는다)
        if (!checkOnly)
        {
            int left = Directory.Exists(PackFolder) ? Directory.GetFiles(PackFolder, "*.wav").Length : 0;
            if (!AssetDatabase.IsValidFolder(ThirdParty)) { errors++; log.AppendLine($"[남은 팩] {ThirdParty} 폴더가 없다 — 옮기지 않음"); }
            else if (AssetDatabase.IsValidFolder(PackParked)) { errors++; log.AppendLine($"[남은 팩] {PackParked}가 이미 있다 — 옮기지 않음(손으로 합칠 것)"); }
            else
            {
                string err = AssetDatabase.MoveAsset(PackFolder, PackParked);
                if (!string.IsNullOrEmpty(err)) { AssetDatabase.Refresh(); }
                bool ok = Directory.Exists(PackParked) && !Directory.Exists(PackFolder);
                if (!ok) errors++;
                log.AppendLine($"[남은 팩] {left}개 → {PackParked} {(ok ? "" : $"실패 — {err}")}");
            }
        }

        // 3) 가져오기 설정
        foreach (var r in Rows)
        {
            if (r.file == null || !guids.ContainsKey(r.file)) continue;
            log.AppendLine($"[설정] {r.file} — {ApplyImportSettings($"{DestFolder}/{r.file}.wav", r.file, checkOnly)}");
        }

        // 4) 주소 — 같은 주소가 다른 에셋을 가리키거나 이 에셋이 다른 주소면 건너뛰고 적는다(SkillSoundSetup 규약)
        int addressed = 0;
        foreach (var r in Rows)
        {
            if (r.file == null)
            {
                bool exists = FindByAddress(settings, r.address) != null;
                if (!exists) errors++;
                log.AppendLine($"[주소] {r.address} — 기존 소리 {(exists ? "있음" : "없음(!)")}");
                continue;
            }
            if (!guids.TryGetValue(r.file, out var guid) || string.IsNullOrEmpty(guid)) continue;
            string path = $"{DestFolder}/{r.file}.wav";
            var byAddr = FindByAddress(settings, r.address);
            var byGuid = settings.FindAssetEntry(guid);
            if (byAddr != null && byAddr.guid != guid)
            {
                errors++;
                log.AppendLine($"[주소] {r.address} → {path} (충돌 — 이 주소는 {byAddr.AssetPath}를 가리킨다)");
            }
            else if (byGuid != null && byGuid.address != r.address)
            {
                errors++;
                log.AppendLine($"[주소] {r.address} → {path} (충돌 — 이 파일은 이미 「{byGuid.address}」)");
            }
            else if (byGuid != null)
            {
                addressed++;
                log.AppendLine($"[주소] {r.address} → {path} (이미)");
            }
            else if (checkOnly)
            {
                errors++;
                log.AppendLine($"[주소] {r.address} → {path} (없음)");
            }
            else
            {
                var entry = settings.CreateOrMoveEntry(guid, group, false, false);
                entry.address = r.address;
                addressed++;
                log.AppendLine($"[주소] {r.address} → {path} (새로)");
            }
        }

        // 5) 효과음 표 — 같은 eventId는 덮어쓰고 없으면 더한다(keep 줄은 이미 있으면 둔다)
        var so      = new SerializedObject(table);
        var entries = so.FindProperty("_entries");
        int tabled  = 0;
        foreach (var r in Rows)
        {
            int idx = -1;
            for (int i = 0; i < entries.arraySize; i++)
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("eventId").stringValue == r.evt) { idx = i; break; }

            if (idx >= 0)
            {
                var cur = entries.GetArrayElementAtIndex(idx);
                string curKey = cur.FindPropertyRelative("sfxKey").stringValue;
                float  curVol = cur.FindPropertyRelative("volume").floatValue;
                float  curJit = cur.FindPropertyRelative("pitchJitter").floatValue;
                bool   same   = curKey == r.address && Mathf.Approximately(curVol, r.volume) && Mathf.Approximately(curJit, r.jitter);
                if (same || r.keep)
                {
                    tabled++;
                    log.AppendLine($"[표] {r.evt} → {curKey} {curVol:0.00} {curJit:0.00} ({(same ? "이미" : "유지 — 플레이어 담당 몫")})");
                    continue;
                }
                if (checkOnly) { errors++; log.AppendLine($"[표] {r.evt} → {curKey} (다름 — 목록 {r.address} {r.volume:0.00} {r.jitter:0.00})"); continue; }
            }
            else if (checkOnly) { errors++; log.AppendLine($"[표] {r.evt} (없음)"); continue; }

            bool added = idx < 0;
            if (added) { idx = entries.arraySize; entries.arraySize++; }
            var e = entries.GetArrayElementAtIndex(idx);
            e.FindPropertyRelative("eventId").stringValue    = r.evt;
            e.FindPropertyRelative("sfxKey").stringValue     = r.address;
            e.FindPropertyRelative("volume").floatValue      = r.volume;
            e.FindPropertyRelative("pitchJitter").floatValue = r.jitter;
            tabled++;
            log.AppendLine($"[표] {r.evt} → {r.address} {r.volume:0.00} {r.jitter:0.00} ({(added ? "추가" : "갱신")})");
        }

        if (!checkOnly)
        {
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(table);
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true, true);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        string summary = $"이동 {moved}/{files} · 주소 {addressed} · 표 {tabled}/{Rows.Length}(전체 {entries.arraySize}줄) · 오류 {errors}";
        log.AppendLine(summary);
        Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
        File.WriteAllText(ResultPath, log.ToString(), new UTF8Encoding(false));
        if (errors > 0) Debug.LogWarning($"{Tag} {summary} — {ResultPath}");
        else            Debug.Log($"{Tag} {summary} — {ResultPath}");
    }

    // ── Private Methods ──────────────────────────────────────────
    /// <summary>.meta의 guid 줄을 읽는다(읽기만 — .meta는 Unity가 관리한다).</summary>
    private static string MetaGuid(string assetPath)
    {
        string meta = assetPath + ".meta";
        if (!File.Exists(meta)) return null;
        foreach (var line in File.ReadLines(meta))
            if (line.StartsWith("guid: ")) return line.Substring(6).Trim();
        return null;
    }

    private static AddressableAssetEntry FindByAddress(AddressableAssetSettings settings, string address)
    {
        foreach (var g in settings.groups)
        {
            if (g == null) continue;
            foreach (var e in g.entries)
                if (e != null && e.address == address) return e;
        }
        return null;
    }

    /// <summary>실제 소리 1.5초 미만 = Decompress On Load + ADPCM · 긴 묶음 = Compressed In Memory + Vorbis 70%. 모노 끔 · 미리 읽기 켬.</summary>
    private static string ApplyImportSettings(string path, string file, bool checkOnly)
    {
        if (!(AssetImporter.GetAtPath(path) is AudioImporter imp)) return "가져오기 설정을 못 읽음";

        bool longSound = false;
        foreach (var p in LongPrefixes)
            if (file.StartsWith(p)) { longSound = true; break; }
        var loadType = longSound ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
        var format   = longSound ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM;
        string want  = longSound ? "Vorbis 70% · 메모리 압축" : "ADPCM · 로드 시 풀기";

        var s = imp.defaultSampleSettings;
        bool same = !imp.forceToMono && s.loadType == loadType && s.compressionFormat == format && s.preloadAudioData
                    && (!longSound || Mathf.Approximately(s.quality, VorbisQuality));
        if (same) return $"{want} (이미)";
        if (checkOnly) return $"다름 — 목표 {want}";

        imp.forceToMono     = false;
        s.loadType          = loadType;
        s.compressionFormat = format;
        s.preloadAudioData  = true;
        if (longSound) s.quality = VorbisQuality;
        imp.defaultSampleSettings = s;
        imp.SaveAndReimport();
        return want;
    }
}
#endif
