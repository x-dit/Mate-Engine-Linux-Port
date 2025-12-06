using System;
using System.Collections.Generic;
using UnityEngine;
using X11;
using Random = UnityEngine.Random;

public class AvatarWindowHandler : MonoBehaviour
{
    private const float CacheUpdateCooldown = 0.05f; // Optional cooldown during dragging
    private static readonly int[] TRI = { 0, 1, 2, 0, 2, 3 };
    private static readonly int windowSitIndexParam = Animator.StringToHash("WindowSitIndex");

    [Header("Snap Safety")]
    public float minDragHoldSecondsToSit = 1f;

    public float unsnapCooldownSeconds = 0.3f;

    [Header("Snap Probe Offset")]
    public float probeZoneYOffsetLocal = 0f;

    [Header("Snap Probe")]
    public float probeRadiusPx = 24f;

    public bool showProbeGizmo = true;
    public Color probeGizmoColor = Color.magenta;

    [Header("Snap Guard Zone")]
    public bool useGuardZone = true;

    public float probeGuardPx = 240f;
    public Color probeGuardGizmoColor = Color.cyan;

    [Header("Sit Blockers")]
    public List<string> blockSitIfBoolTrue = new List<string>();

    [Header("Seat Alignment")]
    [Range(-256f, 256f)] public float seatOffsetPx = 0f;

    [Header("Occluder")]  // Port full occluder system
    public Material occluderMaterial;

    public Camera targetCamera;
    public float targetQuadZOffset = 0.001f;
    public float othersQuadZOffset = 0.002f;
    public int maxOtherQuads = 12;

    [Header("Occluder Pool")]
    public bool precreateQuadsOnStart = true;

    public int prewarmOtherQuads = 6;

    [Header("Target Quad Z Auto-Scale")]
    public bool autoScaleTargetZ = true;

    public float targetZBase = 3.2f;
    public float targetZRefScale = 1.0f;
    public float targetZSensitivity = 3.0f;
    public float targetZMin = 0.05f;
    public float targetZMax = 10f;

    [Header("Snap Smoothing")]
    public bool enableSnapSmoothing = true;

    [Range(0.01f, 0.5f)] public float snapSmoothingTime = 0.12f;
    public float snapSmoothingMaxSpeed = 6000f;

    [Header("Snap Trigger")]
    public int minDragPixelsToSnap = 4;

    [Header("Snap Guard")]
    public int snapGuardFrames = 8;

    public int snapLatchFrames = 18;
    public int unsnapVerticalBand = 16;

    [Header("Transparent-Window-Filter")]  // Approx
    [Range(0, 255)] public int layeredAlphaIgnoreBelow = 230;  // Not directly usable; ignore via shape

    public bool ignoreLayeredClickThrough = true;
    public bool ignoreLayeredToolOrNoActivate = true;  // Approx via window type
    public bool ignoreLayeredWithColorKey = true;  // Approx via shape

    [Header("Performance")]
    public float windowEnumFPS = 15f;

    public float windowEnumIdleFPS = 8f;
    public int snapThreshold = 30, verticalOffset;
    public float desktopScale = 1f;

    [Header("Pink Snap Zone (Unity-side)")]
    public Vector2 snapZoneOffset = new(0, -5);

    public Vector2 snapZoneSize = new(100, 10);

    [Header("Window Sit BlendTree")]
    public int totalWindowSitAnimations = 4;

    [Header("User Y-Offset Slider")]
    [Range(-0.015f, 0.015f)]
    public float windowSitYOffset;

    [Header("Fine-Tune")]
    public float baseOffset = 40f;

    public float baseScale = 1f;
    private readonly List<string> _blockSitValidNames = new List<string>();
    private readonly List<WindowEntry> activeOccluders = new List<WindowEntry>(16);
    readonly List<WindowEntry> cachedWindows = new();
    private readonly System.Text.StringBuilder classNameBuffer = new System.Text.StringBuilder(256);
    private readonly List<Mesh> otherMeshes = new List<Mesh>(16);
    private readonly List<GameObject> otherQuadGOs = new List<GameObject>(16);
    private readonly Vector3[] verts4 = new Vector3[4];
    private readonly Vector3[] verts4Other = new Vector3[4];
    private bool _canSitHold;
    private int _dragStartCursorX, _dragStartCursorY;
    private float _dragStartTime = -1f;
    private int _guard, _latch;
    private Vector2 _guardCenterDesktop;
    private float _guardRadiusSq;
    private bool _guardZoneActive;
    private bool _havePrevSnapRect;
    private bool _haveUnityCli;
    private int _lastSnapTopY;
    private Rect _lastUnityCli;
    private float _nextEnumTime;
    private Material _occluderSharedMat;
    private int _postSettleFrames;
    private bool _postSettleRecalib;
    private Vector3 _prevLossyScale;
    private Rect _prevSnapRect;
    private bool _recentUnsnap;
    private bool _skinnedCached;
    private int _snapCursorY;
    private bool _snapSmoothingActive;
    private float _snapVelX, _snapVelY;
    private float _unsnapCooldownUntil = -1f;

    Animator animator;
    private Transform boneHips, boneLUL, boneRUL, boneLFoot, boneRFoot, boneHead;  // From CacheRigRefs
    private Vector3 boundsMinSnapLocal;
    private Vector3 boundsSizeSnapLocal;
    AvatarAnimatorController controller;

    private float lastCacheUpdateTime;
    Vector2 lastDesktopPosition;
    private Transform occluderRoot;
    Rect pinkZoneDesktopRect;
    private bool seatCalibrated;
    private Vector3 seatLocalAtSnap;
    private float seatNormY;
    private SkinnedMeshRenderer[] skinned;

