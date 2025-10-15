using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

namespace ProximityPrompts
{
    /// <summary>
    /// Handles the UI rendering for proximity prompts (Roblox-style billboard UI)
    /// Attach this to a UI Canvas in the scene
    /// </summary>
    public class ProximityPromptUI : MonoBehaviour
    {
        [Header("Prefab Reference")]
        [Tooltip("Prefab for individual prompt UI elements")]
        public GameObject promptUIPrefab;

        [Header("UI Settings")]
        [Tooltip("Font size for action text")]
        public int actionTextSize = 19;

        [Tooltip("Font size for object text")]
        public int objectTextSize = 14;

        [Header("Animation Settings")]
        [Tooltip("Fade in/out duration")]
        public float fadeDuration = 0.2f;

        [Tooltip("Hold animation scale factor")]
        public float holdScaleFactor = 1.33f;

        [Tooltip("Progress bar color")]
        public Color progressColor = new Color(1f, 1f, 1f, 0.8f);

        [Header("Smooth Settings")]
        [Tooltip("How quickly UI follows target position")]
        public float followSmoothSpeed = 10f;

        [Tooltip("How quickly UI fades in/out")]
        public float fadeSmoothSpeed = 10f;

        [Header("Distance Scaling")]
        [Tooltip("Scale UI based on distance to maintain consistent screen size")]
        public bool scaleWithDistance = true;

        [Tooltip("Reference distance for scaling (UI will be normal size at this distance)")]
        public float referenceDistance = 5f;

        [Tooltip("Minimum scale multiplier")]
        [Range(0.1f, 1f)]
        public float minScale = 0.5f;

        [Tooltip("Maximum scale multiplier")]
        [Range(1f, 3f)]
        public float maxScale = 2f;

        [Header("Debug")]
        [Tooltip("Enable debug logging")]
        public bool debugMode = false;

        // Internal tracking
        private Camera playerCamera;
        private readonly Dictionary<ProximityPrompt, PromptUIInstance> activePromptUIs = new Dictionary<ProximityPrompt, PromptUIInstance>();
        private readonly List<ProximityPrompt> lastVisiblePrompts = new List<ProximityPrompt>();

        private void Start()
        {
            playerCamera = Camera.main;

            if (ProximityPromptService.Instance != null)
            {
                StartCoroutine(UpdatePromptUIs());
            }
            else
            {
                Debug.LogError("[ProximityPromptUI] ProximityPromptService.Instance is null!");
            }
        }

        private void Update()
        {
            // Update all active UI instances every frame for smooth following
            foreach (var kvp in activePromptUIs)
            {
                UpdatePromptUITransform(kvp.Key, kvp.Value);
            }
        }

        private System.Collections.IEnumerator UpdatePromptUIs()
        {
            while (true)
            {
                yield return new WaitForSeconds(0.05f);

                if (ProximityPromptService.Instance == null)
                {
                    if (debugMode) Debug.LogWarning("[ProximityPromptUI] Service instance is null");
                    continue;
                }

                var visiblePrompts = ProximityPromptService.Instance.GetVisiblePrompts();

                if (debugMode)
                {
                    Debug.Log($"[ProximityPromptUI] Checking {visiblePrompts.Count} visible prompts");
                }

                // Remove UIs for prompts that are no longer visible
                var promptsToRemove = new List<ProximityPrompt>();
                foreach (var kvp in activePromptUIs)
                {
                    if (!visiblePrompts.Contains(kvp.Key))
                    {
                        promptsToRemove.Add(kvp.Key);
                    }
                }

                foreach (var prompt in promptsToRemove)
                {
                    if (activePromptUIs.TryGetValue(prompt, out var uiInstance))
                    {
                        if (debugMode) Debug.Log($"[ProximityPromptUI] Removing UI for {prompt.gameObject.name}");

                        // Immediately destroy the UI object
                        if (uiInstance.rootObject != null)
                        {
                            Destroy(uiInstance.rootObject);
                        }
                        activePromptUIs.Remove(prompt);
                    }
                }

                // Create or update UIs for visible prompts
                foreach (var prompt in visiblePrompts)
                {
                    if (prompt == null) continue;

                    if (!activePromptUIs.ContainsKey(prompt))
                    {
                        if (debugMode) Debug.Log($"[ProximityPromptUI] Creating UI for {prompt.gameObject.name}");
                        CreatePromptUI(prompt);
                    }
                    else
                    {
                        UpdatePromptUIContent(prompt, activePromptUIs[prompt]);
                    }
                }

                lastVisiblePrompts.Clear();
                lastVisiblePrompts.AddRange(visiblePrompts);
            }
        }

