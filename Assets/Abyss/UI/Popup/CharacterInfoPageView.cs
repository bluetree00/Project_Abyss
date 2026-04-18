using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// EscBookPopup의 캐릭터 정보 페이지 (기존 Skill 탭 대체).
/// 왼쪽 페이지: 캐릭터 아이콘 + 이름 + 패시브 + 스탯.
/// 오른쪽 페이지: 장착 무기 2종 (아이콘 + 스킬 설명).
/// pageSkill 오브젝트에 부착.
/// </summary>
public class CharacterInfoPageView : MonoBehaviour
{
    // ── Private ──
    private PlayerController _player;
    private PlayerRunState _playerState;
    private PlayerWeaponManager _weaponManager;
    private bool _bound;

    // ── Left Page ──
    private RectTransform _leftRoot;
    private Image _charIcon;
    private TMP_Text _charIconText;
    private TMP_Text _charName;
    private TMP_Text _passiveBlock;
    private TMP_Text _statBlock;

    // ── Right Page ──
    private RectTransform _rightRoot;
    private RectTransform _weapon0Root;
    private Image _weapon0Icon;
    private TMP_Text _weapon0IconText;
    private TMP_Text _weapon0Info;
    private RectTransform _weapon1Root;
    private Image _weapon1Icon;
    private TMP_Text _weapon1IconText;
    private TMP_Text _weapon1Info;

    // ── Lifecycle ──

    private void OnEnable()
    {
        Bind();
        Refresh();
    }

    private void OnDisable()
    {
        Unbind();
    }

    // ── Public Methods ──

    public void Refresh()
    {
        if (!_bound) Bind();
        RefreshLeftPage();
        RefreshRightPage();
    }

    // ── Private Methods ──

    private void Bind()
    {
        if (_bound) return;

        var run = GameRunBootstrapper.Instance?.Run;
        if (run == null) return;

        _player = run.Player;
        _playerState = run.PlayerState;
        _weaponManager = _player != null ? _player.WeaponManager : null;

        if (_player != null && _player.RuntimeStats != null)
            _player.RuntimeStats.OnChanged += Refresh;
        if (_playerState != null)
            _playerState.OnHpChanged += OnHpChanged;
        if (_weaponManager != null)
            _weaponManager.OnWeaponChanged += OnWeaponChanged;

        EnsureLeftPage();
        EnsureRightPage();
        _bound = true;
    }

    private void Unbind()
    {
        if (_player != null && _player.RuntimeStats != null)
            _player.RuntimeStats.OnChanged -= Refresh;
        if (_playerState != null)
            _playerState.OnHpChanged -= OnHpChanged;
        if (_weaponManager != null)
            _weaponManager.OnWeaponChanged -= OnWeaponChanged;
    }

    // ── Left Page Setup ──

