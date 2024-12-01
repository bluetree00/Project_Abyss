using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameScene : BaseScene
{
    protected override void Init()
    {
        base.Init();

        SceneType = Define.Scene.Game;
        Managers.UI.ShowSceneUI<UI_Inven>();
        //Dictionary<int, Data.Stat> dict = Managers.Data.StatDict;
        //GameObject player = Managers.Game.Spawn(Define.WorldObject.Player, "UnityChan");
       

    }

    public override void Clear()
    {
        
    }
}
