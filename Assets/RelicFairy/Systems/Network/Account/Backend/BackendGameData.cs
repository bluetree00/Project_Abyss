using System;
using BackEnd;
using Cysharp.Threading.Tasks;
using LitJson;
using UnityEngine;

/// <summary>
/// USER_DATA 테이블 관리 싱글톤 (DDOL).
/// 로비 재화(level/gold/jewel/heart)와 인게임 누적 통계(totalRuns/totalClears 등)를 서버에 저장한다.
///
/// ■ 호출 흐름
///   로그인 성공          → LoadAsync()
///   회원가입 완료        → InsertAsync()
///   런 종료              → ApplyRunResultAsync(EndRunResult)
///   재화 직접 변경 후    → SaveAsync()
/// </summary>
public class BackendGameData : MonoBehaviour
{
    // ── Singleton ──────────────────────────────────────────────────────────
    public static BackendGameData Instance { get; private set; }

    // ── State ──────────────────────────────────────────────────────────────
    public UserGameData Data { get; private set; } = new UserGameData();

    /// <summary>Load 완료 또는 ApplyRunResult 저장 완료 시 발행.</summary>
    public event Action OnDataLoaded;

    private string _rowInDate;

    // ── Lifecycle ──────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>서버에서 USER_DATA를 로드한다. 데이터가 없으면 자동으로 Insert.</summary>
    public async UniTask LoadAsync()
    {
        var tcs = new UniTaskCompletionSource();

        Backend.GameData.GetMyData("USER_DATA", new Where(), callback =>
        {
            if (callback.IsSuccess())
            {
                try
                {
                    var json = callback.FlattenRows();

                    if (json.Count <= 0)
                    {
                        Debug.Log("[BackendGameData] USER_DATA 없음 — Insert 실행");
                        InsertAsync().Forget();
                    }
                    else
                    {
                        _rowInDate = json[0]["inDate"].ToString();
                        ParseRow(json[0]);
                        Debug.Log("[BackendGameData] Load 성공");
                        OnDataLoaded?.Invoke();
                    }
                }
                catch (Exception e)
                {
                    Data.Reset();
                    Debug.LogError($"[BackendGameData] Load 파싱 실패: {e}");
                }
            }
            else
            {
                Debug.LogError($"[BackendGameData] Load 실패: {callback.GetMessage()}");
            }

            tcs.TrySetResult();
        });

        await tcs.Task;
    }

    /// <summary>회원가입 완료 시 호출. USER_DATA 테이블에 초기 row를 삽입한다.</summary>
    public async UniTask InsertAsync()
    {
        Data.Reset();
        var param = BuildParam();

        var tcs = new UniTaskCompletionSource();

        Backend.GameData.Insert("USER_DATA", param, callback =>
        {
            if (callback.IsSuccess())
            {
                _rowInDate = callback.GetInDate();
                Debug.Log($"[BackendGameData] Insert 성공: rowInDate={_rowInDate}");
                OnDataLoaded?.Invoke();
            }
            else
            {
                Debug.LogError($"[BackendGameData] Insert 실패: {callback.GetMessage()}");
            }

            tcs.TrySetResult();
        });

        await tcs.Task;
    }

    /// <summary>현재 Data를 서버에 반영(UpdateV2). 재화 직접 변경 후에도 호출 가능.</summary>
    public async UniTask SaveAsync()
    {
        if (string.IsNullOrEmpty(_rowInDate))
        {
            Debug.LogWarning("[BackendGameData] SaveAsync: rowInDate 없음 — Insert로 대체");
            await InsertAsync();
            return;
        }

        var param = BuildParam();
        var tcs   = new UniTaskCompletionSource();

        Backend.GameData.UpdateV2("USER_DATA", _rowInDate, Backend.UserInDate, param, callback =>
        {
            if (callback.IsSuccess())
                Debug.Log("[BackendGameData] Save 성공");
            else
                Debug.LogError($"[BackendGameData] Save 실패: {callback.GetMessage()}");

            tcs.TrySetResult();
        });

        await tcs.Task;
    }

    /// <summary>런 종료 결과를 영구 데이터에 반영하고 서버에 저장한다.</summary>
    public async UniTask ApplyRunResultAsync(EndRunResult result)
    {
        Data.ApplyRunResult(result);
        await SaveAsync();
        OnDataLoaded?.Invoke();
    }

    // ── Private ────────────────────────────────────────────────────────────

    private void ParseRow(JsonData row)
    {
        Data.level           = SafeInt(row,   "level",          1);
        Data.experience      = SafeFloat(row, "exp",            0f);
        Data.gold            = SafeInt(row,   "gold",           0);
        Data.jewel           = SafeInt(row,   "jewel",          0);
        Data.heart           = SafeInt(row,   "heart",          30);
        Data.totalRuns       = SafeInt(row,   "totalRuns",      0);
        Data.totalClears     = SafeInt(row,   "totalClears",    0);
        Data.highestChapter  = SafeInt(row,   "highestChapter", 0);
        Data.totalGoldEarned = SafeInt(row,   "totalGoldEarned",0);

        // 유물의 각성
        Data.abyssEssence         = SafeInt(row, "abyssEssence",         0);
        Data.awakeningLevelSword  = SafeInt(row, "awakeningLevelSword",  0);
        Data.awakeningLevelShield = SafeInt(row, "awakeningLevelShield", 0);
        Data.awakeningLevelHeart  = SafeInt(row, "awakeningLevelHeart",  0);
        Data.awakeningLevelStep   = SafeInt(row, "awakeningLevelStep",   0);
        Data.awakeningLevelMana   = SafeInt(row, "awakeningLevelMana",   0);
        Data.awakeningLevelLuck   = SafeInt(row, "awakeningLevelLuck",   0);
    }

    private Param BuildParam() => new Param
    {
        { "level",                Data.level                },
        { "exp",                  Data.experience           },
        { "gold",                 Data.gold                 },
        { "jewel",                Data.jewel                },
        { "heart",                Data.heart                },
        { "totalRuns",            Data.totalRuns            },
        { "totalClears",          Data.totalClears          },
        { "highestChapter",       Data.highestChapter       },
        { "totalGoldEarned",      Data.totalGoldEarned      },
        // 유물의 각성
        { "abyssEssence",         Data.abyssEssence         },
        { "awakeningLevelSword",  Data.awakeningLevelSword  },
        { "awakeningLevelShield", Data.awakeningLevelShield },
        { "awakeningLevelHeart",  Data.awakeningLevelHeart  },
        { "awakeningLevelStep",   Data.awakeningLevelStep   },
        { "awakeningLevelMana",   Data.awakeningLevelMana   },
        { "awakeningLevelLuck",   Data.awakeningLevelLuck   },
    };

    private static int SafeInt(JsonData row, string key, int fallback)
    {
        try { return row.ContainsKey(key) ? int.Parse(row[key].ToString()) : fallback; }
        catch { return fallback; }
    }

    private static float SafeFloat(JsonData row, string key, float fallback)
    {
        try { return row.ContainsKey(key) ? float.Parse(row[key].ToString()) : fallback; }
        catch { return fallback; }
    }
}
