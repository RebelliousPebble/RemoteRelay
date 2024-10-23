using NUnit.Framework;
using Moq;
using System.Device.Gpio;
using RemoteRelay;

namespace RemoteRelayTests
{
    [TestFixture]
    public class SwitcherStateTests
    {
        private Mock<GpioController> _mockGpioController;
        private SwitcherState _switcherState;
        private AppSettings _settings;

        [SetUp]
        public void Setup()
        {
            _mockGpioController = new Mock<GpioController>();
            _settings = new AppSettings
            {
                Routes = new List<RelayConfig>
                {
                    new RelayConfig { SourceName = "Source1", OutputName = "Output1", RelayPin = 1 },
                    new RelayConfig { SourceName = "Source2", OutputName = "Output2", RelayPin = 2 }
                },
                DefaultSource = "Source1",
                PhysicalSourceButtons = new Dictionary<string, int>(),
                ServerName = "localhost",
                IsServer = true,
                Port = 8080,
                FlashOnSelect = false,
                ShowIpOnScreen = false,
                Logging = false,
                LogoFile = "logo.png"
            };
            _switcherState = new SwitcherState(_settings);
        }

        [Test]
        public void SwitchSource_ShouldEnableCorrectOutput()
        {
            // Arrange
            var source = "Source1";
            var output = "Output1";

            // Act
            _switcherState.SwitchSource(source, output);

            // Assert
            _mockGpioController.Verify(g => g.Write(1, PinValue.High), Times.Once);
            _mockGpioController.Verify(g => g.Write(2, PinValue.Low), Times.Once);
        }

        [Test]
        public void GetSystemState_ShouldReturnCorrectState()
        {
            // Arrange
            _mockGpioController.Setup(g => g.Read(1)).Returns(PinValue.High);
            _mockGpioController.Setup(g => g.Read(2)).Returns(PinValue.Low);

            // Act
            var state = _switcherState.GetSystemState();

            // Assert
            Assert.AreEqual("Output1", state["Source1"]);
            Assert.AreEqual("Output2", state["Source2"]);
        }
    }
}
