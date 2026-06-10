using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class ClearCounter : BaseCounter
{
    [SerializeField] private KitchenObjectSO kitchenObjectSO;


    public override void Interact(Player player)
    {
        if (!HasKitchenObject())
        {
            //there is no kitchenObject here
            if (player.HasKitchenObject())
            {
                //player is carrying something.
                player.GetKitchenObject().SetKitchenObjectParent(this);
            }
            else
            {
                //player not carrying anything 
            }
        }
        else
        {
            //there is a kitchenObject on the counter
            if (player.HasKitchenObject())
            {
                if (player.GetKitchenObject().TryGetPlate(out PlateKitchenObject _))
                {
                    InteractPutCounterObjectOnPlateServerRpc(player.NetworkObject, GetKitchenObject().NetworkObject);
                }
                else if (GetKitchenObject().TryGetPlate(out PlateKitchenObject _))
                {
                    InteractPutPlayerObjectOnCounterPlateServerRpc(player.NetworkObject, GetKitchenObject().NetworkObject);
                }
            }
            else
            {//player is not cariyng anything
                GetKitchenObject().SetKitchenObjectParent(player);
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void InteractPutCounterObjectOnPlateServerRpc(
        NetworkObjectReference playerNetworkObjectReference,
        NetworkObjectReference kitchenObjectNetworkObjectReference)
    {
        if (!TryResolveCounterObjectOnPlayerPlate(
                playerNetworkObjectReference,
                kitchenObjectNetworkObjectReference,
                out KitchenObject counterKitchenObject,
                out PlateKitchenObject plateKitchenObject))
        {
            return;
        }

        if (!plateKitchenObject.TryAddIngredient(counterKitchenObject.GetKitchenObjectSO()))
        {
            return;
        }

        KitchenObject.DestroyKitchenObject(counterKitchenObject);
    }

    [ServerRpc(RequireOwnership = false)]
    private void InteractPutPlayerObjectOnCounterPlateServerRpc(
        NetworkObjectReference playerNetworkObjectReference,
        NetworkObjectReference kitchenObjectNetworkObjectReference)
    {
        if (!TryResolvePlayerObjectOnCounterPlate(
                playerNetworkObjectReference,
                kitchenObjectNetworkObjectReference,
                out Player player,
                out PlateKitchenObject plateKitchenObject))
        {
            return;
        }

        KitchenObject playerKitchenObject = player.GetKitchenObject();
        if (!plateKitchenObject.TryAddIngredient(playerKitchenObject.GetKitchenObjectSO()))
        {
            return;
        }

        KitchenObject.DestroyKitchenObject(playerKitchenObject);
    }

    private bool TryResolveCounterObjectOnPlayerPlate(
        NetworkObjectReference playerNetworkObjectReference,
        NetworkObjectReference kitchenObjectNetworkObjectReference,
        out KitchenObject counterKitchenObject,
        out PlateKitchenObject plateKitchenObject)
    {
        counterKitchenObject = null;
        plateKitchenObject = null;

        if (!playerNetworkObjectReference.TryGet(out NetworkObject playerNetworkObject))
        {
            return false;
        }

        if (!kitchenObjectNetworkObjectReference.TryGet(out NetworkObject kitchenObjectNetworkObject))
        {
            return false;
        }

        if (!playerNetworkObject.TryGetComponent(out Player player))
        {
            return false;
        }

        if (!kitchenObjectNetworkObject.TryGetComponent(out counterKitchenObject))
        {
            return false;
        }

        if (!HasKitchenObject() || GetKitchenObject() != counterKitchenObject || !player.HasKitchenObject())
        {
            return false;
        }

        return player.GetKitchenObject().TryGetPlate(out plateKitchenObject);
    }

    private bool TryResolvePlayerObjectOnCounterPlate(
        NetworkObjectReference playerNetworkObjectReference,
        NetworkObjectReference kitchenObjectNetworkObjectReference,
        out Player player,
        out PlateKitchenObject plateKitchenObject)
    {
        player = null;
        plateKitchenObject = null;

        if (!playerNetworkObjectReference.TryGet(out NetworkObject playerNetworkObject))
        {
            return false;
        }

        if (!kitchenObjectNetworkObjectReference.TryGet(out NetworkObject kitchenObjectNetworkObject))
        {
            return false;
        }

        if (!playerNetworkObject.TryGetComponent(out player))
        {
            return false;
        }

        if (!kitchenObjectNetworkObject.TryGetComponent(out KitchenObject counterKitchenObject))
        {
            return false;
        }

        if (!HasKitchenObject() || GetKitchenObject() != counterKitchenObject || !player.HasKitchenObject())
        {
            return false;
        }

        if (player.GetKitchenObject().TryGetPlate(out _))
        {
            return false;
        }

        return counterKitchenObject.TryGetPlate(out plateKitchenObject);
    }

}
