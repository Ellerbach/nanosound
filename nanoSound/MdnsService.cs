using System;
using System.Diagnostics;
using System.Net;
using CCSWE.nanoFramework.MdnsServer;

namespace nanoSound
{
    public static class MdnsService
    {
        public const string Hostname = "nanoSound";

        private static MdnsServer _server;

        public static void Start()
        {
            try
            {
                MdnsServerOptions options = new MdnsServerOptions
                {
                    Hostname = Hostname,
                    IPAddress = IPAddress.Parse(AccessPointService.Address)
                };
                options.AddService(new MdnsServiceRegistration(
                    Hostname,
                    MdnsServiceType.Http,
                    80,
                    "path=/"));

                _server = new MdnsServer(options, null);
                if (_server.Start())
                {
                    Debug.WriteLine("mDNS is ready at http://" + Hostname + ".local/");
                }
                else
                {
                    Debug.WriteLine("mDNS could not start; use http://" + AccessPointService.Address + "/ instead.");
                    _server.Dispose();
                    _server = null;
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine("mDNS initialization failed: " + exception.Message);
                if (_server != null)
                {
                    _server.Dispose();
                    _server = null;
                }
            }
        }
    }
}