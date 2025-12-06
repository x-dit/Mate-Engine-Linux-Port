﻿using UnityEngine;
using DiscordRPC;
using System;
using System.Collections.Generic;
using Lachee.Discord.Control; // Required for UnityNamedPipe and UnityLogger

public class DiscordPresence : MonoBehaviour
{
    public enum TimerMode
    {
        None,
        StartNow,
        FixedStartTime
    }

    public enum Pipe
    {
        FirstAvailable = -1,
        Pipe0 = 0,
        Pipe1 = 1,
        Pipe2 = 2,
        Pipe3 = 3,
        Pipe4 = 4,
        Pipe5 = 5,
        Pipe6 = 6,
        Pipe7 = 7,
        Pipe8 = 8,
        Pipe9 = 9
    }

    [Header("Discord App Info")]
    public string appId = "123456789012345678";

    [Header("Advanced")]
    [Tooltip("The pipe discord is located on. Useful for testing multiple clients.")]
    public Pipe targetPipe = Pipe.FirstAvailable;

    [Header("Default Text")]
    public string detailsLine = "Playing with my desktop pet";
    public string stateLine = "Just vibing";

    [Header("Timer")]
    public TimerMode timerMode = TimerMode.StartNow;
    public string fixedStartTimeISO = "2025-04-07T12:00:00Z";

    [Header("Button")]
    public string buttonLabel = "Visit Website";
    public string buttonUrl = "https://mateengine.com";

    [Header("Icons")]
    public string largeImageKey = "logo";
    public string largeImageText = "MateEngine";
    public string smallImageKey = "steam-icon";
    public string smallImageText = "Steam Edition";

    [Header("Model Root (VRMModel or CustomVRM must be child)")]
    public GameObject modelRoot;

    [Header("State-Based Overrides")]
    public List<PresenceEntry> presenceOverrides = new List<PresenceEntry>();

    [Serializable]
    public class PresenceEntry
    {
        public string stateName;
        public string details;
        public string state;
    }

    public static DiscordPresence Instance;

    public DiscordRpcClient client;
    private RichPresence presence;
    private string lastState = "";
    public Animator cachedAnimator;
    private bool wasRPCEnabled = false;

    private long gameStartTimestamp = 0;

    public bool isInitialized { get { return client != null && client.IsInitialized; } }

    void Awake()
    {
        SetupSingleton();
    }

