using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

public interface ILoader<Key, Value>
{
    Dictionary<Key, Value> MakeDict();
}

public class DataManager
{
    public Dictionary<int, Data.Stat> StatDict { get; private set; } = new Dictionary<int, Data.Stat>();

    public void Init()
    {
        StatDict = LoadJson<Data.StatData, int, Data.Stat>("StatData").MakeDict();
    }

    // Loader LoadJson<Loader, Key, Value>(string path) where Loader : ILoader<Key, Value>
    // {
    //     TextAsset textAsset = Managers.Resource.Load<TextAsset>($"Data/{path}");
    //     return JsonUtility.FromJson<Loader>(textAsset.text);
    // }


    //NOTE: JsonUtility는 Dictionary를 지원하지 않기 때문에 Newtonsoft.Json을 사용합니다.
    //NOTE: JsonUtility => Newtonsoft.Json
    Loader LoadJson<Loader, Key, Value>(string path) where Loader : ILoader<Key, Value>
    {
        // Resources 폴더에서 json 텍스트를 읽어옴
        TextAsset textAsset = Managers.Resource.Load<TextAsset>($"Data/{path}");
        // Newtonsoft.Json으로 역직렬화
        return JsonConvert.DeserializeObject<Loader>(textAsset.text);
    }

    // -------------------------------
    // 공용 Json 로드/저장 메서드 추가
    // -------------------------------

    public static T LoadJsonFile<T>(string fileName) where T : class
    {
        string path = Path.Combine(Application.persistentDataPath, fileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"파일 없음: {path}");
            return null;
        }
        try
        {
            string json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<T>(json);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Json 로드 실패: {ex}");
            return null;
        }
    }

    public static void SaveJsonFile<T>(string fileName, T data)
    {
        string path = Path.Combine(Application.persistentDataPath, fileName);
        try
        {
            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(path, json);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Json 저장 실패: {ex}");
        }
    }
}
