using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;

public class MainMenuMatchmakingUI : MonoBehaviour
{
    [Header("UI References")]
    public Button readyButton;
    public TMP_Text readyButtonLabel;
    public TMP_Text statusLabel;
    public GameObject searchingIndicator;

    [Header("Connection Settings")]
    [Tooltip("Address of the matchmaking server or host to connect to.")]
    public string serverAddress = "localhost";
    [Tooltip("Start as a host automatically when playing inside the Unity Editor (useful for testing).")]
    public bool startHostInEditor = true;

    MatchmakingRoomPlayer localRoomPlayer;
    NetworkManager networkManager;
    bool isConnecting;

    void Awake()
    {
        EnsureNetworkManagerReference();
        networkManager = NetworkManager.singleton;

        if (readyButton != null)
        {
            readyButton.onClick.AddListener(HandleReadyButtonPressed);
        }

        MatchmakingRoomPlayer.AuthorityStarted += HandleAuthorityStarted;
        NetworkClient.OnConnectedEvent += HandleClientConnected;
        NetworkClient.OnDisconnectedEvent += HandleClientDisconnected;

        UpdateStatusLabel("Press Ready to search for a match.");
        UpdateReadyButton(false);
        SetSearchingIndicator(false);
        TryAssignLocalPlayer();
    }

    void Start()
    {
        // When scenes change, the NetworkManager moves to DontDestroyOnLoad.
        // Grab a fresh reference after the first frame so the singleton is ready.
        EnsureNetworkManagerReference();
    }

    void OnDestroy()
    {
        if (readyButton != null)
        {
            readyButton.onClick.RemoveListener(HandleReadyButtonPressed);
        }

        MatchmakingRoomPlayer.AuthorityStarted -= HandleAuthorityStarted;
        NetworkClient.OnConnectedEvent -= HandleClientConnected;
        NetworkClient.OnDisconnectedEvent -= HandleClientDisconnected;

        if (localRoomPlayer != null)
        {
            localRoomPlayer.ReadyStateChangedClient -= UpdateReadyButton;
            localRoomPlayer.MatchFound -= HandleMatchFound;
            localRoomPlayer.MatchCountdownCancelled -= HandleMatchCountdownCancelled;
        }
    }

    void HandleReadyButtonPressed()
    {
        if (!NetworkClient.active)
        {
            BeginConnection();
            return;
        }

        if (localRoomPlayer == null)
        {
            UpdateStatusLabel("Still setting up the player. Please wait...");
            return;
        }

        bool nextState = !localRoomPlayer.readyToBegin;
        localRoomPlayer.RequestSetReady(nextState);
    }

    void BeginConnection()
    {
        EnsureNetworkManagerReference();

        if (networkManager == null)
        {
            Debug.LogError("No NetworkManager singleton is available. Ensure a CustomNetworkManager is placed in the startup scene.");
        if (networkManager == null)
        {
            Debug.LogError("No NetworkManager found in the scene.");
            return;
        }

        networkManager.networkAddress = serverAddress;

#if UNITY_EDITOR
        if (startHostInEditor && !NetworkServer.active)
        {
            networkManager.StartHost();
        }
        else
#endif
        {
            networkManager.StartClient();
        }

        isConnecting = true;
        SetSearchingIndicator(true);
        UpdateStatusLabel("Looking for another player...");
    }

    void HandleAuthorityStarted(MatchmakingRoomPlayer player)
    {
        if (!player.isOwned)
        if (!player.hasAuthority)
            return;

        if (localRoomPlayer != null)
        {
            localRoomPlayer.ReadyStateChangedClient -= UpdateReadyButton;
            localRoomPlayer.MatchFound -= HandleMatchFound;
            localRoomPlayer.MatchCountdownCancelled -= HandleMatchCountdownCancelled;
        }

        localRoomPlayer = player;
        localRoomPlayer.ReadyStateChangedClient += UpdateReadyButton;
        localRoomPlayer.MatchFound += HandleMatchFound;
        localRoomPlayer.MatchCountdownCancelled += HandleMatchCountdownCancelled;

        UpdateReadyButton(localRoomPlayer.readyToBegin);
        if (NetworkClient.active)
        {
            UpdateStatusLabel("Connected. Press Ready to search.");
        }
    }

    void HandleClientConnected()
    {
        isConnecting = false;
        SetSearchingIndicator(false);
        UpdateStatusLabel("Connected. Press Ready to search.");
    }

    void HandleClientDisconnected()
    {
        isConnecting = false;
        SetSearchingIndicator(false);
        UpdateStatusLabel("Connection lost. Press Ready to try again.");
        UpdateReadyButton(false);
        localRoomPlayer = null;
    }

    void HandleMatchFound(float countdown)
    {
        SetSearchingIndicator(false);
        if (countdown > 0f)
        {
            UpdateStatusLabel($"Match found! Starting in {countdown:0.#} seconds.");
        }
        else
        {
            UpdateStatusLabel("Match found! Loading...");
        }
    }

    void HandleMatchCountdownCancelled()
    {
        UpdateStatusLabel("Match cancelled. Waiting for players.");
        SetSearchingIndicator(true);
    }

    void UpdateReadyButton(bool isReady)
    {
        if (readyButtonLabel != null)
        {
            readyButtonLabel.text = isReady ? "Cancel" : "Ready Up";
        }

        if (readyButton != null)
        {
            readyButton.interactable = !isConnecting;
        }

        if (!NetworkClient.active)
        {
            UpdateStatusLabel("Press Ready to search for a match.");
            SetSearchingIndicator(false);
        }
        else if (isReady)
        {
            UpdateStatusLabel("Ready! Waiting for another player...");
            SetSearchingIndicator(true);
        }
        else if (!isConnecting)
        {
            UpdateStatusLabel("Connected. Press Ready to search.");
            SetSearchingIndicator(false);
        }
    }

    void UpdateStatusLabel(string message)
    {
        if (statusLabel != null)
        {
            statusLabel.text = message;
        }
    }

    void SetSearchingIndicator(bool active)
    {
        if (searchingIndicator != null)
        {
            searchingIndicator.SetActive(active);
        }
    }

    void TryAssignLocalPlayer()
    {
        if (localRoomPlayer != null)
            return;

        MatchmakingRoomPlayer[] players = FindObjectsOfType<MatchmakingRoomPlayer>();
        foreach (MatchmakingRoomPlayer player in players)
        {
            if (player.isOwned)
            if (player.hasAuthority)
            {
                HandleAuthorityStarted(player);
                break;
            }
        }
    }

    void EnsureNetworkManagerReference()
    {
        if (networkManager != null)
            return;

        networkManager = NetworkManager.singleton;

        if (networkManager == null)
        {
            networkManager = FindObjectOfType<NetworkManager>();
        }
    }
}
