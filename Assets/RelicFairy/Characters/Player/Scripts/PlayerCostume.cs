using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어 의상 — 유물별 · 기억 성장에 따라 덧입기(10-08 · 기획 플레이어의상_유물별덧입기).
/// 프리팹 기본 = 0단계(견습 기사 옷 · 망토 끔 · 유물 없음). 유물이 적용되면 그 유물의 <see cref="RelicCostumeSO"/>로 입히고,
/// 런의 유물 기억 조각 최고 등급으로 단계를 정한다 — 찬란 3 · 선명 2 · 그 외 1.
///   1 = 견습 옷(유물 색) + 망토 · 2 = + 흉갑 · 팔 · 어깨 · 3 = + 갑옷 치마 + 망토 테두리 빛
/// 런 안에서 단계가 오르면 새로 입는 부위(와 재질이 바뀐 망토)가 유물 색 테두리 디졸브로 나타난다(사용자 「의상이 변할 때 디졸브 연출로」).
/// 스폰 · 불러오기 때 처음 입히는 건 바로(연출 없음).
/// 부위는 GameObject.SetActive로만 켜고 끈다(입장 연출이 렌더러 enabled를 일괄로 켠다). 재질은 미리 만든 재질 에셋을 넣는다(인스턴스 없음).
/// 거점 소환 디졸브가 도는 동안 바꾸면 디졸브가 끝날 때 원래 재질로 되돌리므로 끝난 뒤 입힌다.
/// </summary>
public sealed class PlayerCostume : MonoBehaviour
{
    // ── Constants: CombatGirls 모델 부위 이름 ─────────────────────
    private const string BodySquire = "Body_Squire";
    private const string BodyCloth  = "Body_Cloth";
    private const string BodyFull   = "Body_Full";
    private const string Squire     = "Squire_Cloth";
    private const string Top        = "SwordAndShiled_Cloth_Top";
    private const string Pauldrons  = "SwordAndShiled_Clotth_Pauldrons";
    private const string Skirt      = "SwordAndShiled_Cloth_Skirt";
    private const string Boots      = "SwordAndShiled_Cloth_Boots";   // 견습 부츠와 겹친다 — 쓰지 않는다
    private const string Hair       = "SwordAndShiled_Hair";
    private const float  DissolveSeconds = 0.9f;
    private static readonly string[] Layers = { Top, Pauldrons, Skirt };   // 단계가 오를 때 디졸브로 나타나는 부위

    // ── Private ───────────────────────────────────────────────────
    private readonly Dictionary<string, GameObject> _parts = new();
    private Renderer[] _renderers;
    private RelicCostumeSO _costume;
    private Color _edgeColor = Color.white;
    private PlayerLoadout _loadout;
    private int _stage = -1;
    private bool _pending;    // 디졸브 중이라 미룬 입히기
    private bool _animate;    // 미룬 입히기가 단계 오름(디졸브)인지

    // ── Properties ────────────────────────────────────────────────
    /// <summary>지금 입은 단계(0 = 유물 없음).</summary>
    public int Stage => _stage < 0 ? 0 : _stage;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake() => CacheParts();

    private void OnEnable()
    {
        _loadout = AppBootstrapper.Instance != null ? AppBootstrapper.Instance.Loadout : null;
        if (_loadout != null) _loadout.RelicPartsChanged += OnRelicPartsChanged;
    }

    private void Update()
    {
        if (_pending) TryWear();
    }

    private void OnDisable()
    {
        if (_loadout != null) _loadout.RelicPartsChanged -= OnRelicPartsChanged;
        _loadout = null;
    }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>유물이 적용될 때(PlayerController.ApplyRelic) — 그 유물의 의상과 지금 단계를 바로 입힌다.</summary>
    public void ApplyRelic(RelicClassSO relic)
    {
        _costume = relic != null ? relic.Costume : null;
        if (relic != null)
        {
            Color c = relic.ThemeColor(Color.white);
            _edgeColor = new Color(c.r * 2.2f, c.g * 2.2f, c.b * 2.2f, 1f);   // HDR — 디졸브 테두리를 유물 색으로 빛나게
        }
        _stage = -1;
        Refresh(animate: false);
    }

