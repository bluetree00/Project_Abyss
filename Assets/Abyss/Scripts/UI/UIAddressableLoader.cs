// using System;
// using System.Collections;
// using System.Collections.Generic;
// using UnityEngine;

// public class UIAddressableLoader
// {
//     private AddressableManager _addressable;

//     public UIAddressableLoader(AddressableManager addressable)
//     {
//         _addressable = addressable;
//     }

//     public void LoadSceneUI(string uiName, Action<GameObject> onLoaded, Action onFail = null)
//     {
//         string key = $"UI/Scene/{uiName}";
//         _addressable.LoadAsset<GameObject>(key, onLoaded, onFail);
//     }

//     public void LoadPopupUI(string uiName, Action<GameObject> onLoaded, Action onFail = null)
//     {
//         string key = $"UI/Popup/{uiName}";
//         _addressable.LoadAsset<GameObject>(key, onLoaded, onFail);
//     }

//     public void LoadWorldUI(string uiName, Action<GameObject> onLoaded, Action onFail = null)
//     {
//         string key = $"UI/WorldSpace/{uiName}";
//         _addressable.LoadAsset<GameObject>(key, onLoaded, onFail);
//     }
// }
