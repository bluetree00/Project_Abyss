namespace RelicFairy.Monster
{
    /// <summary>
    /// 보스 등장 연출 트리거 인터페이스.
    /// BossSpawner가 카메라 팬 완료 후 이 인터페이스를 통해 등장 애니메이션을 시작한다.
    /// </summary>
    public interface IBossEntrance
    {
        void TriggerEntrance();
    }
}
