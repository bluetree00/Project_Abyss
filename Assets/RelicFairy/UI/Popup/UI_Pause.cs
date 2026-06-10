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
		resumeText,
		exitText,
	}

	enum Buttons
	{
		Tab_Chapter,
		Tab_Weapon,
		Tab_Inventory,
		ResumeButton,
		ExitButton
	}

	// 탭 전환을 위한 변수
	private string _currentTab = "Chapter";
	private int _tabIndex = 0;
	private readonly string[] _tabNames = { "Chapter", "Weapon", "Inven" };

	public override void Init()
	{
		base.Init();

		// 일시정지 진입 — 시간 정지 요청(PR4 마이그레이션서 누락됐던 Acquire 복원).
		// 기존 Resume/Exit 버튼의 Release 와 짝. 강제 닫힘(씬 전환 등) 대비 OnDestroy 에 방어 Release.
		TimeScaleArbiter.Acquire(this, 0f, TimeScaleArbiter.Priority.Pause);

		Bind<GameObject>(typeof(GameObjects));
		Bind<TextMeshProUGUI>(typeof(Texts));
		Bind<Button>(typeof(Buttons));

		// 현재 탭 인덱스 초기화
    	_tabIndex = System.Array.IndexOf(_tabNames, _currentTab);

    	// 초기 탭 설정
    	ShowTab(_tabNames[_tabIndex]);

		GetTMPText((int)Texts.resumeText).text = "Resume";
		GetTMPText((int)Texts.exitText).text = "Exit";

		// 버튼 이벤트 바인딩
		GameObject tabChapter = GetButton((int)Buttons.Tab_Chapter).gameObject;
		BindEvent(tabChapter, (PointerEventData data) =>
		{
			ShowTab("Chapter");
			//UpdateChapterInfo();  // 챕터 정보 업데이트
		}, Define.UIEvent.Click);

		GameObject tabWeapon = GetButton((int)Buttons.Tab_Weapon).gameObject;
		BindEvent(tabWeapon, (PointerEventData data) =>
		{
			ShowTab("Weapon");
			//UpdateWeaponInfo();  // 무기 정보 업데이트
		}, Define.UIEvent.Click);

		GameObject tabInven = GetButton((int)Buttons.Tab_Inventory).gameObject;
		BindEvent(tabInven, (PointerEventData data) =>
		{
			ShowTab("Inven");
			//UpdateInventoryInfo();  // 인벤토리 정보 업데이트
		}, Define.UIEvent.Click);

		// 초기 탭 설정
		if (_currentTab == "Chapter")
		{
			ShowTab("Chapter");
			//UpdateChapterInfo();
		}
		else if (_currentTab == "Weapon")
		{
			ShowTab("Weapon");
			//UpdateWeaponInfo();
		}
		else if (_currentTab == "Inven")
		{
			ShowTab("Inven");
			//UpdateInventoryInfo();
		}

		GameObject resumeGo = GetButton((int)Buttons.ResumeButton).gameObject;
		BindEvent(resumeGo, (PointerEventData data) =>
		{
			Managers.UI.ClosePopupUI(this);
			TimeScaleArbiter.Release(this);
		}, Define.UIEvent.Click);

		GameObject exitGo = GetButton((int)Buttons.ExitButton).gameObject;
		BindEvent(exitGo, (PointerEventData data) =>
		{
			// Resume 과 동일하게 팝업을 닫고 timeScale 복원(닫기→Release 순서 정합, 멱등).
			Managers.UI.ClosePopupUI(this);
			TimeScaleArbiter.Release(this);
			// TODO: 실제 게임 종료/메인화면 이동 씬 전환 미구현. 현재는 팝업 닫기+게임 재개만 수행.
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

		// ★ 탭이 바뀔 때마다 _tabIndex도 동기화
    	_tabIndex = System.Array.IndexOf(_tabNames, tabName);
	}

	private void Update() {

		if (gameObject.activeInHierarchy)
		{
			// UI_Pause가 활성화된 상태에서만 Tab키 입력 처리
			if (Input.GetKeyDown(KeyCode.Tab))
			{
				_tabIndex = (_tabIndex + 1) % _tabNames.Length;
				ShowTab(_tabNames[_tabIndex]);
			}
		}

	}

	private void OnDestroy()
	{
		// 방어적 Release — Resume/Exit 없이 강제 파괴되어도 timeScale 0 고착 방지(멱등 no-op 가드).
		TimeScaleArbiter.Release(this);
	}

	//TODO: 탭에 따라 표시할 정보를 업데이트하는 메서드들 구현 필요. 테스트 요망
	// 예시로, "Chapter" 탭에 대해 표시할 텍스트나 아이템 업데이트
	// private void UpdateChapterInfo()
	// {
	// 	// 현재 챕터 정보 업데이트 예시
	// 	Get<TextMeshProUGUI>((int)GameObjects.Panel_Chapter).text = "현재 챕터: 테스트중(텍스트 연결 필요)";
	// }

	// // 예시로, "Weapon" 탭에 대해 표시할 무기 정보 업데이트
	// private void UpdateWeaponInfo()
	// {
	//     // 현재 사용 중인 무기 정보 업데이트 예시
	//     Get<TextMeshProUGUI>((int)GameObjects.Panel_Weapon).text = "현재 무기: 테스트중(텍스트 연결 필요)";
	// }

	// // 예시로, "Inventory" 탭에 대해 표시할 아이템 정보 업데이트
	// private void UpdateInventoryInfo()
	// {
	//     // 인벤토리 정보 업데이트 예시
	//     Get<TextMeshProUGUI>((int)GameObjects.Panel_Inven).text = "인벤토리: 테스트중(텍스트 연결 필요)";
	// }
}
