using System;
using System.Collections.Generic;
using System.Text;

namespace SmartInverterTest1
{
    public class AppSettings
    {
        public string serialport { get; set; }
        public string rsignalhost { get; set; }
        public string[] inverters { get; set; }
        public string radio { get; set; }
        public string sunrisesunseturl { get; set; }
    }
}
