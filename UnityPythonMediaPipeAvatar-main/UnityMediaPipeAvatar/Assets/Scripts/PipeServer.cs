// PipeServer.cs (แก้ไขให้ใช้ lifecycle แบบระยะยาว ปิด thread/resource อย่างปลอดภัย)
using System;
using System.Collections;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;

/* Updated PipeServer with proper thread lifecycle handling and safe cleanup.
 * - Uses serverThread and isRunning flag to stop Run() gracefully.
 * - Provides CleanupResources() to dispose sockets/pipes.
 * - OnApplicationQuit waits for serverThread to finish (with timeout).
 */

[DefaultExecutionOrder(-1)]
public class PipeServer : MonoBehaviour
{
    public bool useLegacyPipes = false; // True to use NamedPipes for interprocess communication (not supported on Linux)
    public string host = "127.0.0.1"; // This machines host.
    public int port = 52733; // Must match the Python side.
    public Transform bodyParent;
    public GameObject landmarkPrefab;
    public GameObject linePrefab;
    public GameObject headPrefab;
    public bool enableHead = false;
    public float multiplier = 10f;
    public float landmarkScale = 1f;
    public float maxSpeed = 50f;
    public float debug_samplespersecond;
    public int samplesForPose = 1;
    public bool active;

    private NamedPipeServerStream serverNP;
    private BinaryReader reader;
    private ServerUDP server;

    private Body body;

    // Thread management
    private Thread serverThread;
    private volatile bool isRunning = false;

    // these virtual transforms are not actually provided by mediapipe pose, but are required for avatars.
    // so I just manually compute them
    private Transform virtualNeck;
    private Transform virtualHip;

    public Transform GetLandmark(Landmark mark)
    {
        return body.instances[(int)mark].transform;
    }
    public Transform GetVirtualNeck()
    {
        return virtualNeck;
    }
    public Transform GetVirtualHip()
    {
        return virtualHip;
    }

    private void Start()
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

        body = new Body(bodyParent, landmarkPrefab, linePrefab, landmarkScale, enableHead ? headPrefab : null);
        virtualNeck = new GameObject("VirtualNeck").transform;
        virtualHip = new GameObject("VirtualHip").transform;

