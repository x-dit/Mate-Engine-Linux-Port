using Gtk;
using UnityEngine;
using Application = Gtk.Application;

public class LinuxSpecificSettings : MonoBehaviour
{
    private Window window;

    public GameObject background;
    
    public Texture2D icon;
    
    private bool useXMoveWindow;
    private bool enableAutoMemoryTrim;

    private Rect windowRect;
    private bool showWindow;

    private bool inEditor;
    public void Start()
    {
        #if UNITY_EDITOR
        inEditor = true;
        return;
        #endif
        window = new Window(UnityEngine.Application.productName)
        {
            Resizable = false,
            WindowPosition = WindowPosition.Center,
            TransientFor = GtkX11Helper.Instance.DummyParent
        };
        window.SetDefaultSize(660, 520);
        window.Destroyed += (s, e) =>
        {
            ShowWindow(false);
        };
        
        SetupGtkWindow(window);
    }

    public void ShowWindow(bool show = true)
    {
        showWindow = show;
        if (inEditor || SaveLoadHandler.Instance.safeMode)
        {
            background.SetActive(true);
            if (!show)
            {
                background.SetActive(false);
            }
            return;
        }
        if (show)
        {
            WindowManager.Instance.SetTopmost(false);
            window.ShowAll();
            Application.Run();
            return;
        }
        WindowManager.Instance.SetTopmost(SaveLoadHandler.Instance.data.isTopmost);
        window.Hide();
        Application.Quit();
    }

    void SetupGtkWindow(Window window)
    {
        var mainBox = new Box(Orientation.Horizontal, 0);
        window.Add(mainBox);

        var iconBox = new Box(Orientation.Vertical, 0);
        mainBox.PackStart(iconBox, false, false, 0);

        var image = new Image(new Gdk.Pixbuf(icon.EncodeToPNG()));
        iconBox.PackStart(image, true, true, 40);
        
        var contentBox = new Box(Orientation.Vertical, 28)
        {
            MarginEnd = 50
        };
        mainBox.PackStart(contentBox, true, true, 0);
        
        var title = new Label(null)
        {
            Markup = "<span size=\"x-large\" weight=\"bold\">Linux-Specific Settings</span>"
        };
        title.Xalign = 0.0f;
        title.Yalign = 0.5f;
        contentBox.PackStart(title, false, false, 0);
        
        var card = new Frame { Name = "card", ShadowType = ShadowType.None };
        var cardBox = new Box(Orientation.Vertical, 20) { BorderWidth = 24 };
        card.Add(cardBox);
        contentBox.PackStart(card, true, true, 0);
        
        var intro = new Label("These options are only provided for Linux, and they could act differently on different distros.")
        {
            LineWrap = true
        };
        intro.Xalign = 0.0f;
        intro.Yalign = 0.5f;
        cardBox.PackStart(intro, false, false, 0);
        
        var check1 = new CheckButton("Use XMoveWindow instead of _NET_MOVERESIZE_WINDOW") {Active = SaveLoadHandler.Instance.data.useXMoveWindow, UseUnderline = false};
        ((Label)check1.Child).Xalign = 0.0f;
        ((Label)check1.Child).Yalign = 0.5f;
        cardBox.PackStart(check1, false, false, 0);

        var desc1 = CreateDescriptionLabel("XMoveWindow bypasses WM and updates the window’s origin directly, which can leave window decorations out of sync. _NET_MOVERESIZE_WINDOW protocol sends a message to ask WM to move MateEngine window as a complete, decorated unit on every modern Linux desktop while respecting the user’s compositor animations and tiling rules.\n\nOnly use XMoveWindow in case that you cannot drag and move your avatar at all.");
        cardBox.PackStart(desc1, false, false, 0);
        
        var check2 = new CheckButton("Enable Periodic Memory Optimization") {Active = SaveLoadHandler.Instance.data.enableAutoMemoryTrim, UseUnderline = false};
        ((Label)check2.Child).Xalign = 0.0f;
        ((Label)check2.Child).Yalign = 0.5f;
        cardBox.PackStart(check2, false, false, 0);

        var desc2 = CreateDescriptionLabel("This feature reduces MateEngine's physical RAM usage by releasing inactive memory pages and returning freed heap memory to the system. It proactively frees RAM for better multitasking on low-memory devices.\n\nNote: To safely preserve modified data, some pages may be temporarily written to swap space (virtual memory on disk). This can cause a short-term increase in swap usage, but the data reloads quickly if needed. Minor hitches may occur if frequently accessed data is reloaded.\n\nRecommended for systems with 16GB RAM or less. Require at least 1 GiB swap space.");
        cardBox.PackStart(desc2, false, false, 0);
        
        var buttonBox = new Box(Orientation.Horizontal, 20) { Halign = Align.End };
        contentBox.PackEnd(buttonBox, false, false, 0);

        var backBtn = new Button("Cancel");
        var continueBtn = new Button("Save");
        continueBtn.StyleContext.AddClass("suggested-action");

        backBtn.Clicked += (_, _) =>
        {
            ShowWindow(false);
        };
        continueBtn.Clicked += (_, _) =>
        {
            ShowWindow(false);
            SaveLoadHandler.Instance.data.useXMoveWindow = check1.Active;
            SaveLoadHandler.Instance.data.enableAutoMemoryTrim = check2.Active;
            FindFirstObjectByType<SettingsHandlerToggles>().ApplySettings();
            SaveLoadHandler.Instance.SaveToDisk();
        };

        buttonBox.PackStart(backBtn, false, false, 0);
        buttonBox.PackStart(continueBtn, false, false, 0);

        // CSS 样式
        var cssProvider = new CssProvider();
        cssProvider.LoadFromData(@"
            #card {
                background: rgba(255, 255, 255, 0.20);
                border: 1px solid;
                margin-bottom: 20px;
            }
            .description-label {
                font-size: 9pt;
                opacity: 0.85;
            }
        ");

        StyleContext.AddProviderForScreen(
            Gdk.Screen.Default,
            cssProvider,
            600 // StyleProviderPriority.Application
        );
    }

