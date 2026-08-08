using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UI_Scene : UI_Base
{
	public override void Init()
	{
		// base가 IsInitialized를 세운다 — UIManager.ShowMenuUI가 이 값으로 재초기화(리스너 중복)를 막는다.
		base.Init();
		Managers.UI.SetCanvas(gameObject, false);
	}
}
