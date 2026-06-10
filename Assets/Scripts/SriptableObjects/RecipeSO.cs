using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu]
public class RecipeSO : ScriptableObject
{
    public string recipeName;
    //菜谱变量是厨房变量的集合，盘子变量也同样是厨房变量的合集所以说可以进行比较两者是否相同；
    public List<KitchenObjectSO> kitchenObjectSOList;

}
