using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace SmartInverterConnectionService
{
    [JsonObject(MemberSerialization.OptIn)]
    public class StatusMessage
    {
        const int rawdatasize = 27;
        [JsonProperty]
        public DateTime StatusTime { get; set; }
        [JsonProperty]
        public byte[] Rawdata { get; set; } = new byte[rawdatasize];

        public int BytesRead { get; set; } = 0;
        public bool ReceiveComplete { get { return BytesRead == rawdatasize; } }

        public bool IsValidMessage { get; private set; } = false;
        [JsonProperty] 
        public short DCVoltageShort { get; set; }
        [JsonProperty]
        public short ACVoltageShort { get; set; }
        [JsonProperty] 
        public short DCCurrentShort { get; set; }
        [JsonProperty] 
        public short ACCurrentShort { get; set; }

        public double DCVoltage { get { return DCVoltageShort / 100.0; } }
        public double DCCurrent { get { return DCCurrentShort / 100.0; } }
        public double ACVoltage { get { return ACVoltageShort / 100.0; } }
        public double ACCurrent { get { return ACCurrentShort / 100.0; } }

        public int AddByte(byte newbyte)
        {
            // check if we've reached the end of the raw data buffer
            if (BytesRead >= rawdatasize) return 0;

            // otherwise add the next byte
            Rawdata[BytesRead] = newbyte;

            // if we are complete, process the data
            if (++BytesRead == rawdatasize) ProcessRawData();
            return BytesRead;
        }

        /// <summary>
        /// Processes the raw data into useful components
        /// </summary>
        private void ProcessRawData()
        {
            // confirm the checksum is correct before processing


            DCVoltageShort = (short)((Rawdata[15] << 8) | Rawdata[16]);
            DCCurrentShort = (short)((Rawdata[17] << 8) | Rawdata[18]);
            ACVoltageShort = (short)((Rawdata[19] << 8) | Rawdata[20]);
            ACCurrentShort = (short)((Rawdata[21] << 8) | Rawdata[22]);

            if ((DCVoltageShort >= 0) && (DCCurrentShort >= 0) && (ACVoltageShort >= 0) && (ACCurrentShort >= 0)) IsValidMessage = true; 

            //Console.WriteLine("Raw[15]: {0}, Raw[16]{1}, Temp = {2}", Rawdata[15], Rawdata[16], tmp);

        }

        /// <summary>
        /// Builds a request for the inverter status
        /// 
        /// last byte is checksum.  Add all previous 14 bytes and take the LSB of the sum as the 15th byte in the message
        /// Message structure:
        /// 01: 0x43 - start
        /// 02: 0xCx - command - C0 = status, C1 = set powerstate, C2 = ?, C3 = set powergrade
        /// 03: 0x10 - databox ID upper e.g. 1088
        /// 04: 0x88 - databox ID lower
        /// 05: 0x00
        /// 06: 0x00
        /// 07: 0x55 - inverter ID MSB e.g. 55000414
        /// 08: 0x00 
        /// 09: 0x04
        /// 10: 0x14 - inverter ID LSB
        /// 11: 0x00
        /// 12: 0x00
        /// 13: 0x00
        /// 14: 0x00 - data
        /// 15: 0x08 - checksum
        /// </summary>
        /// <param name="radio">2 x hex bytes of the radio id e.g. 1088</param>
        /// <param name="inverter">4 x hex bytes of the inverter id e.g. 55000414</param>
        /// <returns>
        /// A byte array that can be sent to the inverter to request its status
        /// </returns>
        static public byte[] BuildRequest(string radio, string inverter)
        {
            if (radio.Length != 4) throw new Exception("radio must be four characters long i.e. 2 x hex coded bytes");
            if(inverter.Length != 8) throw new Exception("inverter must be eight characters long i.e. 4 x hex coded bytes");

            byte[] retbuf = new byte[15];
            short cs = 0;
            

            retbuf[0] = 0x43; cs += retbuf[0];
            retbuf[1] = 0xC0; cs += retbuf[1];
            retbuf[2] = byte.Parse(radio.Substring(0, 2), System.Globalization.NumberStyles.HexNumber); cs += retbuf[2]; 
            retbuf[3] = byte.Parse(radio.Substring(2, 2), System.Globalization.NumberStyles.HexNumber); cs += retbuf[3];
            retbuf[4] = 0x00; cs += retbuf[4];
            retbuf[5] = 0x00; cs += retbuf[5];
            retbuf[6] = byte.Parse(inverter.Substring(0, 2), System.Globalization.NumberStyles.HexNumber); cs += retbuf[6];
            retbuf[7] = byte.Parse(inverter.Substring(2, 2), System.Globalization.NumberStyles.HexNumber); cs += retbuf[7];
            retbuf[8] = byte.Parse(inverter.Substring(4, 2), System.Globalization.NumberStyles.HexNumber); cs += retbuf[8];
            retbuf[9] = byte.Parse(inverter.Substring(6, 2), System.Globalization.NumberStyles.HexNumber); cs += retbuf[9];
            retbuf[10] = 0x00; cs += retbuf[10];
            retbuf[11] = 0x00; cs += retbuf[11];
            retbuf[12] = 0x00; cs += retbuf[12];
            retbuf[13] = 0x00; cs += retbuf[13];  
            retbuf[14] = (byte)cs;

            return retbuf;

        }

    }
}
