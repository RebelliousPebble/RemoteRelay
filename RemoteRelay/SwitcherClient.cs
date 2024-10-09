using System;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;

namespace RemoteRelay
{
    public class SwitcherClient
    {
        private Socket _clientSocket;
        private string _serverAddress;
        private int _port;
        private Dictionary<int, int> _systemState;

        public SwitcherClient(string serverAddress, int port)
        {
            _serverAddress = serverAddress;
            _port = port;
            _clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _systemState = new Dictionary<int, int>();
        }

        public Dictionary<int, int> SystemState => _systemState;

        public void Connect()
        {
            _clientSocket.Connect(_serverAddress, _port);
        }

        public Dictionary<int, int> SwitchSource(int source, int output)
        {
            string message = $"SWITCH {source} {output}";
            byte[] byteData = Encoding.ASCII.GetBytes(message);
            _clientSocket.Send(byteData);

            // Receive the status response from the server
            byte[] buffer = new byte[1024];
            int bytesRead = _clientSocket.Receive(buffer);
            string response = Encoding.ASCII.GetString(buffer, 0, bytesRead);

            _systemState = ParseStatusMessage(response);
            return _systemState;
        }

        public Dictionary<int, int> GetSystemState()
        {
            string message = $"STATUS";
            byte[] byteData = Encoding.ASCII.GetBytes(message);
            _clientSocket.Send(byteData);
            
            byte[] buffer = new byte[1024];
            int bytesRead = _clientSocket.Receive(buffer);
            string response = Encoding.ASCII.GetString(buffer, 0, bytesRead);

            _systemState = ParseStatusMessage(response);
            return _systemState;
        }

        private Dictionary<int, int> ParseStatusMessage(string message)
        {
            var state = new Dictionary<int, int>();
            if (message.StartsWith("STATE"))
            {
                string[] parts = message.Split(' ');
                for (int i = 1; i < parts.Length; i += 2)
                {
                    if (int.TryParse(parts[i], out int source) && int.TryParse(parts[i + 1], out int output))
                    {
                        state[source] = output;
                    }
                }
            }
            return state;
        }

        public void Close()
        {
            _clientSocket.Shutdown(SocketShutdown.Both);
            _clientSocket.Close();
        }
    }
}