using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlateCounterVisual : MonoBehaviour
{

    [SerializeField] private PlatesCounter platesCounter;
    [SerializeField] private Transform counterTopPoint;
    [SerializeField] private Transform plateVisualPrefab;

    private List<GameObject> platVisualGamObjectList;


    private void Awake()
    {
        platVisualGamObjectList = new List<GameObject>();
    }

    private void Start()
    {
        platesCounter.OnPlateSpawned += PlatesCounter_OnPlateSpawned;
        platesCounter.OnPlateRemoved += PlatesCounter_OnPlateRemoved;

    }

    private void PlatesCounter_OnPlateRemoved(object sender, System.EventArgs e)
    {
      GameObject plateGameObject = platVisualGamObjectList[platVisualGamObjectList.Count-1];
        platVisualGamObjectList.Remove(plateGameObject);
        Destroy(plateGameObject);

    }

    private void PlatesCounter_OnPlateSpawned(object sender, System.EventArgs e)
    {
        Transform plateVisualTransfrom=Instantiate(plateVisualPrefab,counterTopPoint);

        float plateOffsetY = .1f;
        plateVisualTransfrom.localPosition = new Vector3(0, plateOffsetY * platVisualGamObjectList.Count, 0);

        platVisualGamObjectList.Add(plateVisualTransfrom.gameObject);
    }
}
