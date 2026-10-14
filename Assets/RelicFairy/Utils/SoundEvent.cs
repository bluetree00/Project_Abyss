public static class SoundEvent
{
    // Room
    public const string RoomClear  = "room_clear";
    public const string DoorOpen   = "door_open";
    public const string BossAppear = "boss_appear";

    // 보스 클리어 출구 계단(10-10) — 돌 디딤판이 솟음 · 꼭대기 포탈이 열림 · 포탈에 들어감
    public const string StairRise   = "stair_rise";
    public const string PortalOpen  = "portal_open";
    public const string PortalEnter = "portal_enter";

    // Player
    public const string PlayerDie  = "player_die";
    public const string PlayerHit  = "player_hit";

    // Player 공격 · 스킬 (09-25) — 어떤 소리를 낼지는 SoundEventTable 에셋이 정한다
    public const string PlayerSwing      = "player_swing";        // 근접 휘두름(칼끝 궤적 시작)
    public const string PlayerSwingHeavy = "player_swing_heavy";  // 대검 휘두름 · 유물 마무리 베기
    public const string PlayerShot       = "player_shot";         // 원거리 기본 발사
    public const string PlayerSkill      = "player_skill";        // 무기 스킬(E·R) 시작
    public const string PlayerFinisher   = "player_finisher";     // 막타가 적중한 순간
    // 장비 스킬(10-08 A6) — 시작음 10개가 다 같았다(C9). 소리 고르기는 길이 · 포락선 실측(귀 확인은 사용자)
    public const string SkillCharge      = "skill_charge";        // 모아 쏘기 시작(궁수의 집중) — 0.85초에 터지는 모음 소리
    public const string SkillBuff        = "skill_buff";          // 자기 강화(석궁) — 잔잔한 반짝임
    public const string SkillHeavyImpact = "skill_heavy_impact";  // 내려찍기 충격(최후의 일격) — 낮은 쿵

    // Relic
    public const string RelicSunFall   = "relic_sun_fall";    // 가웨인 Q — 태양이 떨어지기 시작
    public const string RelicSunImpact = "relic_sun_impact";  // 가웨인 Q — 착탄
    // 유물 조각 · 형 · 스킬 단계 신호(10-12 4b · 소리는 83 점검표 4-1b R1 ~ R6)
    public const string RelicHarvest   = "relic_harvest";       // 가웨인 정오 수확(R1)
    public const string RelicBloom     = "relic_bloom";         // 가웨인 개화 한 번(R2)
    public const string RelicRend      = "relic_rend";          // 랜슬롯 찢기 X자(R3)
    public const string RelicGrudgeWave = "relic_grudge_wave";   // 랜슬롯 원한 참격파(R4)
    public const string FormWindblade  = "form_windblade";      // 형 발동 풍절 · 카타나(R5a)
    public const string FormWeight     = "form_weight";         // 형 발동 내려찍기 · 대검(R5b)
    public const string SkillTierUp    = "skill_tier_up";       // 장비 스킬 단계 오른 뒤 첫 사용(R6)
    public const string PotionUse      = "potion_use";          // 포션 마심(83 표 · 4b 몫)
    public const string CostumeUp      = "costume_up";          // 의상 단계 오름 — 갑옷 덧입기 디졸브(83 표 · 4b 몫)

    // Monster
    public const string MonsterDie = "monster_die";
    public const string MonsterHit = "monster_hit";

    // Economy
    public const string ItemPickup = "item_pickup";
    public const string GoldPickup = "gold_pickup";

    // UI
    public const string UiClick    = "ui_click";
    public const string UiHover    = "ui_hover";

    // UI · 화면 공용(10-12 Fantasy UI SFX) — 어떤 소리를 낼지는 SoundEventTable이 정한다. UI 채널은 SoundManager.PlayUiEvent로 낸다.
    // 상점 · 재련소 · 정제소 · 제단의 뜻 키(shop_buy · crucible_hit · refine_spin · altar_engrave …)는 ShopUIStyle.PlaySfx에 문자열로 넘긴다.
    public const string UiSelect  = "ui_select";    // 카드 · 칸 고르기 · 캐러셀 넘김 · 탭
    public const string UiOpen    = "ui_open";      // 팝업 열기 공용
    public const string UiClose   = "ui_close";     // 닫기 · 뒤로 · 넘기기
    public const string UiReject  = "ui_reject";    // 「안 됨」
    public const string UiConfirm = "ui_confirm";   // 확정(시작 파츠 · 룬판 · 결과 [계속])
    public const string UiTick    = "ui_tick";      // 놀이 카운트다운 · 마지막 5초 · 간발

    // 서약서 · 서약 발동(전투 HUD)
    public const string CovenantPick   = "covenant_pick";
    public const string CovenantReroll = "covenant_reroll";
    public const string CovenantFix    = "covenant_fix";
    public const string CovenantForge  = "covenant_forge";
    public const string ClauseSilver   = "clause_silver";
    public const string ClauseGold     = "clause_gold";
    public const string ClauseRuby     = "clause_ruby";

    // 룬 선택 · 룬판 · 보관함
    public const string RuneFlip    = "rune_flip";
    public const string RuneLegend  = "rune_legend";
    public const string RuneTake    = "rune_take";
    public const string RuneLift    = "rune_lift";
    public const string RunePlace   = "rune_place";
    public const string RuneReturn  = "rune_return";
    public const string RuneSalvage = "rune_salvage";
    public const string RuneReset   = "rune_reset";

    // 무기대 · 되살아난 기억 · 결과 · 퀘스트
    public const string ForgeTake    = "forge_take";
    public const string MemoryAbsorb = "memory_absorb";
    public const string MemoryTake   = "memory_take";
    public const string MemoryRedraw = "memory_redraw";
    public const string ResultReveal = "result_reveal";
    public const string ResultCount  = "result_count";
    public const string QuestDone    = "quest_done";

    // 재화 · 월드 상호작용 · 보상
    public const string FuelMaterial   = "fuel_material";
    public const string FuelOre        = "fuel_ore";
    public const string EssencePickup  = "essence_pickup";
    public const string GateSeal       = "gate_seal";
    public const string GambleOpen     = "gamble_open";
    public const string VaultOpen      = "vault_open";
    public const string BuffboxOpen    = "buffbox_open";
    public const string SacrificeOffer = "sacrifice_offer";
    public const string OracleChoose   = "oracle_choose";
    public const string SpringDrink    = "spring_drink";
    public const string SwordPickup    = "sword_pickup";
    public const string RestForge      = "rest_forge";
    public const string RestHeal       = "rest_heal";
    public const string RewardGrade    = "reward_grade";
    public const string RewardArrive   = "reward_arrive";

    // 이벤트방 놀이
    public const string MgSuccess   = "mg_success";
    public const string MgFail      = "mg_fail";      // 표가 기존 피격음 중간 단계(sfx_player_hit_medium)를 가리킨다
    public const string MgStart     = "mg_start";
    public const string MgCard      = "mg_card";
    public const string MgGradeUp   = "mg_grade_up";
    public const string MgGradeDown = "mg_grade_down";
    public const string MgStamp     = "mg_stamp";
    public const string MgNote      = "mg_note";
}
