/// <summary>
/// 맵 그리드의 타일 역할.
/// 뒤끝 차트 grid_csv에서 한 글자 기호로 매핑.
/// </summary>
public enum TileType
{
    Floor,          // F — 바닥
    Wall,           // W — 벽
    Obstacle,       // O — 장애물
    MonsterSpawn,   // M — 확정 몬스터 스폰 (항상 활성)
    MonsterSpawnCandidate, // m — 후보 몬스터 스폰 (active_monster_spawners개만 랜덤 활성)
    PlayerSpawn,    // P — 플레이어 시작
    BossSpawn,      // B — 보스 스폰
    ShopStall,      // S — 상점 (레거시 호환, 단일 매대)
    ShopStallWeapon,// Sw — 상점 장비 매대
    ShopStallItem,  // Si — 상점 아이템 매대
    NPCSpawn,       // N — NPC
    Entrance,       // E — 입구
    Exit,           // X — 출구
    Trap,           // T — 함정
    Chest,          // C — 보물상자
    Empty,          // . — 빈 칸 (구멍)
    BuffBox,        // R — 버프 상자 (상호작용으로 발동)
    BuffPedestal,   // D — 버프 발판 (밟으면 즉시 발동)
}
