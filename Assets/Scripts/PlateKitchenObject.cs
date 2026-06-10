using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlateKitchenObject : KitchenObject
{
    public event EventHandler<OnIngredientAddedEventArgs> OnIngredientAdded;

    public class OnIngredientAddedEventArgs : EventArgs
    {
        public KitchenObjectSO kitchenObjectSO;
    }

    [SerializeField] private List<KitchenObjectSO> validKitchenObjectObjectSOList;

    private NetworkList<int> kitchenObjectSOIndexNetworkList;

    protected override void Awake()
    {
        base.Awake();
        kitchenObjectSOIndexNetworkList = new NetworkList<int>(
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        kitchenObjectSOIndexNetworkList.OnListChanged += KitchenObjectSOIndexNetworkList_OnListChanged;
    }

    public override void OnNetworkDespawn()
    {
        kitchenObjectSOIndexNetworkList.OnListChanged -= KitchenObjectSOIndexNetworkList_OnListChanged;
        base.OnNetworkDespawn();
    }

    private void KitchenObjectSOIndexNetworkList_OnListChanged(NetworkListEvent<int> changeEvent)
    {
        if (changeEvent.Type != NetworkListEvent<int>.EventType.Add)
        {
            return;
        }

        KitchenObjectSO kitchenObjectSO = KitchenGameMultiplayer.Instance.GetKitchenObjectSOFromIndex(changeEvent.Value);
        if (kitchenObjectSO == null)
        {
            return;
        }

        OnIngredientAdded?.Invoke(this, new OnIngredientAddedEventArgs
        {
            kitchenObjectSO = kitchenObjectSO
        });
    }

    public bool TryAddIngredient(KitchenObjectSO kitchenObjectSO)
    {
        if (!IsValidIngredient(kitchenObjectSO))
        {
            return false;
        }

        int kitchenObjectSOIndex = KitchenGameMultiplayer.Instance.GetKitchenObjectSOIndex(kitchenObjectSO);
        if (kitchenObjectSOIndex < 0)
        {
            return false;
        }

        if (ContainsIngredientIndex(kitchenObjectSOIndex))
        {
            return false;
        }

        if (IsServer)
        {
            return TryAddIngredientServer(kitchenObjectSOIndex);
        }

        AddIngredientServerRpc(kitchenObjectSOIndex);
        return false;
    }

    [ServerRpc(RequireOwnership = false)]
    private void AddIngredientServerRpc(int kitchenObjectSOIndex)
    {
        TryAddIngredientServer(kitchenObjectSOIndex);
    }

    private bool TryAddIngredientServer(int kitchenObjectSOIndex)
    {
        if (!IsServer)
        {
            return false;
        }

        KitchenObjectSO kitchenObjectSO = KitchenGameMultiplayer.Instance.GetKitchenObjectSOFromIndex(kitchenObjectSOIndex);
        if (!IsValidIngredient(kitchenObjectSO))
        {
            return false;
        }

        if (ContainsIngredientIndex(kitchenObjectSOIndex))
        {
            return false;
        }

        kitchenObjectSOIndexNetworkList.Add(kitchenObjectSOIndex);
        return true;
    }

    private bool IsValidIngredient(KitchenObjectSO kitchenObjectSO)
    {
        return kitchenObjectSO != null && validKitchenObjectObjectSOList.Contains(kitchenObjectSO);
    }

    private bool ContainsIngredientIndex(int kitchenObjectSOIndex)
    {
        foreach (int index in kitchenObjectSOIndexNetworkList)
        {
            if (index == kitchenObjectSOIndex)
            {
                return true;
            }
        }

        return false;
    }

    public List<KitchenObjectSO> GetKitchenObjectSOList()
    {
        List<KitchenObjectSO> kitchenObjectSOList = new List<KitchenObjectSO>();

        foreach (int kitchenObjectSOIndex in kitchenObjectSOIndexNetworkList)
        {
            KitchenObjectSO kitchenObjectSO = KitchenGameMultiplayer.Instance.GetKitchenObjectSOFromIndex(kitchenObjectSOIndex);
            if (kitchenObjectSO != null)
            {
                kitchenObjectSOList.Add(kitchenObjectSO);
            }
        }

        return kitchenObjectSOList;
    }
}
