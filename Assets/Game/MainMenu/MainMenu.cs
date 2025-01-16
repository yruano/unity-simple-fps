using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Unity.Netcode;

public class MainMenu : MonoBehaviour
{
    private UIDocument _document;
    private VisualElement _root;
    private Button _startGameButton;
    private Button _optionsButton;
    private Button _exitGameButton;

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
        _root = _document.rootVisualElement;

        _startGameButton = _root.Q("StartGameButton") as Button;
        _optionsButton = _root.Q("OptionsGameButton") as Button;
        _exitGameButton = _root.Q("ExitGameButton") as Button;

        _startGameButton.RegisterCallback<ClickEvent>(OnClickStartGameButton);
        _optionsButton.RegisterCallback<ClickEvent>(OnClickOptionsButton);
        _exitGameButton.RegisterCallback<ClickEvent>(OnClickExitGameButton);
    }

    private void Start()
    {
        NetworkManager.Singleton.OnClientStarted += OnClientStarted;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton)
        {
            NetworkManager.Singleton.OnClientStarted -= OnClientStarted;
        }
    }

    private void OnClientStarted()
    {
        SceneManager.LoadScene(Scenes.LobbyListMenu);
    }

    private void OnClickStartGameButton(ClickEvent evt)
    {
        Debug.Log("StartGameButton");
        SceneManager.LoadScene(Scenes.LobbyListMenu);
    }

    private void OnClickOptionsButton(ClickEvent evt)
    {
        Debug.Log("OptionsButton");
    }

    private void OnClickExitGameButton(ClickEvent evt)
    {
        Debug.Log("ExitGameButton");
        Application.Quit();
    }
}
