using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace SmartInverterConnectionService
{
    [JsonObject(MemberSerialization.OptIn)]
    class ServiceState
    {
        [JsonProperty]
        public DateTime LoadTime { get; set; }
        [JsonProperty]
        public double TotalEnergy { get; set; } = 0.0;
        [JsonProperty]
        public DateTime StateTime { get; set; }
        [JsonProperty]
        public StatusMessage LastMessage { get; set; }

        public async Task SaveToFileAsync(FileStream f)
        {
            f.SetLength(0);
            //f.Seek(0, SeekOrigin.Begin);
            string s = JsonConvert.SerializeObject(this);
            StreamWriter sw = new StreamWriter(f);
            await sw.WriteAsync(s);
            await sw.FlushAsync();
        }

    }
}
