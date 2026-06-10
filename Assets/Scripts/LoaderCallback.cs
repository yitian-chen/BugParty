using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LoaderCallback : MonoBehaviour
{
    private bool isFristUpdate = true;

    private void Update()
    {
        if (isFristUpdate)
        {
            isFristUpdate = false;
            Loader.LoaderCallback();
        }
    }
}
