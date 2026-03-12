using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 캐릭터 선택 PrepPanel의 개별 캐릭터 카드 아이템.
/// PrepPanel이 직접 풀에서 꺼내 Setup()을 호출합니다.
/// </summary>
public class UI_CharacterSelectItem : UI_Base
{
    [SerializeField] private Image     portrait;
    [SerializeField] private TMP_Text  nameText;
    [SerializeField] private TMP_Text  classText;
    [SerializeField] private TMP_Text  hpText;
    [SerializeField] private GameObject selectedMark;   // 선택 표시 오브젝트 (e.g. 테두리 이미지)
    [SerializeField] private Button    button;

    private CharacterRoster.CharacterEntry _entry;
    private Action<UI_CharacterSelectItem> _onSelected;

    public CharacterRoster.CharacterEntry Entry => _entry;

    public override void Init()
    {
        base.Init();
        if (button != null)
            button.onClick.AddListener(OnClick);
    }

    // 초상화가 없을 때 클래스별로 표시할 색상
    private static readonly Color[] _classColors =
    {
        new Color(0.30f, 0.55f, 0.85f), // 0 - 기본/기사 (파란색)
        new Color(0.85f, 0.30f, 0.30f), // 1 - 전사 (빨간색)
        new Color(0.55f, 0.30f, 0.85f), // 2 - 마법사 (보라색)
        new Color(0.30f, 0.75f, 0.40f), // 3 - 궁수 (초록색)
    };

    public void Setup(CharacterRoster.CharacterEntry entry, Action<UI_CharacterSelectItem> onSelected)
    {
        _entry      = entry;
        _onSelected = onSelected;

        Debug.Log($"[CharSelectItem] Setup called — portrait_field={portrait != null}, entry.portrait={entry.portrait?.name ?? "NULL"}");

        if (portrait != null)
        {
            if (entry.portrait != null)
            {
                portrait.sprite = entry.portrait;
                portrait.color  = Color.white;
                Debug.Log($"[CharSelectItem] Sprite set: {entry.portrait.name}, rect={entry.portrait.rect}");
            }
            else
            {
                // 초상화 없으면 클래스 색상 블록으로 표시
                portrait.sprite = null;
                int classIdx = entry.data != null ? (int)entry.data.conClass % _classColors.Length : 0;
                portrait.color = _classColors[classIdx];
            }
        }
        else
        {
            Debug.LogWarning("[CharSelectItem] portrait field is NULL — check serialized reference in prefab");
        }

        if (nameText  != null) nameText.text  = entry.data != null ? entry.data.characterName : "???";
        if (classText != null) classText.text = entry.data != null ? entry.data.conClass.ToString() : "";
        if (hpText    != null) hpText.text    = entry.data != null ? $"HP {entry.data.maxHealth}" : "";

        SetSelected(false);
    }

    public void SetSelected(bool selected)
    {
        if (selectedMark != null)
            selectedMark.SetActive(selected);
    }

    private void OnClick() => _onSelected?.Invoke(this);
}
