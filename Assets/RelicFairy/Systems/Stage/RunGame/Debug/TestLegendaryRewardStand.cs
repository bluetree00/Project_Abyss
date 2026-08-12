using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Test씬 레전드리 룬 보상 스탠드.
/// 지정 속성(zoneId)의 레전드리 룬 3종을 ClearRewardTrigger를 통해 무한 반복 선택 제공.
/// ClearRewardTrigger 소멸 감지 시 자동 재생성.
/// </summary>
public class TestLegendaryRewardStand : MonoBehaviour
{
    [SerializeField] private string zoneId = "FIRE"; // FIRE/ICE/ELECTRIC/GRASS/LIGHT/DARK

    private ClearRewardTrigger _activeTrigger;
    private float _nextRetryTime;

    private void Start()
    {
        SpawnTrigger();
    }

    private void Update()
    {
        if (_activeTrigger == null && Time.time >= _nextRetryTime)
            SpawnTrigger();
    }

    private void SpawnTrigger()
    {
        var run = GameRunBootstrapper.Instance?.Run;
        if (run == null)
        {
            _nextRetryTime = Time.time + 1f;
            return;
        }

        var rewards = BuildRewards();
        if (rewards == null || rewards.Count == 0) return;

        var go = new GameObject($"LegendaryReward_{zoneId}");
        go.transform.position = transform.position;

        _activeTrigger = go.AddComponent<ClearRewardTrigger>();
        _activeTrigger.Initialize(run, rewards, isBossRoom: false, isChoice: true, choiceRounds: 1);
    }

    private List<(RuntimeItemData data, ItemSO so)> BuildRewards()
    {
        return zoneId switch
        {
            "FIRE"     => BuildFireRewards(),
            "ICE"      => BuildIceRewards(),
            "ELECTRIC" => BuildElecRewards(),
            "GRASS"    => BuildGrassRewards(),
            "LIGHT"    => BuildLightRewards(),
            "DARK"     => BuildDarkRewards(),
            _          => null,
        };
    }

    // ── FIRE ─────────────────────────────────────────────────────

    private static List<(RuntimeItemData, ItemSO)> BuildFireRewards() => new List<(RuntimeItemData, ItemSO)>
    {
        (MakeItem("item_t4_fire_aoe", "마그마 분출", 21, "FIRE",
            "FireLegendAoe", "OnTimer", 0.65f, 12f, 5f,
            "12초마다 반경 5m 화염 폭발 공격력 65%"), null),
        (MakeItem("item_t4_fire_single", "불사조 강타", 22, "FIRE",
            "FireLegendSingle", "OnHitCount", 3.5f, 12f, 0.5f,
            "12회 적중마다 공격력 350% 강타"), null),
        (MakeItem("item_t4_fire_proj", "화염 오브", 23, "FIRE",
            "FireLegendProjectile", "OnActivate", 0.8f, 3f, 5f,
            "전투 중 화염 오브 비행, 주기 0.8배 피해"), null),
    };

    // ── ICE ──────────────────────────────────────────────────────

    private static List<(RuntimeItemData, ItemSO)> BuildIceRewards() => new List<(RuntimeItemData, ItemSO)>
    {
        (MakeItem("item_t4_ice_aoe", "빙하 파동", 24, "ICE",
            "IceLegendAoe", "OnTimer", 0.5f, 10f, 2f,
            "10초마다 전 필드 공격력 50% + 이속 감소"), null),
        (MakeItem("item_t4_ice_single", "얼음 창 폭격", 25, "ICE",
            "IceLegendSingle", "OnTimer", 0.6f, 8f, 1f,
            "8초마다 최저체력 적 5발 + 빙하 폭파"), null),
        (MakeItem("item_t4_ice_field", "영구 빙판", 26, "ICE",
            "IceLegendField", "OnActivate", 0.15f, 3f, 0.5f,
            "룸 진입 시 빙판 생성, 3개 초당 0.15배 피해"), null),
    };

    // ── ELECTRIC ─────────────────────────────────────────────────

