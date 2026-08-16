/* 
 * To change this license header, choose License Headers in Project Properties.
 * To change this template file, choose Tools | Templates
 * and open the template in the editor.
 */
/* jshint esversion: 6 */
 'use strict';

 angular.module('waEngineCalc').component('engineSchemEditor', {
    templateUrl: 'engine-schem-editor/engine-schem-editor.template.html',
    bindings: {
        enginePartData: '<',
        engineSchemData: '<',
        onUpdate: '&'
    },
    
    controller: ['engineParts' , '$filter',
        function(engineParts, $filter) {
            //propagate changes on engine schematic to parent component
            this.update = function(property) {
                this.onUpdate({property: property, engineSchemData: this.engineSchemData });
            };
            
            this.validateProceduralParts = function() {
                try {
                    //filter list of engine heads to match the materialClass of the case
                    let caseMaterialClass = this.enginePartData.case.find(Case => {return Case.name === this.engineSchemData.name.case;}).materialClass;
                    this.validHeadModels = $filter('filter')(this.enginePartData.head, {materialClass: caseMaterialClass} );
                    if(!this.validHeadModels.find(head=> {return head.name === this.engineSchemData.name.head;})) 
                        this.engineSchemData.name.head = this.validHeadModels[0].name;

                    //automatically determine number from head's base power and engine power
                    let headData = this.enginePartData.head.find(head => { return head.name === this.engineSchemData.name.head; });
                    let newNum = this.engineSchemData.baseStats.power - headData.basePower;
                    if(newNum != this.engineSchemData.name.number) {
                        this.engineSchemData.name.number = newNum;
                        this.update('name.number');
                    }
                }
                catch(e) {}
            };
            //create a copy of the bound data on change
            this.$onChanges = function(changesObj) {
                if(changesObj.engineSchemData) {
                    this.engineSchemData = angular.copy(changesObj.engineSchemData.currentValue);
                    this.validateProceduralParts();
                }
                if(changesObj.enginePartData) {
                    this.validateProceduralParts();
                }
            };
        }
    ]
});

