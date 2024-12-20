using UnityEngine;
using UnityEngine.PlayerLoop;

public class WeaponManager
{
    public CharacterData characterData { get; private set; }
    public WeaponData weaponData { get; private set; }

    /// <summary>
    /// 컨테이너와 캐릭터, 무기 데이터를 받아 매니저 변수와 동기화(초기화) 하는 함수
    /// </summary>
    /// <param name="cData"> 캐릭터 데이터 </param>
    public void WMDataInit(CharacterData cData)
    {
        characterData = cData;      
        weaponData = Managers.Resource.Load<WeaponData>($"Data/Weapon/{$"Basic_{cData.class_string()}"}");
    }


    /// <summary>
    /// 무기 매니저에서 가지고 있는 데이터를 받을 때 사용하는 함수
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public T GetData<T>() where T : ScriptableObject
    {
        if (typeof(T) == typeof(CharacterData))
        {
            return characterData as T;
        }
        else if (typeof(T) == typeof(WeaponData))
        {
            return weaponData as T;
        }
        else
        {
            Debug.LogWarning("지원하지 않는 스크립터블 오브젝트 형식.");
            return null;
        }
    }

    /// <summary>
    /// 무기나 캐릭터 데이터 둘 다 대응가능한 함수
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="newData"></param>
    public void UpdateData<T>(T newData)
    {
        if (newData is CharacterData characterData)
        {
            this.characterData = characterData;
        }
        else if (newData is WeaponData weaponData)
        {
            this.weaponData = weaponData;
        }
        else
        {
            Debug.LogWarning("지원하지 않는 데이터 타입.");
        }
    }

}
