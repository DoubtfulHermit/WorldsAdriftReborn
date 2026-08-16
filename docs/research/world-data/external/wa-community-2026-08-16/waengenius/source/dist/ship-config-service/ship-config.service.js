
//jshint esversion: 6
'use strict';

//an engine set consists of the id of an EngineSchem, the count of that engine 
//and the actual material configuration for their components
function EngineSet(schem) {
    this.setId = shortid.gen();
    //this.engineId = engineId;
    this.count = 2;
    this.schem = schem;
    this.slots = { 
        Casing: { material: 'Iron' , quality: 5 },
        'Combustion Internals': {material: 'Iron', quality: 5 },
        'Mechanical Internals': {material: 'Iron', quality: 5 },
        Propeller: { material: 'Iron', quality: 5 }
    };
    this.getIndividualPower = function(materialData) {
        try {
            //let schem = app.engineList.find((val) => { return val.id === this.engineId; }).schem;
            return schem.baseStats.power * 
            (1 + materialData[this.slots['Combustion Internals'].material].comb[this.slots['Combustion Internals'].quality - 1] +
            materialData[this.slots.Propeller.material].prop[this.slots.Propeller.quality - 1]);
        }
        catch(e) {
            //console.log(e);
        }
    };
    
    this.getPower = function() {
            return this.count * this.getIndividualPower();
    };
    this.getIndividualMass = function(materialData) {
        try {
            //let schem = engineList.find((val) => { return val.id === this.engineId; }).schem;
            let compKeys = Object.keys(this.slots);
            let mass = 0;
            for(let ss in compKeys) 
                mass += materialData[this.slots[compKeys[ss]].material].unitMass * EngineSchem.wpuFactors[schem.tier - 1] * schem.components[compKeys[ss]].matCount;
            return mass;
        }
        catch(e) {
            //console.log(e);
            return 0;
        }
    };
    this.getMass = function() {
        return this.count * this.getIndividualMass();
    };
}


//the actual ship configuration
function ShipConfig () {
    this.engines = [];
    this.baseMass = '1000';
    this.addPower = '0';
    this.getPower = function() {
        var power = 0;
        if(this.addPower.match('^[\\d\\(\\)\\+\\-\\*\\/\\.\\s]+$')) {
            try {
                power = eval(this.addPower);
            }
            catch(e) {
                power = 0;
            }
        }
        else
            power = 0;
        for(let ee in this.engines)  
            power += this.engines[ee].getPower();
        return power;
    };
    this.getMass = function() {
        let mass = 0;
        if(this.baseMass.match('^[\\d\\(\\)\\+\\-\\*\\/\\.\\s]+$')) {
            try {
                mass = eval(this.baseMass);
            }
            catch(e) {
                mass = 0;
            }
        }
        else
            mass = 0;
        for(let ee in this.engines)
            mass+= this.engines[ee].getMass();
        return mass;
    };
    this.getSpeed = function() {
        return 50* Math.sqrt(2 * this.getPower()/this.getMass());
    };
    this.clone = function()  {
        var copy = _.merge(new ShipConfig(),JSON.parse(JSON.stringify(this)));
        return copy;
    };
};


ShipConfig.Default = function () {
    var c = new ShipConfig();
    c.engines = [new EngineSet()];
    return c;
};


angular.module('waEngineCalc').service('shipConfig', [
    '$q',
    '$window',
    function($q, $window) {
        // properties
        this.shipList = [];
        this.currentShip = '';
        
        //read the ship list, and the current ship configuration from local storage, or create if blank defaults, if needed
        this.init = function() {
            return $q((resolve, reject) => {
                var storageData = $window.localStorage.getItem('ships');
                if(!storageData) {
                    let shipdesc = new this.ShipDesc();
                    this.shipList = [shipdesc];
                    this.currentShip = this.ShipConfig.Default();
                    $window.localStorage.setItem('ships', JSON.stringify(this.shipList));
                    $window.localStorage.setItem('ship:'+ shipdesc.id, JSON.stringify(this.currentShip));
                }
                else {
                    this.shipList = JSON.parse(storageData);
            
                    let storageShipData = this.window.localStorage.getItem('ship:' + this.shipList[0].id);
                    if(!storageShipData) {
                        this.currentShip = ShipConfig.Default();
                        $window.localStorage.setItem('ship:'+ this.shipList[0].id, JSON.stringify(this.currentShip));
                    }
                    else {
                        this.currentShip = _.merge(new ShipConfig(), JSON.parse(storageShipData));
                        for(let ee in this.currentShip.engines) {
                            this.currentShip.engines[ee] = _.merge(new EngineSet(), this.currentShip.engines[ee]);
                        }
                    }
                }
            });
        };
    
        //save the current ship configuration to local storage
        this.save = function() {
            console.log('saving ship config');
            $window.localStorage.setItem('ship:' + this.shipList[0].id, JSON.stringify(this.currentShip));
        };

        //add a new engine set to the current ship configuration
        this.addEngineSet = function () {
            this.currentShip.engines.push(new EngineSet());
            this.save();
        };

    }
]);