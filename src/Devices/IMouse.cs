using System;

namespace Nibble.Devices
{
    // A mouse driver. Everything above this interface is device-neutral; to support another mouse,
    // implement it and add the driver to Drivers.All.
    //
    // Calls other than the properties run on background threads and may block on USB I/O.
    interface IMouse
    {
        string Name { get; }          // short, for the tray and panels, e.g. "RK M3"
        string Model { get; }         // full product name
        MouseCaps Caps { get; }
        string VendorSite { get; }    // firmware links must start with this

        bool IsPresent();
        MouseStatus Query(bool withSettings);
        MouseSettings ReadSettings();
        bool Write(MouseSettings settings, SettingGroup group);
        bool FactoryReset();
        FirmwareInfo CheckFirmware();

        // Blocks until stop() returns true, reporting events pushed by the device.
        void Listen(Func<bool> stop, Action<DeviceEvent> onEvent);
    }

    enum SettingGroup { Dpi, PollingRate, Sleep, Sensor }

    sealed class MouseStatus
    {
        public bool Found;            // receiver or cable present
        public bool Online;           // the mouse itself answered
        public bool Charging, Wired;
        public int Percent = -1;
        public MouseSettings Settings;
    }

    enum DeviceEventKind
    {
        Battery,      // Percent and Charging are set
        DpiButton,    // the stage was switched on the mouse
        Link,         // Online is set
        Reattached    // the receiver or cable was plugged back in
    }

    sealed class DeviceEvent
    {
        public DeviceEventKind Kind;
        public int Percent;
        public bool Charging, Online;
    }
}
