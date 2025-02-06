using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class MapBase : NetworkBehaviour
{
    [SerializeField] protected GameObject PrefabInGameEscapeMenu;
    [SerializeField] protected GameObject PrefabInGameHud;

    protected InGameEscapeMenu _inGameEscapeMenu;
    protected InGameHud _inGameHud;

    protected InputAction _inputEscape;

    protected virtual void Start()
    {
        _inGameEscapeMenu = Instantiate(PrefabInGameEscapeMenu).GetComponent<InGameEscapeMenu>();
        _inGameEscapeMenu.SetDocumentVisible(false);

        _inGameHud = Instantiate(PrefabInGameHud).GetComponent<InGameHud>();

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientStopped += OnClientStopped;

        _inputEscape = InputSystem.actions.FindAction("InGame/Escape");
        _inputEscape.performed += OnInputEscape;
    }

    public override void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientStopped -= OnClientStopped;
        }

        _inputEscape.performed -= OnInputEscape;

        base.OnDestroy();
    }

    protected virtual void OnClientConnected(ulong clientId) { }
    protected virtual void OnClientStopped(bool isHost) { }

    protected virtual void OnInputEscape(InputAction.CallbackContext ctx)
    {
        _inGameEscapeMenu.ToggleDocumentVisible();
    }
}
