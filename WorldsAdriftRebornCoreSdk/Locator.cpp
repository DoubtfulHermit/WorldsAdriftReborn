#include "Locator.h"
#include <cstdlib>
#include <string>

Locator::Locator(std::string hostname, LocatorParameters* parameters)
{
	m_hostname = hostname;
}

DeploymentListFuture* Locator::GetDeploymentListAsync()
{
    return new DeploymentListFuture();
}

ConnectionFuture* Locator::ConnectAsync(char* deployment_name, ConnectionParameters* parameters, void* data, QueueStatusCallback callback)
{
    ENetHost* client = NULL;
    
    if (ENet_Initialize() < 0) {
        Logger::Debug("[ERROR] Could not initialize ENet, no networking possible.");
    }
    else {
        // port set to 0 means its a client and not a server
        client = ENet_Create_Host(0, 1, 5, 0, 0);

        if (client == NULL) {
            Logger::Debug("[ERROR] Could not create an ENet client, no networking possible.");
        }
    }

    // Game server port. Configurable so a client can reach a server hosted
    // where 7777 is already taken (e.g. a VPS running another game server).
    // The mod calls the exported WAR_SetGamePort() at startup; an environment
    // variable does NOT work here (this DLL's CRT caches the environment at load
    // time, so a value set later by .NET is invisible to getenv()).
    extern int g_warGamePort;
    int port = (g_warGamePort > 0) ? g_warGamePort : 7777;
    Logger::Debug(("[INFO] connecting to game server port " + std::to_string(port)).c_str());

    return new ConnectionFuture((char*)m_hostname.c_str(), port, parameters, client);
}
