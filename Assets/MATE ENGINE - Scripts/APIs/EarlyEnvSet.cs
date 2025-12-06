using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Gtk;
using Unity.Collections;
using UnityEngine;
using X11;
using Debug = UnityEngine.Debug;

public static class EarlyEnvSet
{
    [DllImport("libc")]
    private static extern IntPtr setenv(string name, string value, int overwrite);
    
    [DllImport(X11Manager.LibX11)]
    private static extern int XGetWindowAttributes(IntPtr display, IntPtr window, out X11Manager.XWindowAttributes attributes);
    
    [DllImport(X11Manager.LibXRender)]
    private static extern IntPtr XRenderFindVisualFormat(IntPtr display, IntPtr visual);
    
    [DllImport(X11Manager.LibX11)]
    private static extern IntPtr XOpenDisplay(string displayName);
    
    [DllImport(X11Manager.LibX11)]
    private static extern IntPtr XInternAtom(IntPtr display, string atomName, bool onlyIfExists);
    
    [DllImport(X11Manager.LibX11)]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);
    
    [DllImport(X11Manager.LibX11)]
    private static extern bool XTranslateCoordinates(IntPtr display, IntPtr srcW, IntPtr destW,
        int srcX, int srcY, out int destX, out int destY, out IntPtr child);
    
    [DllImport(X11Manager.LibX11)]
    private static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property,
        long longOffset, long longLength, bool delete, IntPtr reqType,
        out IntPtr actualTypeReturn, out int actualFormatReturn,
        out ulong nItemsReturn, out ulong bytesAfterReturn, out IntPtr propReturn);
    
    [DllImport(X11Manager.LibX11)]
    private static extern int XGetSelectionOwner(IntPtr display, IntPtr atom);
    
    [DllImport(X11Manager.LibX11)]
    private static extern int XScreenCount(IntPtr display);
    
    [DllImport(X11Manager.LibX11)]
    private static extern int XFree(IntPtr data);
    
    
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitBeforeAnything()
    {
        #if UNITY_EDITOR
        return;
        #endif
        setenv("GDK_BACKEND", "x11", 0);
        setenv("NO_AT_BRIDGE", "1", 0);
        string[] argc = { };
        if (!Gtk.Application.InitCheck(string.Empty, ref argc))
        {
            throw new Exception("Gtk initialization failed.");
        }
        var display = XOpenDisplay(null);
        if (display == IntPtr.Zero)
        {
            throw new Exception("Cannot open X11 display");
        }
        if (!CheckVisual(display, out var window))
        {
            return;
        }

        GtkX11Helper.Init(window);
        if (!IsCompositionSupported(display))
        {
            var dialog = new MessageDialog(GtkX11Helper.Instance.DummyParent, DialogFlags.DestroyWithParent, MessageType.Warning, ButtonsType.Ok, false, "Composition is unavailable for this window manager.");
            dialog.SecondaryText = "A compositor is required for MateEngine to show a transparent background.\n\nIf you are running MateEngine on WMs that don't compose (like Openbox), try installing a compositing manager (such as picom) and configure it correctly, or simply switch to Mutter (GNOME), Xfwm4 (Xfce4) and other WMs which natively supports composition.\n\nIf you are running KWin (KDE), please make sure \"Allow applications to block compositing\" is turned off in KDE System Settings (It's in the compositor section of Display and Monitor).";
            dialog.MessageType = MessageType.Warning;
            var image = new Image(new Gdk.Pixbuf(Resources.Load<Texture2D>("KWinHint").EncodeToPNG()));
            image.Halign = Align.Center;
            image.Show();
            dialog.ContentArea?.PackStart(image, false, false, 0);
            dialog.ShowAll();
            dialog.Response += (_, _) =>
            {
                dialog.Hide();
                Gtk.Application.Quit();
            };
            Gtk.Application.Run();
        }
    }
    
    private static bool IsCompositionSupported(IntPtr display)
    {
        for (int screen = 0; screen < XScreenCount(display); screen++)
        {
            IntPtr selectionAtom = XInternAtom(display, "_NET_WM_CM_S" + screen, false);
            if (selectionAtom == IntPtr.Zero)
                continue;
            return XGetSelectionOwner(display, selectionAtom) != 0;
        }
        return false;
    }

    static bool CheckVisual(IntPtr display, out IntPtr window)
    {
        int pid = Process.GetCurrentProcess().Id;
        List<IntPtr> windows = FindWindowsByPid(display, pid);
        
        IntPtr unityWindow = IntPtr.Zero;

        if (windows.Count > 0)
        {
            unityWindow = windows[0]; // Typically the first is the main window
        }
        window = unityWindow;
        if (XGetWindowAttributes(display, unityWindow, out X11Manager.XWindowAttributes attrs) == 0)
        {
            Debug.LogError("Failed to get window attributes");
            return false;
        }
        
        if (attrs.depth != 32 || !IsArgbVisual(display, attrs.visual))
        {
            var dialog = new MessageDialog(GtkX11Helper.Instance.DummyParent, DialogFlags.DestroyWithParent, MessageType.Warning, ButtonsType.Ok, false, "It looks like MateEngine is not running under an ARGB visual.");
            dialog.SecondaryText = "MateEngine must rely on a true transparent canvas (aka ARGB visual) to show a transparent background. Without it, your avatar will display on a big black background.\n\nTo acquire an ARGB visual, try launching MateEngine using the launch script (usually called launch.sh) provided in MateEngine executable path.";
            dialog.MessageType = MessageType.Warning;
            dialog.Show();
            dialog.Response += (_, _) =>
            {
                dialog.Hide();
                Gtk.Application.Quit();
            };
            Gtk.Application.Run();
            return false;
        }

        return true;
    }
    
    private static bool IsArgbVisual(IntPtr display, IntPtr visual)
    {
        IntPtr formatPtr = XRenderFindVisualFormat(display, visual);
        if (formatPtr == IntPtr.Zero) return false;

        X11Manager.XRenderPictFormat format = Marshal.PtrToStructure<X11Manager.XRenderPictFormat>(formatPtr);
        return format.type == 1 && format.direct.alphaMask != 0;
    }
    
    private static List<IntPtr> FindWindowsByPid(IntPtr display, int targetPid)
    {
        var result = new List<IntPtr>();
        var atom = XInternAtom(display, "_NET_WM_PID", false);

        if (atom == IntPtr.Zero)
        {
            Debug.Log("_NET_WM_PID atom not found");
            return result;
        }

        var windows = GetAllVisibleWindows(display, XDefaultRootWindow(display));

        foreach (var window in windows)
        {
            int pid = GetWindowPid(display, window, atom);
            if (pid == targetPid)
            {
                result.Add(window);
            }
        }

        return result;
    }
    
    private static int GetWindowPid(IntPtr display, IntPtr window, IntPtr pidAtom)
    {
        int status = XGetWindowProperty(display, window, pidAtom,
            0, 1, false, (IntPtr)6,
            out _, out _,
            out var nItems, out _, out var prop);

        if (status == 0 && prop != IntPtr.Zero && nItems > 0)
        {
            int pid = Marshal.ReadInt32(prop);
            XFree(prop);
            return pid;
        }

        return -1;
    }
    
    private static bool IsWindowVisible(IntPtr display, IntPtr rootWindow, IntPtr window)
    {
        if (display == IntPtr.Zero) return false;

        int result = XGetWindowAttributes(display, window, out X11Manager.XWindowAttributes attr);
        if (result == 0 || attr.map_state != 2) return false;
    
        if (!XTranslateCoordinates(display, window, rootWindow, 0, 0, out int absX, out int absY, out _))
            return false;

        float targetX1 = absX;
        float targetY1 = absY;
        float targetX2 = absX + attr.width;
        float targetY2 = absY + attr.height;
        float targetArea = (float)attr.width * attr.height;
    
        List<IntPtr> stacking = GetClientStackingList(display, rootWindow);
        int index = stacking.IndexOf(window);
        if (index < 0) return false;
    
        List<(float x1, float y1, float x2, float y2)> coversList = new();
        for (int i = index + 1; i < stacking.Count; i++)
        {
            IntPtr ow = stacking[i];
            XGetWindowAttributes(display, ow, out X11Manager.XWindowAttributes oattr);
            if (oattr.map_state != 2) continue;

            if (!XTranslateCoordinates(display, ow, rootWindow, 0, 0, out int oabsX, out int oabsY, out _))
                continue;

            float ox1 = oabsX;
            float oy1 = oabsY;
            float ox2 = oabsX + oattr.width;
            float oy2 = oabsY + oattr.height;
        
            float ix1 = Math.Max(targetX1, ox1);
            float iy1 = Math.Max(targetY1, oy1);
            float ix2 = Math.Min(targetX2, ox2);
            float iy2 = Math.Min(targetY2, oy2);
            if (ix1 < ix2 && iy1 < iy2)
            {
                coversList.Add((ix1, iy1, ix2, iy2));
            }
        }

        if (coversList.Count == 0) return true;
    
        // Burst-optimized union area computation
        NativeArray<X11Manager.RectF> covers = new NativeArray<X11Manager.RectF>(coversList.Count, Allocator.Temp);
        for (int i = 0; i < coversList.Count; i++)
        {
            var t = coversList[i];
            covers[i] = new X11Manager.RectF { x1 = t.x1, y1 = t.y1, x2 = t.x2, y2 = t.y2 };
        }
        float coveredArea = X11Manager.UnionAreaCalculator.Compute(covers);
        covers.Dispose();
    
        return coveredArea < targetArea - 1e-4f;
    }
    
    private static List<IntPtr> GetClientStackingList(IntPtr display, IntPtr rootWindow)
    {
        IntPtr atom = XInternAtom(display, "_NET_CLIENT_LIST_STACKING", false);
        if (atom == IntPtr.Zero) return new List<IntPtr>();

        int status = XGetWindowProperty(display, rootWindow, atom, 0, 1024, false, (IntPtr)33,
            out _, out int actualFormat, out ulong nItems, out _, out IntPtr prop);

        if (status != 0 || prop == IntPtr.Zero || nItems == 0 || actualFormat != 32)
        {
            if (prop != IntPtr.Zero) XFree(prop);
            return new List<IntPtr>();
        }

        List<IntPtr> windows = new List<IntPtr>((int)nItems);
        for (int i = 0; i < (int)nItems; i++)
        {
            IntPtr w = Marshal.ReadIntPtr(prop, (i * IntPtr.Size));
            windows.Add(w);
        }
        XFree(prop);
        return windows;
    }
    
    private static List<IntPtr> GetAllVisibleWindows(IntPtr display, IntPtr rootWindow)
    {
        var result = new List<IntPtr>();

        IntPtr clientListAtom = XInternAtom(display, "_NET_CLIENT_LIST", true);
        if (clientListAtom != IntPtr.Zero)
        {
            int status = XGetWindowProperty(display, rootWindow, clientListAtom, 0, 1024, false, (IntPtr)33,
                out IntPtr actualType, out int actualFormat, out ulong nItems, out _, out IntPtr prop);

            if (status == 0 && actualType == (IntPtr)33 && actualFormat == 32 && prop != IntPtr.Zero)
            {
                for (ulong i = 0; i < nItems; i++)
                {
                    IntPtr win = Marshal.ReadIntPtr(prop, (int)(i * (ulong)IntPtr.Size));
                    if (IsWindowVisible(display, rootWindow, win))
                    {
                        result.Add(win);
                    }
                }
                XFree(prop);
                return result;
            }
            if (prop != IntPtr.Zero) XFree(prop);
        }
        return result;
    }
}
