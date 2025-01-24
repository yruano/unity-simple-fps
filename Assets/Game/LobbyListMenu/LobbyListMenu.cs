using System.Collections.Generic;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Unity.Properties;
using Unity.Netcode;

public struct LobbyListEntryData
{
    public CSteamID LobbyId;
    public string LobbyOwnerId;
    public string LobbyName;
    public string LobbyOwnerName;
}

public class LobbyListEntryController
{
    private LobbyListEntryData data;
    private readonly Label _lobbyNameLabel;
    private readonly Label _lobbyOwnerLabel;
    private readonly Button _lobbyJoinButton;

    public LobbyListEntryController(LobbyDashboardMenu lobbyListMenu, VisualElement root, EventCallback<ClickEvent> onClickJoinButton)
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
        _lobbyJoinButton.SetEnabled(!NetworkManager.Singleton.IsHost);
    }
}

public struct LobbyMemberListEntryData
{
    public string Name;
    public string Role;
}

public class LobbyMemberListEntryController
{
    private readonly Label _nameLabel;
    private readonly Label _roleLabel;

    public LobbyMemberListEntryController(VisualElement root)
    {
        _nameLabel = root.Q("NameLabel") as Label;
        _roleLabel = root.Q("RoleLabel") as Label;
    }

    public void UpdateElements(LobbyMemberListEntryData data)
    {
        _nameLabel.text = data.Name;
        _roleLabel.text = data.Role;
    }
}

public struct LobbyChatHistroyListEntryData
{
    public string Name;
    public string Message;
}

public class LobbyChatHistoryListEntryController
{
    private Label _nameLabel;
    private Label _messageLabel;

    public LobbyChatHistoryListEntryController(VisualElement root)
    {
        _nameLabel = root.Q("NameLabel") as Label;
        _messageLabel = root.Q("MessageLabel") as Label;
    }

    public void UpdateElements(LobbyChatHistroyListEntryData data)
    {
        _nameLabel.text = data.Name;
        _messageLabel.text = data.Message;
    }
}

public class LobbyDashboardMenu : MonoBehaviour
{
    [SerializeField] private VisualTreeAsset LobbyListEntryAsset;
    [SerializeField] private VisualTreeAsset LobbyMemberListEntryAsset;
    [SerializeField] private VisualTreeAsset LobbyChatHistoryListEntryAsset;
    [SerializeField] private GameObject PrefabLobbyChatManager;

    // UI Elements
    private UIDocument _document;
    private VisualElement _root;
    private Button _refreshButton;
    private ListView _lobbyListView;
    private TextField _lobbyNameTextField;
    private IntegerField _maxPlayersIntField;
    private Toggle _friendOnlyToggle;
    private Button _lobbyButton1;
    private Button _lobbyButton2;
    private ListView _lobbyMemberListView;
    private ListView _lobbyChatHistoryListView;
    private TextField _lobbyChatTextField;
    private Button _startMatchButton;

    // ListEntries
    private List<LobbyListEntryData> _lobbyListEntries = new();
    private List<LobbyMemberListEntryData> _lobbyMemberListEntries = new();
    private List<LobbyChatHistroyListEntryData> _lobbyChatHistoryListEntries = new();

    public LobbyListEntryData? JoiningLobbyData;

    [HideInInspector] public LobbyChatManager LobbyChatManager;

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

    // Steamworks
    private CallResult<LobbyMatchList_t> _steamOnRequestLobbyList;
    private CallResult<LobbyCreated_t> _steamOnCreateLobby;
    private Callback<LobbyChatUpdate_t> _steamOnClientLobbyEvent;

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
        _root = _document.rootVisualElement;

        _refreshButton = _root.Q("RefreshButton") as Button;
        _lobbyListView = _root.Q("LobbyListView") as ListView;
        _lobbyNameTextField = _root.Q("LobbyNameTextField") as TextField;
        _maxPlayersIntField = _root.Q("MaxPlayersNum") as IntegerField;
        _friendOnlyToggle = _root.Q("FriendOnlyToggle") as Toggle;
        _lobbyButton1 = _root.Q("LobbyButton1") as Button;
        _lobbyButton2 = _root.Q("LobbyButton2") as Button;
        _lobbyMemberListView = _root.Q("LobbyMemberListView") as ListView;
        _lobbyChatHistoryListView = _root.Q("LobbyChatHistoryListView") as ListView;
        _lobbyChatTextField = _root.Q("LobbyChatTextField") as TextField;
        _startMatchButton = _root.Q("StartMatchButton") as Button;

