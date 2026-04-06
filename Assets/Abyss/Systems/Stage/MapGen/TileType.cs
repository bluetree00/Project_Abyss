/// <summary>
/// 맵 그리드의 타일 역할.
/// 뒤끝 차트 grid_csv에서 한 글자 기호로 매핑.
/// </summary>
public enum TileType
{
    Floor,          // F — 바닥
    Wall,           // W — 벽
    Obstacle,       // O — 장애물
    MonsterSpawn,   // M — 몬스터 스폰 (바닥 + 마커)
    PlayerSpawn,    // P — 플레이어 시작
    BossSpawn,      // B — 보스 스폰
    ShopStall,      // S — 상점
    NPCSpawn,       // N — NPC
    Entrance,       // E — 입구
    Exit,           // X — 출구
    Trap,           // T — 함정
    Chest,          // C — 보물상자
    Empty,          // . — 빈 칸 (구멍)
}
