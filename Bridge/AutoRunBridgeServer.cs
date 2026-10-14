using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

public sealed class AutoRunBridgeServer
{
    public const string Version = "0.3.0";
    public const string Host = "127.0.0.1";
    public const int DefaultPort = 17331;

    private const int WindowsSharingViolation = 32;
    private const int MacOsAddressAlreadyInUse = 48;
    private const int LinuxAddressAlreadyInUse = 98;
    private const int WindowsAlreadyExists = 183;
    private const int WindowsAddressAlreadyInUse = 10048;

    private readonly object _lock = new();
    private HttpListener _listener;
    private Thread _thread;
    private AutoRunBridgeDispatcher _dispatcher;

    public bool IsRunning => _listener != null && _listener.IsListening;
    public int Port { get; private set; } = DefaultPort;
    public string Url => $"http://{Host}:{Port}/";

    public void Start(int port = DefaultPort)
    {
        if (IsRunning)
        {
            return;
        }

        if (port < 1 || port > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, $"Port must be between 1 and {IPEndPoint.MaxPort}.");
        }

        HttpListener listener = StartListener(port, out int selectedPort);
        var dispatcher = new AutoRunBridgeDispatcher();
        var thread = new Thread(ListenLoop) { IsBackground = true };
        bool pumpSubscribed = false;

        try
        {
            _listener = listener;
            Port = selectedPort;
            _dispatcher = dispatcher;
            _thread = thread;
            _thread.Start();
            EditorApplication.update += _dispatcher.Pump;
            pumpSubscribed = true;
            AutoRunBridgeEndpointState.Publish(Host, Port);
        }
        catch
        {
            if (pumpSubscribed)
            {
                EditorApplication.update -= dispatcher.Pump;
            }

            listener.Close();
            AutoRunBridgeEndpointState.Clear();
            _listener = null;
            _dispatcher = null;
            _thread = null;
            throw;
        }

        if (selectedPort != port)
        {
            AutoRunWindow.AppendBridgeConsoleText(
                $"AutoRun MCP bridge port {port} is occupied; using {selectedPort}.",
                AutoRunLogLevel.Warning
            );
        }

        AutoRunWindow.AppendBridgeConsoleText($"AutoRun MCP bridge started on {Url}", AutoRunLogLevel.Info);
    }

    public void Stop()
    {
        bool wasRunning = false;
        AutoRunBridgeEndpointState.Clear();
        lock (_lock)
        {
            if (_listener == null)
            {
                return;
            }

            wasRunning = _listener.IsListening;
            _listener.Stop();
            _listener.Close();
            _listener = null;
            _thread = null;
        }

        if (_dispatcher != null)
        {
            EditorApplication.update -= _dispatcher.Pump;
            _dispatcher = null;
        }

        if (wasRunning)
        {
            AutoRunWindow.AppendBridgeConsoleText("AutoRun MCP bridge stopped.", AutoRunLogLevel.Info);
        }
    }

    public AutoRunBridgeResponse Enqueue(string requestJson)
    {
        if (!IsRunning || _dispatcher == null)
        {
            return AutoRunBridgeResponses.Fail(null, "bridge_not_running", "AutoRun MCP bridge is not running.");
        }

        return _dispatcher.Enqueue(requestJson);
    }

    private void ListenLoop()
    {
        while (IsRunning)
        {
            try
            {
                HttpListenerContext context = _listener.GetContext();
                ThreadPool.QueueUserWorkItem(_ => HandleContext(context));
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    private static HttpListener StartListener(int startPort, out int selectedPort)
    {
        Exception lastPortConflict = null;
        for (int port = startPort; port <= IPEndPoint.MaxPort; port++)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://{Host}:{port}/");

            try
            {
                listener.Start();
                selectedPort = port;
                return listener;
            }
            catch (HttpListenerException ex) when (IsPortConflict(ex))
            {
                lastPortConflict = ex;
                listener.Close();
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                lastPortConflict = ex;
                listener.Close();
            }
            catch
            {
                listener.Close();
                throw;
            }
        }

        throw new InvalidOperationException(
            $"No available port was found between {startPort} and {IPEndPoint.MaxPort}.",
            lastPortConflict
        );
    }

    private static bool IsPortConflict(HttpListenerException exception)
    {
        int errorCode = exception.NativeErrorCode;
        return errorCode == WindowsSharingViolation
            || errorCode == MacOsAddressAlreadyInUse
            || errorCode == LinuxAddressAlreadyInUse
            || errorCode == WindowsAlreadyExists
            || errorCode == WindowsAddressAlreadyInUse;
    }

    private void HandleContext(HttpListenerContext context)
    {
        if (context.Request.HttpMethod == "GET" && context.Request.Url.AbsolutePath == "/status")
        {
            WriteJson(context, _dispatcher.Enqueue("{\"id\":\"status\",\"command\":\"status\"}"));
            return;
        }

        if (context.Request.HttpMethod != "POST" || context.Request.Url.AbsolutePath != "/rpc")
        {
            context.Response.StatusCode = 404;
            WriteJson(context, AutoRunBridgeResponses.Fail(null, "not_found", "Endpoint not found."));
            return;
        }

        string body;
        using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
        {
            body = reader.ReadToEnd();
        }

        AutoRunBridgeResponse response = _dispatcher.Enqueue(body);
        WriteJson(context, response);
    }

    private static void WriteJson(HttpListenerContext context, AutoRunBridgeResponse response)
    {
        string json = JsonUtility.ToJson(response);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        context.Response.ContentType = "application/json";
        context.Response.ContentEncoding = Encoding.UTF8;
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.OutputStream.Close();
    }
}
