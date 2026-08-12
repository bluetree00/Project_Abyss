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

    // 메타 로컬 저장소(슬롯별 권위) + 뒤끝 병행(백업/텔레메트리). 보수적 이중 기록.
    private readonly LocalFileMetaStore _metaStore = new LocalFileMetaStore();

    /// <summary>계정의 원래 진행이 사는 슬롯. 서버/레거시 값은 여기로만 흘러든다.</summary>
    private const int PrimarySlot = 0;

    /// <summary>현재 Data가 어느 슬롯 것인지. -1 = 아직 슬롯을 안 읽음.</summary>
    private int _loadedSlot = -1;

    /// <summary>
    /// 로그인 시 서버에서 읽은 값의 사본. 0번 슬롯에 로컬 파일이 <b>처음</b> 생길 때 시작값으로 딱 한 번 쓰인다.
    /// 한 번 쓰고 버리는 이유: 이게 남아 있으면 0번 슬롯에서 "새 게임"을 눌러도 서버 진행이 되살아난다.
    /// </summary>
    private UserGameData _serverSnapshot;

    private static int ActiveSlot => RunProgressManager.Instance?.ActiveSlotIndex ?? 0;

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

        // 서버에서 읽은 값을 따로 붙잡아 둔다 — 0번 슬롯 최초 생성 때의 시작값이다.
        _serverSnapshot = JsonUtility.FromJson<UserGameData>(JsonUtility.ToJson(Data));

        // 로컬 슬롯 메타가 권위. 로그인 직후엔 아직 슬롯을 안 골랐으므로 0번으로 시작한다.
        LoadLocalMeta(ActiveSlot);
    }

    /// <summary>
    /// 활성 슬롯의 메타를 현재 Data로 올린다(로비에서 슬롯을 고른 직후 호출).
    /// 슬롯은 독립 세이브라, 갈아끼우지 않으면 직전 슬롯의 각성·정수가 그대로 따라온다.
    /// </summary>
    public void ApplyActiveSlot()
    {
        int slot = ActiveSlot;
        if (_loadedSlot == slot) return;
        LoadLocalMeta(slot);
    }

    /// <summary>
    /// 슬롯 삭제/새 게임으로 그 슬롯 메타 파일이 사라졌을 때 — 메모리에 남은 값을 버리고 다시 세운다.
    /// 이게 없으면 방금 지운 슬롯의 각성이 화면에 그대로 떠 있다.
    /// </summary>
    public void InvalidateSlot(int slot)
    {
        if (_loadedSlot != slot) return;
        _loadedSlot = -1;
        LoadLocalMeta(slot);
    }

    /// <summary>슬롯 메타를 읽어 Data에 앉힌다. 파일이 없으면 그 슬롯의 시작값을 정하고 곧바로 기록한다.</summary>
    private void LoadLocalMeta(int slot)
    {
        _metaStore.MigrateIfNeeded(slot);   // 레거시 meta_save.json → 슬롯 파일(1회, 원본 보관)

        var local = _metaStore.Load(slot);
        if (local != null)
        {
            Data = local;
        }
        else if (slot == PrimarySlot && _serverSnapshot != null)
        {
            // 계정의 원래 진행을 0번 슬롯이 물려받는다(기존 플레이어 이관). 딱 한 번만.
            Data = _serverSnapshot;
            _serverSnapshot = null;
            _metaStore.Save(slot, Data);
        }
        else
        {
            // 처음 쓰는 슬롯 — 다른 슬롯의 성장을 물려주지 않는다.
            Data.Reset();
            _metaStore.Save(slot, Data);
        }

        _loadedSlot = slot;
        OnDataLoaded?.Invoke();
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

    /// <summary>현재 Data를 로컬(권위) + 뒤끝(백업/텔레메트리)에 반영. 재화 직접 변경 후에도 호출 가능.</summary>
    public async UniTask SaveAsync()
    {
        // 로컬(슬롯) 우선 저장 — 오프라인에도 각성·정수가 보존된다.
        // 아직 슬롯을 안 읽었으면(로그인 전 재화 변경 등) 활성 슬롯에 기록한다.
        int slot = _loadedSlot >= 0 ? _loadedSlot : ActiveSlot;
        _metaStore.Save(slot, Data);

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

    /// <summary>리치 조우 횟수를 1 증가시키고 서버에 저장한다. 보스 초기화 시 즉시 호출.</summary>
    public async UniTask RecordLichEncounterAsync()
    {
        Data.lichEncounterCount++;
        await SaveAsync();
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
        Data.lichEncounterCount   = SafeInt(row, "lichEncounterCount",   0);
        Data.sealBrokenBossIds    = SafeString(row, "sealBrokenBossIds", "");

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
        { "lichEncounterCount",   Data.lichEncounterCount   },
        { "sealBrokenBossIds",    Data.sealBrokenBossIds ?? "" },
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

    private static string SafeString(JsonData row, string key, string fallback)
    {
        try { return row.ContainsKey(key) ? row[key].ToString() : fallback; }
        catch { return fallback; }
    }
}
