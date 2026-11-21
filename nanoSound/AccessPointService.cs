using System;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using Iot.Device.DhcpServer;
using nanoFramework.Hardware.Esp32;

namespace nanoSound
{
    public static class AccessPointService
    {
        public const string Address = "192.168.4.1";
        public const string Ssid = "nanoSound";
        public const string Password = "LaurentIsTheBest";

        private static DhcpServer _dhcpServer;

        public static bool Start(string ssid, string password)
        {
            NetworkInterface accessPointInterface = FindInterface(NetworkInterfaceType.WirelessAP);
            if (accessPointInterface == null)
            {
                throw new InvalidOperationException("The firmware does not expose a WirelessAP interface.");
            }

            WirelessAPConfiguration configuration =
                WirelessAPConfiguration.GetAllWirelessAPConfigurations()[accessPointInterface.SpecificConfigId];
            WirelessAPConfiguration.ConfigurationOptions requiredOptions =
                WirelessAPConfiguration.ConfigurationOptions.AutoStart |
                WirelessAPConfiguration.ConfigurationOptions.Enable;

            bool configurationChanged = (configuration.Options & requiredOptions) != requiredOptions ||
                configuration.Ssid != ssid ||
                configuration.Password != password ||
                configuration.Authentication != System.Net.NetworkInformation.AuthenticationType.WPA2 ||
                configuration.Encryption != EncryptionType.WPA2 ||
                configuration.MaxConnections != 4;

            if (configurationChanged)
            {
                if (Sleep.GetWakeupCause() == Sleep.WakeupCause.Timer)
                {
                    Debug.WriteLine("SoftAP configuration still differs after restart; automatic retry stopped.");
                    return false;
                }

                DisableStationInterface();
                accessPointInterface.EnableStaticIPv4(Address, "255.255.255.0", Address);
                configuration.Options = requiredOptions;
                configuration.Ssid = ssid;
                configuration.Password = password;
                configuration.Authentication = System.Net.NetworkInformation.AuthenticationType.WPA2;
                configuration.Encryption = EncryptionType.WPA2;
                configuration.MaxConnections = 4;
                configuration.SaveConfiguration();
                EspNativeError wakeupResult = Sleep.EnableWakeupByTimer(TimeSpan.FromSeconds(1));
                if (wakeupResult != EspNativeError.OK)
                {
                    Debug.WriteLine("SoftAP configuration saved, but wake-up timer failed: " + wakeupResult.ToString() + ". Restart the device manually.");
                    return false;
                }

                Debug.WriteLine("SoftAP configuration saved. Restarting to activate it.");
                Sleep.StartDeepSleep();
                return false;
            }

            _dhcpServer = new DhcpServer
            {
                CaptivePortalUrl = "http://" + Address + "/"
            };

            if (!_dhcpServer.Start(
                IPAddress.Parse(Address),
                new IPAddress(new byte[] { 255, 255, 255, 0 })))
            {
                Debug.WriteLine("The SoftAP DHCP server could not start. No automatic reboot will be attempted.");
                return false;
            }

            Debug.WriteLine("SoftAP " + ssid + " is ready at http://" + Address + "/");
            return true;
        }

        private static void DisableStationInterface()
        {
            NetworkInterface stationInterface = FindInterface(NetworkInterfaceType.Wireless80211);
            if (stationInterface == null)
            {
                return;
            }

            Wireless80211Configuration configuration =
                Wireless80211Configuration.GetAllWireless80211Configurations()[stationInterface.SpecificConfigId];

            if (configuration.Options == Wireless80211Configuration.ConfigurationOptions.None &&
                string.IsNullOrEmpty(configuration.Ssid) &&
                string.IsNullOrEmpty(configuration.Password))
            {
                return;
            }

            configuration.Options = Wireless80211Configuration.ConfigurationOptions.None;
            configuration.Ssid = string.Empty;
            configuration.Password = string.Empty;
            configuration.SaveConfiguration();
        }

        private static NetworkInterface FindInterface(NetworkInterfaceType interfaceType)
        {
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface networkInterface in interfaces)
            {
                if (networkInterface.NetworkInterfaceType == interfaceType)
                {
                    return networkInterface;
                }
            }

            return null;
        }
    }
}