using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class KitchenObject : NetworkBehaviour
{
    [SerializeField] private KitchenObjectSO kitchenObjectSO;

    private IKitchenObjectParent kitchenObjectParent;
    private FollowTransform followTransform;



    protected virtual void Awake()
    {
        followTransform = GetComponent<FollowTransform>();
    }
    public KitchenObjectSO GetKitchenObjectSO()
    {
        return kitchenObjectSO;
    }

    public void SetKitchenObjectParent(IKitchenObjectParent kitchenObjectParent)
    {
        if (kitchenObjectParent == null)
        {
            Debug.LogError("SetKitchenObjectParent: kitchenObjectParent is null");
            return;
        }

        NetworkObject kitchenObjectParentNetworkObject = kitchenObjectParent.GetNetworkObject();
        if (kitchenObjectParentNetworkObject == null)
        {
            Debug.LogError("SetKitchenObjectParent: kitchenObjectParent has no NetworkObject");
            return;
        }

        SetKitchenObjectParentServerRpc(kitchenObjectParentNetworkObject);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetKitchenObjectParentServerRpc(NetworkObjectReference kitchonObjectParentNetworkObjectReference)
    {
        SetKitchenObjectParentFromServer(kitchonObjectParentNetworkObjectReference);
    }

    public void SetKitchenObjectParentFromServer(NetworkObjectReference kitchenObjectParentNetworkObjectReference)
    {
        if (!IsServer)
        {
            return;
        }

        if (!kitchenObjectParentNetworkObjectReference.TryGet(out NetworkObject kitchenObjectParentNetworkObject))
        {
            Debug.LogError("SetKitchenObjectParentFromServer: Failed to resolve parent NetworkObjectReference");
            return;
        }

        IKitchenObjectParent kitchenObjectParent = GetKitchenObjectParentFromNetworkObject(kitchenObjectParentNetworkObject);
        if (kitchenObjectParent == null)
        {
            Debug.LogError("SetKitchenObjectParentFromServer: Parent NetworkObject has no IKitchonObjectParent");
            return;
        }

        SetKitchenObjectParentLocal(kitchenObjectParent);
        SetKitchenObjectParentClientRpc(kitchenObjectParentNetworkObjectReference);
    }

    [ClientRpc]
    public void SetKitchenObjectParentClientRpc(NetworkObjectReference kitchonObjectParentNetworkObjectReference)
    {
        if (!kitchonObjectParentNetworkObjectReference.TryGet(out NetworkObject kitchonObjectParentNetworkObject))
        {
            Debug.LogError("SetKitchenObjectParentClientRpc: Failed to resolve parent NetworkObjectReference");
            return;
        }

        IKitchenObjectParent kitchonObjectParent = GetKitchenObjectParentFromNetworkObject(kitchonObjectParentNetworkObject);
        if (kitchonObjectParent == null)
        {
            Debug.LogError("SetKitchenObjectParentClientRpc: Parent NetworkObject has no IKitchonObjectParent");
            return;
        }

        SetKitchenObjectParentLocal(kitchonObjectParent);
    }

    private void SetKitchenObjectParentLocal(IKitchenObjectParent kitchenObjectParent)
    {
        if (this.kitchenObjectParent != null)
        {
            this.kitchenObjectParent.ClearKitchenObject();
        }
        this.kitchenObjectParent = kitchenObjectParent;

        if (kitchenObjectParent.HasKitchenObject())
        {
            Debug.LogError("柜台已经有了厨房物品了");
        }
        kitchenObjectParent.SetKitchenObject(this);

        if (followTransform != null)
        {
            followTransform.SetTargetTransform(kitchenObjectParent.GetKitchenObjectFollowTransform());
        }
    }

    private static IKitchenObjectParent GetKitchenObjectParentFromNetworkObject(NetworkObject networkObject)
    {
        if (networkObject.TryGetComponent(out Player player))
        {
            return player;
        }

        if (networkObject.TryGetComponent(out BaseCounter baseCounter))
        {
            return baseCounter;
        }

        return null;
    }

    public IKitchenObjectParent GetKitchenObjectParent()
    {
        return kitchenObjectParent;
    }


    public void DestroySelf()
    {
        if (!IsServer)
        {
            return;
        }

        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
            return;
        }

        Destroy(gameObject);
    }
    public void ClearKitchenObjectOnParent()
    {
        if (kitchenObjectParent != null)
        {
            kitchenObjectParent.ClearKitchenObject();
        }
    }


    public bool TryGetPlate(out PlateKitchenObject plateKitchenObject)
    {
        if (this is PlateKitchenObject)
        {
            plateKitchenObject = this as PlateKitchenObject;
            return true;
        }
        else
        {
            plateKitchenObject = null;
            return false;
        }
    }


    public static void SpawnKitchenObject(KitchenObjectSO kitchenObjectSO, IKitchenObjectParent kitchonObjectParent)
    {
        KitchenGameMultiplayer.Instance.SpawnKitchenObject(kitchenObjectSO, kitchonObjectParent);
    }

    public static void DestroyKitchenObject(KitchenObject kitchenObject)
    {
        KitchenGameMultiplayer.Instance.DestroyKitchenObject(kitchenObject);

    }
}
