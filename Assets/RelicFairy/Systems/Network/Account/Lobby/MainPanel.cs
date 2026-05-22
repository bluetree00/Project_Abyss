using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MainPanel : MonoBehaviour
{
    public void BtnClickGameStart()
    {
        SceneUtilitys.LoadScene(SceneNames.GameScene);
    }
}
