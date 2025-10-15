using System.Collections;
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

    [Header("Network Manager Fallbacks")]
    [Tooltip("Assign when the UI should talk to a specific NetworkManager instance instead of auto-discovering it.")]
    public NetworkManager networkManagerOverride;
    [Tooltip("Instantiate this prefab if no active NetworkManager exists (useful for additive main-menu setups). Leave empty to disable auto-instantiation.")]
    public NetworkManager networkManagerPrefab;
    [Tooltip("How often (in seconds) the UI should retry locating the NetworkManager while it is still starting up.")]
    public float managerPollInterval = 0.25f;

    MatchmakingRoomPlayer localRoomPlayer;
    NetworkManager networkManager;
    bool isConnecting;
    Coroutine locateManagerRoutine;

    void Awake()
    {
        EnsureNetworkManagerReference();

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
        if (locateManagerRoutine != null)
        {
            StopCoroutine(locateManagerRoutine);
            locateManagerRoutine = null;
        }

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

        TryCaptureNetworkManager();

        if (networkManager == null && locateManagerRoutine == null && managerPollInterval > 0f && isActiveAndEnabled)
        {
            locateManagerRoutine = StartCoroutine(PollForNetworkManager());
        }
    }

    void TryCaptureNetworkManager()
    {
        if (networkManagerOverride != null)
        {
            networkManager = networkManagerOverride;
        }

        if (networkManager != null)
            return;

        networkManager = NetworkManager.singleton;

        if (networkManager != null)
            return;

        networkManager = FindObjectOfType<NetworkManager>(true);

        if (networkManager != null)
            return;

        NetworkManager[] managers = Resources.FindObjectsOfTypeAll<NetworkManager>();
        foreach (NetworkManager candidate in managers)
        {
            if (candidate != null && candidate.gameObject.hideFlags == HideFlags.None)
            {
                networkManager = candidate;
                break;
            }
        }

        if (networkManager == null && networkManagerPrefab != null)
        {
            networkManager = Instantiate(networkManagerPrefab);
            networkManager.name = networkManagerPrefab.name;
            DontDestroyOnLoad(networkManager.gameObject);
        }
    }

    IEnumerator PollForNetworkManager()
    {
        var wait = new WaitForSeconds(managerPollInterval);

        while (networkManager == null)
        {
            TryCaptureNetworkManager();

            if (networkManager != null)
                break;

            yield return wait;
        }

        locateManagerRoutine = null;
    }
}