    private static List<(RuntimeItemData, ItemSO)> BuildElecRewards() => new List<(RuntimeItemData, ItemSO)>
    {
        (MakeItem("item_t4_elec_aoe", "천둥 폭격", 27, "ELECTRIC",
            "ElecLegendAoe", "OnTimer", 0.7f, 10f, 3f,
            "10초마다 낙뢰 3발 공격력 70%"), null),
        (MakeItem("item_t4_elec_single", "과전류 포박", 28, "ELECTRIC",
            "ElecLegendSingle", "OnTimer", 0.4f, 15f, 2f,
            "15초마다 단일 포박 + 방전 폭발 200%"), null),
        (MakeItem("item_t4_elec_proj", "전자기 구체", 29, "ELECTRIC",
            "ElecLegendProjectile", "OnActivate", 0.6f, 3f, 1.2f,
            "전투 중 전자기 구체, 3초마다 볼트 0.6배 + 폭발 1.2배"), null),
    };

    // ── GRASS ─────────────────────────────────────────────────────

    private static List<(RuntimeItemData, ItemSO)> BuildGrassRewards() => new List<(RuntimeItemData, ItemSO)>
    {
        (MakeItem("item_t4_grass_aoe", "포자 폭발", 30, "GRASS",
            "GrassLegendAoe", "OnTimer", 0.4f, 10f, 0.12f,
            "10초마다 전 필드 독 40% + DoT"), null),
        (MakeItem("item_t4_grass_single", "덩굴 구속", 31, "GRASS",
            "GrassLegendSingle", "OnTimer", 0.8f, 20f, 2.4f,
            "20초마다 최강 적 구속 + 독 240%"), null),
        (MakeItem("item_t4_grass_proj", "사방 독 화살", 32, "GRASS",
            "GrassLegendProjectile", "OnActivate", 0.7f, 3f, 1f,
            "전투 중 독 화살, 3초마다 0.7배 관통"), null),
    };

    // ── LIGHT ─────────────────────────────────────────────────────

    private static List<(RuntimeItemData, ItemSO)> BuildLightRewards() => new List<(RuntimeItemData, ItemSO)>
    {
        (MakeItem("item_t4_light_aoe", "신성 폭발", 33, "LIGHT",
            "LightLegendAoe", "OnTimer", 0.8f, 12f, 0.4f,
            "12초마다 반경 6m 공격력 80%"), null),
        (MakeItem("item_t4_light_single", "성스러운 심판", 34, "LIGHT",
            "LightLegendSingle", "OnCritCount", 1.5f, 15f, 5f,
            "치명타 15회마다 집중 광선 5타 150%"), null),
        (MakeItem("item_t4_light_proj", "영원의 성검", 35, "LIGHT",
            "LightLegendProjectile", "OnActivate", 1f, 2f, 0f,
            "전투 중 성검 비행, 2초마다 1.0배"), null),
    };

    // ── DARK ──────────────────────────────────────────────────────

    private static List<(RuntimeItemData, ItemSO)> BuildDarkRewards() => new List<(RuntimeItemData, ItemSO)>
    {
        (MakeItem("item_t4_dark_aoe", "심연 잠식", 36, "DARK",
            "DarkLegendAoe", "OnTimer", 0.6f, 8f, 5f,
            "8초마다 전 필드 공격력 60%"), null),
        (MakeItem("item_t4_dark_single", "그림자 분신", 37, "DARK",
            "DarkLegendSingle", "OnTimer", 1.5f, 15f, 5f,
            "15초마다 분신 5체 공격력 150%"), null),
        (MakeItem("item_t4_dark_proj", "어둠의 낫", 38, "DARK",
            "DarkLegendProjectile", "OnActivate", 0.9f, 5f, 3f,
            "전투 중 낫 5개 비행, 관통"), null),
    };

    // ── Helper ────────────────────────────────────────────────────

    private static RuntimeItemData MakeItem(string id, string name, int shapeId, string element,
                                             string effectType, string trigger,
                                             float v1, float v2, float v3, string desc)
    {
        return new RuntimeItemData
        {
            itemId      = id,
            displayName = name,
            rarity      = ItemRarity.Legendary,
            shapeId     = shapeId,
            element     = element,
            effects     = new List<ItemEffectSlot>
            {
                new ItemEffectSlot
                {
                    effectType  = effectType,
                    trigger     = trigger,
                    value       = v1,
                    value2      = v2,
                    value3      = v3,
                    description = desc,
                }
            }
        };
    }
}