    private void EnsureLeftPage()
    {
        if (_leftRoot != null) return;

        var existing = transform.Find("LeftPage");
        if (existing != null) Destroy(existing.gameObject);

        var go = new GameObject("LeftPage", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(RectMask2D));
        go.transform.SetParent(transform, false);

        _leftRoot = go.GetComponent<RectTransform>();
        _leftRoot.anchorMin = new Vector2(0.18f, 0.10f);
        _leftRoot.anchorMax = new Vector2(0.48f, 0.80f);
        _leftRoot.offsetMin = Vector2.zero;
        _leftRoot.offsetMax = Vector2.zero;

        var vlg = go.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(14, 14, 8, 8);
        vlg.spacing = 5f;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        // 아이콘 + 이름 헤더
        var headerGO = new GameObject("Header", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        headerGO.transform.SetParent(go.transform, false);
        var headerLayout = headerGO.GetComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 8f;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        var headerLE = headerGO.AddComponent<LayoutElement>();
        headerLE.preferredHeight = 50f;

        // 캐릭터 아이콘 박스
        var iconGO = new GameObject("CharIcon", typeof(RectTransform), typeof(Image));
        iconGO.transform.SetParent(headerGO.transform, false);
        _charIcon = iconGO.GetComponent<Image>();
        _charIcon.color = new Color(0.3f, 0.25f, 0.2f, 0.8f);
        _charIcon.preserveAspect = true;
        var iconLE = iconGO.AddComponent<LayoutElement>();
        iconLE.preferredWidth = 46f;
        iconLE.preferredHeight = 46f;

        // 아이콘 폴백 텍스트
        var iconTextGO = new GameObject("IconText", typeof(RectTransform), typeof(TextMeshProUGUI));
        iconTextGO.transform.SetParent(iconGO.transform, false);
        var iconTextRT = iconTextGO.GetComponent<RectTransform>();
        iconTextRT.anchorMin = Vector2.zero;
        iconTextRT.anchorMax = Vector2.one;
        iconTextRT.offsetMin = Vector2.zero;
        iconTextRT.offsetMax = Vector2.zero;
        _charIconText = iconTextGO.GetComponent<TMP_Text>();
        _charIconText.fontSize = 20;
        _charIconText.alignment = TextAlignmentOptions.Center;
        _charIconText.color = Color.white;
        _charIconText.raycastTarget = false;

        // 이름 + 클래스
        _charName = CreateText(headerGO.GetComponent<RectTransform>(), "CharName", 18,
            new Color(0.15f, 0.1f, 0.05f), TextAlignmentOptions.Left, FontStyles.Bold);
        var nameLE = _charName.gameObject.AddComponent<LayoutElement>();
        nameLE.flexibleWidth = 1f;

        // 구분선
        CreateSeparator(_leftRoot);

        // 패시브
        _passiveBlock = CreateText(_leftRoot, "Passive", 12,
            new Color(0.3f, 0.25f, 0.15f), TextAlignmentOptions.TopLeft);

        // 구분선
        CreateSeparator(_leftRoot);

        // 스탯 블록
        _statBlock = CreateText(_leftRoot, "StatBlock", 12,
            new Color(0.2f, 0.15f, 0.1f), TextAlignmentOptions.TopLeft);
        var statLE = _statBlock.gameObject.AddComponent<LayoutElement>();
        statLE.flexibleHeight = 1f;
    }

    // ── Right Page Setup ──

    private void EnsureRightPage()
    {
        if (_rightRoot != null) return;

        var existing = transform.Find("RightPage");
        if (existing != null) Destroy(existing.gameObject);

        // 오른쪽 페이지 컨테이너 (레이아웃 없이 anchor로 분할)
        var go = new GameObject("RightPage", typeof(RectTransform), typeof(RectMask2D));
        go.transform.SetParent(transform, false);

        _rightRoot = go.GetComponent<RectTransform>();
        _rightRoot.anchorMin = new Vector2(0.55f, 0.10f);
        _rightRoot.anchorMax = new Vector2(0.94f, 0.80f);
        _rightRoot.offsetMin = Vector2.zero;
        _rightRoot.offsetMax = Vector2.zero;

        // ── 무기 슬롯 1 (상단 48%) ──
        _weapon0Root = CreateWeaponPanel(_rightRoot, "WeaponSlot0",
            new Vector2(0f, 0.52f), Vector2.one,
            "무기 슬롯 1", out _weapon0Icon, out _weapon0IconText, out _weapon0Info);

        // ── 중앙 구분선 (짧게) ──
        var sepGO = new GameObject("MidSep", typeof(RectTransform), typeof(Image));
        sepGO.transform.SetParent(_rightRoot, false);
        var sepRT = sepGO.GetComponent<RectTransform>();
        sepRT.anchorMin = new Vector2(0.05f, 0.497f);
        sepRT.anchorMax = new Vector2(0.65f, 0.503f);
        sepRT.offsetMin = Vector2.zero;
        sepRT.offsetMax = Vector2.zero;
        sepGO.GetComponent<Image>().color = new Color(0.4f, 0.35f, 0.25f, 0.4f);

        // ── 무기 슬롯 2 (하단 48%) ──
        _weapon1Root = CreateWeaponPanel(_rightRoot, "WeaponSlot1",
            Vector2.zero, new Vector2(1f, 0.48f),
            "무기 슬롯 2", out _weapon1Icon, out _weapon1IconText, out _weapon1Info);
    }

    private RectTransform CreateWeaponPanel(RectTransform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax,
        string label, out Image icon, out TMP_Text iconText, out TMP_Text info)
    {
        var panelGO = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
        panelGO.transform.SetParent(parent, false);

        var rt = panelGO.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var vlg = panelGO.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 6, 6);
        vlg.spacing = 3f;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        CreateWeaponHeader(panelGO.transform, label, out icon, out iconText);
        info = CreateText(rt, "Info", 11,
            new Color(0.2f, 0.15f, 0.1f), TextAlignmentOptions.TopLeft);
        var infoLE = info.gameObject.AddComponent<LayoutElement>();
        infoLE.flexibleHeight = 1f;

        return rt;
    }

