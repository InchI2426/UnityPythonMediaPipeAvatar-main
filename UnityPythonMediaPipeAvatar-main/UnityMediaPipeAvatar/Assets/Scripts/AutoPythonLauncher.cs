using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

public class AutoPythonLauncher : MonoBehaviour
{
    private Process pyProcess;
    void Start()
    {
        StartPython();
    }
    void StartPython()
    {
        string pythonExe = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            @"Programs\Python\Python311\python.exe"
        );
        string scriptPath = System.IO.Path.Combine(
            Application.dataPath, @"../mediapipeavatar/main.py"
        );
        string workingDir = System.IO.Path.GetDirectoryName(scriptPath);
        pyProcess = new Process();
        pyProcess.StartInfo.FileName = pythonExe;
        pyProcess.StartInfo.Arguments = $"\"{scriptPath}\"";
        pyProcess.StartInfo.WorkingDirectory = workingDir;
        pyProcess.StartInfo.UseShellExecute = false;
        pyProcess.StartInfo.CreateNoWindow = true;
        pyProcess.Start();

        UnityEngine.Debug.Log("[Unity] ✅ Python started: " + scriptPath);
    }
    void OnApplicationQuit()
    {
        StopPythonGracefully();
    }
    void StopPythonGracefully()
    {
        try
        {
            using (var client = new UdpClient())
            {
                var bytes = Encoding.UTF8.GetBytes("__QUIT__");
                client.Send(bytes, bytes.Length, "127.0.0.1", 54321);
                UnityEngine.Debug.Log("[Unity] 📨 Sent quit signal to Python.");
            }
            if (pyProcess != null && !pyProcess.HasExited)
            {
                if (!pyProcess.WaitForExit(3000))
                {
                    pyProcess.Kill();
                    UnityEngine.Debug.LogWarning("[Unity] ⚠️ Python forced to close.");
                }
                else
                {
                    UnityEngine.Debug.Log("[Unity] ✅ Python closed gracefully.");
                }
            }
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError($"[Unity] ❌ Error stopping Python: {ex.Message}");
        }
    }
}
