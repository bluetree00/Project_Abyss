using BackEnd;
using System.Collections.Generic;
using UnityEngine;

public class MonsterTableTest : MonoBehaviour
{
    private async void Start()
    {
        var bro = Backend.Chart.GetChartContents("MONSTER_STAT_DATA");

        if (!bro.IsSuccess())
        {
            Debug.LogError("데이터 테이블 불러오기 실패: " + bro.GetStatusCode());
            return;
        }

        var rows = bro.FlattenRows(); // List<object>

        Debug.Log($"데이터 테이블 행 개수: {rows.Count}");

        foreach (var rowObj in rows)
        {
            var row = rowObj as Dictionary<string, object>;
            if (row == null)
            {
                Debug.LogWarning("행 데이터 변환 실패");
                continue;
            }

            // 예시로 monster_id와 monster_name 필드 출력
            if (row.TryGetValue("monster_id", out var id) && row.TryGetValue("monster_name", out var name))
            {
                Debug.Log($"몬스터 ID: {id}, 이름: {name}");
            }
            else
            {
                Debug.LogWarning("필드 누락");
            }
        }
    }
}