        // Start server thread
        isRunning = true;
        serverThread = new Thread(new ThreadStart(Run));
        serverThread.IsBackground = true;
        serverThread.Start();
    }

    private void Update()
    {
        UpdateBody(body);
    }

    private void UpdateBody(Body b)
    {
        for (int i = 0; i < LANDMARK_COUNT; ++i)
        {
            if (b.positionsBuffer[i].accumulatedValuesCount < samplesForPose)
                continue;

            b.localPositionTargets[i] = b.positionsBuffer[i].value / (float)b.positionsBuffer[i].accumulatedValuesCount * multiplier;
            b.positionsBuffer[i] = new AccumulatedBuffer(Vector3.zero, 0);
        }

        Vector3 offset = Vector3.zero;
        for (int i = 0; i < LANDMARK_COUNT; ++i)
        {
            Vector3 p = b.localPositionTargets[i] - offset;
            b.instances[i].transform.localPosition = Vector3.MoveTowards(b.instances[i].transform.localPosition, p, Time.deltaTime * maxSpeed);
        }

        virtualNeck.transform.position = (b.instances[(int)Landmark.RIGHT_SHOULDER].transform.position + b.instances[(int)Landmark.LEFT_SHOULDER].transform.position) / 2f;
        virtualHip.transform.position = (b.instances[(int)Landmark.RIGHT_HIP].transform.position + b.instances[(int)Landmark.LEFT_HIP].transform.position) / 2f;

        b.UpdateLines();
    }

    public void SetVisible(bool visible)
    {
        bodyParent.gameObject.SetActive(visible);
    }

    private void Run()
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

        try
        {
            if (useLegacyPipes)
            {
                // Open the named pipe.
                serverNP = new NamedPipeServerStream("UnityMediaPipeBody1", PipeDirection.InOut, 99, PipeTransmissionMode.Message);
                Debug.Log("PipeServer: Waiting for connection (NamedPipe)...");
                // WaitForConnection is blocking; if we need to abort, closing the stream from another thread will raise an exception
                serverNP.WaitForConnection();
                Debug.Log("PipeServer: Connected (NamedPipe).");
                reader = new BinaryReader(serverNP, Encoding.UTF8);
            }
            else
            {
                server = new ServerUDP(host, port);
                server.Connect();
                server.StartListeningAsync();
                Debug.Log($"PipeServer: Listening @{host}:{port}");
            }

            // Main receive loop
            while (isRunning)
            {
                try
                {
                    Body h = body;
                    var len = 0;
                    var str = "";

                    if (useLegacyPipes)
                    {
                        // If pipe not connected, small sleep
                        if (serverNP == null || !serverNP.IsConnected)
                        {
                            Thread.Sleep(10);
                            continue;
                        }

                        // BinaryReader.ReadUInt32 will block until data; if stream is closed from another thread it will throw
                        len = (int)reader.ReadUInt32();
                        if (len <= 0)
                        {
                            // nothing
                            continue;
                        }
                        str = new string(reader.ReadChars(len));
                    }
                    else
                    {
                        if (server.HasMessage())
                        {
                            str = server.GetMessage();
                        }
                        else
                        {
                            // no message - avoid busy loop
                            Thread.Sleep(1);
                            continue;
                        }
                        len = str.Length;
                    }

                    if (string.IsNullOrEmpty(str))
                        continue;

                    string[] lines = str.Split('\n');
                    foreach (string l in lines)
                    {
                        if (string.IsNullOrWhiteSpace(l))
                            continue;
                        string[] s = l.Split('|');
                        if (s.Length < 4) continue;
                        int i;
                        if (!int.TryParse(s[0], out i)) continue;
                        // Parse floats using invariant culture
                        float x = float.Parse(s[1]);
                        float y = float.Parse(s[2]);
                        float z = float.Parse(s[3]);
                        h.positionsBuffer[i].value += new Vector3(x, y, z);
                        h.positionsBuffer[i].accumulatedValuesCount += 1;
                        h.active = true;
                    }
                }
                catch (EndOfStreamException)
                {
                    Debug.Log("PipeServer: Client Disconnected (EndOfStream).");
                    break;
                }
                catch (IOException ioex)
                {
                    Debug.LogWarning("PipeServer IO exception: " + ioex.Message);
                    // Likely stream closed during shutdown - break
                    break;
                }
                catch (ThreadAbortException)
                {
                    Debug.Log("PipeServer: ThreadAbortException caught - exiting loop.");
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("PipeServer Run() exception: " + ex.Message);
                    // For non-fatal errors, continue loop unless shutting down
                    if (!isRunning)
                        break;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("PipeServer startup exception: " + e.Message);
        }
        finally
        {
            // Ensure resources cleaned up if loop exits
            CleanupResources();
            Debug.Log("PipeServer: Run() exiting.");
        }
    }

    // Centralized cleanup used by OnApplicationQuit and finally block
    private void CleanupResources()
    {
        try
        {
            Debug.Log("PipeServer: Cleaning up resources...");
            if (useLegacyPipes)
            {
                if (reader != null)
                {
                    try { reader.Close(); } catch { }
                    reader = null;
                }
                if (serverNP != null)
                {
                    try { serverNP.Disconnect(); } catch { }
                    try { serverNP.Close(); serverNP.Dispose(); } catch { }
                    serverNP = null;
                }
            }
            else
            {
                if (server != null)
                {
                    try { server.Disconnect(); } catch { }
                    server = null;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("PipeServer CleanupResources exception: " + e.Message);
        }
    }

    private void OnDisable()
    {
        // Ensure orderly shutdown when object disabled (e.g. stop play in Editor)
        OnApplicationQuit();
    }

    private void OnApplicationQuit()
    {
        Debug.Log("PipeServer: OnApplicationQuit called - shutting down server thread and resources.");

        // Signal the run loop to stop
        isRunning = false;

        // Close resources to unblock any blocking reads/waits
        try
        {
            if (useLegacyPipes)
            {
                if (reader != null)
                {
                    try { reader.Close(); } catch { }
                    reader = null;
                }
                if (serverNP != null)
                {
                    try { serverNP.Close(); serverNP.Dispose(); } catch { }
                    serverNP = null;
                }
            }
            else
            {
                if (server != null)
                {
                    try { server.StopListening(); server.WaitForStop(2000); server.Disconnect(); } catch { }
                    server = null;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("PipeServer OnApplicationQuit early cleanup error: " + e.Message);
        }

        // Wait for thread to finish gracefully
        if (serverThread != null && serverThread.IsAlive)
        {
            try
            {
                // Give thread a short time to exit cleanly
                if (!serverThread.Join(2000))
                {
                    Debug.LogWarning("PipeServer: serverThread did not exit in time.");
                    // As last resort, attempt abort (not recommended for normal operation)
                    try
                    {
                        serverThread.Abort();
                    }
                    catch (Exception abortExc)
                    {
                        Debug.LogWarning("PipeServer: Thread.Abort failed: " + abortExc.Message);
                    }
                }
            }
            catch (Exception joinEx)
            {
                Debug.LogWarning("PipeServer waiting for thread error: " + joinEx.Message);
            }
            finally
            {
                serverThread = null;
            }
        }

        // Final cleanup to be safe
        CleanupResources();
    }

    const int LANDMARK_COUNT = 33;
    const int LINES_COUNT = 11;

    public struct AccumulatedBuffer
    {
        public Vector3 value;
        public int accumulatedValuesCount;
        public AccumulatedBuffer(Vector3 v, int ac)
        {
            value = v;
            accumulatedValuesCount = ac;
        }
    }

    public class Body
    {
        public Transform parent;
        public AccumulatedBuffer[] positionsBuffer = new AccumulatedBuffer[LANDMARK_COUNT];
        public Vector3[] localPositionTargets = new Vector3[LANDMARK_COUNT];
        public GameObject[] instances = new GameObject[LANDMARK_COUNT];
        public LineRenderer[] lines = new LineRenderer[LINES_COUNT];

        public bool active;

        public Body(Transform parent, GameObject landmarkPrefab, GameObject linePrefab, float s, GameObject headPrefab)
        {
            this.parent = parent;
            for (int i = 0; i < instances.Length; ++i)
            {
                instances[i] = Instantiate(landmarkPrefab);// GameObject.CreatePrimitive(PrimitiveType.Sphere);
                instances[i].transform.localScale = Vector3.one * s;
                instances[i].transform.parent = parent;
                instances[i].name = ((Landmark)i).ToString();
            }
            for (int i = 0; i < lines.Length; ++i)
            {
                lines[i] = Instantiate(linePrefab).GetComponent<LineRenderer>();
                lines[i].transform.parent = parent;
            }

            if (headPrefab)
            {
                GameObject head = Instantiate(headPrefab);
                head.transform.parent = instances[(int)Landmark.NOSE].transform;
                head.transform.localPosition = headPrefab.transform.position;
                head.transform.localRotation = headPrefab.transform.localRotation;
                head.transform.localScale = headPrefab.transform.localScale;
            }
        }
        public void UpdateLines()
        {
            lines[0].positionCount = 4;
            lines[0].SetPosition(0, Position((Landmark)32));
            lines[0].SetPosition(1, Position((Landmark)30));
            lines[0].SetPosition(2, Position((Landmark)28));
            lines[0].SetPosition(3, Position((Landmark)32));
            lines[1].positionCount = 4;
            lines[1].SetPosition(0, Position((Landmark)31));
            lines[1].SetPosition(1, Position((Landmark)29));
            lines[1].SetPosition(2, Position((Landmark)27));
            lines[1].SetPosition(3, Position((Landmark)31));

            lines[2].positionCount = 3;
            lines[2].SetPosition(0, Position((Landmark)28));
            lines[2].SetPosition(1, Position((Landmark)26));
            lines[2].SetPosition(2, Position((Landmark)24));
            lines[3].positionCount = 3;
            lines[3].SetPosition(0, Position((Landmark)27));
            lines[3].SetPosition(1, Position((Landmark)25));
            lines[3].SetPosition(2, Position((Landmark)23));

            lines[4].positionCount = 5;
            lines[4].SetPosition(0, Position((Landmark)24));
            lines[4].SetPosition(1, Position((Landmark)23));
            lines[4].SetPosition(2, Position((Landmark)11));
            lines[4].SetPosition(3, Position((Landmark)12));
            lines[4].SetPosition(4, Position((Landmark)24));

            lines[5].positionCount = 4;
            lines[5].SetPosition(0, Position((Landmark)12));
            lines[5].SetPosition(1, Position((Landmark)14));
            lines[5].SetPosition(2, Position((Landmark)16));
            lines[5].SetPosition(3, Position((Landmark)22));
            lines[6].positionCount = 4;
            lines[6].SetPosition(0, Position((Landmark)11));
            lines[6].SetPosition(1, Position((Landmark)13));
            lines[6].SetPosition(2, Position((Landmark)15));
            lines[6].SetPosition(3, Position((Landmark)21));

            lines[7].positionCount = 4;
            lines[7].SetPosition(0, Position((Landmark)16));
            lines[7].SetPosition(1, Position((Landmark)18));
            lines[7].SetPosition(2, Position((Landmark)20));
            lines[7].SetPosition(3, Position((Landmark)16));
            lines[8].positionCount = 4;
            lines[8].SetPosition(0, Position((Landmark)15));
            lines[8].SetPosition(1, Position((Landmark)17));
            lines[8].SetPosition(2, Position((Landmark)19));
            lines[8].SetPosition(3, Position((Landmark)15));

            lines[9].positionCount = 2;
            lines[9].SetPosition(0, Position((Landmark)10));
            lines[9].SetPosition(1, Position((Landmark)9));


            lines[10].positionCount = 5;
            lines[10].SetPosition(0, Position((Landmark)8));
            lines[10].SetPosition(1, Position((Landmark)5));
            lines[10].SetPosition(2, Position((Landmark)0));
            lines[10].SetPosition(3, Position((Landmark)2));
            lines[10].SetPosition(4, Position((Landmark)7));
        }

        public Vector3 Direction(Landmark from, Landmark to)
        {
            return (instances[(int)to].transform.position - instances[(int)from].transform.position).normalized;
        }
        public float Distance(Landmark from, Landmark to)
        {
            return (instances[(int)from].transform.position - instances[(int)to].transform.position).magnitude;
        }
        public Vector3 LocalPosition(Landmark Mark)
        {
            return instances[(int)Mark].transform.localPosition;
        }
        public Vector3 Position(Landmark Mark)
        {
            return instances[(int)Mark].transform.position;
        }

    }
}