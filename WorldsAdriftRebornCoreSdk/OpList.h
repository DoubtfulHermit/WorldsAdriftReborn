#pragma once
#include "Structs.h"

class OpList
{
public:
    AddEntityOp* addEntityOp = nullptr;
    RemoveEntityOp* removeEntityOp = nullptr;
    AssetLoadRequestOp* assetLoadRequestOp = nullptr;
    AddComponentOp* addComponentOp = nullptr;
    RemoveComponentOp* removeComponentOp = nullptr;
    AuthorityChangeOp* authorityChangeOp = nullptr;
    ComponentUpdateOp* componentUpdateOp = nullptr;
    int addComponentLen = 0;
    int removeComponentLen = 0;
    int authorityChangeOpLen = 0;
    int componentUpdateOpLen = 0;
};