    private float snapFraction;
    Vector2 snapOffset;

    IntPtr snappedHWND = IntPtr.Zero, unityHWND = IntPtr.Zero;
    private Mesh targetMesh;
    private GameObject targetQuadGO;
    private bool wasDragging;
    private bool wasSitting;

    void Start()
    {
        unityHWND = X11Manager.Instance.UnityWindow;
        animator = GetComponent<Animator>();
        controller = GetComponent<AvatarAnimatorController>();
        if (targetCamera == null) targetCamera = Camera.main;
        CacheRigRefs();  // New: Add below
        BuildBlockSitCache();  // New: Add below
        EnsureOccluderRoot();  // New: Add below
        if (occluderMaterial != null) _occluderSharedMat = new Material(occluderMaterial);
        if (precreateQuadsOnStart)
        {
            EnsureTargetQuad();  // New: Add below
            int pre = Mathf.Clamp(prewarmOtherQuads, 0, Mathf.Max(maxOtherQuads, 0));
            for (int i = 0; i < pre; i++) EnsureOtherQuad(i);  // New
            SetTargetQuadActive(false);
            SetOtherQuadsActive(0);
        }
        SetTopMost(SaveLoadHandler.Instance != null ? SaveLoadHandler.Instance.data.isTopmost : true);
        _nextEnumTime = 0f;
        _prevLossyScale = transform.lossyScale;
        _lastSnapTopY = int.MinValue;
        _guardRadiusSq = probeGuardPx * probeGuardPx;
        cachedWindows.Capacity = Mathf.Max(cachedWindows.Capacity, 128);  // Reuse from existing
        activeOccluders.Capacity = Mathf.Max(activeOccluders.Capacity, maxOtherQuads);
    }
    
    void LateUpdate()
    {
        UpdateOccluderQuadsFrameSync();
    }

    void Update()
    {
        if (unityHWND == IntPtr.Zero || animator == null || controller == null) return;
        if (!SaveLoadHandler.Instance.data.enableWindowSitting) { ClearSnapAndHide(); return; }
        if (IsSitBlocked()) { if (snappedHWND != IntPtr.Zero) ClearSnapAndHide(); return; }

        bool isWindowSitNow = animator.GetBool("isWindowSit");
        if (isWindowSitNow && !wasSitting) animator.SetFloat(windowSitIndexParam, Random.Range(0, totalWindowSitAnimations));
        wasSitting = isWindowSitNow;

        // Scale check for smoothing reset (port)
        if (snappedHWND != IntPtr.Zero && (transform.lossyScale - _prevLossyScale).sqrMagnitude > 1e-8f)
        { _snapSmoothingActive = false; _snapVelX = _snapVelY = 0f; }
        _prevLossyScale = transform.lossyScale;

        // Performance-tuned enum
        float enumHz = (controller.isDragging || snappedHWND != IntPtr.Zero) ? Mathf.Max(1f, windowEnumFPS) : Mathf.Max(1f, windowEnumIdleFPS);
        if (Time.unscaledTime >= _nextEnumTime)
        {
            UpdateCachedWindows();  // Enhanced below
            if (snappedHWND != IntPtr.Zero) RebuildActiveOccluders();  // New: Add below
            _nextEnumTime = Time.unscaledTime + 1f / enumHz;
        }

        // Drag safety
        if (controller.isDragging && !wasDragging)
        {
            Vector2 cp = X11Manager.Instance.GetMousePosition();
            _dragStartCursorX = (int)cp.x; _dragStartCursorY = (int)cp.y;
            if (snappedHWND != IntPtr.Zero && isWindowSitNow) _snapCursorY = (int)cp.y;
            _dragStartTime = Time.unscaledTime;
            _canSitHold = false;
        }
        if (controller.isDragging)
        {
            if (!_canSitHold && _dragStartTime >= 0f && Time.unscaledTime - _dragStartTime >= minDragHoldSecondsToSit) _canSitHold = true;
        }
        else
        {
            _canSitHold = false;
            _dragStartTime = -1f;
        }

        // Recent unsnap guard (approx)
        if (_recentUnsnap)
        {
            if (!controller.isDragging) _recentUnsnap = false;
            else if (ComputeZoneDesktop(out _, out float py))
            {
                int vBand = Mathf.Max(unsnapVerticalBand, ScaledProbeRadiusI());
                if (Mathf.Abs(py - _lastSnapTopY) >= vBand) _recentUnsnap = false;
            }
        }

        // Snapped window checks (max/fullscreen, iconic approx via !visible)
        if (snappedHWND != IntPtr.Zero)
        {
            bool handled = false;
            foreach (var win in cachedWindows)
            {
                if (win.hwnd != snappedHWND) continue;
                if (X11Manager.Instance.IsWindowMaximized(win.hwnd) || IsWindowFullscreen(win.hwnd)) { ClearSnapAndHide(); handled = true; break; }
            }
            if (!handled && !X11Manager.Instance.IsWindowVisible(snappedHWND)) { ClearSnapAndHide(); }  // Approx IsIconic/IsCloaked
        }

        // Snapping logic
        if (controller.isDragging)
        {
            if (snappedHWND == IntPtr.Zero) { if (_canSitHold && DraggedPastSnapThreshold()) TrySnap(); }
            else if (!IsStillNearSnappedWindow()) { SetGuardZoneFromCurrent(); ClearSnapAndHide(true); }
            else FollowSnapped(true);  // With smoothing
        }
        else if (!controller.isDragging && snappedHWND != IntPtr.Zero) FollowSnapped(false);

        // Big screen alarm
        if (animator.GetBool("isBigScreenAlarm"))
        {
            if (isWindowSitNow) animator.SetBool("isWindowSit", false);
            ClearSnapAndHide();
        }

        // Post-settle recalib
        if (snappedHWND != IntPtr.Zero && _postSettleRecalib)
        {
            if (_postSettleFrames > 0) _postSettleFrames--;
            else
            {
                if (X11Manager.Instance.GetWindowRect(snappedHWND, out Rect tr))
                {
                    CalibrateSeatAnchorToDesktopY((int)tr.y + seatOffsetPx);  // Approx Top
                    if (ComputeSeatDesktop(out float px2, out _))
                    {
                        float w = Mathf.Max(1f, tr.width);
                        snapFraction = Mathf.Clamp01((px2 - tr.x) / w);
                    }
                    _snapSmoothingActive = enableSnapSmoothing;
                    _snapVelX = _snapVelY = 0f;
                    _havePrevSnapRect = false;
                    PinToTarget(tr);  // New: Add below
                }
                _postSettleRecalib = false;
            }
        }

        wasDragging = controller.isDragging;
    }

