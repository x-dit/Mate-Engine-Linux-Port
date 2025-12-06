using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace KWinTool
{
    public class WindowInfo
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string ClassName { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Screen { get; set; }
        public int Pid { get; set; }
    }

    public class KWinWindowManager
    {
        private readonly bool _kde5;
        
        public KWinWindowManager()
        {
            // Check KDE version
            var kdeSessionVersion = Environment.GetEnvironmentVariable("KDE_SESSION_VERSION");
            _kde5 = kdeSessionVersion == "5";
        }

        public List<WindowInfo> GetWindowList()
        {
            var script = _kde5 ? GetKDE5WindowListScript() : GetKDE6WindowListScript();
            var result = ExecuteKWinScript(script);
            return ParseWindowList(result);
        }

        public WindowInfo GetActiveWindow()
        {
            var script = _kde5 ? GetKDE5ActiveWindowScript() : GetKDE6ActiveWindowScript();
            var result = ExecuteKWinScript(script);
            return ParseWindowInfo(result);
        }

        public List<WindowInfo> SearchWindows(string searchTerm, bool matchClass = true, bool matchName = true, 
            bool matchClassName = true, bool matchRole = true, bool matchId = false, int limit = 0)
        {
            var script = _kde5 ? 
                GetKDE5SearchScript(searchTerm, matchClass, matchName, matchClassName, matchRole, matchId, limit) :
                GetKDE6SearchScript(searchTerm, matchClass, matchName, matchClassName, matchRole, matchId, limit);
            
            var result = ExecuteKWinScript(script);
            return ParseWindowList(result);
        }

        private string ExecuteKWinScript(string script)
        {
            var tempFile = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllText(tempFile, script);
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "qdbus",
                        Arguments = $"org.kde.KWin /Scripting org.kde.kwin.Scripting.loadScript \"{tempFile}\" \"temp_script\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                var scriptId = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                
                if (int.TryParse(scriptId, out var id) && id >= 0)
                {
                    // Run the script
                    var runProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "qdbus",
                            Arguments = $"org.kde.KWin /{id} org.kde.kwin.Script.run",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    
                    runProcess.Start();
                    var output = runProcess.StandardOutput.ReadToEnd();
                    var error = runProcess.StandardError.ReadToEnd();
                    runProcess.WaitForExit();
                    
                    // Stop and unload the script
                    var stopProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "qdbus",
                            Arguments = $"org.kde.KWin /{id} org.kde.kwin.Script.stop",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    stopProcess.Start();
                    stopProcess.WaitForExit();
                    
                    var unloadProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "qdbus",
                            Arguments = "org.kde.KWin /Scripting org.kde.kwin.Scripting.unloadScript \"temp_script\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    unloadProcess.Start();
                    unloadProcess.WaitForExit();
                    
                    return output;
                }
                
                throw new Exception($"Failed to load script: {scriptId}");
            }
            finally
            {
                if (System.IO.File.Exists(tempFile))
                {
                    System.IO.File.Delete(tempFile);
                }
            }
        }

        private List<WindowInfo> ParseWindowList(string output)
        {
            var windows = new List<WindowInfo>();
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            var currentWindow = new WindowInfo();
            foreach (var line in lines)
            {
                if (line.StartsWith("Window "))
                {
                    if (currentWindow.Id != 0)
                    {
                        windows.Add(currentWindow);
                    }
                    
                    var idMatch = Regex.Match(line, @"Window (\d+)");
                    if (idMatch.Success)
                    {
                        currentWindow = new WindowInfo
                        {
                            Id = int.Parse(idMatch.Groups[1].Value)
                        };
                    }
                }
                else if (line.Contains("Position:"))
                {
                    var positionMatch = Regex.Match(line, @"Position: (\d+),(\d+)");
                    if (positionMatch.Success)
                    {
                        currentWindow.X = int.Parse(positionMatch.Groups[1].Value);
                        currentWindow.Y = int.Parse(positionMatch.Groups[2].Value);
                        
                        var screenMatch = Regex.Match(line, @"screen: (\d+)");
                        if (screenMatch.Success)
                        {
                            currentWindow.Screen = int.Parse(screenMatch.Groups[1].Value);
                        }
                    }
                }
                else if (line.Contains("Geometry:"))
                {
                    var geometryMatch = Regex.Match(line, @"Geometry: (\d+)x(\d+)");
                    if (geometryMatch.Success)
                    {
                        currentWindow.Width = int.Parse(geometryMatch.Groups[1].Value);
                        currentWindow.Height = int.Parse(geometryMatch.Groups[2].Value);
                    }
                }
                else if (line.Contains("Name:"))
                {
                    currentWindow.Name = line.Substring("Name:".Length).Trim();
                }
                else if (line.Contains("Class:"))
                {
                    currentWindow.ClassName = line.Substring("Class:".Length).Trim();
                }
                else if (line.Contains("PID:"))
                {
                    if (int.TryParse(line.Substring("PID:".Length).Trim(), out var pid))
                    {
                        currentWindow.Pid = pid;
                    }
                }
            }
            
            if (currentWindow.Id != 0)
            {
                windows.Add(currentWindow);
            }
            
            return windows;
        }

        private WindowInfo ParseWindowInfo(string output)
        {
            var windows = ParseWindowList(output);
            return windows.Count > 0 ? windows[0] : null;
        }

        #region KDE5 Scripts
        private string GetKDE5WindowListScript()
        {
            return @"
var windows = [];
var clients = workspace.clientList();
for (var i = 0; i < clients.length; i++) {
    var w = clients[i];
    print('Window ' + w.internalId);
    print('  Position: ' + w.x + ',' + w.y + ' (screen: ' + w.screen + ')');
    print('  Geometry: ' + w.width + 'x' + w.height);
    print('  Name: ' + w.caption);
    print('  Class: ' + w.resourceClass);
    print('  PID: ' + w.pid);
}
";
        }

        private string GetKDE5ActiveWindowScript()
        {
            return @"
var w = workspace.activeClient;
print('Window ' + w.internalId);
print('  Position: ' + w.x + ',' + w.y + ' (screen: ' + w.screen + ')');
print('  Geometry: ' + w.width + 'x' + w.height);
print('  Name: ' + w.caption);
print('  Class: ' + w.resourceClass);
print('  PID: ' + w.pid);
";
        }

        private string GetKDE5SearchScript(string searchTerm, bool matchClass, bool matchName, bool matchClassName, bool matchRole, bool matchId, int limit)
        {
            var conditions = new List<string>();
            if (matchClass) conditions.Add("w.resourceClass.search(re) >= 0");
            if (matchName) conditions.Add("w.caption.search(re) >= 0");
            if (matchClassName) conditions.Add("w.resourceName.search(re) >= 0");
            if (matchRole) conditions.Add("w.windowRole.search(re) >= 0");
            if (matchId) conditions.Add("w.internalId.toString().search(re) >= 0");
            
            var condition = string.Join(" || ", conditions);
            
            return $@"
var re = new RegExp('{Regex.Escape(searchTerm)}', 'i');
var windows = [];
var clients = workspace.clientList();
var count = 0;
for (var i = 0; i < clients.length; i++) {{
    var w = clients[i];
    if ({condition}) {{
        print('Window ' + w.internalId);
        print('  Position: ' + w.x + ',' + w.y + ' (screen: ' + w.screen + ')');
        print('  Geometry: ' + w.width + 'x' + w.height);
        print('  Name: ' + w.caption);
        print('  Class: ' + w.resourceClass);
        print('  PID: ' + w.pid);
        count++;
        if ({limit} > 0 && count >= {limit}) break;
    }}
}}
";
        }
        #endregion

        #region KDE6 Scripts
        private string GetKDE6WindowListScript()
        {
            return @"
var windows = [];
var clients = workspace.windowList();
for (var i = 0; i < clients.length; i++) {
    var w = clients[i];
    print('Window ' + w.internalId);
    print('  Position: ' + w.x + ',' + w.y);
    print('  Geometry: ' + w.width + 'x' + w.height);
    print('  Name: ' + w.caption);
    print('  Class: ' + w.resourceClass);
    print('  PID: ' + w.pid);
}
";
        }

        private string GetKDE6ActiveWindowScript()
        {
            return @"
var w = workspace.activeWindow;
print('Window ' + w.internalId);
print('  Position: ' + w.x + ',' + w.y);
print('  Geometry: ' + w.width + 'x' + w.height);
print('  Name: ' + w.caption);
print('  Class: ' + w.resourceClass);
print('  PID: ' + w.pid);
";
        }

        private string GetKDE6SearchScript(string searchTerm, bool matchClass, bool matchName, bool matchClassName, bool matchRole, bool matchId, int limit)
        {
            var conditions = new List<string>();
            if (matchClass) conditions.Add("w.resourceClass.search(re) >= 0");
            if (matchName) conditions.Add("w.caption.search(re) >= 0");
            if (matchClassName) conditions.Add("w.resourceName.search(re) >= 0");
            if (matchRole) conditions.Add("w.windowRole.search(re) >= 0");
            if (matchId) conditions.Add("w.internalId.toString().search(re) >= 0");
            
            var condition = string.Join(" || ", conditions);
            
            return $@"
var re = new RegExp('{Regex.Escape(searchTerm)}', 'i');
var windows = [];
var clients = workspace.windowList();
var count = 0;
for (var i = 0; i < clients.length; i++) {{
    var w = clients[i];
    if ({condition}) {{
        print('Window ' + w.internalId);
        print('  Position: ' + w.x + ',' + w.y);
        print('  Geometry: ' + w.width + 'x' + w.height);
        print('  Name: ' + w.caption);
        print('  Class: ' + w.resourceClass);
        print('  PID: ' + w.pid);
        count++;
        if ({limit} > 0 && count >= {limit}) break;
    }}
}}
";
        }
        #endregion
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            var manager = new KWinWindowManager();
            
            if (args.Length == 0)
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("  WindowGeometry list                    - List all windows");
                Console.WriteLine("  WindowGeometry active                  - Get active window");
                Console.WriteLine("  WindowGeometry search <term>           - Search windows by term");
                Console.WriteLine("  WindowGeometry search <term> --class   - Search by class name");
                Console.WriteLine("  WindowGeometry search <term> --name    - Search by window title");
                return;
            }

            try
            {
                switch (args[0].ToLower())
                {
                    case "list":
                        var windows = manager.GetWindowList();
                        foreach (var window in windows)
                        {
                            PrintWindowInfo(window);
                        }
                        break;
                        
                    case "active":
                        var activeWindow = manager.GetActiveWindow();
                        if (activeWindow != null)
                        {
                            PrintWindowInfo(activeWindow);
                        }
                        break;
                        
                    case "search":
                        if (args.Length < 2)
                        {
                            Console.WriteLine("Error: Search term required");
                            return;
                        }
                        
                        var searchTerm = args[1];
                        var matchClass = true;
                        var matchName = true;
                        var matchClassName = true;
                        var matchRole = true;
                        var matchId = false;
                        
                        // Parse search options
                        for (int i = 2; i < args.Length; i++)
                        {
                            switch (args[i].ToLower())
                            {
                                case "--class":
                                    matchClass = true;
                                    matchName = false;
                                    matchClassName = false;
                                    matchRole = false;
                                    matchId = false;
                                    break;
                                case "--name":
                                    matchClass = false;
                                    matchName = true;
                                    matchClassName = false;
                                    matchRole = false;
                                    matchId = false;
                                    break;
                                case "--classname":
                                    matchClass = false;
                                    matchName = false;
                                    matchClassName = true;
                                    matchRole = false;
                                    matchId = false;
                                    break;
                                case "--role":
                                    matchClass = false;
                                    matchName = false;
                                    matchClassName = false;
                                    matchRole = true;
                                    matchId = false;
                                    break;
                                case "--id":
                                    matchClass = false;
                                    matchName = false;
                                    matchClassName = false;
                                    matchRole = false;
                                    matchId = true;
                                    break;
                            }
                        }
                        
                        var searchResults = manager.SearchWindows(searchTerm, matchClass, matchName, matchClassName, matchRole, matchId);
                        foreach (var window in searchResults)
                        {
                            PrintWindowInfo(window);
                        }
                        break;
                        
                    default:
                        Console.WriteLine($"Unknown command: {args[0]}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private static void PrintWindowInfo(WindowInfo window)
        {
            Console.WriteLine($"Window {window.Id}");
            Console.WriteLine($"  Name: {window.Name}");
            Console.WriteLine($"  Class: {window.ClassName}");
            Console.WriteLine($"  Position: {window.X},{window.Y}{(window.Screen > 0 ? $" (screen: {window.Screen})" : "")}");
            Console.WriteLine($"  Geometry: {window.Width}x{window.Height}");
            Console.WriteLine($"  PID: {window.Pid}");
            Console.WriteLine();
        }
    }
}