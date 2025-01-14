using System;
using System.Collections.Generic;
using Steamworks;
using Unity.Netcode;
using Unity.Properties;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public struct LobbyListEntryData
{
    public CSteamID LobbyId;
    public string LobbyName;
    public string LobbyOwnerName;
}

public class LobbyListEntryController
{
    private LobbyListEntryData data;
    private readonly Label _lobbyNameLabel;
    private readonly Label _lobbyOwnerLabel;
    private readonly Button _lobbyJoinButton;

    public LobbyListEntryController(LobbyListMenu lobbyListMenu, VisualElement root, EventCallback<ClickEvent> onClickJoinButton)
    {
        _lobbyNameLabel = root.Q("LobbyNameLabel") as Label;
        _lobbyOwnerLabel = root.Q("LobbyOwnerLabel") as Label;
        _lobbyJoinButton = root.Q("JoinButton") as Button;

        _lobbyJoinButton.RegisterCallback((ClickEvent evt) =>
        {
            lobbyListMenu.JoiningLobbyData = data;
            onClickJoinButton(evt);
        });
    }

    public void UpdateElements(LobbyListEntryData data)
    {
        this.data = data;
        _lobbyNameLabel.text = data.LobbyName;
        _lobbyOwnerLabel.text = data.LobbyOwnerName;
    }
}

public class LobbyListMenu : MonoBehaviour
{
    [SerializeField] private VisualTreeAsset LobbyListEntryAsset;

    private UIDocument _document;
    private VisualElement _root;
    private Button _refreshButton;
    private ListView _lobbyListView;
    private TextField _lobbyNameTextField;
    private Button _lobbyButton1;
    private Button _lobbyButton2;
    private Button _startMatchButton;
    private IntegerField _maxPlayersIntField;
    private Toggle _friendOnlyToggle;

    private CallResult<LobbyMatchList_t> _onRequestLobbyList;
    private CallResult<LobbyCreated_t> _onCreateLobby;

    private List<LobbyListEntryData> _lobbyEntries = new();
    public LobbyListEntryData? JoiningLobbyData;

    private int _myMaxPlayersValue = 10;
    [CreateProperty]
    public int MyMaxPlayersValue
    {
        get => _myMaxPlayersValue;
        set => _myMaxPlayersValue = Mathf.Clamp(value, 0, 10);
    }

    private string _myLobbyNameValue = "";
    [CreateProperty]
    public string MyLobbyNameValue
    {
        get => _myLobbyNameValue;
        set => _myLobbyNameValue = value.Trim();
    }

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
        _root = _document.rootVisualElement;

        _refreshButton = _root.Q("RefreshButton") as Button;
        _lobbyListView = _root.Q("LobbyListView") as ListView;
        _lobbyNameTextField = _root.Q("LobbyNameTextField") as TextField;
        _lobbyButton1 = _root.Q("LobbyButton1") as Button;
        _lobbyButton2 = _root.Q("LobbyButton2") as Button;
        _startMatchButton = _root.Q("StartMatchButton") as Button;
        _maxPlayersIntField = _root.Q("MaxPlayersNum") as IntegerField;
        _friendOnlyToggle = _root.Q("FriendOnlyToggle") as Toggle;

