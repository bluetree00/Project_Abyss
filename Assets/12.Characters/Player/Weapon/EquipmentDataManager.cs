using BackEnd;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using System;
using LitJson;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

// -----------------------------
// 데이터 모델들 (확장된 구조)
// -----------------------------
[Serializable]
public class AnimationVariantModel
{
    public string animKey;       // Addressables key for AnimationClip or AnimationSet
    public string vfxKey;        // Addressables key for VFX prefab
    public string abilityId;     // optional ability id
    public float weight = 1f;    // 선택 시 가중치 (기본 1)
    public string eventMapId;    // optional: 애니 이벤트 매핑 id
}

[Serializable]
public class WeaponComboSlotModel
{
    public int index;
    public List<AnimationVariantModel> variants = new List<AnimationVariantModel>();
}

[Serializable]
public class WeaponDataModel
{
    public int weapon_id;
    public string weapon_key;    // 예: "sword_001"
    public string display_name;
    public string rarity;
    public int stat_version;

    // asset keys
    public string prefabKey;     // Addressables key for the weapon prefab
    public string iconKey;       // Addressables key for icon

    // base stats / modifiers
    public float baseAttack;
    public float baseDefense;
    public float baseCritRate;
    public float percentAttack;
    public int levelRequirement;

    // combo slots (ground / air)
    public List<WeaponComboSlotModel> comboSlots = new List<WeaponComboSlotModel>();
    public List<WeaponComboSlotModel> airComboSlots = new List<WeaponComboSlotModel>();
}

[Serializable]
public class WeaponDataCollection
{
    public List<WeaponDataModel> weapons = new List<WeaponDataModel>();
}

// -----------------------------
// EquipmentDataManager
// -----------------------------
public class EquipmentDataManager
{
    private const string WeaponDataFileName = "weapon_data.json";
    private string FilePath => Path.Combine(Application.persistentDataPath, WeaponDataFileName);

    // Chart ID: 서버에서 장비 데이터를 관리하는 ChartId로 변경하세요
    private const string ChartId = "YOUR_WEAPON_CHART_ID";

    // 내부 딕셔너리: weapon_id 또는 weapon_key로 조회 가능
    private readonly Dictionary<int, WeaponDataModel> _weaponById = new();
    private readonly Dictionary<string, WeaponDataModel> _weaponByKey = new();

    // Addressables 로드 캐시 (간단한 Object 캐시)
    private readonly Dictionary<string, UnityEngine.Object> _assetCache = new();

    public bool IsInitialized { get; private set; } = false;

    // 초기화
    public async UniTask InitializeAsync()
    {
        if (File.Exists(FilePath))
        {
            Debug.Log("[EquipmentDataManager] 로컬 장비 데이터 로드");
            LoadFromJson();
            Debug.Log($"[EquipmentDataManager] 로컬 데이터 경로: {FilePath}");
            Debug.Log("[EquipmentDataManager] 서버에서 변경된 장비 데이터만 갱신");
            await LoadFromServerAsync(); // stat_version 비교 후 부분 갱신
        }
        else
        {
            Debug.Log("[EquipmentDataManager] 로컬 데이터 없음. 서버에서 전체 다운로드");
            await LoadFromServerAsync();
        }

        IsInitialized = true;
    }

    #region 로컬 파일 입출력
    private void LoadFromJson()
    {
        try
        {
            string json = File.ReadAllText(FilePath);
            var wrapper = JsonUtility.FromJson<WeaponDataCollection>(json);

            _weaponById.Clear();
            _weaponByKey.Clear();

            if (wrapper?.weapons != null)
            {
                foreach (var w in wrapper.weapons)
                {
                    _weaponById[w.weapon_id] = w;
                    if (!string.IsNullOrEmpty(w.weapon_key))
                        _weaponByKey[w.weapon_key] = w;
                }
            }
            Debug.Log($"[EquipmentDataManager] 로컬에서 {_weaponById.Count}개의 장비 로드됨");
        }
        catch (Exception e)
        {
            Debug.LogError($"[EquipmentDataManager] 로컬 JSON 로드 실패: {e.Message}");
        }
    }

