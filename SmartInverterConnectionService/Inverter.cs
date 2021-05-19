using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace SmartInverterConnectionService
{
    public class Inverter
    {
        [JsonProperty]
        public string Id { get; set; }
        public StatusMessage LastMessage { get; set; } = null;
        public StatusMessage CurrentMessage { get; set; } = null;
        [JsonProperty]
        public double TotalACEnergy { get; set; } = 0.0;
        [JsonProperty]
        public double TotalDCEnergy { get; set; } = 0.0;
        
        public int NoResponseCount = 0;
        public bool ReadError = false;

        public double CalculateEnergy()
        {
            double ACEnergy = 0.0;
            
            if (LastMessage != null)
            {
                double secs = (CurrentMessage.StatusTime - LastMessage.StatusTime).TotalSeconds;
                TotalACEnergy += ACEnergy = secs * 0.5 * (LastMessage.ACVoltage * LastMessage.ACCurrent + CurrentMessage.ACVoltage * CurrentMessage.ACCurrent);
                TotalDCEnergy += secs * 0.5 * (LastMessage.DCVoltage * LastMessage.DCCurrent + CurrentMessage.DCVoltage * CurrentMessage.DCCurrent);

            }
            else return 0.0;

            return ACEnergy;
        }
    }
}