    /// <summary>의상 · 단계를 바로 입힌다(디졸브 확인 없음). 편집 모드 렌더 비교도 이 길로 입힌다.</summary>
    public void Wear(RelicCostumeSO costume, int stage)
    {
        CacheParts();
        bool relic = costume != null && stage >= 1;
        SetActive(BodySquire, true);
        SetActive(Squire, true);
        SetActive(BodyCloth, false);
        SetActive(BodyFull, false);
        SetActive(Boots, false);
        SetActive(Top, relic && stage >= 2);
        SetActive(Pauldrons, relic && stage >= 2);
        SetActive(Skirt, relic && stage >= 3);
        if (!relic) return;

        SetMaterial(Squire, costume.SquireMaterial);
        SetMaterial(Top, costume.ArmorMaterial);
        SetMaterial(Pauldrons, costume.ArmorMaterial);
        SetMaterial(Skirt, costume.ArmorMaterial);
        SetMaterial(Hair, costume.HairMaterial);
    }

    // ── Private Methods ───────────────────────────────────────────
    private void CacheParts()
    {
        if (_parts.Count > 0) return;
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (!_parts.ContainsKey(t.name)) _parts[t.name] = t.gameObject;
        _renderers = GetComponentsInChildren<Renderer>(true);
    }

    private void Refresh(bool animate)
    {
        if (_costume == null) return;   // 유물 없음 = 프리팹 그대로(0단계)
        int stage = ComputeStage();
        if (stage == _stage) return;
        _animate = animate && _stage >= 1 && stage > _stage;   // 런 안에서 오를 때만 연출
        _stage = stage;
        _pending = true;
        TryWear();
    }

    /// <summary>런의 유물 기억 조각 최고 등급 → 단계(찬란 3 · 선명 2 · 그 외 1).</summary>
    private int ComputeStage()
    {
        var best = RelicMemoryGrade.None;
        if (_loadout != null)
            foreach (var id in _loadout.RelicPartIds)
            {
                var g = _loadout.GetRelicPartGrade(id);
                if (g > best) best = g;
            }
        return best >= RelicMemoryGrade.Radiant ? 3 : best >= RelicMemoryGrade.Clear ? 2 : 1;
    }

    private void TryWear()
    {
        foreach (var r in _renderers)
            if (DissolveEffect.IsDissolving(r)) return;   // 디졸브가 끝나면 다시(Update가 다시 부른다)
        _pending = false;
        if (!_animate) { Wear(_costume, _stage); return; }

        // 단계 오름 — 입기 전 상태를 기억했다가, 새로 켜진 부위 · 재질이 바뀐 부위만 투명에서 디졸브로 나타나게 한다.
        // 재질을 먼저 바꾼 뒤 디졸브를 건다(디졸브는 시작 때 재질을 잡아 끝나면 그것으로 되돌린다).
        var wasOn = new bool[Layers.Length];
        var wasMat = new Material[Layers.Length];
        for (int i = 0; i < Layers.Length; i++)
            if (_parts.TryGetValue(Layers[i], out var go))
            {
                wasOn[i] = go.activeSelf;
                wasMat[i] = go.TryGetComponent<Renderer>(out var r) ? r.sharedMaterial : null;
            }
        Wear(_costume, _stage);
        int shown = 0;
        for (int i = 0; i < Layers.Length; i++)
        {
            if (!_parts.TryGetValue(Layers[i], out var go) || !go.activeSelf) continue;
            var mat = go.TryGetComponent<Renderer>(out var r) ? r.sharedMaterial : null;
            if (wasOn[i] && mat == wasMat[i]) continue;
            go.SetActive(false);   // 디졸브가 완전히 투명한 상태에서 켠다
            DissolveEffect.PlayAppear(go, DissolveSeconds, edgeColor: _edgeColor);
            shown++;
        }
        if (shown > 0) Managers.Sound?.PlayEvent(SoundEvent.CostumeUp);   // 단계 오름 한 번(10-12 4b 몫 · 83 표)
    }

    private void SetActive(string part, bool on)
    {
        if (_parts.TryGetValue(part, out var go) && go.activeSelf != on) go.SetActive(on);
    }

    private void SetMaterial(string part, Material mat)
    {
        if (mat == null || !_parts.TryGetValue(part, out var go) || !go.TryGetComponent<Renderer>(out var r)) return;
        var mats = r.sharedMaterials;
        bool changed = false;
        for (int i = 0; i < mats.Length; i++)
            if (mats[i] != mat) { mats[i] = mat; changed = true; }
        if (changed) r.sharedMaterials = mats;
    }

    // ── Event Handlers ────────────────────────────────────────────
    private void OnRelicPartsChanged() => Refresh(animate: true);
}
