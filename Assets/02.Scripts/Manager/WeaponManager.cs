using UnityEngine;
using System;

public class WeaponManager
{
    WeaponData itemNameData;
    /// <summary>
    /// 무기 컨테이너
    /// </summary>
    public static WeaponContainer w_Con { get; private set; } // 일반 변수에서 static 변수로 변경

    public void ContainerDataInit(WeaponContainer con, Transform weaponHandTransform = null)
    {
        w_Con = con;   // 매니저 변수 = 매개변수 동기화
        SetDefult();   // 기본 무기 설정
        if (weaponHandTransform == null)
        {
            Debug.LogError("손의 트랜스폼이 null입니다.");
            return;
        }
        w_Con.weaponHandTransform = weaponHandTransform; // 손의 트랜스폼 설정
        w_Con.SpawnWeaponObject(); // 무기 오브젝트 생성
    }

    /// <summary>
    /// 기본 무기 설정
    /// </summary>
    void SetDefult()
    {
        string ClassName = w_Con.conClass.ToString(); // 컨테이너 클래스를 문자열로 변환
        WeaponData resourceWData = Managers.Resource.Load<WeaponData>($"Data/WeaponData/basic_{ClassName}_01"); // 무기 데이터 로드
        w_Con.currentWeapon = resourceWData;
        w_Con.ownWeapons[0] = resourceWData;
    }


    /// <summary>
    /// 무기가 바뀔 때 호출할 함수 => 무기 변경 함수
    /// </summary>
    /// <param name="itemName"></param>
    public void SetWeapon(string itemName)
    {
        itemNameData = Managers.Resource.Load<WeaponData>($"Data/WeaponData/{itemName}"); // 충돌한 아이템 이름으로 무기 데이터 로드
        Debug.Log(itemNameData.weaponName + "을 획득했습니다.");
        if (Array.Exists(w_Con.ownWeapons, weapon => weapon == itemNameData)) // 이미 소지중인 무기인지 확인
        {
            Debug.Log("이미 소지중인 무기입니다.");
            return;
        }
        else
        {
            Debug.Log("새로운 무기를 획득했습니다.");
        }
    
        int emptySlotIndex = Array.IndexOf(w_Con.ownWeapons, null); // 빈 공간 찾기
        if (emptySlotIndex != -1)
        {
            w_Con.ownWeapons[emptySlotIndex] = itemNameData; // 빈 공간에 무기 추가
        }
    
        w_Con.currentWeapon = itemNameData;
    }

    public void ChangeWeapon(int index)
    {
        if (w_Con.ownWeapons[index - 1] != null)
        {
            w_Con.currentWeapon = w_Con.ownWeapons[index - 1];
            w_Con.SpawnWeaponObject();
        }
    }

    // 캐릭터 오브젝트 이름으로 찾아서 변수에 동기화
    // Find() 함수 사용 => 캐릭터 오브젝트 찾고 => 캐릭터 오브젝트에 붙어있는 컴포넌트로 접근해서 => 컨테이너의 배열에 접근
    public void FindCharacterWeaponContainer(string characterName)
    {
        GameObject characterObj = GameObject.Find(characterName);
        if (characterObj != null)
        {
            w_Con = characterObj.GetComponent<WeaponContainer>();
        }
    }

    public WeaponData GetCurrentWeaponData()
    {
        return w_Con.currentWeapon;
    }


}
