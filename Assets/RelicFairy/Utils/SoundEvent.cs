public static class SoundEvent
{
    // Room
    public const string RoomClear  = "room_clear";
    public const string DoorOpen   = "door_open";
    public const string BossAppear = "boss_appear";

    // Player
    public const string PlayerDie  = "player_die";
    public const string PlayerHit  = "player_hit";

    // Player 공격 · 스킬 (09-25) — 어떤 소리를 낼지는 SoundEventTable 에셋이 정한다
    public const string PlayerSwing      = "player_swing";        // 근접 휘두름(칼끝 궤적 시작)
    public const string PlayerSwingHeavy = "player_swing_heavy";  // 대검 휘두름 · 유물 마무리 베기
    public const string PlayerShot       = "player_shot";         // 원거리 기본 발사
    public const string PlayerSkill      = "player_skill";        // 무기 스킬(E·R) 시작
    public const string PlayerFinisher   = "player_finisher";     // 막타가 적중한 순간

    // Relic
    public const string RelicSunFall   = "relic_sun_fall";    // 가웨인 Q — 태양이 떨어지기 시작
    public const string RelicSunImpact = "relic_sun_impact";  // 가웨인 Q — 착탄

    // Monster
    public const string MonsterDie = "monster_die";
    public const string MonsterHit = "monster_hit";

    // Economy
    public const string ItemPickup = "item_pickup";
    public const string GoldPickup = "gold_pickup";

    // UI
    public const string UiClick    = "ui_click";
    public const string UiHover    = "ui_hover";
}
