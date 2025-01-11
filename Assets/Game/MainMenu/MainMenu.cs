using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class MainMenu : MonoBehaviour
{
    private UIDocument _document;
    private VisualElement _root;
    private Button _startGameButton;
    private Button _optionsGameButton;
    private Button _exitGameButton;

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
        _root = _document.rootVisualElement;

        _startGameButton = _root.Q("StartGameButton") as Button;
        _optionsGameButton = _root.Q("OptionsGameButton") as Button;
        _exitGameButton = _root.Q("ExitGameButton") as Button;

        _startGameButton.RegisterCallback<ClickEvent>(OnClickStartGameButton);
        _optionsGameButton.RegisterCallback<ClickEvent>(OnClickOptionsGameButton);
        _exitGameButton.RegisterCallback<ClickEvent>(OnClickExitGameButton);
    }

    private void OnClickStartGameButton(ClickEvent evt)
    {
        Debug.Log("Start Game");
        SceneManager.LoadScene(Scenes.LobbyListMenu, LoadSceneMode.Single);
    }

    private void OnClickOptionsGameButton(ClickEvent evt)
    {
        Debug.Log("Options");
    }
    private void OnClickExitGameButton(ClickEvent evt)
    {
        Debug.Log("Exit Game");
        Application.Quit();
    }
}
