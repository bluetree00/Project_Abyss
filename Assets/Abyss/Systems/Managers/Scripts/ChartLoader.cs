using BackEnd;
using LitJson;
using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// 뒤끝 V5 CDN Chart API 공통 유틸.
/// Backend.CDN.Content.Table.Get() → Content.Get() → Local.Save/Load 패턴.
///
/// ■ 사용법
///   ChartLoader.Load("ITEM_DATA", row => ParseRow(row));
///
/// ■ 동작
///   1. CDN에서 전체 차트 테이블 조회
///   2. 차트명으로 필터링
///   3. 로컬 저장 후 읽기
///   4. 행 순회
/// </summary>
public static class ChartLoader
{
    // 캐시: 한 번 로드한 CDN 딕셔너리를 재사용
    private static Dictionary<string, BackEnd.Content.ContentItem> _cachedContent;

    /// <summary>
    /// 차트명으로 데이터 로드. 성공 시 각 행마다 onRow 콜백 호출.
    /// </summary>
    /// <returns>성공 시 행 수, 실패 시 -1</returns>
    public static int Load(string chartName, Action<JsonData> onRow)
    {
        if (string.IsNullOrEmpty(chartName))
        {
            Debug.LogWarning("[ChartLoader] chartName is empty");
            return -1;
        }

        // CDN 딕셔너리 캐시 없으면 로드
        if (_cachedContent == null)
        {
            if (!LoadCDNContent())
                return -1;
        }

        // 차트명으로 찾기
        if (!_cachedContent.TryGetValue(chartName.Trim(), out var contentItem))
        {
            Debug.LogWarning($"[ChartLoader] {chartName} 차트명 없음 (CDN에 {_cachedContent.Count}개 차트)");
            return -1;
        }

        // JSON 파싱 — contentJson이 JsonData 객체이거나 string일 수 있음
        JsonData json;
        try
        {
            var raw = contentItem.contentJson;
            if (raw is JsonData jd)
                json = jd;
            else
                json = JsonMapper.ToObject(raw.ToString());
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ChartLoader] {chartName} JSON 파싱 실패: {e.Message}");
            return -1;
        }

        // 배열이 아닌 경우 내부 키 탐색
        if (json != null && json.IsObject)
        {
            if (json.ContainsKey("rows")) json = json["rows"];
            else if (json.ContainsKey("Charts")) json = json["Charts"];
        }

        if (json == null || !json.IsArray || json.Count == 0)
        {
            Debug.LogWarning($"[ChartLoader] {chartName} 데이터 0행 (type={json?.GetJsonType()})");
            return 0;
        }

        int count = 0;
        foreach (JsonData row in json)
        {
            try
            {
                onRow?.Invoke(row);
                count++;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ChartLoader] {chartName} 행 파싱 오류: {e.Message}");
            }
        }

        Debug.Log($"[ChartLoader] {chartName} → {count}행 로드");
        return count;
    }

    /// <summary>캐시 초기화 (다음 Load 시 CDN 재조회).</summary>
    public static void ClearCache()
    {
        _cachedContent = null;
    }

    private static bool LoadCDNContent()
    {
        try
        {
            var tableResult = Backend.CDN.Content.Table.Get();
            if (!tableResult.IsSuccess())
            {
                Debug.LogWarning($"[ChartLoader] CDN Table 조회 실패: {tableResult.GetStatusCode()}");
                return false;
            }

            var contentResult = Backend.CDN.Content.Get(tableResult.GetContentTableItemList());
            if (!contentResult.IsSuccess())
            {
                Debug.LogWarning($"[ChartLoader] CDN Content 다운로드 실패: {contentResult.GetStatusCode()}");
                return false;
            }

            Backend.CDN.Content.Local.Save(contentResult.GetContentList(), out _);
            var localResult = Backend.CDN.Content.Local.Load();
            if (!localResult.IsSuccess())
            {
                Debug.LogWarning("[ChartLoader] CDN Local 로드 실패");
                return false;
            }

            // chartId 기반 딕셔너리를 chartName 기반으로 변환
            var byId = localResult.GetContentDictionarySortByChartId();
            _cachedContent = new Dictionary<string, BackEnd.Content.ContentItem>();
            var names = new System.Text.StringBuilder();
            foreach (var kv in byId)
            {
                string name = kv.Value.chartName?.Trim();
                if (!string.IsNullOrEmpty(name))
                    _cachedContent[name] = kv.Value;
                names.Append($"  {kv.Key} → [{name}]\n");
            }
            Debug.Log($"[ChartLoader] CDN 로드 완료: {_cachedContent.Count}개 차트\n{names}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ChartLoader] CDN 예외: {e.Message}");
            return false;
        }
    }
}
