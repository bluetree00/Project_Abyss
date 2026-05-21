namespace RelicFairy.Monster
{
    /// <summary>
    /// 몬스터 FSM 상태 인터페이스.
    /// 모든 상태는 컨텍스트를 통해서만 몬스터 데이터에 접근한다.
    /// </summary>
    public interface IMonsterState
    {
        void Enter (MonsterContext ctx);
        void Update(MonsterContext ctx);
        void Exit  (MonsterContext ctx);
    }
}