    void OnDrawGizmos()
    {
        if (!showProbeGizmo || targetCamera == null) return;
        Vector3 hip = GetProbeWorld();
        Vector3 sp = targetCamera.WorldToScreenPoint(hip);
        if (sp.z <= 0f) return;
        Vector3 sp2 = sp + new Vector3(ScaledProbeRadiusF(), 0f, 0f);
        Vector3 w1 = targetCamera.ScreenToWorldPoint(new Vector3(sp.x, sp.y, sp.z));
        Vector3 w2 = targetCamera.ScreenToWorldPoint(new Vector3(sp2.x, sp2.y, sp2.z));
        float worldR = Vector3.Distance(w1, w2);
        Gizmos.color = probeGizmoColor; Gizmos.DrawWireSphere(hip, worldR);
        Vector3 spg2 = sp + new Vector3(ScaledGuardRadiusF(), 0f, 0f);
        Vector3 wg2a = targetCamera.ScreenToWorldPoint(new Vector3(spg2.x, spg2.y, spg2.z));
        float worldRGuard = Vector3.Distance(w1, wg2a);
        Gizmos.color = probeGuardGizmoColor; Gizmos.DrawWireSphere(hip, worldRGuard);
    }

    private Vector3 GetProbeWorld() => GetHipWorld() + transform.up * (probeZoneYOffsetLocal * transform.lossyScale.y);

    void CacheRigRefs()
    {
        // Assume Animator human; adapt from AvatarHideHandler
        if (animator == null || !animator.isHuman) return;
        boneHips = animator.GetBoneTransform(HumanBodyBones.Hips);
        // Add others: boneLUL = LeftUpperLeg, etc.
        _skinnedCached = true;
    }

    Vector3 GetHipWorld() => boneHips != null ? boneHips.position : transform.position;

    void BuildBlockSitCache()
    {
        _blockSitValidNames.Clear();
        if (animator == null || blockSitIfBoolTrue == null || blockSitIfBoolTrue.Count == 0) return;
        var wanted = new HashSet<string>(blockSitIfBoolTrue);
        var ps = animator.parameters;
        for (int i = 0; i < ps.Length; i++)
            if (ps[i].type == AnimatorControllerParameterType.Bool && wanted.Contains(ps[i].name))
                _blockSitValidNames.Add(ps[i].name);
    }

    bool IsSitBlocked()
    {
        if (animator == null || _blockSitValidNames.Count == 0) return false;
        for (int i = 0; i < _blockSitValidNames.Count; i++)
            if (animator.GetBool(_blockSitValidNames[i])) return true;
        return false;
    }

    void EnsureOtherQuad(int idx)
    {
        if (idx >= otherQuadGOs.Count)
        {
            GameObject go = new GameObject($"OtherOccluder{idx}");
            go.transform.SetParent(occluderRoot, false);
            MeshFilter mf = go.AddComponent<MeshFilter>();
            Mesh mesh = new Mesh { vertices = verts4Other, triangles = TRI, name = $"OtherQuad{idx}" };
            mf.mesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = _occluderSharedMat;
            go.transform.localPosition = Vector3.zero;
            otherQuadGOs.Add(go);
            otherMeshes.Add(mesh);
            go.SetActive(false);
        }
    }

    void UpdateCachedWindows()
    {
        cachedWindows.Clear();
        var allWindows = X11Manager.Instance.GetAllVisibleWindows();
        foreach (var hWnd in allWindows)
        {
            if (!X11Manager.Instance.GetWindowRect(hWnd, out Rect r)) continue;
            string cls = X11Manager.Instance.GetClassName(hWnd);  // Assume added to X11Manager
            bool isTaskbar = X11Manager.Instance.IsDock(hWnd);
            // Transparent filter (approx)
            bool ignore = ignoreLayeredClickThrough && X11Manager.Instance.IsWindowTransparentOrClickThrough(hWnd);
            ignore |= ignoreLayeredToolOrNoActivate && (cls.Contains("tool") || X11Manager.Instance.IsDesktop(hWnd));
            if (!isTaskbar && !ignore)
            {
                if (r.width < 100 || r.height < 100) continue;
                if (string.IsNullOrEmpty(cls)) continue;
                if (X11Manager.Instance.IsDesktop(hWnd)) continue;
            }
            cachedWindows.Add(new WindowEntry { hwnd = hWnd, rect = r, isTaskbar = isTaskbar, className = cls });
        }
    }

