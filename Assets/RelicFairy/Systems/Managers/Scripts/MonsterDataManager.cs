using BackEnd;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using System;
using LitJson;

public class MonsterDataManager //TODO: 해당 기능은 제이슨 런타임 데이터 구조이지만 중간에 SO를 거처서 사용하는 내용으로 가야할 듯
{
    private const string MonsterDataFileName = "monster_data.json";
    private string FilePath => Path.Combine(Application.persistentDataPath, MonsterDataFileName);
    private const string ChartId = "195107";

    private Dictionary<int, MonsterStat> _monsterDataDict = new();
    public bool IsInitialized { get; private set; } = false;

    public async UniTask InitializeAsync()
    {
        if (File.Exists(FilePath))
        {
            Debug.Log("로컬 데이터 로드");
            LoadFromJson();

            Debug.Log($"몬스터 데이터 저장 경로: {FilePath}");
            Debug.Log("서버에서 변경된 몬스터 데이터만 갱신");
            await LoadFromServerAsync(); // 버전 비교 후 부분 갱신
        }
        else
        {
            Debug.Log("로컬 데이터 없음. 서버에서 전체 다운로드");
            await LoadFromServerAsync();
        }

        IsInitialized = true;
    }

    // 테스트용으로 public
    public void LoadFromJson()
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

    private void SaveToJson()
    {
        try
        {
            var list = new List<MonsterStat>(_monsterDataDict.Values);
            var collection = new MonsterStatCollection { monsters = list };
            string json = JsonUtility.ToJson(collection, true);
            File.WriteAllText(FilePath, json);
            Debug.Log($"몬스터 데이터 {list.Count}개 저장 완료 at {FilePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"로컬 JSON 저장 실패: {e.Message}");
        }
    }

    private UniTask LoadFromServerAsync()
    {
        int loaded = ChartLoader.Load("MONSTER_STAT_DATA", row =>
        {
            int.TryParse(row["monster_id"].ToString(), out int monsterId);
            int.TryParse(row["stat_version"].ToString(), out int statVersion);

            if (_monsterDataDict.TryGetValue(monsterId, out var existing))
            {
                if (statVersion <= existing.stat_version)
                    return;
            }

            string type = row["type"].ToString();
            string monsterName = row["monster_name"].ToString();
            int.TryParse(row["level"].ToString(), out int level);
            int.TryParse(row["max_hp"].ToString(), out int maxHp);
            int.TryParse(row["attack"].ToString(), out int attack);
            float.TryParse(row["move_speed"].ToString(), out float moveSpeed);
            float.TryParse(row["attack_range"].ToString(), out float attackRange);
            float.TryParse(row["attack_cooldown"].ToString(), out float attackCooldown);
            int.TryParse(row["def"].ToString(), out int def);

            _monsterDataDict[monsterId] = new MonsterStat
            {
                monster_id = monsterId,
                type = type,
                monster_name = monsterName,
                level = level,
                max_hp = maxHp,
                attack = attack,
                move_speed = moveSpeed,
                attack_range = attackRange,
                attack_cooldown = attackCooldown,
                def = def,
                stat_version = statVersion,
            };
        });

        if (loaded > 0) SaveToJson();
        return UniTask.CompletedTask;
    }

    public MonsterStat GetStatById(int monsterId)
    {
        if (_monsterDataDict.TryGetValue(monsterId, out var stat))
            return stat;

        Debug.LogWarning($"몬스터 ID {monsterId} 의 데이터를 찾을 수 없습니다.");
        return null;
    }
}