        private void CreatePromptUI(ProximityPrompt prompt)
        {
            if (prompt.customStyle) return;

            GameObject uiObject = promptUIPrefab != null
                ? Instantiate(promptUIPrefab, transform)
                : CreateDefaultPromptUI();

            var uiInstance = new PromptUIInstance
            {
                rootObject = uiObject,
                canvasGroup = uiObject.GetComponent<CanvasGroup>() ?? uiObject.AddComponent<CanvasGroup>(),
                actionText = uiObject.transform.Find("ActionText")?.GetComponent<TextMeshProUGUI>(),
                objectText = uiObject.transform.Find("ObjectText")?.GetComponent<TextMeshProUGUI>(),
                keyText = uiObject.transform.Find("KeyBackground/KeyText")?.GetComponent<TextMeshProUGUI>(),
                progressBar = uiObject.transform.Find("ProgressBarContainer/FillArea/ProgressBar")?.GetComponent<Image>(),
                progressBarRect = uiObject.transform.Find("ProgressBarContainer/FillArea/ProgressBar")?.GetComponent<RectTransform>(),
                keyBackground = uiObject.transform.Find("KeyBackground")?.GetComponent<Image>()
            };

            // Start fully transparent
            uiInstance.canvasGroup.alpha = 0f;
            uiInstance.targetAlpha = 1f;

            activePromptUIs[prompt] = uiInstance;

            // Subscribe to events
            prompt.PromptButtonHoldBegan += (player) => OnHoldBegan(prompt, uiInstance);
            prompt.PromptButtonHoldEnded += (player) => OnHoldEnded(prompt, uiInstance);

            UpdatePromptUIContent(prompt, uiInstance);
        }

        private GameObject CreateDefaultPromptUI()
        {
            var root = new GameObject("PromptUI");
            root.transform.SetParent(transform, false);
            var rectTransform = root.AddComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(300, 100);

            var canvasGroup = root.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;

            var bg = new GameObject("Background");
            bg.transform.SetParent(root.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            bg.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.8f);

            var keyBg = new GameObject("KeyBackground");
            keyBg.transform.SetParent(root.transform, false);
            var keyBgRect = keyBg.AddComponent<RectTransform>();
            keyBgRect.anchorMin = new Vector2(0, 0.5f);
            keyBgRect.anchorMax = new Vector2(0, 0.5f);
            keyBgRect.pivot = new Vector2(0, 0.5f);
            keyBgRect.anchoredPosition = new Vector2(10, 0);
            keyBgRect.sizeDelta = new Vector2(60, 60);
            keyBg.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 0.9f);

            var keyTextObj = new GameObject("KeyText");
            keyTextObj.transform.SetParent(keyBg.transform, false);
            var keyTextRect = keyTextObj.AddComponent<RectTransform>();
            keyTextRect.anchorMin = Vector2.zero;
            keyTextRect.anchorMax = Vector2.one;
            keyTextRect.offsetMin = Vector2.zero;
            keyTextRect.offsetMax = Vector2.zero;
            var keyText = keyTextObj.AddComponent<TextMeshProUGUI>();
            keyText.text = "E";
            keyText.fontSize = 32;
            keyText.alignment = TextAlignmentOptions.Center;
            keyText.color = Color.white;

            var actionTextObj = new GameObject("ActionText");
            actionTextObj.transform.SetParent(root.transform, false);
            var actionTextRect = actionTextObj.AddComponent<RectTransform>();
            actionTextRect.anchorMin = new Vector2(0, 0.5f);
            actionTextRect.anchorMax = new Vector2(1, 0.5f);
            actionTextRect.pivot = new Vector2(0, 0.5f);
            actionTextRect.anchoredPosition = new Vector2(80, 10);
            actionTextRect.sizeDelta = new Vector2(-90, 30);
            var actionText = actionTextObj.AddComponent<TextMeshProUGUI>();
            actionText.fontSize = actionTextSize;
            actionText.alignment = TextAlignmentOptions.Left;
            actionText.color = Color.white;

            var objectTextObj = new GameObject("ObjectText");
            objectTextObj.transform.SetParent(root.transform, false);
            var objectTextRect = objectTextObj.AddComponent<RectTransform>();
            objectTextRect.anchorMin = new Vector2(0, 0.5f);
            objectTextRect.anchorMax = new Vector2(1, 0.5f);
            objectTextRect.pivot = new Vector2(0, 0.5f);
            objectTextRect.anchoredPosition = new Vector2(80, -15);
            objectTextRect.sizeDelta = new Vector2(-90, 25);
            var objectText = objectTextObj.AddComponent<TextMeshProUGUI>();
            objectText.fontSize = objectTextSize;
            objectText.alignment = TextAlignmentOptions.Left;
            objectText.color = new Color(0.7f, 0.7f, 0.7f);

            var progressContainer = new GameObject("ProgressBarContainer");
            progressContainer.transform.SetParent(root.transform, false);
            var progressContainerRect = progressContainer.AddComponent<RectTransform>();
            progressContainerRect.anchorMin = new Vector2(0, 0);
            progressContainerRect.anchorMax = new Vector2(1, 0);
            progressContainerRect.pivot = new Vector2(0.5f, 0);
            progressContainerRect.anchoredPosition = new Vector2(0, 5);
            progressContainerRect.sizeDelta = new Vector2(-20, 5);