    bool DraggedPastSnapThreshold()
    {
        Vector2 cp = X11Manager.Instance.GetMousePosition();
        if (cp == Vector2.zero) return true;
        return Mathf.Abs(cp.x - _dragStartCursorX) >= minDragPixelsToSnap || Mathf.Abs(cp.y - _dragStartCursorY) >= minDragPixelsToSnap;
    }

    void SetGuardZoneFromCurrent()
    {
        if (!useGuardZone) return;
        if (!ComputeZoneDesktop(out float px, out float py))
        {
            Vector2 unityPos = GetUnityWindowPosition();
            px = unityPos.x + GetUnityWindowWidth() / 2f;
            py = unityPos.y + GetUnityWindowHeight();
        }
        _guardCenterDesktop = new Vector2(px, py);
        _guardZoneActive = true;
        _guard = snapGuardFrames;
    }

    void TrySnap()
    {
        Vector2 probeDesktop = ProjectProbeToDesktop();  // New: Add below
        Rect probeRect = new Rect(probeDesktop.x - probeRadiusPx, probeDesktop.y - probeRadiusPx, probeRadiusPx * 2, probeRadiusPx * 2);
        foreach (var win in cachedWindows)
        {
            if (win.hwnd == unityHWND) continue;
            Rect topBar = new Rect(win.rect.x, win.rect.y, win.rect.width, 5);  // Thin top bar
            if (probeRect.Overlaps(topBar))
            {
                lastDesktopPosition = GetUnityWindowPosition();
                snappedHWND = win.hwnd;
                float winW = win.rect.width, unityW = GetUnityWindowWidth();
                float petCenterX = GetUnityWindowPosition().x + unityW * 0.5f;
                snapFraction = (petCenterX - win.rect.x) / winW;
                _lastSnapTopY = (int)win.rect.y;
                animator.SetBool("isWindowSit", true);
                _postSettleRecalib = true;
                _postSettleFrames = snapLatchFrames;
                return;
            }
        }
    }

    Vector2 ProjectProbeToDesktop()
    {
        Vector3 probeWorld = GetProbeWorld();
        Vector3 screenP = targetCamera.WorldToScreenPoint(probeWorld);
        if (screenP.z < 0.01f) return Vector2.zero;
        // Project to desktop (approx client-to-desktop via X11Manager)
        if (!GetUnityClientRect(out Rect uCli)) return Vector2.zero;
        float clientW = uCli.width, pxW = targetCamera.pixelWidth;
        float sx = screenP.x * (clientW / pxW);
        return new Vector2(uCli.x + sx, uCli.y + uCli.height - screenP.y);  // Flip Y
    }

    bool ComputeZoneDesktop(out float px, out float py)
    {
        px = py = 0;
        Vector2 probe = ProjectProbeToDesktop();
        if (probe == Vector2.zero) return false;
        px = probe.x; py = probe.y;
        return true;
    }

    int ScaledProbeRadiusI() => Mathf.RoundToInt(probeRadiusPx);
    float ScaledProbeRadiusF() => probeRadiusPx;

    bool GetUnityClientRect(out Rect r)
    {
        r = new Rect();
        return X11Manager.Instance.GetWindowRect(unityHWND, out r);  // Borderless approx
    }

    void FollowSnapped(bool dragging)
    {
        if (!X11Manager.Instance.GetWindowRect(snappedHWND, out Rect winRect) || !X11Manager.Instance.IsWindowVisible(snappedHWND))
        {
            ClearSnapAndHide();
            return;
        }
        float winW = winRect.width, unityW = GetUnityWindowWidth();
        float newCenterX = winRect.x + snapFraction * winW;
        int targetX = Mathf.RoundToInt(newCenterX - unityW * 0.5f);
        float yOffset = GetUnityWindowHeight() + baseOffset;  // Adapt
        float scale = transform.lossyScale.y;
        float scaleOffset = (baseScale - scale) * baseOffset;
        float windowSitOffset = windowSitYOffset * GetUnityWindowHeight();
        float targetY = winRect.y - (yOffset + scaleOffset) + verticalOffset + Mathf.RoundToInt(windowSitOffset);
        MoveSmooth(targetX, (int)targetY, dragging);  // New: With smoothing
    }

    void MoveSmooth(int targetX, int targetY, bool dragging)
    {
        if (!enableSnapSmoothing || !dragging || !_snapSmoothingActive)
        {
            SetUnityWindowPosition(targetX, targetY);
            return;
        }
        Vector2 curPos = GetUnityWindowPosition();
        float dt = Time.unscaledDeltaTime;
        float nx = Mathf.SmoothDamp(curPos.x, targetX, ref _snapVelX, snapSmoothingTime, snapSmoothingMaxSpeed, dt);
        float ny = Mathf.SmoothDamp(curPos.y, targetY, ref _snapVelY, snapSmoothingTime, snapSmoothingMaxSpeed, dt);
        int ix = Mathf.RoundToInt(nx), iy = Mathf.RoundToInt(ny);
        if (Mathf.Abs(targetX - ix) <= 1 && Mathf.Abs(targetY - iy) <= 1)
        {
            ix = targetX; iy = targetY; _snapSmoothingActive = false; _snapVelX = _snapVelY = 0f;
        }
        SetUnityWindowPosition(ix, iy);
    }

    bool IsStillNearSnappedWindow()
    {
        if (!X11Manager.Instance.GetWindowRect(snappedHWND, out Rect winRect)) return false;
        Vector2 probe = ProjectProbeToDesktop();
        Rect probeR = new Rect(probe.x - probeRadiusPx, probe.y - probeRadiusPx, probeRadiusPx * 2, probeRadiusPx * 2);
        Rect topBar = new Rect(winRect.x, winRect.y, winRect.width, 5);
        return probeR.Overlaps(topBar) || (_guardZoneActive && Vector2.Distance(probe, _guardCenterDesktop) < probeGuardPx);
    }

