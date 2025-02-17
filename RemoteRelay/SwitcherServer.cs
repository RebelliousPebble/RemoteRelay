using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;

namespace RemoteRelay;

public class SwitcherServer
{
   private static SwitcherServer? _instance;
   private static readonly Mutex _lock = new();

   private readonly AppSettings _config;
   private readonly EndPoint _sendEndPoint;
   private readonly Socket _sendSocket; //server only
   private readonly Socket _socket;
   private readonly SwitcherState? _switcher;
   private readonly Socket _tcpSocket;
   private EndPoint _receiveEndPoint;
   public Subject<Dictionary<string, string>> _stateChanged = new();

   /// <summary>
   ///    Constructor for the SwitcherServer class
   /// </summary>
   /// <param name="port">The port to open the switching service on</param>
   /// <param name="config">The configuration object</param>
   public SwitcherServer(int port, AppSettings config)
   {
      if (_config.IsServer) _config.ServerName = "localhost";
      _switcher = config.IsServer ? new SwitcherState(config) : null;
      int incoming_port;
      int outgoing_port;

      if (config.IsServer)
      {
         incoming_port = port + 1;
         outgoing_port = port;
      }
      else
      {
         incoming_port = port;
         outgoing_port = port + 1;
      }

      Debug.WriteLine($"Incoming Port {incoming_port}");
      Debug.WriteLine($"Outgoing Port {outgoing_port}");

      _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
      _socket.Bind(new IPEndPoint(IPAddress.Any, incoming_port));

      _sendSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
      _sendSocket.Bind(new IPEndPoint(IPAddress.Any, outgoing_port));

      _config = config;

      _sendEndPoint = new IPEndPoint(IPAddress.Broadcast, outgoing_port);
      _receiveEndPoint = new IPEndPoint(IPAddress.Any, incoming_port);

      _sendSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);


      // if TcpMirrorAddress is set, connect to the remote server
      if (string.IsNullOrEmpty(config.TcpMirrorAddress) || config.TcpMirrorPort is null) return;
      _tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
      _tcpSocket.Connect(config.TcpMirrorAddress, config.TcpMirrorPort.Value);
   }

   /// <summary>
   ///    Get the instance of the SwitcherServer class. Will instantiate if it doesn't exist
   /// </summary>
   /// <param name="port">The port to open the switching service on</param>
   /// <param name="config">The configuration object</param>
   /// <returns>The SwitcherServer instance</returns>
   public static SwitcherServer Instance(int port, AppSettings settings)
   {
      if (_instance == null)
         lock (_lock)
         {
            _instance = new SwitcherServer(port, settings);
         }

      return _instance;
   }

   /// <summary>
   ///    Get the instance of the SwitcherServer class. Will throw an exception if it doesn't exist
   /// </summary>
   /// <returns>The SwitcherServer instance</returns>
   /// <exception cref="NullReferenceException">The SwitcherServer has not been instantiated</exception>
   public static SwitcherServer Instance()
   {
      if (_instance != null) return _instance;
      throw new NullReferenceException("SwitcherServer instance not created");
   }

   /// <summary>
   ///    Start the SwitcherServer
   /// </summary>
   public void Start()
   {
      Console.WriteLine("Starting up the SwitcherServer");
      BeginReceive();
   }

   /// <summary>
   ///    Begin receiving data from the socket
   /// </summary>
   private void BeginReceive()
   {
      var buffer = new byte[StateObject.BufferSize];
      EndPoint receiveEP = new IPEndPoint(IPAddress.Any, 0);
      _socket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref receiveEP, ReceiveCallback, buffer);
   }


   /// <summary>
   ///    Request a status update from a server on the network.
   /// </summary>
   public void RequestStatus()
   {
      var byteData = Encoding.ASCII.GetBytes("RELAYREMOTE GETSTATE");
      _sendSocket.BeginSendTo(byteData, 0, byteData.Length, SocketFlags.None, _sendEndPoint, SendCallback, null);
   }

   /// <summary>
   ///    Sends a switching command to a server on the network
   /// </summary>
   /// <param name="source">The source to switch</param>
   /// <param name="output">The output to switch to</param>
   public void SwitchSource(string source, string output)
   {
      var byteData = Encoding.ASCII.GetBytes($"RELAYREMOTE SWITCH-{source}-{output}");
      _sendSocket.BeginSendTo(byteData, 0, byteData.Length, SocketFlags.None, _sendEndPoint, SendCallback, null);
   }

   /// <summary>
   ///    Callback for when data is received from the socket
   /// </summary>
   /// <param name="ar">The async result</param>
   private void ReceiveCallback(IAsyncResult ar)
   {
      var buffer = (byte[])ar.AsyncState;
      EndPoint senderEP = new IPEndPoint(IPAddress.Any, 0);
      var bytesRead = _socket.EndReceiveFrom(ar, ref senderEP);

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

   /// <summary>
   ///    Process any data received on the socket
   /// </summary>
   /// <param name="data">A string containing the source data</param>
   private void ProcessData(string data)
   {
      Debug.WriteLine($"Processing data: {data}");

      // We are configured to be a server, so we want to process switching and getstate commands
      if (_config.IsServer)
      {
         if (data.StartsWith("RELAYREMOTE SWITCH"))
         {
            // Messages to switch the source will be formatted as "RELAYREMOTE SWITCH-<source>-<output>"
            var parts = data.Split('-');
            if (parts.Length == 3) _switcher?.SwitchSource(parts[1], parts[2]);

            SendStatusPacket();

            // if TcpMirrorAddress is set, mirror the switch command to the TCP server (expecting this to be something like Myriad or Zetta virtual hardware)
            if (_config.TcpMirrorAddress != null)
            {
               var byteData = Encoding.ASCII.GetBytes(data);
               _tcpSocket.BeginSend(byteData, 0, byteData.Length, SocketFlags.None, SendCallback, null);
            }
         }

         if (data.StartsWith("RELAYREMOTE GETSTATE")) SendStatusPacket();
      }

      if (data.StartsWith("RELAYREMOTE STATE")) ProcessStatusPacket(data);
   }

   /// <summary>
   ///    Send a status packet across the network to all clients
   /// </summary>
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
      _sendSocket.BeginSendTo(byteData, 0, byteData.Length, SocketFlags.None, _sendEndPoint, SendCallback, null);
   }

   /// <summary>
   ///    Process a status packet received from the network
   /// </summary>
   /// <param name="data">The data containing the status packet</param>
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

      _stateChanged.OnNext(state);
   }

   /// <summary>
   ///    Callback for when data is sent to the socket
   /// </summary>
   /// <param name="ar"></param>
   private void SendCallback(IAsyncResult ar)
   {
      _sendSocket.EndSendTo(ar);
   }
}

public abstract class StateObject
{
   public const int BufferSize = 1024;
}