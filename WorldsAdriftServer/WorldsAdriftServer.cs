using System.Net;
using WorldsAdriftServer.Server;

namespace WorldsAdriftServer
{
    internal class WorldsAdriftServer
    {
        static void Main( string[] args )
        {
            // Configurable so the login server can run where 8080 is taken.
            // Override with WAREBORN_REST_PORT; defaults to the stock 8080.
            int restPort = 8080;
            string restPortEnv = Environment.GetEnvironmentVariable("WAREBORN_REST_PORT");
            if (!string.IsNullOrWhiteSpace(restPortEnv) && int.TryParse(restPortEnv, out int parsedRestPort))
            {
                restPort = parsedRestPort;
            }
            Console.WriteLine("[info] login/REST server listening on TCP " + restPort + ".");

            RequestRouterServer restServer = new RequestRouterServer(IPAddress.Any, restPort);

            //server.AddStaticContent() here to add some filesystem path to serve
            restServer.Start();

            Console.WriteLine("enter something to stop");
            Console.ReadKey();

            restServer.Stop();
        }
    }
}
