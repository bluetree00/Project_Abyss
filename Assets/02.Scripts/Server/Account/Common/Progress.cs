using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using BackEnd.Quobject.SocketIoClientDotNet.Client;

public class Progress : MonoBehaviour
{
    [SerializeField]
    private Slider sliderProgress;
    [SerializeField]
    private TextMeshProUGUI textProgressData;
    [SerializeField]
    private float progressTime = 0.0f;  //로딩바 재생 시간  

    public void Play(UnityAction action=null)
    {
        StartCoroutine(Onprogress(action));
    }

    private IEnumerator Onprogress(UnityAction action = null)
    {
        float current = 0;
        float persent = 0;

        while ( persent < 1)
        {
            current += Time.deltaTime;
            persent = current / progressTime;

            textProgressData.text = $"Now Loading.... {sliderProgress.value*100:F0}%";

            sliderProgress.value = Mathf.Lerp(0, 1, persent);

            yield return null;
        }

        action?.Invoke(); // 액션이 null이 아닐 경우에만 호출


    }
}
