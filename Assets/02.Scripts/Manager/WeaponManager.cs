using UnityEngine;

public class WeaponManager
{
    public WeaponContainer weaponContainer { get; private set; }

    // 컨테이너 데이터를 받아옴 => 컨테이너 안에 무기 SO 존재
    public void WMDataInit(WeaponContainer con)
    {
        weaponContainer = con;              // 매니저 변수 = 매개변수 동기화
    }

    // 컨테이너 내부에 있는 변수나 함수를 WeaponManager에서 사용 => SO는 함수 실행이 아닌 변수로만 사용


}
