using System.Net;
using WorldsAdriftServer.Persistence;
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
            // Before the socket, so a bad connection string is a loud failure on
            // startup rather than a player staring at a login form that never
            // answers.
            try
            {
                Accounts.Initialize();
            }
            catch (Exception e)
            {
                Console.WriteLine("[fatal] could not open the account database. Set "
                    + WorldsAdriftReborn.Storage.Db.ConnectionStringVariable
                    + " to a reachable Postgres. Nobody can log in until this works.");
                Console.WriteLine(e);
                return;
            }

            Console.WriteLine("[info] login/REST server listening on TCP " + restPort + ".");
            Console.WriteLine("[info] sign-up page at http://<this host>:" + restPort + "/signup");

            RequestRouterServer restServer = new RequestRouterServer(IPAddress.Any, restPort);

            //server.AddStaticContent() here to add some filesystem path to serve
            restServer.Start();

            Console.WriteLine("enter something to stop");
            Console.ReadKey();

            restServer.Stop();
        }
    }
}
