using UnityEngine;
using System;

public class WeaponManager
{
    //NOTE: 무기 획득 이벤트를 위한 델리게이트 선언
    public delegate void WeaponAddedHandler(string newWeaponName);  // 무기 획득 이벤트 
    public static event WeaponAddedHandler OnWeaponAdded;
    WeaponData itemNameData;
    /// <summary>
    /// 무기 컨테이너
    /// </summary>
    // public static WeaponContainer _cont { get; private set; } // 일반 변수에서 static 변수로 변경

    private static WeaponContainer _cont; // 백킹 필드

    public static WeaponContainer Cont
    {
        get { return _cont; }
        private set { _cont = value; }
    }

    /// <summary>
    /// 무기 컨테이너 초기화
    /// </summary>
    /// <param name="con"></param>
    /// <param name="weaponHandTransform"></param>
    public void ContainerDataInit(WeaponContainer con, Transform weaponHandTransform = null)
    {
        _cont = con;   // 매니저 변수 = 매개변수 동기화
        SetDefult();   // 기본 무기 설정
        if (weaponHandTransform == null)
        {
            Debug.LogError("손의 트랜스폼이 null입니다.");
            return;
        }
        _cont.weaponHandTransform = weaponHandTransform; // 손의 트랜스폼 설정
        _cont.SpawnWeaponObject(); // 무기 오브젝트 생성
    }

    /// <summary>
    /// 기본 무기 설정
    /// </summary>
    void SetDefult()
    {
        string ClassName = _cont.conClass.ToString(); // 컨테이너 클래스를 문자열로 변환
        WeaponData resourceWData = Managers.Resource.Load<WeaponData>($"Data/WeaponData/basic_{ClassName}_01"); // 무기 데이터 로드
        _cont.currentWeapon = resourceWData;
        _cont.ownWeapons[0] = resourceWData;
    }


    /// <summary>
    /// 무기가 바뀔 때 호출할 함수 => 무기 획득 함수
    /// </summary>
    /// <param name="itemName"></param>
    public void SetWeapon(string itemName)
    {
        itemNameData = Managers.Resource.Load<WeaponData>($"Data/WeaponData/{itemName}"); // 충돌한 아이템 이름으로 무기 데이터 로드
        Debug.Log(itemNameData.weaponName + "을 획득했습니다.");
        if (Array.Exists(_cont.ownWeapons, weapon => weapon == itemNameData)) // 이미 소지중인 무기인지 확인
        {
            Debug.Log("이미 소지중인 무기입니다.");
            return;
        }
        else
        {
            Debug.Log($"새로운 무기({itemNameData})를 획득했습니다.");
        }
    
        int emptySlotIndex = Array.IndexOf(_cont.ownWeapons, null); // 빈 공간 찾기
        if (emptySlotIndex != -1)
        {
            _cont.ownWeapons[emptySlotIndex] = itemNameData; // 빈 공간에 무기 추가
            OnWeaponAdded?.Invoke(itemNameData.name); // 이벤트 호출
        }
    
        if (_cont.currentWeapon == null) _cont.currentWeapon = itemNameData; // 현재 무기가 없으면 현재 무기로 설정
        if (_cont.CurrentWeaponObject == null) _cont.SpawnWeaponObject(); // 무기 오브젝트가 없으면 생성
    }

    /// <summary>
    /// 무기 변경 함수
    /// </summary>
    /// <param name="index"></param>
    public void ChangeWeapon(int index)
    {
        if (_cont.ownWeapons[index - 1] != null)
        {
            _cont.currentWeapon = _cont.ownWeapons[index - 1];
            _cont.SpawnWeaponObject();
        }
    }

    /// <summary>
    /// 무기 제거 함수
    /// </summary>
    /// <param name="index"></param>
    public void RemoveWeapon(int index)
    {
        if (_cont.ownWeapons[index - 1] != null)
        {
            if (_cont.currentWeapon == _cont.ownWeapons[index - 1])
            {
                _cont.currentWeapon = null;
                _cont.DestroyCurrentWeaponObject();
            }

            Debug.Log($"{_cont.ownWeapons[index - 1].name} 를 제거했습니다.");
            _cont.ownWeapons[index - 1] = null;
            // TODO--->무기 제거 후 해당 이름으로 생성되었던 오브젝트 풀러 제거
            
        }
    }

    // 캐릭터 오브젝트 이름으로 찾아서 변수에 동기화
    // Find() 함수 사용 => 캐릭터 오브젝트 찾고 => 캐릭터 오브젝트에 붙어있는 컴포넌트로 접근해서 => 컨테이너의 배열에 접근
    public void FindCharacterWeaponContainer(string characterName)
    {
        GameObject characterObj = GameObject.Find(characterName);
        if (characterObj != null)
        {
            _cont = characterObj.GetComponent<WeaponContainer>();
        }
    }

    /// <summary>
    /// 현재 무기 데이터 넘겨주기
    /// </summary>
    /// <returns></returns>
    public WeaponData GetCurrentWeaponData()
    {
        return _cont.currentWeapon;
    }
}
