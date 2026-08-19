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

            // After the database is open, because it writes to it; before the
            // socket, so the first minute of an audience is recorded rather than
            // missed. Never throws: see ViewerSampler.Tick.
            PublicMap.ViewerSampler.Start();

            Console.WriteLine("[info] login/REST server listening on TCP " + restPort + ".");
            Console.WriteLine("[info] sign-up page at http://<this host>:" + restPort + "/signup");

            // Say out loud where alliance crests will be fetched from, and shout
            // if that is a scheme the GAME cannot use.
            //
            // This is not decoration. The crest URL is normally taken from the
            // request being answered (Emblems.EmblemOrigin), so this fallback is
            // only reached by a request with no Host header - but when it IS
            // reached and it is https, the emblem silently never appears: the
            // client's Mono TLS stack has no protocol above TLS 1.0, a modern
            // proxy answers that with a protocol_version alert, and the rejected
            // download leaves the alliance panel showing its placeholder. That
            // failure has no visible tell anywhere else, so it gets a line here.
            Emblems.EmblemImages.ReportConfiguration(Console.WriteLine);

            RequestRouterServer restServer = new RequestRouterServer(IPAddress.Any, restPort);

            //server.AddStaticContent() here to add some filesystem path to serve
            restServer.Start();

            WaitForShutdown();

            restServer.Stop();
        }

        /// <summary>
        /// Blocks until the operator stops the server.
        ///
        /// Console.ReadKey() throws on a redirected stdin, which is why the
        /// systemd unit used to wrap this in script(1) to fake a terminal, with
        /// a "sleep infinity" to keep that terminal from reaching EOF. That is
        /// two workarounds for one missing branch: under a service manager there
        /// is no keyboard, and the stop signal is SIGTERM.
        ///
        /// So it waits on the signal when there is no terminal, and keeps the
        /// keypress when a person is running it by hand. Ctrl+C is handled in
        /// both cases, and Cancel = true stops the runtime killing the process
        /// before the socket is closed.
        /// </summary>
        private static void WaitForShutdown()
        {
            using ManualResetEventSlim stop = new ManualResetEventSlim(false);

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                stop.Set();
            };

            AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Set();

            if (Console.IsInputRedirected)
            {
                Console.WriteLine("[info] running headless; stop with SIGTERM (systemctl stop) or Ctrl+C.");
                stop.Wait();
                return;
            }

            Console.WriteLine("press a key to stop");

            while (!stop.IsSet)
            {
                if (Console.KeyAvailable)
                {
                    Console.ReadKey(true);
                    return;
                }

                stop.Wait(TimeSpan.FromMilliseconds(200));
            }
        }
    }
}
