using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UI_Pause : UI_Popup
{
	enum GameObjects
	{
		Background,
		Panel_Chapter,
		Panel_Weapon,
		Panel_Inven
	}

	enum Texts
	{
		TitleText,
		resumeText,
		exitText,
	}

	enum Buttons
	{
		Tab_Chapter,
		Tab_Weapon,
		Tab_Inven,
		ResumeButton,
		ExitButton
	}

	// 탭 전환을 위한 변수
	private string _currentTab = "Chapter";

	public override void Init()
	{
		base.Init();

		Bind<GameObject>(typeof(GameObjects));
		Bind<TextMeshProUGUI>(typeof(Texts));
		Bind<Button>(typeof(Buttons));

		GetTMPText((int)Texts.TitleText).text = "Paused";
		GetTMPText((int)Texts.resumeText).text = "Resume";
		GetTMPText((int)Texts.exitText).text = "Exit";

		// 버튼 이벤트 바인딩
        BindEvent(GetButton((int)Buttons.Tab_Chapter).gameObject, (_) => ShowTab("Chapter"));
        BindEvent(GetButton((int)Buttons.Tab_Weapon).gameObject, (_) => ShowTab("Weapon"));
        BindEvent(GetButton((int)Buttons.Tab_Inven).gameObject, (_) => ShowTab("Inven"));
        



		GameObject resumeGo = GetButton((int)Buttons.ResumeButton).gameObject;
		BindEvent(resumeGo, (PointerEventData data) =>
		{
			Managers.UI.ClosePopupUI(this);
			Time.timeScale = 1f;
		}, Define.UIEvent.Click);

		GameObject exitGo = GetButton((int)Buttons.ExitButton).gameObject;
		BindEvent(exitGo, (PointerEventData data) =>
		{
			Time.timeScale = 1f;
			Debug.Log("게임 종료 또는 메인화면 이동");
		}, Define.UIEvent.Click);

	}
	
	// 탭을 전환하는 메서드
    private void ShowTab(string tabName)
    {
        // 현재 탭을 표시하고, 나머지 탭은 숨김
        Get<GameObject>((int)GameObjects.Panel_Chapter).SetActive(tabName == "Chapter");
        Get<GameObject>((int)GameObjects.Panel_Weapon).SetActive(tabName == "Weapon");
        Get<GameObject>((int)GameObjects.Panel_Inven).SetActive(tabName == "Inven");

        _currentTab = tabName;  // 현재 탭 상태 저장
    }
	
	// 예시로, "Chapter" 탭에 대해 표시할 텍스트나 아이템 업데이트
	private void UpdateChapterInfo()
	{
		// 현재 챕터 정보 업데이트 예시
		Get<TextMeshProUGUI>((int)GameObjects.Panel_Chapter).text = "현재 챕터: 1장";
	}

    // 예시로, "Weapon" 탭에 대해 표시할 무기 정보 업데이트
    private void UpdateWeaponInfo()
    {
        // 현재 사용 중인 무기 정보 업데이트 예시
        Get<TextMeshProUGUI>((int)GameObjects.Panel_Weapon).text = "현재 무기: 검";
    }

    // 예시로, "Inventory" 탭에 대해 표시할 아이템 정보 업데이트
    private void UpdateInventoryInfo()
    {
        // 인벤토리 정보 업데이트 예시
        Get<TextMeshProUGUI>((int)GameObjects.Panel_Inven).text = "인벤토리: 10/20";
    }
}
