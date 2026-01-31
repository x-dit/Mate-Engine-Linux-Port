using UnityEngine;
using System.Diagnostics;
using System.Threading.Tasks;

public static class WaylandUtility
{
    public static Vector2 GetMousePositionHyprland() {
        string output = RunCommand("/usr/bin/hyprctl cursorpos");
        string[] cursor = output.Trim().Split(',');
        return new Vector2(float.Parse(cursor[0]),float.Parse(cursor[1]));
    }

    public static void SetWindowPositionHyprland(Vector2 position){
        RunCommand($"/usr/bin/hyprctl dispatch moveactive exact {(position.x - (Screen.width/2))} {(position.y - (Screen.height/2))}");
    }

    public static async Task<Vector2> GetWindowPositionKWin() 
    {
        var windowGeometry = await UnityEngine.Object.FindFirstObjectByType<KWinManager>().GetWindowGeometry();
        return new Vector2(windowGeometry.X, windowGeometry.Y);
    }
    
    public static void SetWindowPositionKWin(Vector2 position) 
    {
        UnityEngine.Object.FindFirstObjectByType<KWinManager>().MoveWindow(position);
    }

    static string RunCommand(string command)
    {
        ProcessStartInfo psi = new ProcessStartInfo()
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{command}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using (Process p = Process.Start(psi))
        {
            p?.WaitForExit();
            return p?.StandardOutput.ReadToEnd();
        }
    }
}
