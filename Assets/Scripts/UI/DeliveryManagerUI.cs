using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DeliveryManagerUI : MonoBehaviour
{
    //这里的容器是存放所有的图标的空物体
    [SerializeField] private Transform container;
    [SerializeField] private Transform recipeTemplate;
    private void Start()
    {
        DeliveryManager.Instance.OnRecipeSpawned += DeliverManager_OnRecipeSpawned;
        DeliveryManager.Instance.OnRecipeCompleted += DeliverManager_OnRecipeCompleted;
        UpdateVisual();
    }

    private void DeliverManager_OnRecipeCompleted(object sender, EventArgs e)
    {
        UpdateVisual();
    }

    private void DeliverManager_OnRecipeSpawned(object sender, EventArgs e)
    {
        UpdateVisual();
    }

    private void Awake()
    {
        recipeTemplate.gameObject.SetActive(false);
    }

    //更新逻辑是原来的实例化容器中有着所有的图标，而
    private void UpdateVisual()
    {
        foreach (Transform child in container)
        {
            //如果包含这个图标物体则保留直接进入下一个循环
            if (child == recipeTemplate) continue;
            //如果不是则直接销毁
            Destroy(child.gameObject);
        }
        //这里将DeliverManager中的recioeSO转换
        foreach (RecipeSO recipeSO in DeliveryManager.Instance.GetWaitingRecipeSOList())
        {
            Transform racipeTransform = Instantiate(recipeTemplate, container);
            racipeTransform.gameObject.SetActive(true);
            racipeTransform.GetComponent<DeliveryManagerSingleUI>().SetRecipeSO(recipeSO);
        }
    }

}
