using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UI_HPBar : UI_Base
{

    MonsterController monster; //스크립터블 오브젝트

    public override void Init()
    {
        Bind<GameObject>(typeof(Define.WorldObjectUI));
        monster = transform.parent.GetComponent<MonsterController>();
    }

    private void Update()
    {
        Transform parent = transform.parent; // 부모의 위치를 가져옴
        transform.position = parent.position + Vector3.up * (parent.GetComponent<Collider>().bounds.size.y); // 콜라이더 위에 위치시켜 오브젝트들의 높이,키 대응
        transform.rotation = Camera.main.transform.rotation; // 카메라에 rotation에 체력 게이지의 rotation을 맞춰줌

        float ratio = monster.CurrentHp / monster.MaximumHp; // 체력 게이지 적용
        SetHpRatio(ratio);
    }

    public void SetHpRatio(float ratio)
    {
        GetObject((int)Define.WorldObjectUI.HPBar).GetComponent<Slider>().value = ratio; // 게임 오브젝트가 가지고있는 체력 게이지를 UI value에 맞춰줌
    }
}