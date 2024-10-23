using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Avalonia.Logging;

namespace RemoteRelay;

public class SwitcherServer
{
    private Socket _socket;
    private ManualResetEvent _allDone = new ManualResetEvent(false);
    private SwitcherState _switcher;
    public SwitcherServer(int port, AppSettings config)
    {
        // Initialize a TCP socket
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
        _switcher = new SwitcherState(config);
    }

    public void Start()
    {
        _socket.Listen(100);
        Logger.Sink?.Log(LogEventLevel.Verbose, "SwitcherServer", this, "Waiting for a connection...");
        while (true)
        {
            _allDone.Reset();
            _socket.BeginAccept(new AsyncCallback(AcceptCallback), _socket);
            _allDone.WaitOne();
        }
    }

    private void AcceptCallback(IAsyncResult ar)
    {
        _allDone.Set();
        Socket listener = (Socket)ar.AsyncState;
        Socket handler = listener.EndAccept(ar);

        StateObject state = new StateObject();
        state.WorkSocket = handler;
        handler.BeginReceive(state.Buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReadCallback), state);
    }

    private void ReadCallback(IAsyncResult ar)
    {
        StateObject state = (StateObject)ar.AsyncState;
        Socket handler = state.WorkSocket;

        int bytesRead = handler.EndReceive(ar);
        if (bytesRead > 0)
        {
            state.Sb.Append(Encoding.ASCII.GetString(state.Buffer, 0, bytesRead));
            handler.BeginReceive(state.Buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReadCallback), state);
        }
        else
        {
            if (state.Sb.Length > 1)
            {
                string content = state.Sb.ToString();
                Logger.Sink?.Log(LogEventLevel.Verbose, "SwitcherServer", this, $"Read {content.Length} bytes from socket. Data: {content}");


                
                // Process the data
                ProcessData(content, handler);
            }
            handler.Close();
        }
    }

    private void ProcessData(string data, Socket handler)
    {
        // Example processing: log the data and send a response back to the client
        Logger.Sink?.Log(LogEventLevel.Verbose, "SwitcherServer", this, $"Processing data: {data}");

        if (data.StartsWith("SWITCH"))
        {
            // Messages to switch the source will be formatted as "SWITCH <source> <output>"
            string[] parts = data.Split(' ');
            if (parts.Length == 3)
            {
                _switcher.SwitchSource(parts[1], parts[2]);
            }
        }
        
        // Return the current state of the system regardless of what is sent to the system.
        var state = _switcher.GetSystemState();
        string response = "STATE";
        foreach (var source in state)
        {
            response += $" {source.Key} {source.Value}";
        }
        byte[] byteData = Encoding.ASCII.GetBytes(response);
        handler.BeginSend(byteData, 0, byteData.Length, 0, new AsyncCallback(SendCallback), handler);
    }

    private void SendCallback(IAsyncResult ar)
    {
        Socket handler = (Socket)ar.AsyncState;
        handler.EndSend(ar);
    }
}

public class StateObject
{
    public Socket WorkSocket = null;
    public const int BufferSize = 1024;
    public byte[] Buffer = new byte[BufferSize];
    public System.Text.StringBuilder Sb = new System.Text.StringBuilder();
}