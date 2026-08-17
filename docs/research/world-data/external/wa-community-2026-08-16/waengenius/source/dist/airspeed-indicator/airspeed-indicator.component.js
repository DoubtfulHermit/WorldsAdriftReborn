/*jshint esversion: 6 */
'use strict';

angular.module('waEngineCalc').component('airspeedIndicator', {
    templateUrl: 'airspeed-indicator/airspeed-indicator.template.html',
    bindings: {
        speed: '<'
    },
    controller: [
        '$document',
        function ($document) 
        {
            this.lastRot = 0;
            this.$onChanges = function(changesObj) {
                if(changesObj.speed) {
                    try {
                        let needle = SVG.get('airspeed_indicator_needle');
                        var newRot = changesObj.speed.currentValue / 80 * 360;
                        
                        if(!isNaN(newRot)) {
                            let diffRot = newRot - this.lastRot;
                            needle.animate(300).transform({rotation: diffRot, cx: 135.5, cy:135.5}, true);
                            this.lastRot = newRot;
                        }
                    }
                    catch(e) {  }
                }
            };
        }
    ]
});