using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FollowTransform : MonoBehaviour
{
    private Transform targetTranform;

    public void SetTargetTransform(Transform targetTranform)
    {
        this.targetTranform = targetTranform;
    }
    private void LateUpdate()
    {
        if (targetTranform == null)
        {
            return;
        }
        transform.position = targetTranform.position;
        transform.rotation = targetTranform.rotation;
    }
}