    private void SaveToJson()
    {
        try
        {
            var list = new List<WeaponDataModel>(_weaponById.Values);
            var collection = new WeaponDataCollection { weapons = list };
            string json = JsonUtility.ToJson(collection, true);
            File.WriteAllText(FilePath, json);
            Debug.Log($"[EquipmentDataManager] 장비 데이터 {list.Count}개 저장 완료 at {FilePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[EquipmentDataManager] 로컬 JSON 저장 실패: {e.Message}");
        }
    }
    #endregion

    #region 서버 로드 및 버전 비교
    // 서버에서 Chart 내용을 받아와 부분 갱신 또는 전체 갱신 수행
    private async UniTask LoadFromServerAsync()
    {
        var bro = Backend.Chart.GetChartContents(ChartId);

        if (!bro.IsSuccess())
        {
            Debug.LogError($"[EquipmentDataManager] 장비 데이터 서버 요청 실패: {bro.GetStatusCode()}");
            return;
        }

        var rows = bro.FlattenRows();
        int updateCount = 0;
        int addCount = 0;

        foreach (var rowObj in rows)
        {
            if (rowObj is not JsonData row) continue;

            // 파싱: 서버 컬럼명에 맞게 조정하세요
            int.TryParse(row["weapon_id"].ToString(), out int weaponId);
            string weaponKey = row.ContainsKey("weapon_key") ? row["weapon_key"].ToString() : $"weapon_{weaponId}";
            string displayName = row.ContainsKey("display_name") ? row["display_name"].ToString() : "";
            string prefabKey = row.ContainsKey("prefab_key") ? row["prefab_key"].ToString() : "";
            string iconKey = row.ContainsKey("icon_key") ? row["icon_key"].ToString() : "";
            string rarity = row.ContainsKey("rarity") ? row["rarity"].ToString() : "common";
            int.TryParse(row.ContainsKey("stat_version") ? row["stat_version"].ToString() : "0", out int statVersion);

            // stats (optional fields)
            float.TryParse(row.ContainsKey("baseAttack") ? row["baseAttack"].ToString() : "0", out float baseAttack);
            float.TryParse(row.ContainsKey("baseDefense") ? row["baseDefense"].ToString() : "0", out float baseDefense);
            float.TryParse(row.ContainsKey("baseCritRate") ? row["baseCritRate"].ToString() : "0", out float baseCritRate);
            float.TryParse(row.ContainsKey("percentAttack") ? row["percentAttack"].ToString() : "0", out float percentAttack);
            int.TryParse(row.ContainsKey("levelRequirement") ? row["levelRequirement"].ToString() : "0", out int levelRequirement);

            // comboSlots와 airComboSlots: JSON 문자열로 들어오는 복잡한 구조(variants 포함)
            List<WeaponComboSlotModel> comboSlots = ParseComboSlotsFromRowVariant(row, "comboSlots");
            List<WeaponComboSlotModel> airSlots = ParseComboSlotsFromRowVariant(row, "airComboSlots");

            // 기존 데이터 확인 및 버전 체크
            if (_weaponById.TryGetValue(weaponId, out var existing))
            {
                if (statVersion <= existing.stat_version)
                    continue; // 기존이 최신이면 스킵
            }

            var wd = new WeaponDataModel
            {
                weapon_id = weaponId,
                weapon_key = weaponKey,
                display_name = displayName,
                rarity = rarity,
                prefabKey = prefabKey,
                iconKey = iconKey,
                stat_version = statVersion,
                baseAttack = baseAttack,
                baseDefense = baseDefense,
                baseCritRate = baseCritRate,
                percentAttack = percentAttack,
                levelRequirement = levelRequirement,
                comboSlots = comboSlots ?? new List<WeaponComboSlotModel>(),
                airComboSlots = airSlots ?? new List<WeaponComboSlotModel>()
            };

            bool existed = _weaponById.ContainsKey(weaponId);
            _weaponById[weaponId] = wd;
            if (!string.IsNullOrEmpty(weaponKey))
                _weaponByKey[weaponKey] = wd;

            if (existed) updateCount++; else addCount++;
        }

        SaveToJson();
        Debug.Log($"[EquipmentDataManager] 서버에서 장비 중 {updateCount}개 갱신, {addCount}개 추가됨");
        await UniTask.Yield();
    }

