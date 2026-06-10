using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;


public class StoveCounter : BaseCounter, IHasProgress
{
    public event EventHandler<IHasProgress.OnProgressChangedEventArgs> OnProgressChanged;

    public event EventHandler<OnStateChangedEventArgs> OnStateChanged;

    public class OnStateChangedEventArgs : EventArgs
    {
        public State state;
    }

    public enum State
    {
        Idle,
        Frying,
        Fried,
        Burned,
    }


    [SerializeField] private FryingRecipeSO[] fryingRecipeSOArray;
    [SerializeField] private BurningRecipeSO[] burningRecipeSOArray;


    private readonly NetworkVariable<State> state = new NetworkVariable<State>(State.Idle);
    private readonly NetworkVariable<float> fryingTimer = new NetworkVariable<float>(0f);
    private readonly NetworkVariable<float> burningTimer = new NetworkVariable<float>(0f);
    private FryingRecipeSO fryingRecipeSO;
    private BurningRecipeSO burningRecipeSO;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        fryingTimer.OnValueChanged += FryingTimer_OnValueChanged;
        burningTimer.OnValueChanged += BurningTimer_OnValueChanged;
        state.OnValueChanged += State_OnValueChanged;

        State_OnValueChanged(State.Idle, state.Value);
    }

    public override void OnNetworkDespawn()
    {
        fryingTimer.OnValueChanged -= FryingTimer_OnValueChanged;
        burningTimer.OnValueChanged -= BurningTimer_OnValueChanged;
        state.OnValueChanged -= State_OnValueChanged;
        base.OnNetworkDespawn();
    }

    private void State_OnValueChanged(State previousState, State newState)
    {
        UpdateRecipeRefsForCurrentState();

        OnStateChanged?.Invoke(this, new OnStateChangedEventArgs
        {
            state = newState,
        });

        switch (newState)
        {
            case State.Idle:
            case State.Burned:
                OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
                {
                    progressNormalized = 0f
                });
                break;
            case State.Frying:
                if (fryingRecipeSO != null)
                {
                    OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
                    {
                        progressNormalized = fryingTimer.Value / fryingRecipeSO.fryingTimerMax
                    });
                }
                break;
            case State.Fried:
                if (burningRecipeSO != null)
                {
                    OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
                    {
                        progressNormalized = burningTimer.Value / burningRecipeSO.burningTimerMax
                    });
                }
                break;
        }
    }

    private void UpdateRecipeRefsForCurrentState()
    {
        if (!HasKitchenObject())
        {
            fryingRecipeSO = null;
            burningRecipeSO = null;
            return;
        }

        KitchenObjectSO kitchenObjectSO = GetKitchenObject().GetKitchenObjectSO();

        switch (state.Value)
        {
            case State.Frying:
                fryingRecipeSO = GetFryingRecipeSOWithInput(kitchenObjectSO);
                burningRecipeSO = null;
                break;
            case State.Fried:
                burningRecipeSO = GetBurningRecipeSOWithInput(kitchenObjectSO);
                break;
            default:
                fryingRecipeSO = null;
                burningRecipeSO = null;
                break;
        }
    }

    private void FryingTimer_OnValueChanged(float oldValue, float newValue)
    {
        if (state.Value != State.Frying || fryingRecipeSO == null)
        {
            return;
        }

        OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
        {
            progressNormalized = newValue / fryingRecipeSO.fryingTimerMax
        });
    }

    private void BurningTimer_OnValueChanged(float oldValue, float newValue)
    {
        if (state.Value != State.Fried || burningRecipeSO == null)
        {
            return;
        }

        OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
        {
            progressNormalized = newValue / burningRecipeSO.burningTimerMax
        });
    }

    private void Update()
    {
        if (!IsServer)
        {
            return;
        }

        if (!HasKitchenObject())
        {
            return;
        }

        switch (state.Value)
        {
            case State.Idle:
                break;
            case State.Frying:
                if (fryingRecipeSO == null)
                {
                    break;
                }

                fryingTimer.Value += Time.deltaTime;

                if (fryingTimer.Value > fryingRecipeSO.fryingTimerMax)
                {
                    KitchenObjectSO outputKitchenObjectSO = fryingRecipeSO.output;
                    KitchenObject.DestroyKitchenObject(GetKitchenObject());
                    KitchenObject.SpawnKitchenObject(outputKitchenObjectSO, this);

                    burningTimer.Value = 0f;
                    burningRecipeSO = GetBurningRecipeSOWithInput(outputKitchenObjectSO);
                    state.Value = State.Fried;
                }
                break;
            case State.Fried:
                if (burningRecipeSO == null)
                {
                    break;
                }

                burningTimer.Value += Time.deltaTime;

                if (burningTimer.Value > burningRecipeSO.burningTimerMax)
                {
                    KitchenObjectSO outputKitchenObjectSO = burningRecipeSO.output;
                    KitchenObject.DestroyKitchenObject(GetKitchenObject());
                    KitchenObject.SpawnKitchenObject(outputKitchenObjectSO, this);

                    state.Value = State.Burned;
                }
                break;
            case State.Burned:
                break;
        }
    }

    public override void Interact(Player player)
    {
        if (!HasKitchenObject())
        {
            if (player.HasKitchenObject())
            {
                if (HasRecipWithInput(player.GetKitchenObject().GetKitchenObjectSO()))
                {
                    KitchenObject kitchenObject = player.GetKitchenObject();
                    InteractPlaceObjectOnStoveServerRpc(player.NetworkObject, kitchenObject.NetworkObject);
                }
            }
        }
        else
        {
            if (player.HasKitchenObject())
            {
                if (player.GetKitchenObject().TryGetPlate(out PlateKitchenObject _))
                {
                    InteractPutOnPlateServerRpc(player.NetworkObject, GetKitchenObject().NetworkObject);
                }
            }
            else
            {
                InteractTakeObjectOffStoveServerRpc(player.NetworkObject, GetKitchenObject().NetworkObject);
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void InteractPlaceObjectOnStoveServerRpc(
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

        fryingRecipeSO = GetFryingRecipeSOWithInput(kitchenObject.GetKitchenObjectSO());
        fryingTimer.Value = 0f;
        burningTimer.Value = 0f;
        burningRecipeSO = null;
        state.Value = State.Frying;
    }

    [ServerRpc(RequireOwnership = false)]
    private void InteractTakeObjectOffStoveServerRpc(
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
        ResetStoveState();
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
        ResetStoveState();
    }

    private void ResetStoveState()
    {
        fryingTimer.Value = 0f;
        burningTimer.Value = 0f;
        fryingRecipeSO = null;
        burningRecipeSO = null;
        state.Value = State.Idle;
    }

    private bool HasRecipWithInput(KitchenObjectSO inputKitchenObjectSO)
    {
        FryingRecipeSO fryingRecipeSO = GetFryingRecipeSOWithInput(inputKitchenObjectSO);
        return fryingRecipeSO != null;
    }

    private FryingRecipeSO GetFryingRecipeSOWithInput(KitchenObjectSO inputKitchenObjectSO)
    {
        foreach (FryingRecipeSO fryingRecipeSO in fryingRecipeSOArray)
        {
            if (fryingRecipeSO.input == inputKitchenObjectSO)
            {
                return fryingRecipeSO;
            }
        }
        return null;
    }

    private BurningRecipeSO GetBurningRecipeSOWithInput(KitchenObjectSO inputKitchenObjectSO)
    {
        foreach (BurningRecipeSO burningRecipeSO in burningRecipeSOArray)
        {
            if (burningRecipeSO.input == inputKitchenObjectSO)
            {
                return burningRecipeSO;
            }
        }
        return null;
    }
}