    void RebuildActiveOccluders()
    {
        activeOccluders.Clear();
        if (!X11Manager.Instance.GetWindowRect(unityHWND, out Rect unityDesktopRect)) return;

        // Target window (the one we're sitting on) is always #0 if snapped
        if (snappedHWND != IntPtr.Zero)
        {
            if (X11Manager.Instance.GetWindowRect(snappedHWND, out Rect targetRect) &&
                X11Manager.Instance.IsWindowVisible(snappedHWND))
            {
                Rect intersect = IntersectRect(targetRect, unityDesktopRect);
                if (intersect.width > 8f && intersect.height > 8f)
                {
                    activeOccluders.Add(new WindowEntry
                    {
                        hwnd = snappedHWND,
                        rect = targetRect,
                        isTaskbar = false,
                        className = X11Manager.Instance.GetClassName(snappedHWND) ?? ""
                    });
                }
            }
        }

        // Find other overlapping windows (excluding our own Unity window and taskbar/dock)
        foreach (var win in cachedWindows)
        {
            if (win.hwnd == unityHWND || win.hwnd == snappedHWND) continue;
            if (win.isTaskbar) continue;
            if (X11Manager.Instance.IsWindowTransparentOrClickThrough(win.hwnd)) continue;
            if (!X11Manager.Instance.IsWindowVisible(win.hwnd)) continue;

            Rect intersect = IntersectRect(win.rect, unityDesktopRect);
            if (intersect.width > 8f && intersect.height > 8f)
            {
                activeOccluders.Add(win);
            }
        }

        // Sort by Z-order: highest (topmost) first
        activeOccluders.Sort((a, b) =>
        {
            if (a.hwnd == snappedHWND) return -1;
            if (b.hwnd == snappedHWND) return 1;
            bool aAboveB = X11Manager.Instance.IsAboveInZOrder(a.hwnd, b.hwnd);
            bool bAboveA = X11Manager.Instance.IsAboveInZOrder(b.hwnd, a.hwnd);
            return aAboveB ? -1 : (bAboveA ? 1 : 0);
        });

        // Limit to maxOtherQuads + 1 (target + others)
        if (activeOccluders.Count > maxOtherQuads + 1)
            activeOccluders.RemoveRange(maxOtherQuads + 1, activeOccluders.Count - (maxOtherQuads + 1));
    }

    private static Rect IntersectRect(Rect a, Rect b)
    {
        float x1 = Mathf.Max(a.xMin, b.xMin);
        float y1 = Mathf.Max(a.yMin, b.yMin);
        float x2 = Mathf.Min(a.xMax, b.xMax);
        float y2 = Mathf.Min(a.yMax, b.yMax);
        if (x2 <= x1 || y2 <= y1) return new Rect(0, 0, 0, 0);
        return new Rect(x1, y1, x2 - x1, y2 - y1);
    }

    void UpdateOccluderQuadsFrameSync()
    {
        if (targetCamera == null || !X11Manager.Instance.GetWindowRect(unityHWND, out Rect unityDesktopRect)) return;

        float clientW = Mathf.Max(1f, unityDesktopRect.width);
        float clientH = Mathf.Max(1f, unityDesktopRect.height);
        float pxW = Mathf.Max(1, targetCamera.pixelWidth);
        float pxH = Mathf.Max(1, targetCamera.pixelHeight);

        int activeCount = 0;
        bool hasTarget = false;

        for (int i = 0; i < activeOccluders.Count; i++)
        {
            var win = activeOccluders[i];
            Rect desktopRect = win.rect;

            // Compute screen-space bounds
            float sx0 = (desktopRect.xMin - unityDesktopRect.xMin) * (pxW / clientW);
            float sx1 = (desktopRect.xMax - unityDesktopRect.xMin) * (pxW / clientW);
            float sy0 = pxH - (desktopRect.yMax - unityDesktopRect.yMin) * (pxH / clientH);
            float sy1 = pxH - (desktopRect.yMin - unityDesktopRect.yMin) * (pxH / clientH);

            // Skip invalid/invisible
            if (sx0 >= sx1 || sy0 >= sy1 || sx1 < 0 || sx0 > pxW || sy1 < 0 || sy0 > pxH) continue;

            float zDepth;
            Vector3[] buffer;
            Mesh mesh;
            GameObject go;

            if (win.hwnd == snappedHWND)
            {
                // Target quad (always first and closest)
                EnsureTargetQuad();
                zDepth = autoScaleTargetZ
                    ? Mathf.Lerp(targetZMin, targetZMax,
                        Mathf.InverseLerp(targetZRefScale * 0.5f, targetZRefScale * 2f, transform.lossyScale.y))
                    : targetZBase;

                zDepth = targetCamera.nearClipPlane + targetQuadZOffset + zDepth * 0.001f;
                buffer = verts4;
                mesh = targetMesh;
                go = targetQuadGO;
                hasTarget = true;
            }
            else
            {
                // Other quads
                int idx = activeCount; // 0-based index among others
                if (idx >= maxOtherQuads) break;
                EnsureOtherQuad(idx);
                zDepth = targetCamera.nearClipPlane + othersQuadZOffset + (idx + 1) * 0.0001f;
                buffer = verts4Other;
                mesh = otherMeshes[idx];
                go = otherQuadGOs[idx];
                activeCount++;
            }

            // Convert screen points to world → local
            Vector3 blW = targetCamera.ScreenToWorldPoint(new Vector3(sx0, sy0, zDepth));
            Vector3 tlW = targetCamera.ScreenToWorldPoint(new Vector3(sx0, sy1, zDepth));
            Vector3 trW = targetCamera.ScreenToWorldPoint(new Vector3(sx1, sy1, zDepth));
            Vector3 brW = targetCamera.ScreenToWorldPoint(new Vector3(sx1, sy0, zDepth));

            buffer[0] = targetCamera.transform.InverseTransformPoint(blW);
            buffer[1] = targetCamera.transform.InverseTransformPoint(tlW);
            buffer[2] = targetCamera.transform.InverseTransformPoint(trW);
            buffer[3] = targetCamera.transform.InverseTransformPoint(brW);

            mesh.vertices = buffer;
            mesh.RecalculateBounds();

            if (!go.activeSelf) go.SetActive(true);
        }

        // Activate target quad
        SetTargetQuadActive(hasTarget && snappedHWND != IntPtr.Zero);

        // Deactivate unused other quads
        for (int i = activeCount; i < otherQuadGOs.Count; i++)
        {
            if (otherQuadGOs[i].activeSelf) otherQuadGOs[i].SetActive(false);
        }
    }

