using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RemoteRelay;

public class SwitcherServer
{
    private readonly AppSettings _config;
    private EndPoint _remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
    private readonly Socket _socket;
    private readonly SwitcherState? _switcher;
    private readonly Socket _tcpSocket;
    public EventHandler<Dictionary<string, string>> stateChanged;

    public SwitcherServer(int port, AppSettings config)
    {
        _switcher = config.IsServer ? new SwitcherState(config) : null;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
        _config = config;

        // if TcpMirrorAddress is set, connect to the remote server
        if (string.IsNullOrEmpty(config.TcpMirrorAddress)) return;
        _tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _tcpSocket.Connect(config.TcpMirrorAddress, port);
    }


    public void Start()
    {
        Console.WriteLine("Waiting for a connection...");
        BeginReceive();
    }

    private void BeginReceive()
    {
        var buffer = new byte[StateObject.BufferSize];
        _socket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref _remoteEndPoint, ReceiveCallback,
            buffer);
    }


    private void ReceiveCallback(IAsyncResult ar)
    {
        var buffer = (byte[])ar.AsyncState;
        var bytesRead = _socket.EndReceiveFrom(ar, ref _remoteEndPoint);

        if (bytesRead > 0)
        {
            var content = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();
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
                var parts = data.Split('-');
                if (parts.Length == 3) _switcher?.SwitchSource(parts[1], parts[2]);
            }

            SendStatusPacket();

            // if TcpMirrorAddress is set, mirror the switch command to the TCP server (expecting this to be something like Myriad or Zetta virtual hardware)
            if (_config.TcpMirrorAddress != null)
            {
                var byteData = Encoding.ASCII.GetBytes(data);
                _tcpSocket.BeginSend(byteData, 0, byteData.Length, SocketFlags.None, SendCallback, null);
            }
        }

        if (data.StartsWith("RELAYREMOTE GETSTATE")) SendStatusPacket();

        if (data.StartsWith("RELAYREMOTE STATE")) ProcessStatusPacket(data);
    }

    private void SendStatusPacket()
    {
        var state = _switcher?.GetSystemState();
        var response = "RELAYREMOTE STATE%";
        if (state != null)
            foreach (var source in state)
                response += $"{source.Key}-{source.Value}%";

        // remove last %
        response = response.Substring(0, response.Length - 1);
        var byteData = Encoding.ASCII.GetBytes(response);
        _socket.BeginSendTo(byteData, 0, byteData.Length, SocketFlags.None, _remoteEndPoint, SendCallback, null);
    }

    private void ProcessStatusPacket(string data)
    {
        //Message format: RELAYREMOTE STATE%<source>-<output>%<source>-<output>
        // put the state into a dictionary and call the event handler
        var state = new Dictionary<string, string>();
        var parts = data.Split('%');
        foreach (var part in parts)
        {
            var sourceOutput = part.Split('-');
            if (sourceOutput.Length == 2) state.Add(sourceOutput[0], sourceOutput[1]);
        }

        stateChanged?.Invoke(this, state);
    }

    private void SendCallback(IAsyncResult ar)
    {
        _socket.EndSendTo(ar);
    }
}

public abstract class StateObject
{
    public const int BufferSize = 1024;
}