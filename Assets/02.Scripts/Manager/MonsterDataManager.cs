using BackEnd;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using System;
using LitJson;

public class MonsterDataManager
{
    private const string MonsterDataFileName = "monster_data.json";
    private string FilePath => Path.Combine(Application.persistentDataPath, MonsterDataFileName);
    private const string ChartId = "194283"; // 실제 차트 ID

    private Dictionary<int, MonsterStat> _monsterDataDict = new();
    public bool IsInitialized { get; private set; } = false;

    public async UniTask InitializeAsync()
    {
        if (File.Exists(FilePath))
        {
            Debug.Log("로컬 데이터 로드");
            LoadFromJson();

            int localVersion = GetLocalStatVersion();
            int serverVersion = await GetServerStatVersionAsync();

            if (serverVersion == -1)
            {
                Debug.LogWarning("서버 버전 조회 실패, 로컬 데이터 사용");
            }
            else if (serverVersion > localVersion)
            {
                Debug.Log("서버 데이터가 최신. 서버에서 데이터 받아오기");
                await LoadFromServerAsync();
            }
            else
            {
                Debug.Log($"몬스터 데이터 저장 경로: {FilePath}");
                Debug.Log("로컬 데이터가 최신.");
            }
        }
        else
        {
            Debug.Log("로컬 데이터 없음. 서버에서 데이터 받아오기");
            await LoadFromServerAsync();
        }

        IsInitialized = true;
    }

    private void LoadFromJson()
    {
        try
        {
            string json = File.ReadAllText(FilePath);
            MonsterStatCollection wrapper = JsonUtility.FromJson<MonsterStatCollection>(json);

            _monsterDataDict.Clear();
            foreach (var monster in wrapper.monsters)
            {
                _monsterDataDict[monster.monster_id] = monster;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"로컬 JSON 로드 실패: {e.Message}");
        }
    }

    private async UniTask LoadFromServerAsync()
    {
        var bro = Backend.Chart.GetChartContents(ChartId);

        Debug.Log($"[차트 전체 JSON 출력]\n{LitJson.JsonMapper.ToJson(bro.GetReturnValuetoJSON())}");


        if (!bro.IsSuccess())
        {
            Debug.LogError($"몬스터 데이터 서버 요청 실패: {bro.GetStatusCode()}");
            return;
        }

        var rows = bro.FlattenRows();
        var monsterList = new List<MonsterStat>();

        foreach (var rowObj in rows)
        {
            if (rowObj is not JsonData row)
            {
                Debug.LogWarning("row 형변환 실패");
                continue;
            }

            int.TryParse(row["monster_id"].ToString(), out int monsterId);
            string type = row["type"].ToString();
            string monsterName = row["monster_name"].ToString();
            int.TryParse(row["level"].ToString(), out int level);
            int.TryParse(row["max_hp"].ToString(), out int maxHp);
            int.TryParse(row["attack"].ToString(), out int attack);
            float.TryParse(row["move_speed"].ToString(), out float moveSpeed);
            float.TryParse(row["attack_range"].ToString(), out float attackRange);
            int.TryParse(row["def"].ToString(), out int def);
            int.TryParse(row["stat_version"].ToString(), out int statVersion);
            string lastUpdated = row.ContainsKey("last_updated") ? row["last_updated"].ToString() : "";

            MonsterStat monster = new MonsterStat
            {
                monster_id = monsterId,
                type = type,
                monster_name = monsterName,
                level = level,
                max_hp = maxHp,
                attack = attack,
                move_speed = moveSpeed,
                attack_range = attackRange,
                def = def,
                stat_version = statVersion,
            };

            monsterList.Add(monster);
            _monsterDataDict[monsterId] = monster;
        }

        // JSON 저장
        MonsterStatCollection collection = new MonsterStatCollection { monsters = monsterList };
        string json = JsonUtility.ToJson(collection, true);
        File.WriteAllText(FilePath, json);

        Debug.Log($"몬스터 데이터 저장 경로: {FilePath}");
        Debug.Log($"몬스터 데이터 {monsterList.Count}개 저장 완료");
    }

    private int GetLocalStatVersion()
    {
        try
        {
            string json = File.ReadAllText(FilePath);
            MonsterStatCollection wrapper = JsonUtility.FromJson<MonsterStatCollection>(json);

            int maxVersion = 0;
            foreach (var m in wrapper.monsters)
            {
                if (m.stat_version > maxVersion)
                    maxVersion = m.stat_version;
            }
            return maxVersion;
        }
        catch (Exception e)
        {
            Debug.LogError($"로컬 데이터 읽기 실패: {e.Message}");
            return 0;
        }
    }

    private async UniTask<int> GetServerStatVersionAsync()
    {
        var bro = Backend.Chart.GetChartContents(ChartId);

        if (!bro.IsSuccess())
        {
            Debug.LogError("서버 버전 조회 실패");
            return -1;
        }

        var rows = bro.FlattenRows();
        int maxVersion = 0;

        foreach (var rowObj in rows)
        {
            if (rowObj is not JsonData row) continue;

            if (int.TryParse(row["stat_version"].ToString(), out int v))
            {
                if (v > maxVersion)
                    maxVersion = v;
            }
        }

        return maxVersion;
    }

    public MonsterStat GetStatById(int monsterId)
    {
        if (_monsterDataDict.TryGetValue(monsterId, out var stat))
            return stat;

        Debug.LogWarning($"몬스터 ID {monsterId} 의 데이터를 찾을 수 없습니다.");
        return null;
    }
}