    void ClearSnapAndHide(bool recent = false)
    {
        if (snappedHWND != IntPtr.Zero)
        {
            snappedHWND = IntPtr.Zero;
            animator.SetBool("isWindowSit", false);
            SetTopMost(true);
            if (recent) { _recentUnsnap = true; _unsnapCooldownUntil = Time.unscaledTime + unsnapCooldownSeconds; }
        }
        ForceExitWindowSitting();  // Existing
    }

    bool CalibrateSeatAnchorToDesktopY(float targetDesktopY)
    {
        if (targetCamera == null || !GetUnityClientRect(out Rect uCli)) return false;

        Matrix4x4 inv = transform.worldToLocalMatrix;
        float yMinW = float.PositiveInfinity, yMaxW = float.NegativeInfinity;

        if (animator != null && animator.isHuman)
        {
            if (boneHead != null) { var p = boneHead.position.y; if (p < yMinW) yMinW = p; if (p > yMaxW) yMaxW = p; }
            if (boneHips != null) { var p = boneHips.position.y; if (p < yMinW) yMinW = p; if (p > yMaxW) yMaxW = p; }
            if (boneLUL != null) { var p = boneLUL.position.y; if (p < yMinW) yMinW = p; if (p > yMaxW) yMaxW = p; }
            if (boneRUL != null) { var p = boneRUL.position.y; if (p < yMinW) yMinW = p; if (p > yMaxW) yMaxW = p; }
            if (boneLFoot != null) { var p = boneLFoot.position.y; if (p < yMinW) yMinW = p; if (p > yMaxW) yMaxW = p; }
            if (boneRFoot != null) { var p = boneRFoot.position.y; if (p < yMinW) yMinW = p; if (p > yMaxW) yMaxW = p; }
        }
        float low, high;
        if (float.IsInfinity(yMinW) || float.IsInfinity(yMaxW))
        {
            Bounds lb = WorldBoundsToRootLocal(GetCombinedWorldBounds());
            float h = Mathf.Max(0.0001f, lb.size.y);
            low = lb.min.y - 0.5f * h - 0.25f;
            high = lb.max.y + 0.5f * h + 0.25f;
            boundsMinSnapLocal = lb.min;
            boundsSizeSnapLocal = lb.size;
        }
        else
        {
            Vector3 lmin = inv.MultiplyPoint3x4(new Vector3(transform.position.x, yMinW, transform.position.z));
            Vector3 lmax = inv.MultiplyPoint3x4(new Vector3(transform.position.x, yMaxW, transform.position.z));
            float ymin = Mathf.Min(lmin.y, lmax.y), ymax = Mathf.Max(lmin.y, lmax.y);
            float pad = Mathf.Max(0.05f, (ymax - ymin) * 0.2f);
            low = ymin - pad; high = ymax + pad;
            Bounds worldB = GetCombinedWorldBounds();
            Bounds localB = WorldBoundsToRootLocal(worldB);
            boundsMinSnapLocal = localB.min;
            boundsSizeSnapLocal = localB.size;
        }
        Vector3 guessL = transform.worldToLocalMatrix.MultiplyPoint3x4(SeatWorldGuess());
        float bestY = guessL.y, bestErr = float.MaxValue;

        for (int i = 0; i < 20; i++)
        {
            float mid = 0.5f * (low + high);
            Vector3 lp = new Vector3(guessL.x, mid, guessL.z);
            Vector3 sp = targetCamera.WorldToScreenPoint(transform.localToWorldMatrix.MultiplyPoint3x4(lp));
            if (sp.z < 0.01f) break;
            float clientH = Mathf.Max(1f, uCli.height);
            float py = uCli.y + (targetCamera.pixelHeight - Mathf.Clamp(sp.y, 0, targetCamera.pixelHeight)) * (clientH / Mathf.Max(1, targetCamera.pixelHeight));
            float err = py - targetDesktopY;
            if (Mathf.Abs(err) < Mathf.Abs(bestErr)) { bestErr = err; bestY = mid; }
            if (err > 0f) high = mid; else low = mid;
        }
        seatLocalAtSnap = new Vector3(guessL.x, bestY, guessL.z);
        float denom = Mathf.Max(0.0001f, boundsSizeSnapLocal.y);
        seatNormY = Mathf.Clamp01((bestY - boundsMinSnapLocal.y) / denom);
        seatCalibrated = true;
        return true;
    }

