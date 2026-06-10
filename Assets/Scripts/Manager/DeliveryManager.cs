using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

public class DeliveryManager : NetworkBehaviour
{
    public event EventHandler OnRecipeSpawned;
    public event EventHandler OnRecipeCompleted;
    public event EventHandler OnRecipeSuccess;
    public event EventHandler OnRecipeFailed;
    public static DeliveryManager Instance { get; private set; }

    [SerializeField] private RecipeListSO recipeListSO;

    private NetworkList<int> waitingRecipeSOIndexNetworkList;
    private readonly NetworkVariable<int> successfulRecipesAmount = new NetworkVariable<int>(0);

    private float spawnRecipeTimer = 4f;
    private readonly float spawnRecipeTimerMax = 4f;
    private readonly int waitingRecipesMax = 4;

    private void Awake()
    {
        Instance = this;
        waitingRecipeSOIndexNetworkList = new NetworkList<int>(
            readPerm: NetworkVariableReadPermission.Everyone,
            writePerm: NetworkVariableWritePermission.Server);
    }

    public override void OnNetworkSpawn()
    {
        waitingRecipeSOIndexNetworkList.OnListChanged += WaitingRecipeSOIndexNetworkList_OnListChanged;
    }

    public override void OnNetworkDespawn()
    {
        waitingRecipeSOIndexNetworkList.OnListChanged -= WaitingRecipeSOIndexNetworkList_OnListChanged;
    }

    private void WaitingRecipeSOIndexNetworkList_OnListChanged(NetworkListEvent<int> changeEvent)
    {
        if (changeEvent.Type == NetworkListEvent<int>.EventType.Add)
        {
            OnRecipeSpawned?.Invoke(this, EventArgs.Empty);
        }
        else if (changeEvent.Type == NetworkListEvent<int>.EventType.Remove)
        {
            OnRecipeCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Update()
    {
        if (!IsServer)
        {
            return;
        }

        spawnRecipeTimer -= Time.deltaTime;
        if (spawnRecipeTimer <= 0f)
        {
            spawnRecipeTimer = spawnRecipeTimerMax;

            if (GameManager.Instance.IsGamePlaying()
                && waitingRecipeSOIndexNetworkList.Count < waitingRecipesMax
                && recipeListSO.recipeSOList.Count > 0)
            {
                int waitingRecipeSOIndex = Random.Range(0, recipeListSO.recipeSOList.Count);
                waitingRecipeSOIndexNetworkList.Add(waitingRecipeSOIndex);
            }
        }
    }

    public void DeliverRecipe(PlateKitchenObject plateKitchenObject)
    {
        DeliverRecipeServerRpc(plateKitchenObject.NetworkObject);
    }

    [ServerRpc(RequireOwnership = false)]
    private void DeliverRecipeServerRpc(NetworkObjectReference plateNetworkObjectReference)
    {
        if (!plateNetworkObjectReference.TryGet(out NetworkObject plateNetworkObject))
        {
            return;
        }

        if (!plateNetworkObject.TryGetComponent(out PlateKitchenObject plateKitchenObject))
        {
            return;
        }

        List<KitchenObjectSO> plateContents = plateKitchenObject.GetKitchenObjectSOList();

        for (int i = 0; i < waitingRecipeSOIndexNetworkList.Count; i++)
        {
            int recipeIndex = waitingRecipeSOIndexNetworkList[i];
            if (recipeIndex < 0 || recipeIndex >= recipeListSO.recipeSOList.Count)
            {
                continue;
            }

            RecipeSO waitingRecipeSO = recipeListSO.recipeSOList[recipeIndex];
            if (PlateMatchesRecipe(plateContents, waitingRecipeSO))
            {
                waitingRecipeSOIndexNetworkList.RemoveAt(i);
                successfulRecipesAmount.Value++;
                DeliverCorrectRecipeClientRpc();
                return;
            }
        }

        DeliverIncorrectRecipeClientRpc();
    }

    private static bool PlateMatchesRecipe(List<KitchenObjectSO> plateContents, RecipeSO waitingRecipeSO)
    {
        if (waitingRecipeSO.kitchenObjectSOList.Count != plateContents.Count)
        {
            return false;
        }

        foreach (KitchenObjectSO recipeKitchenObjectSO in waitingRecipeSO.kitchenObjectSOList)
        {
            bool ingredientFound = false;
            foreach (KitchenObjectSO plateKitchenObjectSO in plateContents)
            {
                if (plateKitchenObjectSO == recipeKitchenObjectSO)
                {
                    ingredientFound = true;
                    break;
                }
            }

            if (!ingredientFound)
            {
                return false;
            }
        }

        return true;
    }

    [ServerRpc(RequireOwnership = false)]
    private void DeliverIncorrectRecipeServerRpc()
    {
        DeliverIncorrectRecipeClientRpc();
    }

    [ClientRpc]
    private void DeliverIncorrectRecipeClientRpc()
    {
        OnRecipeFailed?.Invoke(this, EventArgs.Empty);
    }

    [ClientRpc]
    private void DeliverCorrectRecipeClientRpc()
    {
        OnRecipeSuccess?.Invoke(this, EventArgs.Empty);
    }

    public List<RecipeSO> GetWaitingRecipeSOList()
    {
        List<RecipeSO> waitingRecipeSOList = new List<RecipeSO>();

        foreach (int recipeIndex in waitingRecipeSOIndexNetworkList)
        {
            if (recipeIndex >= 0 && recipeIndex < recipeListSO.recipeSOList.Count)
            {
                waitingRecipeSOList.Add(recipeListSO.recipeSOList[recipeIndex]);
            }
        }

        return waitingRecipeSOList;
    }

    public int GetSuccessfulRecipesAmout()
    {
        return successfulRecipesAmount.Value;
    }
}
