using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class MapBase : NetworkBehaviour
{
    [SerializeField] protected GameObject PrefabInGameEscapeMenu;

    protected InputAction _inputEscape;
    protected InGameEscapeMenu _inGameEscapeMenu;

    protected virtual void Start()
    {
        NetworkManager.Singleton.OnClientStopped += OnClientStopped;

        _inputEscape = InputSystem.actions.FindAction("InGame/Escape");
        _inputEscape.performed += OnInputEscape;

        _inGameEscapeMenu = Instantiate(PrefabInGameEscapeMenu).GetComponent<InGameEscapeMenu>();
        _inGameEscapeMenu.SetDocumentVisible(false);
    }

    public override void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientStopped -= OnClientStopped;
        }

        _inputEscape.performed -= OnInputEscape;

        base.OnDestroy();
    }

    protected virtual void OnClientStopped(bool isHost) { }

    protected virtual void OnInputEscape(InputAction.CallbackContext ctx)
    {
        _inGameEscapeMenu.ToggleDocumentVisible();
    }
}
