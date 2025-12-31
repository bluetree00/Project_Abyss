using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 모든 UI의 공통 베이스 클래스
/// - UI 내부 요소 바인딩
/// - UI 생명주기 분리 (Init / Open / Close)
/// - 데이터 지연 바인딩 지원
/// </summary>
public abstract class UI_Base : MonoBehaviour
{
	/// <summary>
	/// 타입별 UI 오브젝트 캐시
	/// </summary>
	protected Dictionary<Type, UnityEngine.Object[]> _objects = new();

	/// <summary>
	/// 초기화 여부
	/// </summary>
	public bool IsInitialized { get; private set; }

	#region Life Cycle

	/// <summary>
	/// UI 구조 초기화 (Bind 전용)
	/// UIManager가 호출
	/// </summary>
	public virtual void Init()
	{
		IsInitialized = true;
	}

	/// <summary>
	/// 외부 데이터 바인딩
	/// 데이터 준비 시점에 여러 번 호출 가능
	/// </summary>
	public virtual void BindData(object data)
	{
		// 필요 시 override
	}

	/// <summary>
	/// UI 표시
	/// </summary>
	public virtual void Open()
	{
		gameObject.SetActive(true);
	}

	/// <summary>
	/// UI 숨김
	/// </summary>
	public virtual void Close()
	{
		gameObject.SetActive(false);
	}

	#endregion

	#region Bind

	/// <summary>
	/// Enum 기반 UI 요소 바인딩
	/// </summary>
	protected void Bind<T>(Type enumType) where T : UnityEngine.Object
	{
		if (_objects.ContainsKey(typeof(T)))
		{
			Debug.LogWarning($"[{nameof(UI_Base)}] {typeof(T)} 이미 바인딩됨");
			return;
		}

		string[] names = Enum.GetNames(enumType);
		UnityEngine.Object[] objects = new UnityEngine.Object[names.Length];
		_objects.Add(typeof(T), objects);

		for (int i = 0; i < names.Length; i++)
		{
			if (typeof(T) == typeof(GameObject))
				objects[i] = Util.FindChild(gameObject, names[i], true);
			else
				objects[i] = Util.FindChild<T>(gameObject, names[i], true);

			if (objects[i] == null)
				Debug.LogWarning($"[{nameof(UI_Base)}] Bind 실패 : {names[i]}");
		}
	}

	#endregion

	#region Get

	protected T Get<T>(int idx) where T : UnityEngine.Object
	{
		if (_objects.TryGetValue(typeof(T), out var objects) == false)
			return null;

		if (idx < 0 || idx >= objects.Length)
			return null;

		return objects[idx] as T;
	}

	protected GameObject GetObject(int idx) => Get<GameObject>(idx);
	protected Text GetText(int idx) => Get<Text>(idx);
	protected TextMeshProUGUI GetTMPText(int idx) => Get<TextMeshProUGUI>(idx);
	protected Button GetButton(int idx) => Get<Button>(idx);
	protected Image GetImage(int idx) => Get<Image>(idx);

	#endregion

	#region Event

	/// <summary>
	/// UI 이벤트 바인딩
	/// </summary>
	public static void BindEvent(
		GameObject go,
		Action<PointerEventData> action,
		Define.UIEvent type = Define.UIEvent.Click)
	{
		UI_EventHandler evt = Util.GetOrAddComponent<UI_EventHandler>(go);

		switch (type)
		{
			case Define.UIEvent.Click:
				evt.OnClickHandler -= action;
				evt.OnClickHandler += action;
				break;

			case Define.UIEvent.Drag:
				evt.OnDragHandler -= action;
				evt.OnDragHandler += action;
				break;
		}
	}

	#endregion
}
