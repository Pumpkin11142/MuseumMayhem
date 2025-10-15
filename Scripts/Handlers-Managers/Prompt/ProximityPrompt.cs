using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;
using System;

namespace ProximityPrompts
{
    /// <summary>
    /// Main ProximityPrompt component - attach to any GameObject to make it interactable
    /// Mimics Roblox ProximityPrompt behavior with full network synchronization
    /// </summary>
    public class ProximityPrompt : NetworkBehaviour
    {
        [Header("Text Settings")]
        [Tooltip("The action text shown to the player (e.g., 'Open', 'Activate')")]
        public string actionText = "Interact";

        [Tooltip("Optional object name shown above action text (e.g., 'Door', 'Chest')")]
        public string objectText = "";

        [Header("Input Settings")]
        [Tooltip("Reference to the Input Action for proximity prompts")]
        public InputActionReference proximityPromptAction;

        [Tooltip("Display key name (for UI display purposes)")]
        public string keyboardKeyCode = "E";

        [Tooltip("Gamepad button name (for UI display purposes)")]
        public string gamepadKeyCode = "Button South";

        [Header("Distance & Line of Sight")]
        [Tooltip("Maximum distance from player to show the prompt")]
        [Range(1f, 50f)]
        public float maxActivationDistance = 10f;

        [Tooltip("Whether the prompt requires line of sight to be visible")]
        public bool requiresLineOfSight = true;

        [Tooltip("Layers that block line of sight")]
        public LayerMask lineOfSightBlockingLayers = -1;

        [Header("Hold Duration")]
        [Tooltip("Time in seconds the player must hold the button (0 = instant trigger)")]
        [Range(0f, 10f)]
        public float holdDuration = 0f;

        [Header("UI Settings")]
        [Tooltip("Pixel offset for the prompt UI")]
        public Vector2 uiOffset = Vector2.zero;

        [Tooltip("Should the prompt be shown?")]
        public bool enabled = true;

        [Tooltip("Can this prompt be activated by clicking on it?")]
        public bool clickablePrompt = false;

        [Header("Style")]
        [Tooltip("Custom style - set to true to use your own UI")]
        public bool customStyle = false;

        [Header("Exclusivity")]
        [Tooltip("How this prompt interacts with other prompts")]
        public ProximityPromptExclusivity exclusivity = ProximityPromptExclusivity.OnePerButton;

        [Header("Localization")]
        [Tooltip("Enable automatic localization")]
        public bool autoLocalize = false;

        [Header("Debug")]
        [Tooltip("Enable debug logging for this prompt")]
        public bool debugMode = false;

        // Events - these fire on all clients
        public event Action<NetworkIdentity> Triggered;
        public event Action<NetworkIdentity> TriggerEnded;
        public event Action<NetworkIdentity> PromptButtonHoldBegan;
        public event Action<NetworkIdentity> PromptButtonHoldEnded;

        // Client-only events for UI
        public event Action PromptShown;
        public event Action PromptHidden;

        // Internal state
        private bool isVisible = false;
        private bool isHolding = false;
        private float holdProgress = 0f;
        private NetworkIdentity currentPlayer;

        public bool IsVisible => isVisible;
        public bool IsHolding => isHolding;
        public float HoldProgress => holdProgress;
        public NetworkIdentity CurrentPlayer => currentPlayer;

        private void Start()
        {
            // Register with the ProximityPromptService
            if (ProximityPromptService.Instance != null)
            {
                ProximityPromptService.Instance.RegisterPrompt(this);
            }
        }

        private void OnDestroy()
        {
            // Unregister from the ProximityPromptService
            if (ProximityPromptService.Instance != null)
            {
                ProximityPromptService.Instance.UnregisterPrompt(this);
            }
        }

        /// <summary>
        /// Check if the prompt should be visible to a player
        /// </summary>
        public bool ShouldShowForPlayer(Transform playerTransform, Camera playerCamera)
        {
            if (!enabled)
            {
                if (debugMode) Debug.Log($"[{gameObject.name}] Prompt is disabled");
                return false;
            }

            // Distance check - MUST pass this first
            float distance = Vector3.Distance(transform.position, playerTransform.position);

            if (debugMode)
            {
                Debug.Log($"[{gameObject.name}] Distance: {distance:F2} / Max: {maxActivationDistance:F2}");
            }

            if (distance > maxActivationDistance)
            {
                if (debugMode) Debug.Log($"[{gameObject.name}] TOO FAR - returning false");
                return false;
            }

            // Line of sight check
            if (requiresLineOfSight)
            {
                Vector3 directionToPrompt = transform.position - playerCamera.transform.position;
                float distanceToPrompt = directionToPrompt.magnitude;

                if (debugMode)
                {
                    Debug.Log($"[{gameObject.name}] LOS Check - Distance to prompt: {distanceToPrompt:F2}");
                }

                // Only check if distance is greater than a small threshold to avoid self-intersection
                if (distanceToPrompt > 0.1f)
                {
                    // Cast against blocking layers only
                    if (Physics.Raycast(playerCamera.transform.position, directionToPrompt.normalized, out RaycastHit hit, distanceToPrompt, lineOfSightBlockingLayers))
                    {
                        if (debugMode)
                        {
                            Debug.Log($"[{gameObject.name}] Raycast hit: {hit.collider.gameObject.name} at distance {hit.distance:F2}");
                        }

                        // If what we hit is NOT this prompt or its children, it's blocked
                        if (hit.collider != null && hit.collider.transform != transform && !hit.collider.transform.IsChildOf(transform))
                        {
                            if (debugMode) Debug.Log($"[{gameObject.name}] LINE OF SIGHT BLOCKED - returning false");
                            return false;
                        }
                        else
                        {
                            if (debugMode) Debug.Log($"[{gameObject.name}] Hit self or child, ignoring");
                        }
                    }
                    else
                    {
                        if (debugMode) Debug.Log($"[{gameObject.name}] Raycast hit nothing (clear line of sight)");
                    }
                }
            }

            if (debugMode) Debug.Log($"[{gameObject.name}] ALL CHECKS PASSED - returning true");
            return true;
        }

