/* jshint esversion: 6 */
'use strict';

angular.module('waEngineCalc').filter('arrayByMaterialClass',
    ()=> {
        return function(items, targetClass) {
            var out = [];
            angular.forEach(Object.keys(items), element => { 
                var matData = items[element];
                if(matData.materialClass === targetClass)
                    out.push(element);
                
            });
            return out;
        };
    }
);