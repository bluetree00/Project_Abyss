public static class SoundKey
{
    public static class Bgm
    {
        public const string Logo     = "bgm_logo";
        public const string Lobby    = "bgm_lobby";
        public const string InGame   = "bgm_ingame";
        public const string Boss     = "bgm_boss";
        public const string Result   = "bgm_result";
    }

    public static class Sfx
    {
        // Player
        public const string PlayerAttack = "sfx_player_attack";
        public const string PlayerHit    = "sfx_player_hit";
        public const string PlayerDie    = "sfx_player_die";
        public const string PlayerDash   = "sfx_player_dash";
        public const string PlayerJump   = "sfx_player_jump";

        // Monster
        public const string MonsterHit    = "sfx_monster_hit";
        public const string MonsterHit1   = "sfx_monster_hit1";
        public const string MonsterHit2   = "sfx_monster_hit2";
        public const string MonsterHit3   = "sfx_monster_hit3";
        public const string MonsterDie    = "sfx_monster_die";
        public const string MonsterAttack = "sfx_monster_attack";

        // Room
        public const string RoomClear = "sfx_room_clear";
        public const string DoorOpen  = "sfx_door_open";

        // Cinematic
        public const string OpenDoor = "OpenDoor";

        // Item / Economy
        public const string ItemPickup = "sfx_item_pickup";
        public const string GoldPickup = "sfx_gold_pickup";

        // UI
        public const string UiClick  = "sfx_ui_click";
        public const string UiHover  = "sfx_ui_hover";
        public const string UiButton = "Button";
    }
}
