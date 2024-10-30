using System;
using System.Device.Gpio;
using System.Device.Gpio.Drivers;
using System.Threading;
using System.Collections.Generic;

namespace RemoteRelay
{
    public class MockGpioDriver : GpioDriver
    {
        private Dictionary<int, PinValue> _pinStates = new Dictionary<int, PinValue>();

        protected override int PinCount => 40;

        protected override int ConvertPinNumberToLogicalNumberingScheme(int pinNumber)
        {
            Console.WriteLine($"Converting pin {pinNumber} to logical numbering scheme");
            return pinNumber;
        }

        protected override void OpenPin(int pinNumber)
        {
            Console.WriteLine($"Opening pin {pinNumber}");
        }

        protected override void ClosePin(int pinNumber)
        {
            Console.WriteLine($"Closing pin {pinNumber}");
        }

        protected override void SetPinMode(int pinNumber, PinMode mode)
        {
            Console.WriteLine($"Setting pin {pinNumber} to mode {mode}");
        }

        protected override PinMode GetPinMode(int pinNumber)
        {
            Console.WriteLine($"Getting pin mode for pin {pinNumber}");
            return PinMode.Input;
        }

        protected override bool IsPinModeSupported(int pinNumber, PinMode mode)
        {
            Console.WriteLine($"Checking if pin {pinNumber} supports mode {mode}");
            return true;
        }

        protected override void Write(int pinNumber, PinValue value)
        {
            Console.WriteLine($"Writing value {value} to pin {pinNumber}");
            _pinStates[pinNumber] = value;
        }

        protected override WaitForEventResult WaitForEvent(int pinNumber, PinEventTypes eventTypes, CancellationToken cancellationToken)
        {
            Console.WriteLine($"Waiting for event {eventTypes} on pin {pinNumber}");
            return new WaitForEventResult();
        }

        protected override PinValue Read(int pinNumber)
        {
            Console.WriteLine($"Reading value from pin {pinNumber}");
            return _pinStates.ContainsKey(pinNumber) ? _pinStates[pinNumber] : PinValue.Low;
        }

        protected override void AddCallbackForPinValueChangedEvent(int pinNumber, PinEventTypes eventTypes, PinChangeEventHandler callback)
        {
            Console.WriteLine($"Adding callback for pin {pinNumber} on event {eventTypes}");
        }

        protected override void RemoveCallbackForPinValueChangedEvent(int pinNumber, PinChangeEventHandler callback)
        {
            Console.WriteLine($"Removing callback for pin {pinNumber}");
        }

        protected WaitForEventResult WaitForEvent(int pinNumber, PinEventTypes eventTypes, TimeSpan timeout)
        {
            Console.WriteLine($"Waiting for event {eventTypes} on pin {pinNumber} with timeout {timeout}");
            return new WaitForEventResult();
        }

        protected override void Dispose(bool disposing)
        {
            Console.WriteLine("Disposing MockGpioDriver");
            base.Dispose(disposing);
        }
    }
}