    /// <summary>
    /// 서버의 comboSlots 필드가 JSON 문자열로 들어오는 경우(variants 포함) 파싱 유틸.
    /// 예상 포맷:
    /// [
    ///   { "index":0, "variants":[ { "animKey":"anim://..", "abilityId":"..", "vfxKey":"vfx://..", "weight":1 }, ... ] },
    ///   ...
    /// ]
    /// 
    /// 또한 기존 단순 포맷(한 슬롯에 단일 animKey 등)도 어느정도 호환 처리합니다.
    /// </summary>
    private List<WeaponComboSlotModel> ParseComboSlotsFromRowVariant(JsonData row, string columnName)
    {
        try
        {
            if (!row.ContainsKey(columnName)) return null;
            var raw = row[columnName].ToString();
            if (string.IsNullOrEmpty(raw)) return null;

            var data = JsonMapper.ToObject(raw);
            var list = new List<WeaponComboSlotModel>();

            if (data.IsArray)
            {
                foreach (JsonData slotItem in data)
                {
                    var slot = new WeaponComboSlotModel();

                    // index
                    if (slotItem.ContainsKey("index"))
                        int.TryParse(slotItem["index"].ToString(), out slot.index);

                    // variants: expect array. if not exist, fallback to legacy keys (animKey/vfxKey/abilityId)
                    if (slotItem.ContainsKey("variants"))
                    {
                        var variantsData = slotItem["variants"];
                        if (variantsData.IsArray)
                        {
                            foreach (JsonData v in variantsData)
                            {
                                var variant = new AnimationVariantModel();
                                variant.animKey = v.ContainsKey("animKey") ? v["animKey"].ToString() : null;
                                variant.vfxKey = v.ContainsKey("vfxKey") ? v["vfxKey"].ToString() : null;
                                variant.abilityId = v.ContainsKey("abilityId") ? v["abilityId"].ToString() : null;
                                if (v.ContainsKey("weight")) float.TryParse(v["weight"].ToString(), out variant.weight);
                                variant.eventMapId = v.ContainsKey("eventMapId") ? v["eventMapId"].ToString() : null;
                                slot.variants.Add(variant);
                            }
                        }
                    }
                    else
                    {
                        // legacy single-variant support: look for animKey/vfxKey/abilityId at slot level
                        var variant = new AnimationVariantModel();
                        variant.animKey = slotItem.ContainsKey("animKey") ? slotItem["animKey"].ToString() : null;
                        variant.vfxKey = slotItem.ContainsKey("vfxKey") ? slotItem["vfxKey"].ToString() : null;
                        variant.abilityId = slotItem.ContainsKey("abilityId") ? slotItem["abilityId"].ToString() : null;
                        if (slotItem.ContainsKey("weight")) float.TryParse(slotItem["weight"].ToString(), out variant.weight);
                        slot.variants.Add(variant);
                    }

                    // if no index provided, try to infer from order (fallback)
                    if (slot.index == 0 && slotItem.ContainsKey("index") == false)
                    {
                        slot.index = list.Count; // fallback index
                    }

                    list.Add(slot);
                }
            }

            return list;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[EquipmentDataManager] comboSlots 파싱 실패({columnName}): {e.Message}");
            return null;
        }
    }
    #endregion

    #region 조회 API
    public WeaponDataModel GetWeaponById(int id)
    {
        if (_weaponById.TryGetValue(id, out var w)) return w;
        Debug.LogWarning($"[EquipmentDataManager] weapon id {id} 찾을 수 없음");
        return null;
    }

    public bool TryGetWeaponById(int id, out WeaponDataModel weapon)
    {
        return _weaponById.TryGetValue(id, out weapon);
    }

