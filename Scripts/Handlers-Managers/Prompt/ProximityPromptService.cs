using Mirror;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProximityPrompts
{
    /// <summary>
    /// Singleton service that manages all ProximityPrompts in the scene
    /// Handles detection, prioritization, and input routing
    /// </summary>
    public class ProximityPromptService : MonoBehaviour
    {
        public static ProximityPromptService Instance { get; private set; }

        [Header("Player Setup")]
        [Tooltip("Name of the child transform that actually moves (leave empty to auto-detect)")]
        public string playerModelChildName = "PlayerModel";

        [Header("References")]
        [Tooltip("Reference to the Input Action for proximity prompts")]
        public InputActionReference proximityPromptAction;

        [Header("Settings")]
        [Tooltip("How often to check for nearby prompts (in seconds)")]
        [Range(0.01f, 0.5f)]
        public float updateInterval = 0.1f;

        [Tooltip("Maximum number of prompts to show simultaneously")]
        [Range(1, 10)]
        public int maxSimultaneousPrompts = 3;

        [Header("Debug")]
        [Tooltip("Enable debug logging")]
        public bool debugMode = false;

        // Internal tracking
        private readonly List<ProximityPrompt> allPrompts = new List<ProximityPrompt>();
        private readonly List<ProximityPrompt> visiblePrompts = new List<ProximityPrompt>();
        private readonly Dictionary<string, ProximityPrompt> promptsByKey = new Dictionary<string, ProximityPrompt>();

        private NetworkIdentity localPlayer;
        private Camera playerCamera;
        private Transform playerTransform;
        private float updateTimer = 0f;
        private float playerSearchTimer = 0f;

        private InputAction promptInput;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void Start()
        {
            // Setup input action
            if (proximityPromptAction != null)
            {
                promptInput = proximityPromptAction.action;
                promptInput.Enable();

                promptInput.started += OnPromptInputStarted;
                promptInput.canceled += OnPromptInputCanceled;
            }
        }

        private void OnDestroy()
        {
            if (promptInput != null)
            {
                promptInput.started -= OnPromptInputStarted;
                promptInput.canceled -= OnPromptInputCanceled;
                promptInput.Disable();
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            // Continuously search for local player if not found
            playerSearchTimer += Time.deltaTime;
            if (localPlayer == null || playerCamera == null || playerTransform == null || playerSearchTimer > 1f)
            {
                playerSearchTimer = 0f;
                FindLocalPlayer();
            }

            // Don't update prompts if we don't have a valid player
            if (localPlayer == null || playerCamera == null || playerTransform == null)
            {
                if (debugMode && Time.frameCount % 60 == 0)
                {
                    Debug.LogWarning("[ProximityPromptService] Waiting for local player...");
                }
                return;
            }

            // Update visible prompts
            updateTimer += Time.deltaTime;
            if (updateTimer >= updateInterval)
            {
                updateTimer = 0f;
                UpdateVisiblePrompts();
            }

            // Update hold progress for all holding prompts
            foreach (var prompt in visiblePrompts)
            {
                if (prompt != null && prompt.IsHolding)
                {
                    prompt.UpdateHoldProgress(Time.deltaTime);
                }
            }
        }

        private void FindLocalPlayer()
        {
            // Find all NetworkIdentity objects
            var allIdentities = FindObjectsOfType<NetworkIdentity>();

            foreach (var identity in allIdentities)
            {
                if (identity.isLocalPlayer)
                {
                    localPlayer = identity;

                    // Try to find the actual moving transform
                    Transform movingTransform = null;

                    // First, try to find by specified name
                    if (!string.IsNullOrEmpty(playerModelChildName))
                    {
                        movingTransform = identity.transform.Find(playerModelChildName);
                        if (movingTransform != null && debugMode)
                        {
                            Debug.Log($"[ProximityPromptService] Found player model by name: {playerModelChildName}");
                        }
                    }

                    // If not found by name, try to find common components on children
                    if (movingTransform == null)
                    {
                        var characterController = identity.GetComponentInChildren<CharacterController>();
                        var rigidbody = identity.GetComponentInChildren<Rigidbody>();

                        if (characterController != null)
                        {
                            movingTransform = characterController.transform;
                            if (debugMode) Debug.Log($"[ProximityPromptService] Found player via CharacterController");
                        }
                        else if (rigidbody != null)
                        {
                            movingTransform = rigidbody.transform;
                            if (debugMode) Debug.Log($"[ProximityPromptService] Found player via Rigidbody");
                        }
                    }

                    // Fallback to root transform
                    if (movingTransform == null)
                    {
                        movingTransform = identity.transform;
                        if (debugMode) Debug.LogWarning($"[ProximityPromptService] Using root transform (position might be incorrect!)");
                    }

                    playerTransform = movingTransform;

                    // Try to find camera
                    playerCamera = identity.GetComponentInChildren<Camera>();
                    if (playerCamera == null)
                    {
                        playerCamera = Camera.main;
                    }
                    if (playerCamera == null)
                    {
                        playerCamera = FindObjectOfType<Camera>();
                    }

                    if (debugMode)
                    {
                        Debug.Log($"[ProximityPromptService] ===== PLAYER SETUP =====");
                        Debug.Log($"[ProximityPromptService] NetworkIdentity: {localPlayer.name}");
                        Debug.Log($"[ProximityPromptService] Player transform: {playerTransform.name}");
                        Debug.Log($"[ProximityPromptService] Player position: {playerTransform.position}");
                        Debug.Log($"[ProximityPromptService] Camera: {(playerCamera != null ? playerCamera.name : "NULL")}");
                        Debug.Log($"[ProximityPromptService] =======================");
                    }

                    return;
                }
            }

            // If we get here, no local player found
            if (debugMode && localPlayer == null)
            {
                Debug.LogWarning("[ProximityPromptService] No local player found!");
            }
        }

        /// <summary>
        /// Register a prompt with the service
        /// </summary>
        public void RegisterPrompt(ProximityPrompt prompt)
        {
            if (!allPrompts.Contains(prompt))
            {
                allPrompts.Add(prompt);
                if (debugMode)
                {
                    Debug.Log($"[ProximityPromptService] Registered prompt: {prompt.gameObject.name}");
                }
            }
        }

        /// <summary>
        /// Unregister a prompt from the service
        /// </summary>
        public void UnregisterPrompt(ProximityPrompt prompt)
        {
            allPrompts.Remove(prompt);

            if (visiblePrompts.Contains(prompt))
            {
                prompt.OnPromptHidden();
                visiblePrompts.Remove(prompt);
            }

            // Remove from key tracking
            var keysToRemove = promptsByKey.Where(kvp => kvp.Value == prompt)
                                          .Select(kvp => kvp.Key)
                                          .ToList();
            foreach (var key in keysToRemove)
            {
                promptsByKey.Remove(key);
            }

            if (debugMode)
            {
                Debug.Log($"[ProximityPromptService] Unregistered prompt: {prompt?.gameObject.name}");
            }
        }

        public RectTransform SpawnWorldLinkedUI(GameObject prefab, Transform worldTarget, out GameObject instance)
        {
            instance = null;
            if (prefab == null || worldTarget == null) return null;

            // Find the player’s main UI canvas (same one prompts use)
            Canvas playerUICanvas = FindObjectOfType<Canvas>(); // or reference if you already have one cached
            if (playerUICanvas == null)
            {
                Debug.LogWarning("[ProximityPromptService] No player UI canvas found!");
                return null;
            }

            instance = Instantiate(prefab, playerUICanvas.transform);
            var rect = instance.GetComponent<RectTransform>();

            // Start coroutine to follow the target on screen
            StartCoroutine(FollowWorldTarget(rect, worldTarget));

            return rect;
        }

        private IEnumerator FollowWorldTarget(RectTransform rect, Transform worldTarget)
        {
            var cam = Camera.main;
            while (rect != null && worldTarget != null)
            {
                Vector3 screenPos = cam.WorldToScreenPoint(worldTarget.position);
                rect.position = screenPos;
                yield return null;
            }

            if (rect != null) Destroy(rect.gameObject);
        }

        /// <summary>
        /// Update which prompts should be visible
        /// </summary>
        private void UpdateVisiblePrompts()
        {
            if (playerTransform == null || playerCamera == null)
            {
                if (debugMode)
                {
                    Debug.LogWarning("[ProximityPromptService] Cannot update prompts - player transform or camera is null");
                }
                return;
            }

            if (debugMode)
            {
                Debug.Log($"[ProximityPromptService] Player position: {playerTransform.position}");
                Debug.Log($"[ProximityPromptService] Checking {allPrompts.Count} total prompts");
            }

            // Find all prompts that should be visible
            var newVisiblePrompts = new List<ProximityPrompt>();

            foreach (var prompt in allPrompts)
            {
                if (prompt == null) continue;

                if (prompt.ShouldShowForPlayer(playerTransform, playerCamera))
                {
                    newVisiblePrompts.Add(prompt);

                    if (debugMode)
                    {
                        float dist = Vector3.Distance(prompt.transform.position, playerTransform.position);
                        Debug.Log($"[ProximityPromptService] Prompt {prompt.gameObject.name} is visible (distance: {dist:F2})");
                    }
                }
            }

            // Sort by distance (closest first)
            newVisiblePrompts = newVisiblePrompts
                .OrderBy(p => Vector3.Distance(p.transform.position, playerTransform.position))
                .ToList();

            // Apply exclusivity rules and limits
            newVisiblePrompts = ApplyExclusivityRules(newVisiblePrompts);

            // Limit to max simultaneous prompts
            if (newVisiblePrompts.Count > maxSimultaneousPrompts)
            {
                newVisiblePrompts = newVisiblePrompts.Take(maxSimultaneousPrompts).ToList();
            }

            // Hide prompts that are no longer visible
            foreach (var prompt in visiblePrompts.ToList())
            {
                if (!newVisiblePrompts.Contains(prompt))
                {
                    if (debugMode)
                    {
                        Debug.Log($"[ProximityPromptService] Hiding prompt: {prompt.gameObject.name}");
                    }

                    prompt.OnPromptHidden();
                    visiblePrompts.Remove(prompt);

                    // Remove from key tracking
                    var keyToRemove = promptsByKey.FirstOrDefault(kvp => kvp.Value == prompt).Key;
                    if (keyToRemove != null)
                    {
                        promptsByKey.Remove(keyToRemove);
                    }
                }
            }

            // Show new prompts
            foreach (var prompt in newVisiblePrompts)
            {
                if (!visiblePrompts.Contains(prompt))
                {
                    if (debugMode)
                    {
                        Debug.Log($"[ProximityPromptService] Showing prompt: {prompt.gameObject.name}");
                    }

                    prompt.OnPromptShown(localPlayer);
                    visiblePrompts.Add(prompt);

                    // Track by key for exclusivity
                    if (!promptsByKey.ContainsKey(prompt.keyboardKeyCode))
                    {
                        promptsByKey[prompt.keyboardKeyCode] = prompt;
                    }
                }
            }

            if (debugMode)
            {
                Debug.Log($"[ProximityPromptService] Total visible prompts: {visiblePrompts.Count}");
            }
        }

        /// <summary>
        /// Apply exclusivity rules to determine which prompts to show
        /// </summary>
        private List<ProximityPrompt> ApplyExclusivityRules(List<ProximityPrompt> prompts)
        {
            var result = new List<ProximityPrompt>();
            var usedKeys = new HashSet<string>();

            foreach (var prompt in prompts)
            {
                switch (prompt.exclusivity)
                {
                    case ProximityPromptExclusivity.OnePerButton:
                        if (!usedKeys.Contains(prompt.keyboardKeyCode))
                        {
                            result.Add(prompt);
                            usedKeys.Add(prompt.keyboardKeyCode);
                        }
                        break;

                    case ProximityPromptExclusivity.OneGlobally:
                        if (result.Count == 0)
                        {
                            result.Add(prompt);
                        }
                        return result; // Only show one prompt total

                    case ProximityPromptExclusivity.AlwaysShow:
                        result.Add(prompt);
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// Get all currently visible prompts
        /// </summary>
        public List<ProximityPrompt> GetVisiblePrompts()
        {
            return new List<ProximityPrompt>(visiblePrompts);
        }

        private void OnPromptInputStarted(InputAction.CallbackContext context)
        {
            // Begin holding on all visible prompts
            foreach (var prompt in visiblePrompts)
            {
                if (prompt != null)
                {
                    prompt.InputHoldBegin();
                }
            }
        }

        private void OnPromptInputCanceled(InputAction.CallbackContext context)
        {
            // End holding on all visible prompts
            foreach (var prompt in visiblePrompts)
            {
                if (prompt != null)
                {
                    prompt.InputHoldEnd();
                }
            }
        }
    }
}