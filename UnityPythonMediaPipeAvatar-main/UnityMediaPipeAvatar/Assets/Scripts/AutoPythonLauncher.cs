using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

public class AutoPythonLauncher : MonoBehaviour
{
    private Process pyProcess;

    // ✅ เรียกอัตโนมัติเมื่อเริ่ม Play
    void Start()
    {
        StartPython();
    }

    // ✅ ฟังก์ชันเปิด Python main.py
    void StartPython()
    {
        string pythonExe = "python";  // ถ้าใช้ venv หรือ path เฉพาะ ให้ใส่ path เต็ม เช่น "C:/Users/inchi/AppData/Local/Programs/Python/Python310/python.exe"
        string scriptPath = Application.dataPath + "/../PythonScripts/main.py";
        // 👆 ปรับ path ให้ตรงกับตำแหน่งจริงของไฟล์ main.py ของคุณ

        pyProcess = new Process();
        pyProcess.StartInfo.FileName = pythonExe;
        pyProcess.StartInfo.Arguments = $"\"{scriptPath}\"";
        pyProcess.StartInfo.WorkingDirectory = System.IO.Path.GetDirectoryName(scriptPath);
        pyProcess.StartInfo.UseShellExecute = false;
        pyProcess.StartInfo.CreateNoWindow = true;
        pyProcess.StartInfo.RedirectStandardOutput = false;
        pyProcess.Start();

        UnityEngine.Debug.Log("[Unity] ✅ Python started: " + scriptPath);
    }

    // ✅ เรียกอัตโนมัติเมื่อกด Stop หรือปิดเกม
    void OnApplicationQuit()
    {
        StopPythonGracefully();
    }

    // ✅ ฟังก์ชันปิด Python อย่างสุภาพ
    void StopPythonGracefully()
    {
        try
        {
            // 🔹 ส่งข้อความ __QUIT__ ไปให้ฝั่ง Python
            using (var client = new UdpClient())
            {
                var bytes = Encoding.UTF8.GetBytes("__QUIT__");
                client.Send(bytes, bytes.Length, "127.0.0.1", 54321);
                UnityEngine.Debug.Log("[Unity] 📨 Sent quit signal to Python.");
            }

            // 🔹 รอให้ Python ปิดตัวเองภายใน 3 วินาที
            if (pyProcess != null && !pyProcess.HasExited)
            {
                if (!pyProcess.WaitForExit(3000))
                {
                    pyProcess.Kill(); // ถ้ายังไม่ปิดภายใน 3 วิ ให้บังคับปิด
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
