using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

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

        _quitMatchButton.RegisterCallback((ClickEvent evt) =>
        {
            LobbyManager.Singleton.LeaveLobby();
            NetworkManager.Singleton.Shutdown();
        });

        _closeButton.RegisterCallback((ClickEvent evt) =>
        {
            SetDocumentVisible(false);
        });
    }

    public void SetDocumentVisible(bool value)
    {
        _root.visible = value;
    }

    public void ToggleDocumentVisible()
    {
        SetDocumentVisible(!_root.visible);
    }
}
