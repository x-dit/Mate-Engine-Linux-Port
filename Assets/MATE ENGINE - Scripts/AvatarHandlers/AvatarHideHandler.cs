using UnityEngine;
using System;
using System.Linq;
using X11;

public class AvatarHideHandler : MonoBehaviour
{
    public int snapThresholdPx = 12;
    public int unsnapThresholdPx = 24;
    public int edgeInsetPx = 0;
    public bool enableSmoothing = true;
    [Range(0.01f, 0.5f)] public float smoothingTime = 0.10f;
    public float smoothingMaxSpeed = 6000f;
    public bool keepTopmostWhileSnapped = true;
    public float unsnapGraceTime = 0.12f;

    Animator animator;
    AvatarAnimatorController controller;
    IntPtr unityHWND;

    Transform leftHand;
    Transform rightHand;
    Camera cam;

    enum Side { None, Left, Right }
    Side snappedSide = Side.None;

    int cursorOffsetY;
    int windowW, windowH;
    float velX, velY;
    bool smoothingActive;
    bool wasDragging;
    float snappedAt;

    void Start()
    {
        unityHWND = X11Manager.Instance.UnityWindow;
        animator = GetComponent<Animator>();
        controller = GetComponent<AvatarAnimatorController>();
        if (animator != null && animator.isHuman && animator.avatar != null)
        {
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        }
        cam = Camera.main;
        if (cam == null) cam = FindObjectOfType<Camera>();
    }

    void OnDisable()
    {
        SetHide(false, false);
        snappedSide = Side.None;
    }

    void Update()
    {
        if (unityHWND == IntPtr.Zero || animator == null || controller == null) return;

        if (controller.isDragging && !wasDragging)
    {
        if (X11Manager.Instance.GetWindowRect(unityHWND, out Rect wr))
        {
            windowW = Mathf.Max(1, (int)wr.width);
            windowH = Mathf.Max(1, (int)wr.height);
            Vector2 cp = X11Manager.Instance.GetMousePosition();
            cursorOffsetY = (int)(cp.y - wr.y);
            smoothingActive = false;
            velX = velY = 0f;
        }
    }

    if (controller.isDragging)
    {
        Vector2 cp = X11Manager.Instance.GetMousePosition();
        if (cp == Vector2.zero) { wasDragging = controller.isDragging; return; }  // Fallback for query failure
        if (!X11Manager.Instance.GetWindowRect(unityHWND, out Rect wrCur)) { wasDragging = controller.isDragging; return; }
        Rect mon = GetCurrentMonitorRect(cp);  // See Step 5 for implementation

        int anchorLeftDesk = GetAnchorDesktopX(Side.Left);
        int anchorRightDesk = GetAnchorDesktopX(Side.Right);
        if (anchorLeftDesk < 0) anchorLeftDesk = (int)wrCur.x + Mathf.Max(1, windowW / 2);
        if (anchorRightDesk < 0) anchorRightDesk = (int)wrCur.x + Mathf.Max(1, windowW / 2);

        bool nearLeft = anchorLeftDesk - (int)mon.x <= Mathf.Max(1, snapThresholdPx);
        bool nearRight = ((int)mon.x + (int)mon.width) - anchorRightDesk <= Mathf.Max(1, snapThresholdPx);

        if (snappedSide == Side.None)
        {
            if (nearLeft) SnapTo(Side.Left, cp, mon);
            else if (nearRight) SnapTo(Side.Right, cp, mon);
        }
        else
        {
            if (Time.unscaledTime >= snappedAt + unsnapGraceTime)
            {
                if (snappedSide == Side.Left && (cp.x - (int)mon.x) > Mathf.Max(1, unsnapThresholdPx)) Unsnap();
                else if (snappedSide == Side.Right && ((int)mon.x + (int)mon.width - cp.x) > Mathf.Max(1, unsnapThresholdPx)) Unsnap();
            }
        }

        if (snappedSide != Side.None)
        {
            if (!X11Manager.Instance.GetWindowRect(unityHWND, out Rect wr2)) { wasDragging = controller.isDragging; return; }
            Rect monNow = GetCurrentMonitorRect(cp);

            int anchorDesk = GetAnchorDesktopX(snappedSide);
            if (anchorDesk < 0) anchorDesk = (int)(wr2.x + Mathf.Max(1, (wr2.width) / 2));
            int anchorWinX = Mathf.Clamp(anchorDesk - (int)wr2.x, 0, Mathf.Max(1, (int)wr2.width));

            int desiredAnchorDesk = snappedSide == Side.Left ? (int)monNow.x + edgeInsetPx : (int)(monNow.x + monNow.width) - edgeInsetPx;
            int tx = desiredAnchorDesk - anchorWinX;

            int ty = (int)cp.y - cursorOffsetY;

            MoveSmooth((int)wr2.x, (int)wr2.y, tx, ty, windowW, windowH);
            if (keepTopmostWhileSnapped) X11Manager.Instance.SetTopmost(true);
        }
    }
    else
    {
        if (snappedSide != Side.None)
        {
            if (!X11Manager.Instance.GetWindowRect(unityHWND, out Rect wr)) return;
            Rect mon = GetMonitorFromWindow();  // See Step 5 for implementation

            int anchorDesk = GetAnchorDesktopX(snappedSide);
            if (anchorDesk < 0) anchorDesk = (int)(wr.x + Mathf.Max(1, (wr.width) / 2));
            int anchorWinX = Mathf.Clamp(anchorDesk - (int)wr.x, 0, Mathf.Max(1, (int)wr.width));

            int desiredAnchorDesk = snappedSide == Side.Left ? (int)mon.x + edgeInsetPx : (int)(mon.x + mon.width) - edgeInsetPx;
            int tx = desiredAnchorDesk - anchorWinX;

            int ty = (int)wr.y;

            MoveSmooth((int)wr.x, (int)wr.y, tx, ty, windowW, windowH);
            if (keepTopmostWhileSnapped) X11Manager.Instance.SetTopmost(true);
        }
    }

    wasDragging = controller.isDragging;
    }
    
