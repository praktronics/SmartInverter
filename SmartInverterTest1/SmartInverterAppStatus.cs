using System;
using System.Collections.Generic;
using System.Text;

namespace SmartInverterTest1
{
    class SmartInverterAppStatus
    {
        public DateTime LaunchTime { get; }
        public DateTime Sunrise { get; }
        public DateTime Sunset { get; }
        public TimeSpan SunriseOffset { get; set; }
        public TimeSpan SunsetOffset { get; set; }
        public StatusMessage LastStatusMessage {get; set;}
        public CommsStatus CommunicationStatus { get; set; }

    }

    enum CommsStatus { OK, Retrying, Failed}
}
