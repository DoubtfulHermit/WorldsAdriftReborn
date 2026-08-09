using System.Net.Sockets;
using NetCoreServer;
using WorldsAdriftServer.Handlers.Admin;
using WorldsAdriftServer.Handlers.Authentication;
using WorldsAdriftServer.Handlers.CharacterScreen;
using WorldsAdriftServer.Handlers.ServerStatus;
using WorldsAdriftServer.Persistence;

namespace WorldsAdriftServer.Handlers
{
    internal class RequestRouterHandler : HttpSession
    {
        public RequestRouterHandler( HttpServer server ) : base(server) { }

        protected override void OnReceived( byte[] buffer, long offset, long size )
        {
            // OnReceived isn't guaranteed to get entire request. Report what was received.
            if (buffer != null && size != 0)  { DataParser.ParseIncomingData(buffer, offset, size); }
            base.OnReceived(buffer, offset, size);
        }

        // NOTE: OnReceivedRequest is only called once a complete request has been constructed inside HttpRequest's _cache.
        protected override void OnReceivedRequest( HttpRequest request )
        {
            if(request != null)
            {
                // The operator dashboard takes any /admin* URL. Checked first and
                // self-contained: it is auth-gated end to end and shares nothing
                // with the player-facing routes below.
                if (AdminHandler.TryHandle(this, request))
                {
                    return;
                }

                if(request.Method == "POST" && request.Url == "/authenticate")
                {
                    SteamAuthenticationHandler.HandleAuthRequest(this, request);
                }
                else if (request.Method == "GET" && (request.Url == "/signup" || request.Url == "/signup/"))
                {
                    RegistrationHandler.HandleSignupPage(this);
                }
                else if (request.Method == "POST" && request.Url == "/register")
                {
                    RegistrationHandler.HandleRegister(this, request);
                }
                else if (request.Method == "GET" && request.Url.Contains("/characterList/") && request.Url.Contains("/steam/1234"))
                {
                    CharacterListHandler.HandleCharacterListRequest(this, request, "community_server");
                }
                else if (request.Method == "POST" && request.Url.Contains("/reserveCharacterSlot/") && request.Url.Contains("/steam/1234"))
                {
                    // no need to handle this as we provide the needed data in HandleCharacterListRequest()
                }
                else if(request.Method == "GET" && request.Url == "/deploymentStatus")
                {
                    // The server name is now operator-configurable via the admin
                    // panel (server_config table). Read it here so the in-game
                    // browser reflects a change without a redeploy; fall back to
                    // the historic default if the database cannot be reached, so
                    // this hot path never fails where a literal never did.
                    string serverName;
                    try
                    {
                        serverName = Accounts.ServerConfig.GetServerName();
                    }
                    catch (Exception)
                    {
                        serverName = WorldsAdriftReborn.Storage.Policy.ServerConfigPolicy.DefaultServerName;
                    }

                    DeploymentStatusHandler.HandleDeploymentStatusRequest(this, request, serverName, "community_server", 0);
                }
                else if(request.Method == "GET" && request.Url == "/authorizeCharacter")
                {
                    CharacterAuthHandler.HandleCharacterAuth(this, request);
                }
                else if(request.Method == "POST" && request.Url.Contains("/character/") && request.Url.Contains("/steam/1234/"))
                {
                    CharacterSaveHandler.HandleCharacterSave(this, request, "community_server");
                }
            }
        }

        protected override void OnReceivedRequestError( HttpRequest request, string error )
        {
            Console.WriteLine("Request error: " + error);
        }

        protected override void OnError( SocketError error )
        {
            Console.WriteLine("Socket error: " + error.ToString());
        }
    }
}