        _onRequestLobbyList = new(OnRequestLobbyList);
        _onCreateLobby = new(OnCreateLobby);
    }

    private void Start()
    {
        NetworkManager.Singleton.OnClientStarted += OnClientStarted;
        NetworkManager.Singleton.OnClientStopped += OnClientStopped;

        // Setup RefreshButton.
        _refreshButton.RegisterCallback<ClickEvent>((evt) => RefreshLobbyList());

        // Setup LobbyListView.
        _lobbyListView.itemsSource = _lobbyEntries;
        _lobbyListView.makeItem = () =>
        {
            var item = LobbyListEntryAsset.Instantiate();
            item.userData = new LobbyListEntryController(this, item, OnClickJoinLobbyButton);
            return item;
        };
        _lobbyListView.bindItem = (item, index) =>
        {
            (item.userData as LobbyListEntryController)?.UpdateElements(_lobbyEntries[index]);
        };

        // Setup LobbyName.
        var myLobbyNameBinding = new DataBinding { dataSource = this, dataSourcePath = new(nameof(MyLobbyNameValue)) };
        _lobbyNameTextField.SetBinding("value", myLobbyNameBinding);

        // Setup MaxPlayersNum.
        var myMaxPlayerNumBinding = new DataBinding { dataSource = this, dataSourcePath = new(nameof(MyMaxPlayersValue)) };
        _maxPlayersIntField.SetBinding("value", myMaxPlayerNumBinding);

        // Setup LobbyButton.
        _lobbyButton1.RegisterCallback<ClickEvent>(OnClickCreateLobbyButton);
        _lobbyButton2.RegisterCallback<ClickEvent>(OnClickDeleteLobbyButton);

        // Request lobby list.
        InvokeRepeating(nameof(RefreshLobbyList), 0, 5);

        // Update lobby elements.
        UpdateLobbyElements(true);
    }

    public void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientStarted -= OnClientStarted;
            NetworkManager.Singleton.OnClientStopped -= OnClientStopped;
        }
    }

    private void OnClientStarted()
    {
        if (!NetworkManager.Singleton.IsHost && LobbyManager.Singleton.JoinedLobbyId is { } lobbyId)
        {
            _lobbyNameTextField.value = SteamMatchmaking.GetLobbyData(lobbyId, "LobbyName"); // TODO: store lobby name data in the LobbyManager
            _maxPlayersIntField.value = SteamMatchmaking.GetLobbyMemberLimit(lobbyId);
        }
    }

    private void OnClientStopped(bool isHost)
    {
        _lobbyNameTextField.value = "";
        _maxPlayersIntField.value = 10;
        UpdateLobbyElements(true);
    }

    private void UpdateLobbyElements(bool isCreateLobbyMode)
    {
        // Reset callbacks
        _startMatchButton.UnregisterCallback<ClickEvent>(OnClickStartMatchButton);

        if (isCreateLobbyMode)
        {
            _startMatchButton.text = "Start match";
            _startMatchButton.RegisterCallback<ClickEvent>(OnClickStartMatchButton);

            _lobbyNameTextField.SetEnabled(true);
            _maxPlayersIntField.SetEnabled(true);
            _friendOnlyToggle.SetEnabled(true);
            _lobbyButton1.SetEnabled(true);
            _lobbyButton2.SetEnabled(true);
            _lobbyButton1.visible = true;
            _lobbyButton2.visible = true;
        }
        else
        {
            _startMatchButton.text = "Leave lobby";
            _startMatchButton.RegisterCallback<ClickEvent>(OnClickLeaveLobbyButton);

            _lobbyNameTextField.SetEnabled(false);
            _maxPlayersIntField.SetEnabled(false);
            _friendOnlyToggle.SetEnabled(false);
            _lobbyButton1.SetEnabled(false);
            _lobbyButton2.SetEnabled(false);
            _lobbyButton1.visible = false;
            _lobbyButton2.visible = false;
        }
    }

    private void RefreshLobbyList()
    {
        _lobbyEntries.Clear();
        _lobbyListView.ClearSelection();
        _onRequestLobbyList.Set(SteamMatchmaking.RequestLobbyList());
    }

    private void OnClickJoinLobbyButton(ClickEvent evt)
    {
        if (JoiningLobbyData is { } lobbyData)
        {
            UpdateLobbyElements(false);

            var lobbyOwnerId = SteamMatchmaking.GetLobbyMemberByIndex(lobbyData.LobbyId, 0);
            LobbyManager.Singleton.JoinLobby(lobbyData.LobbyId, lobbyOwnerId);

            JoiningLobbyData = null;
        }
        else
        {
            Debug.LogError("JoiningLobbyData is null.");
        }
    }

    private void OnClickLeaveLobbyButton(ClickEvent evt)
    {
        LobbyManager.Singleton.LeaveLobby();
        NetworkManager.Singleton.Shutdown();

        _lobbyNameTextField.value = "";
        _maxPlayersIntField.value = 10;
        UpdateLobbyElements(true);
    }

    private void OnClickCreateLobbyButton(ClickEvent evt)
    {
        if (NetworkManager.Singleton.StartHost())
        {
            _lobbyNameTextField.SetEnabled(false);
            _maxPlayersIntField.SetEnabled(false);
            _friendOnlyToggle.SetEnabled(false);

            var lobbyType = _friendOnlyToggle.value ? ELobbyType.k_ELobbyTypeFriendsOnly : ELobbyType.k_ELobbyTypePublic;
            _onCreateLobby.Set(SteamMatchmaking.CreateLobby(lobbyType, MyMaxPlayersValue));
        }
        else
        {
            Debug.LogError("StartHost failed.");
        }
    }

    private void OnClickDeleteLobbyButton(ClickEvent evt)
    {
        LobbyManager.Singleton.LeaveLobby();
        NetworkManager.Singleton.Shutdown();

        _lobbyNameTextField.SetEnabled(true);
        _maxPlayersIntField.SetEnabled(true);
        _friendOnlyToggle.SetEnabled(true);

        _lobbyNameTextField.value = "";
        _maxPlayersIntField.value = 10;
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

    private void OnRequestLobbyList(LobbyMatchList_t arg, bool bIOFailure)
    {
        if (bIOFailure)
        {
            Debug.LogError("OnRequestLobbyList IOFailure.");
            return;
        }

        var foundLobbiesCount = arg.m_nLobbiesMatching;
        Debug.Log($"Lobbies: {foundLobbiesCount}.");
        for (var i = 0; i < foundLobbiesCount; ++i)
        {
            var lobbyId = SteamMatchmaking.GetLobbyByIndex(i);
            var lobbyName = SteamMatchmaking.GetLobbyData(lobbyId, "LobbyName");
            var lobbyOwnerName = SteamMatchmaking.GetLobbyData(lobbyId, "LobbyOwnerName");

            if (!string.IsNullOrEmpty(lobbyName))
            {
                Debug.Log($"Lobby Name: {lobbyName}.");
                _lobbyEntries.Add(new LobbyListEntryData
                {
                    LobbyId = lobbyId,
                    LobbyName = lobbyName,
                    LobbyOwnerName = lobbyOwnerName,
                });
            }
        }
        _lobbyListView.RefreshItems();
    }

    private void OnCreateLobby(LobbyCreated_t arg, bool bIOFailure)
    {
        if (arg.m_eResult != EResult.k_EResultOK || bIOFailure)
        {
            Debug.Log("Failed to create a lobby.");
            NetworkManager.Singleton.Shutdown();

            _lobbyNameTextField.SetEnabled(true);
            _maxPlayersIntField.SetEnabled(true);
            _friendOnlyToggle.SetEnabled(true);
            return;
        }

        Debug.Log("Lobby created.");
        LobbyManager.Singleton.SetJoinedLobbyId(arg.m_ulSteamIDLobby);

        var lobbyId = new CSteamID(arg.m_ulSteamIDLobby);
        SteamMatchmaking.SetLobbyData(lobbyId, "LobbyName", MyLobbyNameValue);
        SteamMatchmaking.SetLobbyData(lobbyId, "LobbyOwnerName", SteamFriends.GetPersonaName());
    }
}