    void PinToTarget(Rect r)
    {
        if (!ComputeSeatDesktop(out float px, out float py)) return;
        int left = (int)r.x, right = (int)(r.x + r.width), top = (int)r.y;
        float desiredPX = left + snapFraction * Mathf.Max(1, right - left);
        float desiredPY = top + seatOffsetPx;
        int dx = Mathf.RoundToInt(desiredPX - px);
        int dy = Mathf.RoundToInt(desiredPY - py);

        X11Manager.Instance.GetWindowRect(unityHWND, out Rect ur);
        int w = (int)ur.width, h = (int)ur.height;
        int targetX = (int)ur.x + dx, targetY = (int)ur.y + dy;

        if (!_snapSmoothingActive || !enableSnapSmoothing)
        {
            if (dx != 0 || dy != 0) X11Manager.Instance.SetWindowPosition(targetX, targetY);
            return;
        }
        float dt = Time.unscaledDeltaTime;
        float nextX = Mathf.SmoothDamp(ur.x, targetX, ref _snapVelX, snapSmoothingTime, snapSmoothingMaxSpeed, dt);
        float nextY = Mathf.SmoothDamp(ur.y, targetY, ref _snapVelY, snapSmoothingTime, snapSmoothingMaxSpeed, dt);

        if (controller != null && controller.isDragging)
        {
            float predictedSeatY = py + (nextY - ur.y);
            float afterError = predictedSeatY - desiredPY;
            if (afterError > 0f)
            {
                float maxStep = snapSmoothingMaxSpeed * dt;
                float need = Mathf.Max(0f, afterError - 1f);
                nextY -= Mathf.Min(maxStep, need);
            }
        }

        int nx = Mathf.RoundToInt(nextX), ny = Mathf.RoundToInt(nextY);
        if (Mathf.Abs(targetX - nx) <= 1 && Mathf.Abs(targetY - ny) <= 1) { nx = targetX; ny = targetY; _snapSmoothingActive = false; _snapVelX = _snapVelY = 0f; }
        if (!Mathf.Approximately(nx, ur.x) || !Mathf.Approximately(ny, ur.y)) X11Manager.Instance.SetWindowPosition(nx, ny);
    }

    void SetTopMost(bool en) => X11Manager.Instance.SetTopmost(en);
    Vector2 GetUnityWindowPosition() { Vector2 r = X11Manager.Instance.GetWindowPosition(); return new(r.x, r.y); }
    int GetUnityWindowWidth() { Vector2 r = X11Manager.Instance.GetWindowSize(); return (int)r.x; }
    int GetUnityWindowHeight() { Vector2 r = X11Manager.Instance.GetWindowSize(); return (int)r.y; }
    void SetUnityWindowPosition(float x, float y) => X11Manager.Instance.SetWindowPosition(x, y);

    bool IsWindowFullscreen(IntPtr hwnd)
    {
        if (!X11Manager.Instance.GetWindowRect(hwnd, out Rect rect)) return false;

        float width = rect.width;
        float height = rect.height;
        int screenWidth = Display.main.systemWidth;
        int screenHeight = Display.main.systemHeight;
        int tolerance = 2; 
        return Mathf.Abs(width - screenWidth) <= tolerance && Mathf.Abs(height - screenHeight) <= tolerance;
    }

    void MoveMateToDesktopPosition()
    {
        int x = Mathf.RoundToInt(lastDesktopPosition.x);
        int y = Mathf.RoundToInt(lastDesktopPosition.y);
        SetUnityWindowPosition(x, y);
    }

    void DrawDesktopRect(Rect r, float basePixel)
    {
        float cx = r.x + r.width * 0.5f, cy = r.y + r.height * 0.5f;
        int screenWidth = Display.main.systemWidth, screenHeight = Display.main.systemHeight;
        float unityX = (cx - screenWidth * 0.5f) / basePixel, unityY = -(cy - screenHeight * 0.5f) / basePixel;
        Vector3 worldPos = new(unityX, unityY, 0), worldSize = new(r.width / basePixel, r.height / basePixel, 0);
        Gizmos.DrawWireCube(worldPos, worldSize);
    }

    public void ForceExitWindowSitting()
    {
        snappedHWND = IntPtr.Zero;
        if (animator != null)
        {
            animator.SetBool("isWindowSit", false);
            animator.SetBool("isSitting", false);
        }
        SetTopMost(true);
    }

    float ScaleFactor() => boneHips != null ? boneHips.lossyScale.magnitude : Mathf.Max(0.0001f, transform.lossyScale.magnitude);
    int ScaledGuardRadiusI() => Mathf.Max(1, Mathf.RoundToInt(probeGuardPx * ScaleFactor()));
    float ScaledGuardRadiusF() => probeGuardPx * ScaleFactor();
    bool ComputeSeatDesktop(out float px, out float py) => ComputeDesktopFromWorld(GetSeatWorldCurrent(), out px, out py);

    bool ComputeDesktopFromWorld(Vector3 wp, out float px, out float py)
    {
        px = py = 0f;
        if (targetCamera == null) return false;
        if (!GetUnityClientRect(out Rect uCli)) return false;
        _haveUnityCli = true; _lastUnityCli = uCli;
        Vector3 sp = targetCamera.WorldToScreenPoint(wp);
        if (sp.z < 0.01f) return false;
        float clientW = Mathf.Max(1f, uCli.width);
        float clientH = Mathf.Max(1f, uCli.height);
        px = uCli.x + Mathf.Clamp(sp.x, 0, targetCamera.pixelWidth) * (clientW / Mathf.Max(1, targetCamera.pixelWidth));
        py = uCli.y + (targetCamera.pixelHeight - Mathf.Clamp(sp.y, 0, targetCamera.pixelHeight)) * (clientH / Mathf.Max(1, targetCamera.pixelHeight));
        return true;
    }