    private void CreateWeaponHeader(Transform parent, string label,
        out Image icon, out TMP_Text iconText)
    {
        var row = new GameObject(label, typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);
        var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 8f;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        var rowLE = row.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 36f;

        // 무기 아이콘
        var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGO.transform.SetParent(row.transform, false);
        icon = iconGO.GetComponent<Image>();
        icon.color = new Color(0.3f, 0.25f, 0.2f, 0.8f);
        icon.preserveAspect = true;
        var iconLE = iconGO.AddComponent<LayoutElement>();
        iconLE.preferredWidth = 32f;
        iconLE.preferredHeight = 32f;

        // 아이콘 폴백 텍스트
        var itGO = new GameObject("IconText", typeof(RectTransform), typeof(TextMeshProUGUI));
        itGO.transform.SetParent(iconGO.transform, false);
        var itRT = itGO.GetComponent<RectTransform>();
        itRT.anchorMin = Vector2.zero;
        itRT.anchorMax = Vector2.one;
        itRT.offsetMin = Vector2.zero;
        itRT.offsetMax = Vector2.zero;
        iconText = itGO.GetComponent<TMP_Text>();
        iconText.fontSize = 14;
        iconText.alignment = TextAlignmentOptions.Center;
        iconText.color = Color.white;
        iconText.raycastTarget = false;

        // 라벨
        var labelTmp = CreateText(row.GetComponent<RectTransform>(), "Label", 12,
            new Color(0.4f, 0.35f, 0.3f), TextAlignmentOptions.Left);
        labelTmp.text = label;
        var labelLE = labelTmp.gameObject.AddComponent<LayoutElement>();
        labelLE.flexibleWidth = 1f;
    }

    // ── Refresh ──

    private void RefreshLeftPage()
    {
        if (_charName == null || _statBlock == null) return;
        if (_player == null) return;

        var data = _player.CharacterData;
        var stats = _player.RuntimeStats;

        // 캐릭터 아이콘 — 패시브 아이콘 사용, 없으면 이니셜
        PassiveSO passive = data != null ? data.passive : null;
        if (passive != null && passive.icon != null)
        {
            _charIcon.sprite = passive.icon;
            _charIcon.color = Color.white;
            _charIconText.text = "";
        }
        else
        {
            _charIcon.sprite = null;
            _charIcon.color = new Color(0.3f, 0.25f, 0.2f, 0.8f);
            string initial = data != null && !string.IsNullOrEmpty(data.characterName)
                ? data.characterName.Substring(0, 1) : "?";
            _charIconText.text = initial;
        }

        // 이름
        string charNameStr = data != null ? data.characterName : "Unknown";
        string classStr = data != null ? data.conClass.ToString() : "";
        _charName.text = $"{charNameStr}\n<size=12><color=#665544>{classStr}</color></size>";

        // 패시브 정보
        if (_passiveBlock != null)
        {
            if (passive != null)
            {
                string pText = $"<color=#5B4A2F><b>{passive.passiveName}</b></color>";
                if (!string.IsNullOrEmpty(passive.description))
                    pText += $"\n<color=#776655>{passive.description}</color>";
                _passiveBlock.text = pText;
            }
            else
            {
                _passiveBlock.text = "<color=#998877>패시브 없음</color>";
            }
        }

        // 스탯 블록
        if (stats == null) { _statBlock.text = ""; return; }

        int hp = _playerState != null ? _playerState.Hp : stats.Hp;
        int maxHp = _playerState != null ? _playerState.MaxHp : stats.MaxHp;

        string s = "";
        s += StatLine("HP", $"{hp} / {maxHp}");
        s += StatLine("근접 공격력", $"{stats.MeleeAttack}");
        s += StatLine("원거리 공격력", $"{stats.RangedAttack}");
        s += StatLine("방어력", $"{stats.Defense}");
        s += StatLine("행운", $"{stats.Luck}");
        s += StatLine("공격 속도", $"x{stats.AttackSpeedMultiplier:F2}");
        s += StatLine("이동 속도", $"x{stats.MoveSpeedMultiplier:F2}");

        if (stats.SkillCooldownReduction > 0)
            s += StatLine("스킬 쿨감", $"{stats.SkillCooldownReduction:F1}%");
        if (stats.ItemLifesteal > 0)
            s += StatLine("흡혈", $"{stats.ItemLifesteal:F1}%");
        if (stats.DamageReduction > 0)
            s += StatLine("피해 감소", $"{stats.DamageReduction:F1}%");
        if (stats.AllDamagePercent > 0)
            s += StatLine("전체 피해", $"+{stats.AllDamagePercent:F1}%");
        if (stats.AllElementBonus > 0)
            s += StatLine("속성 보너스", $"+{stats.AllElementBonus:F1}%");

        int gold = _playerState != null ? _playerState.TempGold : 0;
        s += StatLine("골드", $"{gold}");

        _statBlock.text = s.TrimEnd('\n');
    }

