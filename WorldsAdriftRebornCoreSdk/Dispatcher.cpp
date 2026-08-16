#include "Dispatcher.h"
#include <corecrt_malloc.h>
#include "Logger.h"
#include <string>

void Dispatcher::RegisterAddEntityCallback(AddEntityCallback callback, void* GCHandle) { this->addEntityCallback = callback; this->GCHandle = GCHandle; }
void Dispatcher::RegisterAssetLoadRequestCallback(AssetLoadRequestCallback callback, void* GCHandle) { this->assetLoadRequestCallback = callback; this->GCHandle = GCHandle; }
void Dispatcher::RegisterAddComponentCallback(AddComponentCallback callback, void* GCHandle) { this->addComponentCallback = callback; this->GCHandle = GCHandle; }
void Dispatcher::RegisterRemoveComponentCallback(RemoveComponentCallback callback, void* GCHandle) { this->removeComponentCallback = callback; this->GCHandle = GCHandle; }
void Dispatcher::RegisterAuthorityChangeCallback(AuthorityChangeCallback callback, void* GCHandle) { this->authorityChangeCallback = callback; this->GCHandle = GCHandle; }
void Dispatcher::RegisterComponentUpdateCallback(ComponentUpdateCallback callback, void* GCHandle) { this->componentUpdateCallback = callback; this->GCHandle = GCHandle; }
void Dispatcher::RegisterRemoveEntityCallback(RemoveEntityCallback callback, void* GCHandle) { this->removeEntityCallback = callback; this->GCHandle = GCHandle; }

void Dispatcher::Process(OpList* op_list) {
    if (op_list != nullptr && op_list->addEntityOp != nullptr) {
        // userptr needs to be set to this dispatcher or you get a nullref in c# land
        this->addEntityCallback(this->GCHandle, op_list->addEntityOp);
    }
    if (op_list != nullptr && op_list->assetLoadRequestOp != nullptr) {
        this->assetLoadRequestCallback(this->GCHandle, op_list->assetLoadRequestOp);

        //free(op_list->assetLoadRequestOp->AssetType);
        //free(op_list->assetLoadRequestOp->Name);
        //free(op_list->assetLoadRequestOp->Context);

        //delete op_list->assetLoadRequestOp;
    }
    if (op_list != nullptr && op_list->addComponentOp != nullptr) {
        for (int i = 0; i < op_list->addComponentLen; i++) {
            // need to copy over here because the EntityId is garbage in c# land if not done.
            AddComponentOp* op = new AddComponentOp();
            op->EntityId = op_list->addComponentOp[i].EntityId;
            op->InitialComponent = op_list->addComponentOp[i].InitialComponent;

            this->addComponentCallback(this->GCHandle, op);

            delete op;
        }
    }
    if (op_list != nullptr && op_list->authorityChangeOp != nullptr) {
        for (int i = 0; i < op_list->authorityChangeOpLen; i++) {
            AuthorityChangeOp* op = new AuthorityChangeOp();
            op->EntityId = op_list->authorityChangeOp[i].EntityId;
            op->ComponentId = op_list->authorityChangeOp[i].ComponentId;
            op->HasAuthority = op_list->authorityChangeOp[i].HasAuthority;

            this->authorityChangeCallback(this->GCHandle, op);

            delete op;
        }
    }
    if (op_list != nullptr && op_list->componentUpdateOp != nullptr) {
        for (int i = 0; i < op_list->componentUpdateOpLen; i++) {
            ComponentUpdateOp* op = new ComponentUpdateOp();
            op->EntityId = op_list->componentUpdateOp[i].EntityId;
            op->Update = op_list->componentUpdateOp[i].Update;

            this->componentUpdateCallback(this->GCHandle, op);

            delete op;
        }
    }
    // SpatialOS removes component state before the entity object. Generated
    // component stores use these callbacks to forget the entity id; omitting
    // them leaves a later re-checkout reporting every component as a duplicate.
    if (op_list != nullptr && op_list->removeComponentOp != nullptr) {
        if (this->removeComponentCallback != nullptr) {
            for (int i = 0; i < op_list->removeComponentLen; i++) {
                this->removeComponentCallback(this->GCHandle, &op_list->removeComponentOp[i]);
            }
        }
        delete[] op_list->removeComponentOp;
        op_list->removeComponentOp = nullptr;
        op_list->removeComponentLen = 0;
    }
    if (op_list != nullptr && op_list->removeEntityOp != nullptr && this->removeEntityCallback != nullptr) {
        this->removeEntityCallback(this->GCHandle, op_list->removeEntityOp);
        delete op_list->removeEntityOp;
        op_list->removeEntityOp = nullptr;
    }
}
