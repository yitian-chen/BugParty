using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlatesCounter : BaseCounter
{

    public event EventHandler OnPlateSpawned;
    public event EventHandler OnPlateRemoved;

    [SerializeField] private KitchenObjectSO plateKitchenObgectSO;

    private float spawnPlateTimer;
    private float spawnPlateTimerMax = 4f;

    private readonly NetworkVariable<int> plateSpawnedAmount = new NetworkVariable<int>(0);
    private int plateSpawnedAmountMax = 4;



    private void Update()
    {
        if (!IsServer)
        {
            return;
        }
        spawnPlateTimer += Time.deltaTime;
        if (spawnPlateTimer > spawnPlateTimerMax)
        {
            spawnPlateTimer = 0f;

            if (plateSpawnedAmount.Value < plateSpawnedAmountMax)
            {
                plateSpawnedAmount.Value++;
                SpawnPlateClientRpc();
            }
        }
    }

    [ClientRpc]
    private void SpawnPlateClientRpc()
    {
        OnPlateSpawned?.Invoke(this, EventArgs.Empty);
    }

    public override void Interact(Player player)
    {
        if (!player.HasKitchenObject())
        {
            if (plateSpawnedAmount.Value > 0)
            {
                TakePlateServerRpc(player.NetworkObject);
            }

        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void TakePlateServerRpc(NetworkObjectReference playerNetworkObjectReference)
    {
        if (plateSpawnedAmount.Value <= 0)
        {
            return;
        }

        if (!playerNetworkObjectReference.TryGet(out NetworkObject playerNetworkObject))
        {
            return;
        }

        if (!playerNetworkObject.TryGetComponent(out Player player))
        {
            return;
        }

        if (player.HasKitchenObject())
        {
            return;
        }

        plateSpawnedAmount.Value--;
        KitchenObject.SpawnKitchenObject(plateKitchenObgectSO, player);
        TakePlateClientRpc();
    }

    [ClientRpc]
    private void TakePlateClientRpc()
    {
        OnPlateRemoved?.Invoke(this, EventArgs.Empty);
    }

}