        /// <summary>
        /// Called by ProximityPromptService when prompt becomes visible
        /// </summary>
        public void OnPromptShown(NetworkIdentity player)
        {
            if (isVisible) return;

            isVisible = true;
            currentPlayer = player;

            if (debugMode) Debug.Log($"[{gameObject.name}] PROMPT SHOWN");

            PromptShown?.Invoke();
        }

        /// <summary>
        /// Called by ProximityPromptService when prompt becomes hidden
        /// </summary>
        public void OnPromptHidden()
        {
            if (!isVisible) return;

            isVisible = false;
            currentPlayer = null;

            if (debugMode) Debug.Log($"[{gameObject.name}] PROMPT HIDDEN");

            if (isHolding)
            {
                InputHoldEnd();
            }

            PromptHidden?.Invoke();
        }

        /// <summary>
        /// Begin holding the prompt button (called by UI or input system)
        /// </summary>
        public void InputHoldBegin()
        {
            if (!isVisible || isHolding) return;

            isHolding = true;
            holdProgress = 0f;

            // Notify server
            if (currentPlayer != null && currentPlayer.isLocalPlayer)
            {
                CmdPromptButtonHoldBegan(currentPlayer);
            }
        }

        /// <summary>
        /// End holding the prompt button (called by UI or input system)
        /// </summary>
        public void InputHoldEnd()
        {
            if (!isHolding) return;

            isHolding = false;

            // Check if we held long enough to trigger
            if (holdDuration == 0f || holdProgress >= holdDuration)
            {
                // Trigger the prompt
                if (currentPlayer != null && currentPlayer.isLocalPlayer)
                {
                    CmdTriggerPrompt(currentPlayer);
                }
            }
            else
            {
                // Didn't hold long enough
                if (currentPlayer != null && currentPlayer.isLocalPlayer)
                {
                    CmdPromptButtonHoldEnded(currentPlayer);
                }
            }

            holdProgress = 0f;
        }

        /// <summary>
        /// Update hold progress
        /// </summary>
        public void UpdateHoldProgress(float deltaTime)
        {
            if (!isHolding) return;

            holdProgress += deltaTime;

            if (holdDuration > 0f && holdProgress >= holdDuration)
            {
                // Auto-trigger when hold duration is reached
                InputHoldEnd();
            }
        }

        // Network Commands (Client -> Server)
        [Command(requiresAuthority = false)]
        private void CmdPromptButtonHoldBegan(NetworkIdentity player)
        {
            RpcPromptButtonHoldBegan(player);
        }

        [Command(requiresAuthority = false)]
        private void CmdPromptButtonHoldEnded(NetworkIdentity player)
        {
            RpcPromptButtonHoldEnded(player);
        }

        [Command(requiresAuthority = false)]
        private void CmdTriggerPrompt(NetworkIdentity player)
        {
            RpcTriggerPrompt(player);
        }

        [Command(requiresAuthority = false)]
        private void CmdTriggerEnded(NetworkIdentity player)
        {
            RpcTriggerEnded(player);
        }

        // Network RPCs (Server -> All Clients)
        [ClientRpc]
        private void RpcPromptButtonHoldBegan(NetworkIdentity player)
        {
            PromptButtonHoldBegan?.Invoke(player);
        }

        [ClientRpc]
        private void RpcPromptButtonHoldEnded(NetworkIdentity player)
        {
            PromptButtonHoldEnded?.Invoke(player);
        }

        [ClientRpc]
        private void RpcTriggerPrompt(NetworkIdentity player)
        {
            Triggered?.Invoke(player);
        }

        [ClientRpc]
        private void RpcTriggerEnded(NetworkIdentity player)
        {
            TriggerEnded?.Invoke(player);
        }

        /// <summary>
        /// Call this when you want to end a continuous trigger (for hold-to-heal type interactions)
        /// </summary>
        public void EndTrigger()
        {
            if (currentPlayer != null && currentPlayer.isLocalPlayer)
            {
                CmdTriggerEnded(currentPlayer);
            }
        }

        private void OnDrawGizmosSelected()
        {
            // Visualize the activation distance
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, maxActivationDistance);
        }
    }

    public enum ProximityPromptExclusivity
    {
        OnePerButton,      // One prompt per input button
        OneGlobally,       // Only one prompt shown at a time
        AlwaysShow         // Show all prompts simultaneously
    }
}