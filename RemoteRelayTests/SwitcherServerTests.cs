using NUnit.Framework;
using Moq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using RemoteRelay;

namespace RemoteRelayTests
{
    [TestFixture]
    public class SwitcherServerTests
    {
        private Mock<Socket> _mockSocket;
        private Mock<SwitcherState> _mockSwitcherState;
        private SwitcherServer _switcherServer;

        [SetUp]
        public void Setup()
        {
            _mockSocket = new Mock<Socket>(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _mockSwitcherState = new Mock<SwitcherState>(new AppSettings());
            _switcherServer = new SwitcherServer(8080, new AppSettings());
        }

        [Test]
        public void Start_ShouldListenForConnections()
        {
            // Arrange
            _mockSocket.Setup(s => s.Listen(It.IsAny<int>()));

            // Act
            var startThread = new Thread(() => _switcherServer.Start());
            startThread.Start();
            Thread.Sleep(1000); // Give some time for the server to start

            // Assert
            _mockSocket.Verify(s => s.Listen(It.IsAny<int>()), Times.Once);
        }

        [Test]
        public void AcceptCallback_ShouldAcceptConnection()
        {
            // Arrange
            var mockAsyncResult = new Mock<IAsyncResult>();
            _mockSocket.Setup(s => s.EndAccept(mockAsyncResult.Object)).Returns(_mockSocket.Object);

            // Act
            _switcherServer.GetType().GetMethod("AcceptCallback", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(_switcherServer, new object[] { mockAsyncResult.Object });

            // Assert
            _mockSocket.Verify(s => s.EndAccept(mockAsyncResult.Object), Times.Once);
        }

        [Test]
        public void ReadCallback_ShouldReadData()
        {
            // Arrange
            var mockAsyncResult = new Mock<IAsyncResult>();
            var stateObject = new StateObject { WorkSocket = _mockSocket.Object };
            stateObject.Buffer = Encoding.ASCII.GetBytes("Test data");
            _mockSocket.Setup(s => s.EndReceive(mockAsyncResult.Object)).Returns(stateObject.Buffer.Length);

            // Act
            _switcherServer.GetType().GetMethod("ReadCallback", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(_switcherServer, new object[] { mockAsyncResult.Object });

            // Assert
            Assert.AreEqual("Test data", stateObject.Sb.ToString());
        }

        [Test]
        public void ProcessData_ShouldProcessSwitchCommand()
        {
            // Arrange
            var mockSocket = new Mock<Socket>(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var data = "SWITCH source output";

            // Act
            _switcherServer.GetType().GetMethod("ProcessData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(_switcherServer, new object[] { data, mockSocket.Object });

            // Assert
            _mockSwitcherState.Verify(s => s.SwitchSource("source", "output"), Times.Once);
        }

        [Test]
        public void SendCallback_ShouldSendData()
        {
            // Arrange
            var mockAsyncResult = new Mock<IAsyncResult>();
            _mockSocket.Setup(s => s.EndSend(mockAsyncResult.Object));

            // Act
            _switcherServer.GetType().GetMethod("SendCallback", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(_switcherServer, new object[] { mockAsyncResult.Object });

            // Assert
            _mockSocket.Verify(s => s.EndSend(mockAsyncResult.Object), Times.Once);
        }
    }
}
