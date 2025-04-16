using System.Collections;
using System.Collections.Generic;
using UnityEditor.SearchService;
using UnityEngine;

public class LogoScenario : MonoBehaviour
{
    [SerializeField]
    private Progress progress; // 로딩바 스크립트

    [SerializeField]
    private SceneNames nextScene; // 다음 씬 이름

    private void Awake()
    {
        SystemSetup();
    }

    private void SystemSetup()
    {
        Application.runInBackground = true; // 백그라운드에서 실행

        //해상도 설정
        int width = Screen.width;
        int height = (int)(Screen.width * 9f / 16f);
        Screen.SetResolution(width, height, true); // 가로 세로 비율 16:9로 설정

        //화면이 꺼지지않도록 설정
        Screen.sleepTimeout = SleepTimeout.NeverSleep; // 화면 꺼짐 방지

        //로딩 애니메이션 시작 재생시 OnAfterProgress() 호출
        progress.Play(OnAfterProgress);
    }

    private void OnAfterProgress()
    {
        SceneUtilitys.LoadScene(nextScene); // 다음 씬 로드
    }
}
