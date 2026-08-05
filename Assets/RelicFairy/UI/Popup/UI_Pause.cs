using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UI_Pause : UI_Popup
{
	/// <summary>
	/// 일시정지도 <b>게임플레이 차단 팝업</b>이다. 이 값이 false면 자체 TimeScaleArbiter로 시간은 멈춰도
	/// <c>UIManager.IsGameplayBlocked</c>가 false라, 이 값을 보는 쪽(대사 이벤트 대기 등)이
	/// "차단 아님"으로 오판한다. TimeScaleArbiter는 소유자별로 Acquire/Release를 관리하므로
	/// UIManager와 이 팝업이 각각 잡아도 충돌하지 않는다(둘 다 Priority.Pause, 각자 해제).
	/// </summary>
	public override bool BlocksGameplay => true;

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

	// 탭 전환 상태 — 전환은 탭 버튼 클릭이 유일한 경로다.
	// (Tab키 순환은 제거했다. Tab은 룬판 토글 전용인데 raw 폴링이라 일시정지 중에도 함께 먹혔다.)
	private string _currentTab = "Chapter";

	public override void Init()
	{
		base.Init();

		// 일시정지 진입 — 시간 정지 요청(PR4 마이그레이션서 누락됐던 Acquire 복원).
		// 기존 Resume/Exit 버튼의 Release 와 짝. 강제 닫힘(씬 전환 등) 대비 OnDestroy 에 방어 Release.
		TimeScaleArbiter.Acquire(this, 0f, TimeScaleArbiter.Priority.Pause);

		Bind<GameObject>(typeof(GameObjects));
		Bind<TextMeshProUGUI>(typeof(Texts));
		Bind<Button>(typeof(Buttons));

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

		// 초기 탭 설정 — 마지막으로 보던 탭을 복원한다.
		ShowTab(_currentTab);

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
