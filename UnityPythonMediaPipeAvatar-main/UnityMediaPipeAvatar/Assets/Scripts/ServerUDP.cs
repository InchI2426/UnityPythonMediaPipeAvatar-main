// ServerUDP.cs (ตัวอย่าง robust implementation)
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class ServerUDP
{
    private UdpClient udp;
    private IPEndPoint remoteEP;
    private Thread listenThread;
    private volatile bool isListening = false;

    private readonly string host;
    private readonly int port;

    private readonly object messageLock = new object();
    private string lastMessage = null;

    public ServerUDP(string host, int port)
    {
        this.host = host;
        this.port = port;
    }

    public void Connect()
    {
        // Bind to local endpoint (listen)
        try
        {
            remoteEP = new IPEndPoint(IPAddress.Any, port);
            udp = new UdpClient(port);
            udp.Client.ReceiveTimeout = 1000; // short timeout so thread can check isListening frequently
            Debug.Log($"ServerUDP: Bound to port {port}");
        }
        catch (Exception e)
        {
            Debug.LogWarning("ServerUDP Connect exception: " + e.Message);
            throw;
        }
    }

    public void StartListeningAsync()
    {
        if (isListening) return;
        isListening = true;
        listenThread = new Thread(ListenLoop);
        listenThread.IsBackground = true;
        listenThread.Start();
    }

    private void ListenLoop()
    {
        Debug.Log("ServerUDP: ListenLoop started.");
        try
        {
            while (isListening)
            {
                try
                {
                    // Use Receive with timeout; if socket closed it will throw ObjectDisposedException or SocketException
                    byte[] data = udp.Receive(ref remoteEP);
                    if (data != null && data.Length > 0)
                    {
                        string msg = Encoding.UTF8.GetString(data);
                        lock (messageLock)
                        {
                            lastMessage = msg;
                        }
                    }
                }
                catch (SocketException sex)
                {
                    // Receive timeout or socket closed
                    // Timeout code: 10060 or others - ignore if we're stopping
                    if (!isListening)
                    {
                        break;
                    }
                    // If real socket error, log for debug but continue/loop
                    Debug.LogWarning("ServerUDP SocketException: " + sex.Message);
                }
                catch (ObjectDisposedException)
                {
                    // Socket closed while receiving - shut down loop gracefully
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("ServerUDP ListenLoop exception: " + ex.Message);
                    // small sleep to avoid busy spin on unknown errors
                    Thread.Sleep(10);
                }
            }
        }
        finally
        {
            Debug.Log("ServerUDP: ListenLoop exiting.");
        }
    }

    public bool HasMessage()
    {
        lock (messageLock)
        {
            return !string.IsNullOrEmpty(lastMessage);
        }
    }

    public string GetMessage()
    {
        lock (messageLock)
        {
            string m = lastMessage;
            lastMessage = null;
            return m;
        }
    }

    // Gracefully stop listening and close socket
    public void StopListening()
    {
        Debug.Log("ServerUDP: StopListening called.");
        isListening = false;

        try
        {
            if (udp != null)
            {
                try
                {
                    // Closing UDP will unblock Receive and lead to ObjectDisposedException inside Receive,
                    // which our ListenLoop handles by breaking the loop.
                    udp.Close();
                }
                catch (Exception e)
                {
                    Debug.LogWarning("ServerUDP StopListening close exception: " + e.Message);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("ServerUDP StopListening exception: " + e.Message);
        }
    }

    // Wait for listen thread to stop (call after StopListening)
    public void WaitForStop(int millisecondsTimeout = 2000)
    {
        if (listenThread != null)
        {
            try
            {
                if (!listenThread.Join(millisecondsTimeout))
                {
                    Debug.LogWarning("ServerUDP: listenThread did not exit in time.");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("ServerUDP WaitForStop exception: " + e.Message);
            }
            finally
            {
                listenThread = null;
            }
        }
    }

    // Full disconnect (stop + cleanup)
    public void Disconnect()
    {
        StopListening();
        WaitForStop(2000);
        if (udp != null)
        {
            try { udp.Close(); } catch { }
            udp = null;
        }
    }
}
