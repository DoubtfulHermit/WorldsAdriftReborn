/* jshint esversion: 6 */
'use strict';

angular.module('waEngineCalc').service('engineParts', 
    function() {
        this.getPartByName = function(partType, partName) {
            try {
                return this.enginePartData[partType].find(part => {return part.name === partName;});
            }
            catch(e) {
                console.log(e);
            }
        };
        
        sheetrock({
            url: 'https://docs.google.com/spreadsheets/d/1RBskFnl2LbcOv9Dr_eLbeUkFY8h8ZMVhwmT5opuGnNg/edit#gid=153389871',
            query: 'select B,C,D,E,F,G',
            callback: (error, options, response) => {
                if(!error) {
                    this.enginePartData = { 
                        case: [],
                        head: [],
                        prop: []
                    };
                    for(let rr in response.rows) {
                        let idx = 0;
                        var partNames = Object.keys(this.enginePartData);
                        for(let pp in partNames) {
                            let partName = response.rows[rr].cellsArray[pp*2];
                            if(partName) {
                                this.enginePartData[partNames[pp]].push({ 
                                    name: partName,
                                    materialClass: response.rows[rr].cellsArray[pp*2+1]
                                });
                            }
                        }
                    }
                }
            }
        });
    }
);