    int GetAnchorDesktopX(Side side)
    {
        Transform t = side == Side.Left ? leftHand : rightHand;
        if (t == null || cam == null) return -1;
        if (!GetUnityClientRect(out Rect uCli)) return -1;

        Vector3 sp = cam.WorldToScreenPoint(t.position);
        if (sp.z < 0.01f) return -1;

        float clientW = Mathf.Max(1f, uCli.width);
        float pxW = Mathf.Max(1, cam.pixelWidth);
        float sx = Mathf.Clamp(sp.x, 0, cam.pixelWidth) * (clientW / pxW);
        int desktopX = (int)uCli.x + Mathf.RoundToInt(sx);
        return desktopX;
    }

    void SnapTo(Side side, Vector2 cp, Rect mon)
    {
        if (!X11Manager.Instance.GetWindowRect(unityHWND, out Rect wr)) return;

        windowW = (int)Math.Max(1, wr.width);
        windowH = (int)Math.Max(1, wr.height);
        cursorOffsetY = (int)(cp.y - wr.y);
        snappedSide = side;
        SetHide(side == Side.Left, side == Side.Right);

        int anchorDesk = GetAnchorDesktopX(side);
        if (anchorDesk < 0) anchorDesk = (int)(wr.x + Math.Max(1, (wr.width) / 2));
        int anchorWinX = (int)Mathf.Clamp(anchorDesk - wr.x, 0, Math.Max(1, wr.width));

        float desiredAnchorDesk = side == Side.Left ? mon.x + edgeInsetPx : mon.x + mon.width - edgeInsetPx;
        float tx = desiredAnchorDesk - anchorWinX;

        int ty = (int)cp.y - cursorOffsetY;

        X11Manager.Instance.SetWindowPosition(new Vector2(tx, ty));
        smoothingActive = enableSmoothing;
        velX = velY = 0f;
        snappedAt = Time.unscaledTime;
        if (keepTopmostWhileSnapped) SetTopMost(true);
    }

    void Unsnap()
    {
        snappedSide = Side.None;
        SetHide(false, false);
        smoothingActive = false;
        velX = velY = 0f;
        SetTopMost(false);
    }

    void SetHide(bool left, bool right)
    {
        animator.SetBool("HideLeft", left);
        animator.SetBool("HideRight", right);
    }

    void MoveSmooth(int curX, int curY, int targetX, int targetY, int w, int h)
    {
        if (!enableSmoothing || !smoothingActive)
        {
            if (curX != targetX || curY != targetY) X11Manager.Instance.SetWindowPosition(new Vector2(targetX, targetY));
            return;
        }
        float dt = Time.unscaledDeltaTime;
        float nx = Mathf.SmoothDamp(curX, targetX, ref velX, smoothingTime, smoothingMaxSpeed, dt);
        float ny = Mathf.SmoothDamp(curY, targetY, ref velY, smoothingTime, smoothingMaxSpeed, dt);
        int ix = Mathf.RoundToInt(nx);
        int iy = Mathf.RoundToInt(ny);
        if (Mathf.Abs(targetX - ix) <= 1 && Mathf.Abs(targetY - iy) <= 1)
        {
            ix = targetX; iy = targetY; smoothingActive = false; velX = velY = 0f;
        }
        if (ix != curX || iy != curY) X11Manager.Instance.SetWindowPosition(new Vector2(targetX, targetY));
    }

    private Rect GetCurrentMonitorRect(Vector2 cp)
    {
        return X11Manager.Instance.GetMonitorRectFromPoint(cp);
    }

    private Rect GetMonitorFromWindow()  // Note: hwnd unused, but for compatibility
    {
        return X11Manager.Instance.GetMonitorRectFromWindow(unityHWND);
    }

    private Rect GetVirtualScreenRect()
    {
        // Compute union of all monitors for virtual bounds
        if (X11Manager.Instance.GetAllMonitors().Count == 0) return new Rect();
        var mons = X11Manager.Instance.GetAllMonitors();
        float minX = mons.Min(m => m.x), minY = mons.Min(m => m.y);
        float maxX = mons.Max(m => m.x + m.width), maxY = mons.Max(m => m.y + m.height);
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    bool GetUnityClientRect(out Rect r)
    {
        r = new Rect();
        return X11Manager.Instance.GetWindowRect(unityHWND, out r);
    }

    void SetTopMost(bool on) => X11Manager.Instance.SetTopmost(on);
}