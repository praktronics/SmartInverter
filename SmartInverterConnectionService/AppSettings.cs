using System;
using System.Collections.Generic;
using System.Text;

namespace SmartInverterConnectionService
{
    public class AppSettings
    {
        public string serialport { get; set; }
        public string rsignalhost { get; set; }
        public string[] inverters { get; set; }
        public string radio { get; set; }
        public string sunrisesunseturl { get; set; }
        public bool writeoutputtologfile { get; set; }
        public string logfilepath { get; set; }
        public string latitude { get; set; }
        public string longitude { get; set; }

    }
}
