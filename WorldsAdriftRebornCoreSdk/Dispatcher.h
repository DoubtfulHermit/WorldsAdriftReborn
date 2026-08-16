#pragma once
#include "Callbacks.h"
#include "OpList.h"
class Dispatcher
{
private:
    void* GCHandle = nullptr;
    AddEntityCallback* addEntityCallback = nullptr;
    AssetLoadRequestCallback* assetLoadRequestCallback = nullptr;
    AddComponentCallback* addComponentCallback = nullptr;
    RemoveComponentCallback* removeComponentCallback = nullptr;
    AuthorityChangeCallback* authorityChangeCallback = nullptr;
    ComponentUpdateCallback* componentUpdateCallback = nullptr;
    RemoveEntityCallback* removeEntityCallback = nullptr;
public:
    void RegisterAddEntityCallback(AddEntityCallback callback, void* GCHandle);
    void RegisterAssetLoadRequestCallback(AssetLoadRequestCallback callback, void* GCHandle);
    void RegisterAddComponentCallback(AddComponentCallback callback, void* GCHandle);
    void RegisterRemoveComponentCallback(RemoveComponentCallback callback, void* GCHandle);
    void RegisterAuthorityChangeCallback(AuthorityChangeCallback callback, void* GCHandle);
    void RegisterComponentUpdateCallback(ComponentUpdateCallback callback, void* GCHandle);
    void RegisterRemoveEntityCallback(RemoveEntityCallback callback, void* GCHandle);

    void Process(OpList* op_list);
};
