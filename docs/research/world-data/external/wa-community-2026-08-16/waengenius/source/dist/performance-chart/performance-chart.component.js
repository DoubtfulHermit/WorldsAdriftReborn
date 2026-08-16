/* 
 * To change this license header, choose License Headers in Project Properties.
 * To change this template file, choose Tools | Templates
 * and open the template in the editor.
 */
/* jshint esversion: 6 */
 'use strict';

angular.module('waEngineCalc').component('performanceChart', {
    templateUrl: 'performance-chart/performance-chart.template.html',
    bindings: {
        focusedEngineSet: '<',
        focusedSlot: '<',
        engineName: '<',
        performanceData: '<',
        materialNames: '<'
    },
    controller: ['$scope',
        function($scope){
            //console.log('chart init');
            Chart.defaults.global.elements.line.fill = false;
            Chart.defaults.global.colors = [
                "#269a2b",
                "#305193",
                "#dcc417",
                "#b73628",
                "#8a49a9",
                "#40c6c6",
                "#e31ae5",
                "#3fe432",
                "#f56c05",
                "#d7f40c",
                //"#ff1265",
                "#979797",
                "#000000"
            ]

            this.graphMode = 'speed';
            this.graphData = [];
            this.override = [];
            this.setGraphMode = function() {
                console.log('setting graph Mode ' + this.graphMode);
                $scope.$emit('graphMode:set',this.graphMode);
                switch(this.graphMode) {
                    case 'speed': {
                            this.options.scales.yAxes[0].scaleLabel.labelString = 'Speed [kn]';
                            break;
                    }
                    case 'power:set':
                    case 'power:ship': {
                            this.options.scales.yAxes[0].scaleLabel.labelString = 'Power';
                            break;
                    }
                    case 'massEfficiency': {
                            this.options.scales.yAxes[0].scaleLabel.labelString = 'Mass Efficiency';
                    }
                }
            };
            
            this.$onChanges = function(changesObj) {
                //console.log(changesObj.toString());
                if(changesObj.materialNames && changesObj.materialNames.currentValue) {
                    /*this.override.borderColor = [];
                    this.override.backgroundColor = [];
                    let colorNames = Object.keys(jQuery.Color.names); */
                    for(let cc=0; cc<this.materialNames.length; cc++) {
                        //let color = jQuery.Color(colorNames[cc]);
                        this.override[cc] = {};
                        /*this.override[cc].borderColor = color.toHexString();
                        this.override[cc].backgroundColor = 'transparent';
                        this.override[cc].pointBackgroundColor = color.toHexString();
                        this.override[cc].pointHoverBorderColor = color.toHexString();
                        this.override[cc].pointHoverBackgroundColor = 'transparent';// color.toHexString(); */
                        this.override[cc].pointHoverRadius = 8;
                    }
                }
            };
            this.labels = [1,2,3,4,5,6,7,8,9,10];
            this.options = {
                scales: {
                    yAxes: [
                    {
                        id: 'y-axis-1',
                        type: 'linear',
                        display: true,
                        position: 'right',
                        scaleLabel: {
                            display: true,
                            labelString: "Speed [kn]",
                            fontFamily: $('body').css('font-family'),
                            fontColor: "black",
                            fontSize: 13
                        }
                    }],
                    xAxes: [
                    {
                        scaleLabel: {
                            display: true,
                            labelString: 'Quality',
                            fontFamily: $('body').css('font-family'),
                            fontColor: "black",
                            fontSize: 13
                        }
                    }]
                },
                legend: {
                    display : true,
                    position: 'bottom',
                    labels: {
                        fontFamily: $('body').css('font-family'),
                        fontSize: 14,
                        fontColor: '#000000',
                        usePointStyle: true
                    }
                    /*onClick: (event, legenditem) => {
                        console.log('clicked: ' + legenditem);
                    }*/
                },
                layout: {
                    padding: 10
                },
                tooltips: {
                    mode: 'point',
                    itemSort: (a,b) => {
                        if(a.y>b.y)
                            return 1;
                        else if(a.y<b.y) 
                            return -1;
                        return 0;
                    },
                    callbacks: {
                        label: (item, data) => {
                            return this.materialNames[item.datasetIndex] + ": " + parseFloat(item.yLabel).toFixed(2);
                        }
                    }
                }
              };
        }
    ]
});

