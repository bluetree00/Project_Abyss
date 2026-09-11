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

        /// <summary>
        /// 실드 상한 (최대 HP 대비 비율).
        ///
        /// ⚠️ <b>선언 시점에 값을 준다.</b> 예전엔 0으로 시작하고 <see cref="Reset"/>이 0.3을 세웠는데,
        /// Reset을 부르는 곳이 프로젝트에 하나도 없어 상한이 계속 0이었다.
        /// 그러면 <c>ShieldCap = MaxHp × 0 = 0</c>이라 <c>AddShield</c>가 무엇을 넣든 0으로 클램프돼
        /// <b>보호막이 통째로 무효</b>였다(서약의 보호막 효과도 같이 죽어 있었다).
        /// </summary>
        public float ShieldCapRatio = 0.3f;

        public void Reset()
        {
            ShieldCurrentValue = 0f;
            ShieldCapRatio     = 0.3f;
        }
    }
}
