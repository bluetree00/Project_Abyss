using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.VisualScripting;
using UnityEngine;
public class Define
{

    public enum Scene
    {
        
    }

    public enum Sound
    {


    }
    public enum WorldObject
    {

    }

    public enum WorldObjectUI
    {

    }

    public enum State //상태
    {
        //기본적으로 사용하는 상태
        Die,
        Idle,
        Moving,
        Runing,
        Dodge,
        
    }

    public enum UIEvent
    {

    }

    public enum MouseEvent
    {
        Press,
        Click,
    }

    public enum cameraMode
    {
        QuarterView,
    }
}