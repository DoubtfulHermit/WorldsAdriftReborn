/* 
 * To change this license header, choose License Headers in Project Properties.
 * To change this template file, choose Tools | Templates
 * and open the template in the editor.
 */
/* jshint esversion: 6 */
 'use strict';


angular.module('waEngineCalc').component('engineSetEditor', {
    templateUrl: 'engine-set-editor/engine-set-editor.template.html',
    bindings:   
        { 
            setId: '<',
            engineSetData: '<' ,
            materialNames: '<',
            materialData: '<',
            engineList: '<',
            engineSetBeingEdited: '<',
            enginePartData: '<',
            engineSchemData: '<',
            onEngineSchemUpdate: '&',
            onEngineSetUpdate: '&'
        },

    controller: [
        '$scope', '$filter', 'engineParts',
        function($scope, $filter, engineParts) {
            this.focusComp = function(slotName) {
                try {
                    //console.log('focusing ' + slotName);
                    $scope.$emit('graphFocus:set', {setId: this.setId, compName: slotName});
                }
                catch(e) {}
            };
            this.updateEngineSet = function(property) {
                $scope.$emit('engineSet:changed', {engineSetData: this.engineSetData});
                this.onEngineSetUpdate({setId: this.setId, property: property, engineSetData: this.engineSetData});
            };
            this.removeEngineSet = () => {
                $scope.$emit('engineSet:remove', {setId: this.setId});
            };
            this.createEngineSchem = () => {
                $scope.$emit('engineSchem:create',{setId: this.setId});
            };
            this.editEngineSchem = () => {
                if(this.engineSetBeingEdited === this.engineSetData.setId)
                    $scope.$emit('engineSchem:stopEdit', {setId: this.setId});
                else
                    $scope.$emit('engineSchem:startEdit', {setId: this.setId});
            };
            
            /////////////////////////////////////////////////////////////////////////////////
            // notification from engineSchemEditor that schematic has changed
            /////////////////////////////////////////////////////////////////////////////////
            this.engineSchemUpdate = function(property, engineSchemData) {
                //console.log('Engine Schematic updated');
                this.onEngineSchemUpdate({engineId: this.engineSetData.engineId, property: property, engineSchemData: engineSchemData});
            };

            /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
            // verify each material slot has a valid material assigned, according to the current materialClass of the respective part
            /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
            this.validateSlotMaterials = function() {
                try {
                    let slots = {
                        Casing: 'case',
                        Propeller: 'prop'
                    };
                    angular.forEach(slots, (partId, slotName)=> {
                        //get target materialClass
                        let enginePartInfo = this.enginePartData[partId].find(part => { return part.name === this.engineSchemData.name[partId];}); 
                        let materialClass = enginePartInfo.materialClass;
                        let subMatList = $filter('arrayByMaterialClass')(this.materialData, materialClass);
                        if(!subMatList.find(matName => { return this.engineSetData.slots[slotName].material === matName;}))
                            this.engineSetData.slots[slotName].material = subMatList[0];
                    } );
                }
                catch(e) {}
            };
            
            ////////////////////////////////////////////////////////////////////////
            // notification that engine data has changed from parent component
            ////////////////////////////////////////////////////////////////////////
            this.$onChanges = function(changesObj)  {
                if(changesObj.engineSchemData) 
                    this.validateSlotMaterials();
                if(changesObj.materialData)
                    this.validateSlotMaterials();
                if(changesObj.enginePartData) 
                    this.validateSlotMaterials();
            };
        }
    ]}
);
