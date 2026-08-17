//jshint esversion: 6
'use strict';

angular.module('waEngineCalc').service('materialData', [
    '$q',
    function($q) {
        ////////////////////////////////////////////
        // bindable properties
        ////////////////////////////////////////////
        this.data = {};

        //query material power boost table from Google spreadsheet
        this.init = function () {
            return $q((resolve, reject) => {
                sheetrock({
                    url: 'https://docs.google.com/spreadsheets/d/1RBskFnl2LbcOv9Dr_eLbeUkFY8h8ZMVhwmT5opuGnNg/edit#gid=0',
                    query: 'select B,C,D,E,F,G,H,I,J,K,L,M,N,O,P,Q,R,S,T,U,V,W,X offset 1',
                    callback: (error, options, response) => {
                        if(!error) {
                            for(var ii = 0; ii<response.rows.length; ii++){
                                var matName = response.rows[ii].cellsArray[0];
                                if(!this.data[matName])
                                    this.data[matName] = {};
                                this.data[matName].standardWpU =  Number(response.rows[ii].cellsArray[2]);
                                this.data[matName].materialClass = response.rows[ii].cellsArray[1];
                                if(!this.data[matName].statBoosts)
                                    this.data[matName].statBoosts = {};
                                this.data[matName].statBoosts['Combustion Internals'] = { 
                                    Power: response.rows[ii].cellsArray.slice(3,13).map(Number)
                                };
                                this.data[matName].statBoosts.Propeller = {
                                    Power: response.rows[ii].cellsArray.slice(13).map(Number)
                                };
                            }
                            this.materialNames = Object.keys(this.data);
                            resolve();
                        }
                        else
                            reject(error);
                    }
                });
            });
        };
    }
]
);