    static Label CreateDescriptionLabel(string text)
    {
        var label = new Label(text)
        {
            LineWrap = true,
            MaxWidthChars = 60,
            LineWrapMode = Pango.WrapMode.WordChar,
            UseUnderline = false
        };
        label.Xalign = 0.0f;
        label.Yalign = 0.5f;
        label.StyleContext.AddClass("description-label");
        return label;
    }
    
    private void OnGUI()
    {
        if (SaveLoadHandler.Instance.safeMode || inEditor)
        {
            if (!showWindow)
                return;
        }
        else
        {
            return;
        }
        
        if (windowRect.width == 0) // windowRect defaults to (0,0,0,0) initially
        {
            var windowWidth = 660f;
            var windowHeight = 420f;
            windowRect = new Rect(0, 0, windowWidth, windowHeight);
            // Center it
            windowRect.center = new Vector2(Screen.width / 2f, Screen.height / 2f);
        }

        windowRect = GUILayout.Window(GetHashCode(), windowRect, DrawWindow, "Linux-Specific Settings");
        
        // Optional: Clamp to screen edges to prevent dragging completely off-screen
        windowRect.x = Mathf.Clamp(windowRect.x, 0, Screen.width - windowRect.width);
        windowRect.y = Mathf.Clamp(windowRect.y, 0, Screen.height - windowRect.height);
    }

    private void DrawWindow(int windowId)
    {
        // Main horizontal layout: icon | content
        GUILayout.BeginHorizontal();

        // Left icon area
        GUILayout.BeginVertical(GUILayout.Width(100f)); // Approximate width for icon
        GUILayout.Space(40f); // Padding top
        if (icon != null)
        {
            GUILayout.Label(icon, GUILayout.Width(80f), GUILayout.Height(80f)); // Adjust size as needed
        }
        GUILayout.EndVertical();
        
        GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        
        GUILayout.Label("These options are only provided for Linux, and they could act differently on different distros.");

        GUILayout.Space(20f);
        
        useXMoveWindow = GUILayout.Toggle(useXMoveWindow, "Use XMoveWindow instead of _NET_MOVERESIZE_WINDOW");
        GUILayout.Label("XMoveWindow bypasses WM and updates the window’s origin directly, which can leave window decorations out of sync. _NET_MOVERESIZE_WINDOW protocol sends a message to ask WM to move MateEngine window as a complete, decorated unit on every modern Linux desktop while respecting the user’s compositor animations and tiling rules.\n\nOnly use XMoveWindow in case that you cannot drag and move your avatar at all.");

        GUILayout.Space(10f);
        
        enableAutoMemoryTrim = GUILayout.Toggle(enableAutoMemoryTrim, "Enable Periodic Memory Optimization");
        GUILayout.Label("This feature reduces the game's physical RAM usage by releasing inactive memory pages and returning freed heap memory to the system. It proactively frees RAM for better multitasking on low-memory devices.\n\nNote: To safely preserve modified data, some pages may be temporarily written to swap space (virtual memory on disk). This can cause a short-term increase in swap usage, but the data reloads quickly if needed. Minor hitches may occur if frequently accessed data is reloaded.\n\nRecommended for systems with 16GB RAM or less.");

        GUILayout.FlexibleSpace();
        
        GUILayout.BeginHorizontal(GUILayout.ExpandWidth(false));
        if (GUILayout.Button("Cancel"))
        {
            ShowWindow(false);
        }
        if (GUILayout.Button("OK"))
        {
            SaveLoadHandler.Instance.data.useXMoveWindow = useXMoveWindow;
            SaveLoadHandler.Instance.data.enableAutoMemoryTrim = enableAutoMemoryTrim;
            FindFirstObjectByType<SettingsHandlerToggles>().ApplySettings();
            SaveLoadHandler.Instance.SaveToDisk();
            ShowWindow(false);
        }
        GUILayout.EndHorizontal();
        
        GUILayout.EndHorizontal();

        GUILayout.EndVertical();

        GUI.DragWindow();
    }
}