    private void SetupSingleton()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[DiscordPresence] Multiple DiscordPresence instances exist already. Destroying self.", Instance);
            Destroy(this);
            return;
        }

        Instance = this;
    }

    void Start()
    {
        wasRPCEnabled = SaveLoadHandler.Instance?.data.enableDiscordRPC == true;
        if (wasRPCEnabled)
        {
            Initialize();
        }
    }

    void Update()
    {
        bool isEnabled = SaveLoadHandler.Instance?.data.enableDiscordRPC == true;

        if (isEnabled != wasRPCEnabled)
        {
            wasRPCEnabled = isEnabled;

            if (isEnabled)
            {
                Initialize();
                Debug.Log("[DiscordPresence] Enabled and client initialized at runtime.");
            }
            else
            {
                Deinitialize();
                Debug.Log("[DiscordPresence] Disabled and client disposed at runtime.");
            }
        }

        if (wasRPCEnabled && client != null)
        {
            if (cachedAnimator == null)
            {
                ResolveAnimator();
                if (cachedAnimator == null) return;
            }

            UpdatePresence();
        }
    }

    void FixedUpdate()
    {
        if (client != null)
        {
            client.Invoke();
        }
    }

    void Initialize()
    {
        if (!Application.isPlaying) return;

        SetupSingleton();

        InitGameStartTimestamp();

        // Create the client with matching parameters
        Debug.Log("[DiscordPresence] Starting Discord Rich Presence");
        client = new DiscordRpcClient(
            appId,        
            pipe: (int)targetPipe,
            logger: SaveLoadHandler.Instance.data.verboseDiscordRPCLog ? new UnityLogger { Level = DiscordRPC.Logging.LogLevel.Trace } : null,
            autoEvents: false,
            client: new UnityNamedPipe()
        );

        // Subscribe to basic events for robustness (matching DiscordManager)
        client.OnError += (s, args) => Debug.LogError("[DiscordPresence] Error Occured within the Discord IPC: (" + args.Code + ") " + args.Message);
        client.OnReady += (s, args) =>
        {
            Debug.Log("[DiscordPresence] Connection established and received READY from Discord IPC.");
        };

        // Initialize the client
        client.Initialize();
        Debug.Log("[DiscordPresence] Discord Rich Presence initialized and connecting...");

        ResolveAnimator();
        UpdatePresence(force: true);
    }

    void Deinitialize()
    {
        if (client != null)
        {
            Debug.Log("[DiscordPresence] Disposing Discord IPC Client...");
            client.ClearPresence();
            client.Dispose();
            client = null;
            Debug.Log("[DiscordPresence] Finished Disconnecting");
        }
    }

    void OnDisable()
    {
        Deinitialize();
    }

    void OnDestroy()
    {
        Deinitialize();
    }

    void InitGameStartTimestamp()
    {
        if (timerMode == TimerMode.FixedStartTime)
        {
            if (DateTimeOffset.TryParse(fixedStartTimeISO, out var fixedTime))
            {
                gameStartTimestamp = fixedTime.ToUnixTimeMilliseconds();
            }
            else
            {
                Debug.LogWarning("[DiscordPresence] Invalid fixed timestamp. Using now.");
                gameStartTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }
        else if (timerMode == TimerMode.StartNow)
        {
            if (gameStartTimestamp == 0)
            {
                gameStartTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }
        else
        {
            gameStartTimestamp = 0;
        }
    }

    void ResolveAnimator()
    {
        if (modelRoot == null)
        {
            Debug.LogWarning("[DiscordPresence] ModelRoot not assigned.");
            return;
        }

        Transform vrmModel = modelRoot.transform.Find("CustomVRM(Clone)");
        if (vrmModel == null || !vrmModel.gameObject.activeInHierarchy)
        {
            vrmModel = modelRoot.transform.Find("VRMModel");
        }
        if (vrmModel != null && vrmModel.gameObject.activeInHierarchy)
        {
            cachedAnimator = vrmModel.GetComponent<Animator>();
        }
    }

    void UpdatePresence(bool force = false)
    {
        ResolveAnimator();
        if (cachedAnimator == null) return;
        
        string currentState = GetCurrentAnimatorState();
        if (!force && currentState == lastState)
            return;

        lastState = currentState;
        string details = detailsLine;
        string state = stateLine;

        for (int i = 0; i < presenceOverrides.Count; i++)
        {
            var entry = presenceOverrides[i];
            if (currentState == entry.stateName)
            {
                if (!string.IsNullOrEmpty(entry.details)) details = entry.details;
                if (!string.IsNullOrEmpty(entry.state)) state = entry.state;
                break;
            }
        }

        presence = new RichPresence
        {
            Details = details,
            State = state,
            Assets = new Assets
            {
                LargeImageKey = largeImageKey,
                LargeImageText = largeImageText,
                SmallImageKey = smallImageKey,
                SmallImageText = smallImageText
            }
        };

        if (timerMode != TimerMode.None)
        {
            presence.Timestamps = new Timestamps
            {
                StartUnixMilliseconds = (ulong?)gameStartTimestamp
            };
        }

        if (!string.IsNullOrEmpty(buttonLabel) && !string.IsNullOrEmpty(buttonUrl))
        {
            presence.Buttons = new DiscordRPC.Button[]
            {
                new DiscordRPC.Button { Label = buttonLabel, Url = buttonUrl }
            };
        }

        client.SetPresence(presence);
        Debug.Log($"[DiscordPresence] Updated to state: {currentState} → {details} / {state}");
    }

    string GetCurrentAnimatorState()
    {
        if (cachedAnimator == null) return "";

        AnimatorStateInfo stateInfo = cachedAnimator.GetCurrentAnimatorStateInfo(0);
        if (cachedAnimator.IsInTransition(0)) return "";

        for (int i = 0; i < presenceOverrides.Count; i++)
        {
            var name = presenceOverrides[i].stateName;
            if (stateInfo.IsName(name))
                return name;
        }

        return "";
    }

    void OnApplicationQuit()
    {
        try
        {
            Deinitialize();
        }
        catch (Exception ex)
        {
            Debug.LogError("[DiscordPresence] Error during Discord shutdown: " + ex);
        }
    }
}