    Vector3 GetSeatWorldCurrent()
    {
        if (!seatCalibrated) return GetHipWorld();
        float yFrac = Mathf.Clamp(seatNormY + windowSitYOffset, -0.5f, 1.5f);
        float yLocal = boundsMinSnapLocal.y + yFrac * boundsSizeSnapLocal.y;
        Vector3 localSeat = new Vector3(seatLocalAtSnap.x, yLocal, seatLocalAtSnap.z);
        return transform.localToWorldMatrix.MultiplyPoint3x4(localSeat);
    }

    Vector3 SeatWorldGuess()
    {
        if (animator != null && animator.isHuman)
        {
            Vector3 pelvis = boneHips != null ? boneHips.position : transform.position;
            Vector3 thighAvg = (boneLUL != null && boneRUL != null) ? (boneLUL.position + boneRUL.position) * 0.5f : pelvis;
            float headY = boneHead != null ? boneHead.position.y : pelvis.y + 0.5f;
            float footY = pelvis.y;
            if (boneLFoot != null) footY = boneLFoot.position.y;
            if (boneRFoot != null) footY = Mathf.Min(footY, boneRFoot.position.y);
            float h = Mathf.Max(0.1f, headY - footY);
            float down = Mathf.Clamp(h * 0.12f, 0.01f, h * 0.5f);
            return thighAvg + Vector3.down * down;
        }
        Bounds b = GetCombinedWorldBounds();
        return new Vector3(b.center.x, Mathf.Lerp(b.min.y, b.center.y, 0.2f), b.center.z);
    }

    Bounds GetCombinedWorldBounds()
    {
        Bounds b = new Bounds(transform.position, Vector3.zero);
        bool has = false;
        if (!_skinnedCached || skinned == null || skinned.Length == 0) { skinned = GetComponentsInChildren<SkinnedMeshRenderer>(true); _skinnedCached = true; }
        if (skinned != null)
        {
            for (int i = 0; i < skinned.Length; i++)
            {
                var s = skinned[i];
                if (s == null || !s.enabled) continue;
                if (!has) { b = s.bounds; has = true; } else b.Encapsulate(s.bounds);
            }
        }
        if (!has)
        {
            var rs = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                var r = rs[i];
                if (r == null || !r.enabled) continue;
                if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
            }
        }
        if (!has) b = new Bounds(transform.position, Vector3.one * 0.5f);
        return b;
    }

    Bounds WorldBoundsToRootLocal(Bounds wb)
    {
        Matrix4x4 inv = transform.worldToLocalMatrix;
        Vector3 min = wb.min, max = wb.max;
        Vector3[] c = new Vector3[8];
        c[0] = inv.MultiplyPoint3x4(new Vector3(min.x, min.y, min.z));
        c[1] = inv.MultiplyPoint3x4(new Vector3(max.x, min.y, min.z));
        c[2] = inv.MultiplyPoint3x4(new Vector3(min.x, max.y, min.z));
        c[3] = inv.MultiplyPoint3x4(new Vector3(min.x, min.y, max.z));
        c[4] = inv.MultiplyPoint3x4(new Vector3(max.x, max.y, min.z));
        c[5] = inv.MultiplyPoint3x4(new Vector3(max.x, min.y, max.z));
        c[6] = inv.MultiplyPoint3x4(new Vector3(min.x, max.y, max.z));
        c[7] = inv.MultiplyPoint3x4(new Vector3(max.x, max.y, max.z));
        Vector3 lmin = c[0], lmax = c[0];
        for (int i = 1; i < 8; i++) { lmin = Vector3.Min(lmin, c[i]); lmax = Vector3.Max(lmax, c[i]); }
        return new Bounds((lmin + lmax) * 0.5f, lmax - lmin);
    }

    float GetAutoTargetZ()
    {
        float s = Mathf.Max(0.0001f, transform.lossyScale.y);
        float z = targetZBase + (s - targetZRefScale) * targetZSensitivity;
        return Mathf.Clamp(z, targetZMin, targetZMax);
    }

    void EnsureOccluderRoot()
    {
        if (occluderRoot != null) return;
        var root = new GameObject("OccluderRoot");
        root.layer = targetCamera != null ? targetCamera.gameObject.layer : 0;
        root.transform.SetParent(targetCamera != null ? targetCamera.transform : null, false);
        occluderRoot = root.transform;
    }

    void EnsureTargetQuad()
    {
        if (targetQuadGO != null) return;
        targetQuadGO = new GameObject("TargetWindowQuad");
        targetQuadGO.layer = targetCamera.gameObject.layer;
        targetQuadGO.transform.SetParent(occluderRoot, false);
        var mf = targetQuadGO.AddComponent<MeshFilter>();
        var mr = targetQuadGO.AddComponent<MeshRenderer>();
        targetMesh = new Mesh(); targetMesh.MarkDynamic();
        mf.sharedMesh = targetMesh;
        mr.sharedMaterial = _occluderSharedMat;
        targetMesh.vertices = verts4;
        targetMesh.triangles = TRI;
        targetMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
        targetQuadGO.SetActive(false);
    }

    void SetTargetQuadActive(bool on) { if (targetQuadGO != null && targetQuadGO.activeSelf != on) targetQuadGO.SetActive(on); }

    void SetOtherQuadsActive(int activeCount)
    {
        for (int i = 0; i < otherQuadGOs.Count; i++)
        {
            bool on = i < activeCount;
            if (otherQuadGOs[i].activeSelf != on) otherQuadGOs[i].SetActive(on);
        }
    }

// Update WindowEntry struct
    private struct WindowEntry { public IntPtr hwnd; public Rect rect; public bool isTaskbar; public string className; } // Add className
}