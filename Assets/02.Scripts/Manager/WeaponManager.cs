using UnityEngine;
using System;

public class WeaponManager
{
    /// <summary>
    /// 무기 컨테이너
    /// </summary>
    public WeaponContainer w_Con { get; private set; }

    // 컨테이너 데이터를 받아옴 => 컨테이너 안에 무기 SO 존재
    void ContainerDataInit(WeaponContainer con)
    {
        w_Con = con;   // 매니저 변수 = 매개변수 동기화
        SetDefult();   // 기본 무기 설정
    }

    // 컨테이너 내부에 있는 변수나 함수를 WeaponManager에서 사용 => SO는 함수 실행이 아닌 변수로만 사용

    void SetDefult()
    {
        string basicClass_WeaponName = w_Con.conClass.ToString(); // 컨테이너 클래스를 문자열로 변환
        WeaponData resourceWData = Managers.Resource.Load<WeaponData>($"Data/WeaponData/basic_{basicClass_WeaponName}"); // 무기 데이터 로드
        w_Con.currentWeapon = resourceWData;

    }

    public void SetWeapon(string itemName)
    {
        WeaponData itemNameData = Managers.Resource.Load<WeaponData>($"Data/WeaponData/{itemName}"); // 충돌한 아이템 이름으로 무기 데이터 로드

        if (Array.Exists(w_Con.ownWeapons, weapon => weapon == itemNameData)) // 이미 소지중인 무기인지 확인
        {
            Debug.Log("이미 소지중인 무기입니다.");
            return;
        }
    
        int emptySlotIndex = Array.IndexOf(w_Con.ownWeapons, null); // 빈 공간 찾기
        if (emptySlotIndex != -1)
        {
            w_Con.ownWeapons[emptySlotIndex] = itemNameData; // 빈 공간에 무기 추가
        }
    
        w_Con.currentWeapon = itemNameData;
    }


}