    public bool TryGetWeaponByKey(string key, out WeaponDataModel weapon)
    {
        return _weaponByKey.TryGetValue(key, out weapon);
    }

    public WeaponDataModel[] GetAllWeapons()
    {
        var arr = new WeaponDataModel[_weaponById.Count];
        _weaponById.Values.CopyTo(arr, 0);
        return arr;
    }
    #endregion

    #region Addressables 헬퍼 (로딩/캐시/프리로드)
    // 간단 캐시 조회
    public bool TryGetCachedAsset<T>(string key, out T asset) where T : UnityEngine.Object
    {
        if (_assetCache.TryGetValue(key, out var o))
        {
            asset = o as T;
            return asset != null;
        }
        asset = null;
        return false;
    }

    // Addressables에서 로드 후 캐시에 저장
    public async UniTask<T> LoadAssetAsync<T>(string key) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(key)) return null;

        if (_assetCache.TryGetValue(key, out var cached) && cached is T cachedT)
        {
            return cachedT;
        }

        var handle = Addressables.LoadAssetAsync<T>(key);
        await handle.ToUniTask();

        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            _assetCache[key] = handle.Result as UnityEngine.Object;
            return handle.Result;
        }
        else
        {
            Debug.LogWarning($"[EquipmentDataManager] Addressables 로드 실패: {key}");
            return null;
        }
    }

    // 여러 키를 병렬로 프리로드 (중복 키는 내부 캐시로 한 번만)
    public async UniTask PreloadAssetsAsync(IEnumerable<string> keys, int maxConcurrent = 8)
    {
        if (keys == null) return;
        var distinct = new HashSet<string>(keys);
        var tasks = new List<UniTask>();

        foreach (var key in distinct)
        {
            if (string.IsNullOrEmpty(key)) continue;
            if (_assetCache.ContainsKey(key)) continue;

            // Throttling: 단순 구현. 필요 시 SemaphoreSlim 등으로 대체
            tasks.Add(LoadAssetAsync<UnityEngine.Object>(key));
            // optionally limit concurrency by batching
            if (tasks.Count >= maxConcurrent)
            {
                await UniTask.WhenAll(tasks);
                tasks.Clear();
            }
        }

        if (tasks.Count > 0)
            await UniTask.WhenAll(tasks);
    }
    #endregion

    #region 유틸 / 헬퍼
    // Animator Override 매핑을 위해 슬롯 키 목록을 뽑아오는 편의 메소드
    public IEnumerable<string> GetWeaponAddressableKeys(WeaponDataModel weapon)
    {
        if (weapon == null) yield break;
        if (!string.IsNullOrEmpty(weapon.prefabKey)) yield return weapon.prefabKey;
        if (!string.IsNullOrEmpty(weapon.iconKey)) yield return weapon.iconKey;

        if (weapon.comboSlots != null)
        {
            foreach (var s in weapon.comboSlots)
            {
                if (s.variants == null) continue;
                foreach (var v in s.variants)
                {
                    if (!string.IsNullOrEmpty(v.animKey)) yield return v.animKey;
                    if (!string.IsNullOrEmpty(v.vfxKey)) yield return v.vfxKey;
                }
            }
        }

        if (weapon.airComboSlots != null)
        {
            foreach (var s in weapon.airComboSlots)
            {
                if (s.variants == null) continue;
                foreach (var v in s.variants)
                {
                    if (!string.IsNullOrEmpty(v.animKey)) yield return v.animKey;
                    if (!string.IsNullOrEmpty(v.vfxKey)) yield return v.vfxKey;
                }
            }
        }
    }

    #endregion
}

// 보조: UniTask 동시성 스케줄러 생성 유틸 (필요 시 구현을 교체)
static class UniTaskUtility
{
    public static object CreateLimitedConcurrencyScheduler(int maxConcurrent)
    {
        // 샘플 코드에서는 사용하지 않음. 필요하면 SemaphoreSlim/TaskScheduler 사용 구현하세요.
        return null;
    }
}