            var fillArea = new GameObject("FillArea");
            fillArea.transform.SetParent(progressContainer.transform, false);
            var fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.offsetMin = Vector2.zero;
            fillAreaRect.offsetMax = Vector2.zero;

            var progressObj = new GameObject("ProgressBar");
            progressObj.transform.SetParent(fillArea.transform, false);
            var progressRect = progressObj.AddComponent<RectTransform>();
            progressRect.anchorMin = new Vector2(0, 0);
            progressRect.anchorMax = new Vector2(0, 1);
            progressRect.pivot = new Vector2(0, 0.5f);
            progressRect.offsetMin = Vector2.zero;
            progressRect.offsetMax = Vector2.zero;

            var progressBar = progressObj.AddComponent<Image>();
            progressBar.color = progressColor;

            return root;
        }

        private void UpdatePromptUIContent(ProximityPrompt prompt, PromptUIInstance uiInstance)
        {
            if (uiInstance.actionText != null)
                uiInstance.actionText.text = prompt.actionText;

            if (uiInstance.objectText != null)
            {
                uiInstance.objectText.text = prompt.objectText;
                uiInstance.objectText.gameObject.SetActive(!string.IsNullOrEmpty(prompt.objectText));
            }

            if (uiInstance.keyText != null)
                uiInstance.keyText.text = prompt.keyboardKeyCode;

            // Update progress bar
            if (uiInstance.progressBarRect != null && uiInstance.progressBar != null)
            {
                if (prompt.holdDuration > 0f && prompt.IsHolding)
                {
                    float fillPercent = Mathf.Clamp01(prompt.HoldProgress / prompt.holdDuration);
                    uiInstance.progressBarRect.anchorMax = new Vector2(fillPercent, 1f);
                    uiInstance.progressBar.gameObject.SetActive(true);
                }
                else
                {
                    uiInstance.progressBarRect.anchorMax = new Vector2(0f, 1f);
                    uiInstance.progressBar.gameObject.SetActive(false);
                }
            }
        }

        private void UpdatePromptUITransform(ProximityPrompt prompt, PromptUIInstance uiInstance)
        {
            if (playerCamera == null || uiInstance.rootObject == null) return;

            Vector3 targetScreenPos = playerCamera.WorldToScreenPoint(prompt.transform.position);
            targetScreenPos.x += prompt.uiOffset.x;
            targetScreenPos.y += prompt.uiOffset.y;

            // Update position and alpha based on whether prompt is in front of camera
            if (targetScreenPos.z > 0)
            {
                // In front of camera - show it
                uiInstance.rootObject.transform.position = Vector3.Lerp(
                    uiInstance.rootObject.transform.position,
                    targetScreenPos,
                    Time.deltaTime * followSmoothSpeed
                );

                // Scale based on distance if enabled
                if (scaleWithDistance)
                {
                    float distance = Vector3.Distance(prompt.transform.position, playerCamera.transform.position);
                    float scaleFactor = distance / referenceDistance;
                    scaleFactor = Mathf.Clamp(scaleFactor, minScale, maxScale);

                    uiInstance.rootObject.transform.localScale = Vector3.Lerp(
                        uiInstance.rootObject.transform.localScale,
                        Vector3.one * scaleFactor,
                        Time.deltaTime * followSmoothSpeed
                    );
                }
                else
                {
                    uiInstance.rootObject.transform.localScale = Vector3.one;
                }

                uiInstance.targetAlpha = 1f;
            }
            else
            {
                // Behind camera - hide it
                uiInstance.targetAlpha = 0f;
            }

            // Smooth alpha transition
            uiInstance.canvasGroup.alpha = Mathf.Lerp(
                uiInstance.canvasGroup.alpha,
                uiInstance.targetAlpha,
                Time.deltaTime * fadeSmoothSpeed
            );
        }

        private void OnHoldBegan(ProximityPrompt prompt, PromptUIInstance uiInstance)
        {
            if (uiInstance.keyBackground != null)
                StartCoroutine(ScaleTransform(uiInstance.keyBackground.transform, Vector3.one, Vector3.one * holdScaleFactor, 0.2f));
        }

        private void OnHoldEnded(ProximityPrompt prompt, PromptUIInstance uiInstance)
        {
            if (uiInstance.keyBackground != null)
                StartCoroutine(ScaleTransform(uiInstance.keyBackground.transform, uiInstance.keyBackground.transform.localScale, Vector3.one, 0.2f));
        }

        private System.Collections.IEnumerator ScaleTransform(Transform target, Vector3 from, Vector3 to, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (target == null) yield break;
                elapsed += Time.deltaTime;
                target.localScale = Vector3.Lerp(from, to, elapsed / duration);
                yield return null;
            }
            if (target != null)
                target.localScale = to;
        }

        private class PromptUIInstance
        {
            public GameObject rootObject;
            public CanvasGroup canvasGroup;
            public TextMeshProUGUI actionText;
            public TextMeshProUGUI objectText;
            public TextMeshProUGUI keyText;
            public Image progressBar;
            public RectTransform progressBarRect;
            public Image keyBackground;
            public float targetAlpha = 1f;
        }
    }
}