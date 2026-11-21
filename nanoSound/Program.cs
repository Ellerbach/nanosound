using System;
using System.Diagnostics;
using System.Threading;
using Iot.Device.Yx5300;
using nanoFramework.Hardware.Esp32;

namespace nanoSound
{
    public static class Program
    {
        private const int Mp3PlayerRxPin = 4;
        private const int Mp3PlayerTxPin = 5;
        private static SoundWebServer _webServer;

        public static void Main()
        {
            Yx5300 player = null;
            try
            {
                Configuration.SetPinFunction(Mp3PlayerRxPin, DeviceFunction.COM2_RX);
                Configuration.SetPinFunction(Mp3PlayerTxPin, DeviceFunction.COM2_TX);
                player = new Yx5300("COM2");
                Thread.Sleep(1000);
            }
            catch (Exception exception)
            {
                Debug.WriteLine("MP3 player initialization failed: " + exception.Message);
            }

            SoundCatalog catalog = new SoundCatalog();
            catalog.Initialize(player);

            bool networkReady = AccessPointService.Start(catalog.AccessPointName, catalog.AccessPointPassword);
            if (networkReady)
            {
                InitializeWebServer(catalog);
                MdnsService.Start();
            }
            else
            {
                Debug.WriteLine("Network services are not started because the SoftAP is not ready.");
            }

            if (player != null)
            {
                player.Volume(catalog.Volume);
            }
            int appliedRevision = catalog.Revision;
            Random random = new Random();
            Debug.WriteLine(networkReady
                ? "Scheduled sounds and web controls are ready."
                : "Scheduled sounds are ready; web controls will start after the SoftAP is ready.");

            while (true)
            {
                if (catalog.Revision != appliedRevision)
                {
                    if (player != null)
                    {
                        player.Volume(catalog.Volume);
                    }

                    appliedRevision = catalog.Revision;
                }

                if (catalog.TryTakeTrack(out int folder, out int track) ||
                    catalog.TryGetScheduledTrack(DateTime.UtcNow, random, out folder, out track))
                {
                    if (player != null)
                    {
                        PlayTrack(player, folder, track);
                    }
                }

                Thread.Sleep(500);
            }
        }

        private static void InitializeWebServer(SoundCatalog catalog)
        {
            _webServer = new SoundWebServer(catalog);
            _webServer.Start();
        }

        private static void PlayTrack(Yx5300 player, int folder, int track)
        {
            Debug.WriteLine("Playing folder " + folder.ToString() + " track " + track.ToString());
            if (!player.PlaySpecific(folder, track))
            {
                Yx5300.Status status = player.GetStatus();
                Debug.WriteLine("Playing failed. Core: " + status.Code + " Data: " + status.Data);
            }
        }
    }
}