using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class GameInput : MonoBehaviour
{
    private const string PLAYER_PREFS_BINDINGS = "InputBindings";
    public static GameInput Instance { get; private set; }
    public event EventHandler OnInteractAction;
    public event EventHandler OnInteractAlternateAction;
    public event EventHandler OnPauseAction;
    public event EventHandler OnBindingRebind;

    public enum Binding
    {
        Move_Up,
        Move_Down,
        Move_Left,
        Move_Right,
        Interact,
        InteractAlternate,
        Pause,
        Gamepad_Interact,
        Gamepad_InteractAlternate,
        Gamepad_Pause,
    }

    private PlayerInputActions playerInputActions;

    private void Awake()
    {
        Instance = this;
        playerInputActions = new PlayerInputActions();


        if (PlayerPrefs.HasKey(PLAYER_PREFS_BINDINGS))
        {
            playerInputActions.LoadBindingOverridesFromJson(PlayerPrefs.GetString(PLAYER_PREFS_BINDINGS));
        }

        RemoveMouseBindingsFromAction(playerInputActions.Player.Interact);
        RemoveMouseBindingsFromAction(playerInputActions.Player.Pause);
        SaveBindingOverrides();

        playerInputActions.Player.Enable();

        playerInputActions.Player.Interact.performed += Interact_performed;
        playerInputActions.Player.InteractAlternate.performed += InteractAlternate_performed;
        playerInputActions.Player.Pause.performed += Pause_performed;
    }

    private void OnDestroy()
    {
        playerInputActions.Player.Interact.performed -= Interact_performed;
        playerInputActions.Player.InteractAlternate.performed -= InteractAlternate_performed;
        playerInputActions.Player.Pause.performed -= Pause_performed;

        playerInputActions.Dispose();
    }

    private void Pause_performed(UnityEngine.InputSystem.InputAction.CallbackContext obj)
    {
        if (obj.control != null && obj.control.device is Mouse)
        {
            return;
        }

        OnPauseAction?.Invoke(this, EventArgs.Empty);
    }

    private void InteractAlternate_performed(UnityEngine.InputSystem.InputAction.CallbackContext obj)
    {
        OnInteractAlternateAction?.Invoke(this, EventArgs.Empty);
    }

    private void Interact_performed(UnityEngine.InputSystem.InputAction.CallbackContext obj)
    {
        if (obj.control != null && obj.control.device is Mouse)
        {
            return;
        }

        OnInteractAction?.Invoke(this, EventArgs.Empty);
    }

    public Vector2 GetMovementVectorNormalized()
    {
        Vector2 inputVector = playerInputActions.Player.Move.ReadValue<Vector2>();

        /*
        //LEGACY INPUT MANAGER CODE  

        Vector2 inputVector = new Vector2(0, 0);

         if (Input.GetKey(KeyCode.W))
        {
            inputVector.y = +1;
        }
        if (Input.GetKey(KeyCode.S))
        {
            inputVector.y = -1;
        }
        if (Input.GetKey(KeyCode.A))
        {
            inputVector.x = -1;
        }
        if (Input.GetKey(KeyCode.D))
        {
            inputVector.x = +1;
        }
        */
        inputVector = inputVector.normalized;

        return inputVector;
    }

    public string GetBindingText(Binding binding)
    {
        switch (binding)
        {
            case Binding.Interact:
                return GetBindingDisplayString(playerInputActions.Player.Interact, "<Keyboard>");
            case Binding.InteractAlternate:
                return GetBindingDisplayString(playerInputActions.Player.InteractAlternate, "<Keyboard>");
            case Binding.Pause:
                return GetBindingDisplayString(playerInputActions.Player.Pause, "<Keyboard>");
            case Binding.Move_Up:
                return GetMoveCompositePartDisplayString("up");
            case Binding.Move_Down:
                return GetMoveCompositePartDisplayString("down");
            case Binding.Move_Left:
                return GetMoveCompositePartDisplayString("left");
            case Binding.Move_Right:
                return GetMoveCompositePartDisplayString("right");
            case Binding.Gamepad_Interact:
                return GetBindingDisplayString(playerInputActions.Player.Interact, "<Gamepad>");
            case Binding.Gamepad_InteractAlternate:
                return GetBindingDisplayString(playerInputActions.Player.InteractAlternate, "<Gamepad>");
            case Binding.Gamepad_Pause:
                return GetBindingDisplayString(playerInputActions.Player.Pause, "<Gamepad>");
            default:
                return string.Empty;
        }
    }

    private static string GetBindingDisplayString(InputAction inputAction, string pathContains)
    {
        for (int i = 0; i < inputAction.bindings.Count; i++)
        {
            string path = inputAction.bindings[i].effectivePath;
            if (string.IsNullOrEmpty(path))
            {
                path = inputAction.bindings[i].path;
            }

            if (path.Contains(pathContains))
            {
                return inputAction.bindings[i].ToDisplayString();
            }
        }

        return string.Empty;
    }

    private string GetMoveCompositePartDisplayString(string partName)
    {
        int bindingIndex = FindBindingIndex(
            playerInputActions.Player.Move,
            compositePartName: partName,
            useWasdComposite: true);

        if (bindingIndex < 0)
        {
            return string.Empty;
        }

        return playerInputActions.Player.Move.bindings[bindingIndex].ToDisplayString();
    }

    private bool TryGetBindingInfo(Binding binding, out InputAction inputAction, out int bindingIndex)
    {
        switch (binding)
        {
            case Binding.Move_Up:
                inputAction = playerInputActions.Player.Move;
                bindingIndex = FindBindingIndex(inputAction, compositePartName: "up", useWasdComposite: true);
                break;
            case Binding.Move_Down:
                inputAction = playerInputActions.Player.Move;
                bindingIndex = FindBindingIndex(inputAction, compositePartName: "down", useWasdComposite: true);
                break;
            case Binding.Move_Left:
                inputAction = playerInputActions.Player.Move;
                bindingIndex = FindBindingIndex(inputAction, compositePartName: "left", useWasdComposite: true);
                break;
            case Binding.Move_Right:
                inputAction = playerInputActions.Player.Move;
                bindingIndex = FindBindingIndex(inputAction, compositePartName: "right", useWasdComposite: true);
                break;
            case Binding.Interact:
                inputAction = playerInputActions.Player.Interact;
                bindingIndex = FindBindingIndex(inputAction, pathContains: "<Keyboard>");
                break;
            case Binding.InteractAlternate:
                inputAction = playerInputActions.Player.InteractAlternate;
                bindingIndex = FindBindingIndex(inputAction, pathContains: "<Keyboard>");
                break;
            case Binding.Pause:
                inputAction = playerInputActions.Player.Pause;
                bindingIndex = FindBindingIndex(inputAction, pathContains: "<Keyboard>");
                break;
            case Binding.Gamepad_Interact:
                inputAction = playerInputActions.Player.Interact;
                bindingIndex = FindBindingIndex(inputAction, pathContains: "<Gamepad>");
                break;
            case Binding.Gamepad_InteractAlternate:
                inputAction = playerInputActions.Player.InteractAlternate;
                bindingIndex = FindBindingIndex(inputAction, pathContains: "<Gamepad>");
                break;
            case Binding.Gamepad_Pause:
                inputAction = playerInputActions.Player.Pause;
                bindingIndex = FindBindingIndex(inputAction, pathContains: "<Gamepad>");
                break;
            default:
                inputAction = null;
                bindingIndex = -1;
                return false;
        }

        return inputAction != null && bindingIndex >= 0;
    }

    private static int FindBindingIndex(
        InputAction inputAction,
        string pathContains = null,
        string compositePartName = null,
        bool useWasdComposite = false)
    {
        for (int i = 0; i < inputAction.bindings.Count; i++)
        {
            InputBinding inputBinding = inputAction.bindings[i];
            string path = inputBinding.effectivePath;
            if (string.IsNullOrEmpty(path))
            {
                path = inputBinding.path;
            }

            if (compositePartName != null)
            {
                if (!inputBinding.isPartOfComposite || inputBinding.name != compositePartName)
                {
                    continue;
                }

                if (useWasdComposite && !IsWasdKeyboardPath(path))
                {
                    continue;
                }

                return i;
            }

            if (pathContains != null && path.Contains(pathContains))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsWasdKeyboardPath(string path)
    {
        return path.Contains("<Keyboard>/w")
            || path.Contains("<Keyboard>/s")
            || path.Contains("<Keyboard>/a")
            || path.Contains("<Keyboard>/d");
    }

    public void RebindBinding(Binding binding, Action onActionRebound)
    {
        if (!TryGetBindingInfo(binding, out InputAction inputAction, out int bindingIndex))
        {
            return;
        }

        playerInputActions.Player.Disable();

        inputAction.PerformInteractiveRebinding(bindingIndex)
               .WithControlsExcluding("<Mouse>")
               .WithControlsExcluding("<Pointer>")
               .OnComplete(callback =>
               {
                   callback.Dispose();
                   RemoveMouseBindingsFromAction(playerInputActions.Player.Interact);
                   RemoveMouseBindingsFromAction(playerInputActions.Player.Pause);
                   playerInputActions.Player.Enable();
                   onActionRebound();

                   SaveBindingOverrides();

                   OnBindingRebind?.Invoke(this, EventArgs.Empty);
               })
               .Start();
    }

    private static void RemoveMouseBindingsFromAction(InputAction inputAction)
    {
        for (int i = 0; i < inputAction.bindings.Count; i++)
        {
            string path = inputAction.bindings[i].effectivePath;
            if (string.IsNullOrEmpty(path))
            {
                path = inputAction.bindings[i].path;
            }

            if (path.Contains("<Mouse>") || path.Contains("<Pointer>"))
            {
                inputAction.ApplyBindingOverride(i, string.Empty);
            }
        }
    }

    private void SaveBindingOverrides()
    {
        PlayerPrefs.SetString(PLAYER_PREFS_BINDINGS, playerInputActions.SaveBindingOverridesAsJson());
        PlayerPrefs.Save();
    }

}
