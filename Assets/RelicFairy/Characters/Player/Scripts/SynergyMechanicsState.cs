namespace RelicFairy
{
    /// <summary>
    /// 플레이어 보호막(실드) 상태.
    ///
    /// ⚠️ 이름은 "SynergyMechanics"지만 시너지와 무관하다.
    ///    원래는 구 스탯존(ATK/MAG/DEF/SPD/HP/LUCK) 시절의 행동 역학 플래그 20여 개를 담고 있었는데,
    ///    룬이 6속성으로 개편되면서 그 플래그들은 <b>쓰는 곳도 켜는 곳도 없는 死코드</b>가 되어 전부 제거했다.
    ///    남은 건 아이템·유물이 실제로 쓰는 <b>실드 값</b>뿐이다.
    ///    (PlayerRuntimeStats.Shield / AddShield / AbsorbWithShield 가 이 상태를 읽고 쓴다)
    /// </summary>
    public class SynergyMechanicsState
    {
        /// <summary>현재 누적 실드 값.</summary>
        public float ShieldCurrentValue;

        /// <summary>실드 상한 (최대 HP 대비 비율).</summary>
        public float ShieldCapRatio;

        public void Reset()
        {
            ShieldCurrentValue = 0f;
            ShieldCapRatio     = 0.3f;
        }
    }
}
