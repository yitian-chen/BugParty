using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class CuttingCounter : BaseCounter, IHasProgress
{
    public static event EventHandler OnAnyCut;
    new public static void RestStaticData()
    {
        OnAnyCut = null;
    }
    public event EventHandler<IHasProgress.OnProgressChangedEventArgs> OnProgressChanged;
    public event EventHandler OnCut;

    [SerializeField] private CuttingRecipeSO[] cuttingRecipsSOArray;


    private int cuttingProgress;


    public override void Interact(Player player)
    {
        if (!HasKitchenObject())
        {
            //有一个厨房物体
            if (player.HasKitchenObject())
            {
                if (HasRecipWithInput(player.GetKitchenObject().GetKitchenObjectSO()))
                {
                    InteractPlaceObjectOnCounterServerRpc(
                        player.NetworkObject,
                        player.GetKitchenObject().NetworkObject);
                }
            }
            else
            {
                //玩家没有任何物体
            }
        }
        else
        {
            //此处没有厨房物体
            if (player.HasKitchenObject())
            {
                if (player.GetKitchenObject().TryGetPlate(out PlateKitchenObject _))
                {
                    InteractPutOnPlateServerRpc(player.NetworkObject, GetKitchenObject().NetworkObject);
                }
            }
            else
            {
                InteractTakeObjectOffCounterServerRpc(
                    player.NetworkObject,
                    GetKitchenObject().NetworkObject);
            }


        }
    }
    [ServerRpc(RequireOwnership = false)]
    private void InteractPutOnPlateServerRpc(
        NetworkObjectReference playerNetworkObjectReference,
        NetworkObjectReference kitchenObjectNetworkObjectReference)
    {
        if (!playerNetworkObjectReference.TryGet(out NetworkObject playerNetworkObject))
        {
            return;
        }

        if (!kitchenObjectNetworkObjectReference.TryGet(out NetworkObject kitchenObjectNetworkObject))
        {
            return;
        }

        if (!playerNetworkObject.TryGetComponent(out Player player))
        {
            return;
        }

        if (!kitchenObjectNetworkObject.TryGetComponent(out KitchenObject kitchenObject))
        {
            return;
        }

        if (!HasKitchenObject() || GetKitchenObject() != kitchenObject || !player.HasKitchenObject())
        {
            return;
        }

        if (!player.GetKitchenObject().TryGetPlate(out PlateKitchenObject plateKitchenObject))
        {
            return;
        }

        if (!plateKitchenObject.TryAddIngredient(kitchenObject.GetKitchenObjectSO()))
        {
            return;
        }

        KitchenObject.DestroyKitchenObject(kitchenObject);
    }

    [ServerRpc(RequireOwnership = false)]
    private void InteractPlaceObjectOnCounterServerRpc(
        NetworkObjectReference playerNetworkObjectReference,
        NetworkObjectReference kitchenObjectNetworkObjectReference)
    {
        if (!playerNetworkObjectReference.TryGet(out NetworkObject playerNetworkObject))
        {
            return;
        }

        if (!kitchenObjectNetworkObjectReference.TryGet(out NetworkObject kitchenObjectNetworkObject))
        {
            return;
        }

        if (!playerNetworkObject.TryGetComponent(out Player player))
        {
            return;
        }

        if (!kitchenObjectNetworkObject.TryGetComponent(out KitchenObject kitchenObject))
        {
            return;
        }

        if (HasKitchenObject() || !player.HasKitchenObject() || player.GetKitchenObject() != kitchenObject)
        {
            return;
        }

        if (!HasRecipWithInput(kitchenObject.GetKitchenObjectSO()))
        {
            return;
        }

        kitchenObject.SetKitchenObjectParentFromServer(NetworkObject);
        cuttingProgress = 0;
        InteractLogicPlaceObjectOnCounterClientRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void InteractTakeObjectOffCounterServerRpc(
        NetworkObjectReference playerNetworkObjectReference,
        NetworkObjectReference kitchenObjectNetworkObjectReference)
    {
        if (!playerNetworkObjectReference.TryGet(out NetworkObject playerNetworkObject))
        {
            return;
        }

        if (!kitchenObjectNetworkObjectReference.TryGet(out NetworkObject kitchenObjectNetworkObject))
        {
            return;
        }

        if (!playerNetworkObject.TryGetComponent(out Player player))
        {
            return;
        }

        if (!kitchenObjectNetworkObject.TryGetComponent(out KitchenObject kitchenObject))
        {
            return;
        }

        if (!HasKitchenObject() || GetKitchenObject() != kitchenObject || player.HasKitchenObject())
        {
            return;
        }

        kitchenObject.SetKitchenObjectParentFromServer(playerNetworkObjectReference);
    }

    [ClientRpc]
    private void InteractLogicPlaceObjectOnCounterClientRpc()
    {
        cuttingProgress = 0;

        OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
        {
            progressNormalized = 0f
        });
    }


    public override void InteractAlternate(Player player)
    {
        if (HasKitchenObject() && HasRecipWithInput(GetKitchenObject().GetKitchenObjectSO()))
        {
            //有一个厨房物体并且可以被切块
            CutObjectServerRpc();
        }
    }
    [ServerRpc(RequireOwnership = false)]
    private void CutObjectServerRpc()
    {
        if (!HasKitchenObject() || !HasRecipWithInput(GetKitchenObject().GetKitchenObjectSO()))
        {
            return;
        }

        cuttingProgress++;

        CuttingRecipeSO cuttingRecipeSO = GetCuttingRecipeSOWithInput(GetKitchenObject().GetKitchenObjectSO());
        CutObjectClientRpc(cuttingProgress, cuttingRecipeSO.cuttingProgressMax);

        if (cuttingProgress >= cuttingRecipeSO.cuttingProgressMax)
        {
            KitchenObjectSO outputKitchenObjectSO = GetOutputForInput(GetKitchenObject().GetKitchenObjectSO());
            KitchenObject.DestroyKitchenObject(GetKitchenObject());
            KitchenObject.SpawnKitchenObject(outputKitchenObjectSO, this);
        }
    }

    [ClientRpc]
    private void CutObjectClientRpc(int cuttingProgress, float cuttingProgressMax)
    {
        this.cuttingProgress = cuttingProgress;

        OnCut?.Invoke(this, EventArgs.Empty);
        OnAnyCut?.Invoke(this, EventArgs.Empty);

        OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
        {
            progressNormalized = (float)cuttingProgress / cuttingProgressMax
        });
    }

    private bool HasRecipWithInput(KitchenObjectSO inputKitchenObjectSO)
    {
        CuttingRecipeSO cuttingRecipeSO = GetCuttingRecipeSOWithInput(inputKitchenObjectSO);
        return cuttingRecipeSO != null;
    }


    private KitchenObjectSO GetOutputForInput(KitchenObjectSO inputKitchenObjectSO)
    {
        CuttingRecipeSO cuttingRecipeSO = GetCuttingRecipeSOWithInput(inputKitchenObjectSO);
        if (cuttingRecipeSO != null)
        {
            return cuttingRecipeSO.output;
        }
        else
        {
            return null;
        }
    }


    private CuttingRecipeSO GetCuttingRecipeSOWithInput(KitchenObjectSO inputKitchenObjectSO)
    {
        foreach (CuttingRecipeSO cuttingRecipeSO in cuttingRecipsSOArray)
        {
            if (cuttingRecipeSO.input == inputKitchenObjectSO)
            {
                return cuttingRecipeSO;
            }
        }
        return null;
    }

}
