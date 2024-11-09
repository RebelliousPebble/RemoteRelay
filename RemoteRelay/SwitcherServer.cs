using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RemoteRelay;

public class SwitcherServer
{
    private Socket _socket;
    private SwitcherState? _switcher;
    private EndPoint _remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
    private AppSettings _config;
    public EventHandler<Dictionary<string, string>> stateChanged;

    public SwitcherServer(int port, AppSettings config)
    {
        if (config.IsServer)
        {
            _switcher = new SwitcherState(config);
        }
        else
        {
            _switcher = null;
        }
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
        _config = config;
    }

    
    
    public void Start()
    {
        Console.WriteLine("Waiting for a connection...");
        BeginReceive();
    }

    private void BeginReceive()
    {
        byte[] buffer = new byte[StateObject.BufferSize];
        _socket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref _remoteEndPoint, new AsyncCallback(ReceiveCallback), buffer);
    }
    

    private void ReceiveCallback(IAsyncResult ar)
    {
        byte[] buffer = (byte[])ar.AsyncState;
        int bytesRead = _socket.EndReceiveFrom(ar, ref _remoteEndPoint);

        if (bytesRead > 0)
        {
            string content = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();
            Console.WriteLine($"Read {content.Length} bytes from socket. Data: {content}");

            // Process the data
            ProcessData(content);
        }

        // Continue receiving data
        BeginReceive();
    }

    private void ProcessData(string data)
    {
        // Example processing: log the data and send a response back to the client
        Console.WriteLine($"Processing data: {data}");

        if (_config.IsServer)
        {
            if (data.StartsWith("RELAYREMOTE SWITCH"))
            {
                // Messages to switch the source will be formatted as "RELAYREMOTE SWITCH-<source>-<output>"
                string[] parts = data.Split('-');
                if (parts.Length == 3)
                {
                    _switcher?.SwitchSource(parts[1], parts[2]);
                }
            }
            SendStatusPacket();
        }
        
        if (data.StartsWith("RELAYREMOTE GETSTATE"))
        {
            SendStatusPacket();
        }

        if (data.StartsWith("RELAYREMOTE STATE"))
        {
            ProcessStatusPacket(data);
        }
        
    }

    private void SendStatusPacket()
    {
        var state = _switcher.GetSystemState();
        string response = "RELAYREMOTE STATE%";
        foreach (var source in state)
        {
            response += $"{source.Key}-{source.Value}%";
        }
        // remove last %
        response = response.Substring(0, response.Length - 1);
        byte[] byteData = Encoding.ASCII.GetBytes(response);
        _socket.BeginSendTo(byteData, 0, byteData.Length, SocketFlags.None, _remoteEndPoint, new AsyncCallback(SendCallback), null);
    }

    private void ProcessStatusPacket(string data)
    {
        //Message format: RELAYREMOTE STATE%<source>-<output>%<source>-<output>
        // put the state into a dictionary and call the event handler
        var state = new Dictionary<string, string>();
        string[] parts = data.Split('%');
        foreach (var part in parts)
        {
            string[] sourceOutput = part.Split('-');
            if (sourceOutput.Length == 2)
            {
                state.Add(sourceOutput[0], sourceOutput[1]);
            }
        }
        stateChanged?.Invoke(this, state);
    }
    
    private void SendCallback(IAsyncResult ar)
    {
        _socket.EndSendTo(ar);
    }
}

public class StateObject
{
    public const int BufferSize = 1024;
}