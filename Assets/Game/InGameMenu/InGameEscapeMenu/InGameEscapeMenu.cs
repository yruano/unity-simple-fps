using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;

public class InGameEscapeMenu : MonoBehaviour
{
    private UIDocument _document;
    private VisualElement _root;
    private Button _optionsButton;
    private Button _quitMatchButton;
    private Button _closeButton;

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
        _root = _document.rootVisualElement;

        _optionsButton = _root.Q("OptionsButton") as Button;
        _quitMatchButton = _root.Q("QuitMatchButton") as Button;
        _closeButton = _root.Q("CloseButton") as Button;

        _quitMatchButton.RegisterCallback((ClickEvent evt) => OnQuitMatchButtonPressed());
        _quitMatchButton.RegisterCallback((NavigationSubmitEvent evt) => OnQuitMatchButtonPressed());

        _closeButton.RegisterCallback((ClickEvent evt) => SetDocumentVisible(false));
        _closeButton.RegisterCallback((NavigationSubmitEvent evt) => SetDocumentVisible(false));
    }

    public void OnQuitMatchButtonPressed()
    {
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;

        LobbyManager.Singleton.LeaveLobby();
        NetworkManager.Singleton.Shutdown();
    }

    public void SetDocumentVisible(bool value)
    {
        UnityEngine.Cursor.lockState = value ? CursorLockMode.None : CursorLockMode.Locked;
        UnityEngine.Cursor.visible = value;

        var player = LobbyManager.Singleton.GetLocalUser().Player;
        if (player != null)
            player.SetInputActive(!value);

        _root.visible = value;
    }

    public void ToggleDocumentVisible()
    {
        SetDocumentVisible(!_root.visible);
    }
}
