using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ResetStaticDataManager : MonoBehaviour
{
    //静态语句不会自动清除，需要手动释放
    private void Awake()
    {
        BaseCounter.RestStaticData();
        CuttingCounter.RestStaticData();
        TrashCounter.RestStaticData();
        Player.RestStaticData();
    }
}
