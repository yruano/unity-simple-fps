using Steamworks;
using Unity.Netcode;
using Unity.Properties;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class LobbyListMenu : MonoBehaviour
{
    private UIDocument _document;
    private VisualElement _root;
    private Button _lobbyButton1;
    private Button _lobbyButton2;
    private Button _startMatchButton;
    private IntegerField _maxPlayersNum;
    private Toggle _friendOnlyToggle;

    private CallResult<LobbyCreated_t> _onCreateLobby;

    private int _maxPlayersValue = 10;
    [CreateProperty]
    public int MaxPlayersValue
    {
        get => _maxPlayersValue;
        set => _maxPlayersValue = Mathf.Clamp(value, 0, 10);
    }

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
        _root = _document.rootVisualElement;

        _lobbyButton1 = _root.Q("LobbyButton1") as Button;
        _lobbyButton2 = _root.Q("LobbyButton2") as Button;
        _startMatchButton = _root.Q("StartMatchButton") as Button;
        _maxPlayersNum = _root.Q("MaxPlayersNum") as IntegerField;
        _friendOnlyToggle = _root.Q("FriendOnlyToggle") as Toggle;

        _onCreateLobby = new(OnCreateLobby);
    }

    private void Start()
    {
        NetworkManager.Singleton.OnClientStopped += OnClientStopped;

        _maxPlayersNum.dataSource = this;
        _maxPlayersNum.SetBinding("value", new DataBinding
        {
            dataSourcePath = PropertyPath.FromName(nameof(MaxPlayersValue))
        });

        SetLobbyButtonCallbacks(true);
    }

    public void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientStopped -= OnClientStopped;
        }
    }

    private void OnClientStopped(bool isHost)
    {
        // TODO: 방 나갔음
    }

    private void SetLobbyButtonCallbacks(bool isCreateLobbyMode)
    {
        // Reset callbacks
        _lobbyButton1.UnregisterCallback<ClickEvent>(OnClickCreateLobbyButton);
        _lobbyButton2.UnregisterCallback<ClickEvent>(OnClickDeleteLobbyButton);
        // _lobbyButton1.UnregisterCallback<ClickEvent>(OnClickJoinLobbyButton);
        // _lobbyButton2.UnregisterCallback<ClickEvent>(OnClickLeaveLobbyButton);
        _startMatchButton.UnregisterCallback<ClickEvent>(OnClickStartMatchButton);

        if (isCreateLobbyMode)
        {
            _lobbyButton1.RegisterCallback<ClickEvent>(OnClickCreateLobbyButton);
            _lobbyButton2.RegisterCallback<ClickEvent>(OnClickDeleteLobbyButton);
            _startMatchButton.RegisterCallback<ClickEvent>(OnClickStartMatchButton);
        }
        else
        {
            // _lobbyButton1.RegisterCallback<ClickEvent>(OnClickJoinLobbyButton);
            // _lobbyButton2.RegisterCallback<ClickEvent>(OnClickLeaveLobbyButton);
        }
    }

    private void OnClickCreateLobbyButton(ClickEvent evt)
    {
        if (NetworkManager.Singleton.StartHost())
        {
            _maxPlayersNum.SetEnabled(false);
            _friendOnlyToggle.SetEnabled(false);

            var lobbyType = _friendOnlyToggle.value ? ELobbyType.k_ELobbyTypeFriendsOnly : ELobbyType.k_ELobbyTypePublic;
            Debug.Log(MaxPlayersValue);
            _onCreateLobby.Set(SteamMatchmaking.CreateLobby(lobbyType, MaxPlayersValue));
        }
        else
        {
            Debug.LogError("StartHost failed.");
        }
    }

    private void OnClickDeleteLobbyButton(ClickEvent evt)
    {
        NetworkManager.Singleton.Shutdown();

        _maxPlayersNum.SetEnabled(true);
        _friendOnlyToggle.SetEnabled(true);
    }

    private void OnClickStartMatchButton(ClickEvent evt)
    {
        if (NetworkManager.Singleton.IsHost)
        {
            Debug.Log("StartMatchButton: Start.");
            NetworkManager.Singleton.SceneManager.LoadScene(Scenes.TestMap, LoadSceneMode.Single);
        }
        else
        {
            Debug.Log("StartMatchButton: You are not the host.");
        }
    }

    private void OnCreateLobby(LobbyCreated_t arg, bool bIOFailure)
    {
        if (bIOFailure)
        {
            Debug.LogError("OnCreateLobby IOFailure.");
            return;
        }

        if (arg.m_eResult == EResult.k_EResultOK)
        {
            Debug.Log("Lobby created.");
            LobbyManager.Singleton.SetJoinedLobbyId(arg.m_ulSteamIDLobby);
        }
        else
        {
            Debug.Log("Failed to create a lobby.");
        }
    }
}