        _steamOnRequestLobbyList = new(SteamOnRequestLobbyList);
        _steamOnCreateLobby = new(SteamOnCreateLobby);
        _steamOnClientLobbyEvent = new(SteamOnClientLobbyEvent);
    }

    private void Start()
    {
        NetworkManager.Singleton.OnClientStarted += OnClientStarted;
        NetworkManager.Singleton.OnClientStopped += OnClientStopped;

        // Setup RefreshButton.
        _refreshButton.RegisterCallback<ClickEvent>((evt) => RefreshLobbyList());

        // Setup LobbyListView.
        _lobbyListView.itemsSource = _lobbyListEntries;
        _lobbyListView.makeItem = () =>
        {
            var item = LobbyListEntryAsset.Instantiate();
            item.userData = new LobbyListEntryController(this, item, OnClickJoinLobbyButton);
            return item;
        };
        _lobbyListView.bindItem = (item, index) =>
        {
            (item.userData as LobbyListEntryController)?.UpdateElements(_lobbyListEntries[index]);
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

        // Setup LobbyMemberListView.
        _lobbyMemberListView.itemsSource = _lobbyMemberListEntries;
        _lobbyMemberListView.makeItem = () =>
        {
            var item = LobbyMemberListEntryAsset.Instantiate();
            item.userData = new LobbyMemberListEntryController(item);
            return item;
        };
        _lobbyMemberListView.bindItem = (item, index) =>
        {
            (item.userData as LobbyMemberListEntryController)?.UpdateElements(_lobbyMemberListEntries[index]);
        };

        // Setup LobbyChatHistortListView.
        _lobbyChatHistoryListView.itemsSource = _lobbyChatHistoryListEntries;
        _lobbyChatHistoryListView.makeItem = () =>
        {
            var item = LobbyChatHistoryListEntryAsset.Instantiate();
            item.userData = new LobbyChatHistoryListEntryController(item);
            return item;
        };
        _lobbyChatHistoryListView.bindItem = (item, index) =>
        {
            (item.userData as LobbyChatHistoryListEntryController)?.UpdateElements(_lobbyChatHistoryListEntries[index]);
        };

        // Setup LobbyChatTextField.
        _lobbyChatTextField.RegisterCallback<ChangeEvent<string>>((evt) =>
        {
            if (string.IsNullOrEmpty(evt.newValue))
                return;

            // TODO: store name in a singleton
            var name = SteamFriends.GetPersonaName();
            var message = evt.newValue;
            LobbyChatManager?.SendMessage(name, message);
            _lobbyChatTextField.value = "";
        });

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

    public void AddChatMessage(string Name, string Message)
    {
        _lobbyChatHistoryListEntries.Add(new LobbyChatHistroyListEntryData { Name = Name, Message = Message });
        _lobbyChatHistoryListView.RefreshItems();
        _lobbyChatHistoryListView.ScrollToItem(_lobbyChatHistoryListEntries.Count - 1);
    }

    private void OnClientStarted()
    {
        if (!NetworkManager.Singleton.IsHost && LobbyManager.Singleton.JoinedLobbyId is { } lobbyId)
        {
            // TODO: store lobby name data in the LobbyManager
            _lobbyNameTextField.value = SteamMatchmaking.GetLobbyData(lobbyId, "LobbyName");
            _maxPlayersIntField.value = SteamMatchmaking.GetLobbyMemberLimit(lobbyId);
            RefreshLobbyMemberList();
        }
    }

    private void OnClientStopped(bool isHost)
    {
        _lobbyNameTextField.value = "";
        _maxPlayersIntField.value = 10;
        _lobbyMemberListEntries.Clear();
        _lobbyMemberListView.RefreshItems();
        _lobbyChatHistoryListEntries.Clear();
        _lobbyChatHistoryListView.RefreshItems();
        UpdateLobbyElements(true);
    }

    private void SteamOnRequestLobbyList(LobbyMatchList_t arg, bool bIOFailure)
    {
        if (bIOFailure)
        {
            Debug.LogError("[Steamworks.NET] OnRequestLobbyList IOFailure.");
            return;
        }

        var foundLobbiesCount = arg.m_nLobbiesMatching;
        for (var i = 0; i < foundLobbiesCount; ++i)
        {
            var lobbyId = SteamMatchmaking.GetLobbyByIndex(i);
            var lobbyOwnerId = SteamMatchmaking.GetLobbyData(lobbyId, "LobbyOwnerId");
            var lobbyName = SteamMatchmaking.GetLobbyData(lobbyId, "LobbyName");
            var lobbyOwnerName = SteamMatchmaking.GetLobbyData(lobbyId, "LobbyOwnerName");

            if (!string.IsNullOrEmpty(lobbyName))
            {
                _lobbyListEntries.Add(new LobbyListEntryData
                {
                    LobbyId = lobbyId,
                    LobbyOwnerId = lobbyOwnerId,
                    LobbyName = lobbyName,
                    LobbyOwnerName = lobbyOwnerName,
                });
            }
        }
        _lobbyListView.RefreshItems();
    }

    private void SteamOnCreateLobby(LobbyCreated_t arg, bool bIOFailure)
    {
        if (arg.m_eResult != EResult.k_EResultOK || bIOFailure)
        {
            Debug.Log("[Steamworks.NET] Failed to create a lobby.");
            NetworkManager.Singleton.Shutdown();

            _lobbyListView.RefreshItems();
            _lobbyNameTextField.SetEnabled(true);
            _maxPlayersIntField.SetEnabled(true);
            _friendOnlyToggle.SetEnabled(true);
            return;
        }

        Debug.Log("[Steamworks.NET] Lobby created.");
        LobbyManager.Singleton.SetJoinedLobbyId(arg.m_ulSteamIDLobby);

        var lobbyId = new CSteamID(arg.m_ulSteamIDLobby);
        SteamMatchmaking.SetLobbyData(lobbyId, "LobbyOwnerId", SteamUser.GetSteamID().m_SteamID.ToString());
        SteamMatchmaking.SetLobbyData(lobbyId, "LobbyName", MyLobbyNameValue);
        SteamMatchmaking.SetLobbyData(lobbyId, "LobbyOwnerName", SteamFriends.GetPersonaName());

        var lobbyChatManager = Instantiate(PrefabLobbyChatManager);
        var networkLobbyChatManager = lobbyChatManager.GetComponent<NetworkObject>();
        networkLobbyChatManager.Spawn(true);

        RefreshLobbyMemberList();
    }

    private void SteamOnClientLobbyEvent(LobbyChatUpdate_t arg)
    {
        // Lobby entered.
        if ((arg.m_rgfChatMemberStateChange & (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0)
        {
            RefreshLobbyMemberList();
        }

        // Lobby lefted.
        var leftedFlag =
            (uint)EChatMemberStateChange.k_EChatMemberStateChangeLeft |
            (uint)EChatMemberStateChange.k_EChatMemberStateChangeDisconnected;
        if ((arg.m_rgfChatMemberStateChange & leftedFlag) != 0)
        {
            RefreshLobbyMemberList();
        }
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
        _lobbyListEntries.Clear();
        _lobbyListView.ClearSelection();
        _steamOnRequestLobbyList.Set(SteamMatchmaking.RequestLobbyList());
    }

    private void RefreshLobbyMemberList()
    {
        _lobbyMemberListEntries.Clear();
        _lobbyMemberListView.ClearSelection();

        if (LobbyManager.Singleton.JoinedLobbyId is { } lobbyId)
        {
            var memberCount = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
            for (var i = 0; i < memberCount; ++i)
            {
                var memberId = SteamMatchmaking.GetLobbyMemberByIndex(lobbyId, i);
                var memberName = SteamFriends.GetFriendPersonaName(memberId);
                var memberRole = (i == 0) ? "Host" : "Client";
                _lobbyMemberListEntries.Add(new LobbyMemberListEntryData { Name = memberName, Role = memberRole });
            }
            _lobbyMemberListView.RefreshItems();
        }
    }

    private void OnClickJoinLobbyButton(ClickEvent evt)
    {
        // FIXME:
        // 로비 리스트가 5초마다 업데이트 되기 때문에
        // 방장이 삭제한 방에 참가할 수도 있다.

        if (JoiningLobbyData is { } lobbyData)
        {
            UpdateLobbyElements(false);

            var lobbyOwnerId = new CSteamID(ulong.Parse(lobbyData.LobbyOwnerId));
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
            _lobbyListView.RefreshItems();
            _lobbyNameTextField.SetEnabled(false);
            _maxPlayersIntField.SetEnabled(false);
            _friendOnlyToggle.SetEnabled(false);

            if (string.IsNullOrEmpty(_lobbyNameTextField.value))
            {
                _lobbyNameTextField.value = $"Lobby created by \"{SteamFriends.GetPersonaName()}\"";
            }

            var lobbyType = _friendOnlyToggle.value ? ELobbyType.k_ELobbyTypeFriendsOnly : ELobbyType.k_ELobbyTypePublic;
            _steamOnCreateLobby.Set(SteamMatchmaking.CreateLobby(lobbyType, MyMaxPlayersValue));
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

        _lobbyListView.RefreshItems();
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
}