    private void RefreshRightPage()
    {
        if (_weapon0Info == null || _weapon1Info == null) return;

        var w0 = _weaponManager != null ? _weaponManager.Weapon0Data : null;
        var w1 = _weaponManager != null ? _weaponManager.Weapon1Data : null;

        SetWeaponIcon(_weapon0Icon, _weapon0IconText, w0);
        SetWeaponIcon(_weapon1Icon, _weapon1IconText, w1);
        _weapon0Info.text = FormatWeaponInfo(w0);
        _weapon1Info.text = FormatWeaponInfo(w1);
    }

    private void SetWeaponIcon(Image icon, TMP_Text fallback, WeaponData w)
    {
        if (icon == null) return;

        if (w != null && w.icon != null)
        {
            icon.sprite = w.icon;
            icon.color = Color.white;
            if (fallback != null) fallback.text = "";
        }
        else
        {
            icon.sprite = null;
            icon.color = new Color(0.3f, 0.25f, 0.2f, 0.8f);
            if (fallback != null)
                fallback.text = w != null ? w.weaponType.ToString().Substring(0, 1) : "?";
        }
    }

    private string FormatWeaponInfo(WeaponData w)
    {
        if (w == null) return "<color=#998877>없음</color>";

        string s = "";
        s += $"<color=#3D2B1F><b>{w.displayName}</b></color>  ";
        s += $"<color=#665544>{w.weaponType}</color>\n";
        s += $"공격력 <color=#8B4513>{w.baseAttack}</color>  ";
        s += $"공속 <color=#8B4513>x{w.attackSpeed:F1}</color>\n";

        if (w.skillQ != null)
        {
            s += $"\n<color=#4A6741><b>[Q] {w.skillQ.skillName}</b></color>";
            s += $"  <color=#807060>({w.skillQCooldown:F1}s)</color>\n";
            if (!string.IsNullOrEmpty(w.skillQ.description))
                s += $"<color=#555544>{w.skillQ.description}</color>\n";
        }

        if (w.skillE != null)
        {
            s += $"\n<color=#4A5A6A><b>[E] {w.skillE.skillName}</b></color>";
            s += $"  <color=#807060>({w.skillECooldown:F1}s)</color>\n";
            if (!string.IsNullOrEmpty(w.skillE.description))
                s += $"<color=#555544>{w.skillE.description}</color>\n";
        }

        return s.TrimEnd('\n');
    }

    // ── Event Handlers ──

    private void OnHpChanged(int hp, int maxHp) => RefreshLeftPage();
    private void OnWeaponChanged(WeaponData data, GameObject instance) => RefreshRightPage();

    private void OnDestroy()
    {
        Unbind();
    }

    // ── Static Helpers ──

    private static string StatLine(string label, string value)
    {
        return $"<color=#665544>{label}</color>  <color=#3D2B1F><b>{value}</b></color>\n";
    }

    private TMP_Text CreateText(RectTransform parent, string objName, float fontSize,
        Color color, TextAlignmentOptions align, FontStyles style = FontStyles.Normal)
    {
        var go = new GameObject(objName, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = align;
        tmp.fontStyle = style;
        tmp.enableWordWrapping = true;
        tmp.richText = true;
        tmp.raycastTarget = false;
        tmp.outlineWidth = 0.2f;
        tmp.outlineColor = new Color32(20, 15, 10, 140);
        return tmp;
    }

    private void CreateSeparator(RectTransform parent)
    {
        var go = new GameObject("Separator", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(0.4f, 0.35f, 0.25f, 0.5f);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 1f;
        le.preferredWidth = 40f;
        le.flexibleWidth = 0f;
    }
}
