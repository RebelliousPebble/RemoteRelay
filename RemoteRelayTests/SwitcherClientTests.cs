using NUnit.Framework;
using Moq;
using System.Net.Sockets;
using System.Text;
using RemoteRelay;

namespace RemoteRelayTests
{
    [TestFixture]
    public class SwitcherClientTests
    {
        private Mock<Socket> _mockSocket;
        private SwitcherClient _switcherClient;

        [SetUp]
        public void Setup()
        {
            _mockSocket = new Mock<Socket>(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _switcherClient = new SwitcherClient("localhost", 8080);
        }

        [Test]
        public void Connect_ShouldConnectToServer()
        {
            // Arrange
            _mockSocket.Setup(s => s.Connect("localhost", 8080));

            // Act
            _switcherClient.Connect();

            // Assert
            _mockSocket.Verify(s => s.Connect("localhost", 8080), Times.Once);
        }

        [Test]
        public void SwitchSource_ShouldSendSwitchCommand()
        {
            // Arrange
            var source = 1;
            var output = 2;
            var message = $"SWITCH {source} {output}";
            var byteData = Encoding.ASCII.GetBytes(message);
            _mockSocket.Setup(s => s.Send(byteData));

            // Act
            _switcherClient.SwitchSource(source, output);

            // Assert
            _mockSocket.Verify(s => s.Send(byteData), Times.Once);
        }

        [Test]
        public void GetSystemState_ShouldSendStatusCommand()
        {
            // Arrange
            var message = "STATUS";
            var byteData = Encoding.ASCII.GetBytes(message);
            _mockSocket.Setup(s => s.Send(byteData));

            // Act
            _switcherClient.GetSystemState();

            // Assert
            _mockSocket.Verify(s => s.Send(byteData), Times.Once);
        }

        [Test]
        public void Close_ShouldShutdownAndCloseSocket()
        {
            // Arrange
            _mockSocket.Setup(s => s.Shutdown(SocketShutdown.Both));
            _mockSocket.Setup(s => s.Close());

            // Act
            _switcherClient.Close();

            // Assert
            _mockSocket.Verify(s => s.Shutdown(SocketShutdown.Both), Times.Once);
            _mockSocket.Verify(s => s.Close(), Times.Once);
        }
    }
}
