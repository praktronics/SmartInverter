using System;
using System.IO.Ports;
using System.Threading;
using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.SignalR.Client;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using System.Net.Http;
using Newtonsoft.Json;
using System.Text.Json;
using System.IO;

namespace SmartInverterTest1
{
    class Program
    {

        static SerialPort _serialPort;
        static AppSettings appSettings;
        static List<StatusMessage> statusMessages;
        static SmartInverterAppStatus appStatus;

        // signalR stuff
        static Task<bool> taskPowerHub;
        static CancellationToken tokenPowerHub;
        static HubConnection PowerHub;
        static int Main(string[] args)
        {
            var cmdList = new Command("list", "Lists the available serial ports");
            var cmdRun = new Command("run", "Logs the readings from the inverter")
            {
                new Argument<string>("port", "The serial port to use"),
                new Option(new string[] {"--untilsunset", "-u"}, "Runs logging until sunset"),
                new Option<int?>(new [] {"--cycles", "-c"}, "Number of readings to take"),
                new Option<string?>(new string[] {"--logfile", "-f"}, "Log file name")
                
            };
            var cmdTestEnc = new Command("testenc", "Test encoding approach for comms");
            var cmdSunrise = new Command("sunrise", "Get the sunrise and sunset times");

            cmdList.Handler = CommandHandler.Create<bool, IConsole>(doListPorts);
            cmdRun.Handler = CommandHandler.Create<string, IConsole, bool, int?, string?>(Run);
            cmdTestEnc.Handler = CommandHandler.Create<IConsole>(TestEncoding);
            cmdSunrise.Handler = CommandHandler.Create(GetSunriseSunset);

             var cmd = new RootCommand
            {
                cmdList,
                cmdRun,
                cmdTestEnc,
                cmdSunrise
            };

            appSettings = new AppSettings();

            ConfigurationBuilder cb = new ConfigurationBuilder();
            cb.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            var config = cb.Build();
            ConfigurationBinder.Bind(config.GetSection("AppSettings"), appSettings);

            return cmd.Invoke(args);
        }

        private static async Task Run(string port, IConsole console, bool untilsunset = false, int? cycles = 60, string? logfile = null)
        //    static async void Run(string port, IConsole console, int? cycles = 60, string? logfile = null)
        {
            //bool untilsunset = true;

            System.IO.StreamWriter? file = null;
            //System.IO.FileStream? fs = null;
            const int period = 5000;
            int noresponsecount = 0;

            double energy = 0.0;
            double dcenergy = 0.0;
            StatusMessage? previous = null;

            if (statusMessages == null) statusMessages = new List<StatusMessage>();

            if (logfile != null)
            {
                Console.WriteLine("using file name {0}", logfile);

                try
                {
                    //fs = File.OpenWrite(logfile);
                    file = new System.IO.StreamWriter(logfile, true);
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine("Error opening log file: " + e.ToString());
                    Console.WriteLine("Error opening log file: " + e.ToString());
                }
            }

            if(untilsunset)
            {
                DateTime endtime;
                SunriseSunsetResults srss = await GetSunriseSunsetData();
                if (srss == null || (srss.results.sunset<DateTime.Now)) endtime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 18, 0, 0);
                else endtime = srss.results.sunset;

                Console.WriteLine("Using end time {0:HH:mm}", endtime);

                cycles = (int)((endtime - DateTime.Now).TotalSeconds/(period/1000));
                

            }

            Console.WriteLine("Opening port {0} and running {1} cycles", port, cycles);
            if (file != null) Console.WriteLine("Using file {0}", logfile);

            Console.WriteLine(appSettings.serialport);
            appSettings.serialport = port;


            //ListPorts();

            // Create a new SerialPort on port COM7
            _serialPort = new SerialPort(port, 9600)
            {
                // Set the read/write timeouts
                ReadTimeout = 1500,
                WriteTimeout = 1500,
                StopBits = StopBits.One
            };
            _serialPort.Open();

            _serialPort.DataReceived += _serialPort_DataReceived;

            Console.WriteLine("Writing to serial port");

            byte[] buf = StatusMessage.BuildRequest(appSettings.radio, appSettings.inverters[0]);
            byte[] inbuf = null;
            //ReadPort();

            StatusMessage sm = null;
            StringBuilder sb = new StringBuilder();

            for (int cnt = 0; cnt < cycles; cnt++)
            {
                if (_serialPort != null)
                {
                    statusMessages.Add(sm = new StatusMessage() { StatusTime = DateTime.Now });
                    _serialPort.Write(buf, 0, 15); ;
                }
                Thread.Sleep(period);
                //Console.Write("\r\n");
                if ((sm != null) && sm.ReceiveComplete)
                {
                    noresponsecount = 0; // reset the non-response count

                    inbuf = sm.Rawdata;
                    foreach (byte b in inbuf)
                    {
                        sb.Append(b.ToString("X2"));
                    }

                    if (previous != null)
                    {
                        double secs = (sm.StatusTime - previous.StatusTime).TotalSeconds;
                        energy += secs * 0.5 * (previous.ACVoltage * previous.ACCurrent + sm.ACVoltage * sm.ACCurrent);
                        dcenergy += secs * 0.5 * (previous.DCVoltage * previous.DCCurrent + sm.DCVoltage * sm.DCCurrent);
                    }

                    string s = string.Format(
                        "{0:HH:mm:ss}\t{1}\t{2}\t{3:0.00}\t{4:0.00}\t{5}\t{6}\t{7:0.00}\t{8:0.00}",
                        sm.StatusTime,
                        //sb.ToString(),
                        sm.DCVoltage,
                        sm.DCCurrent,
                        sm.DCVoltage*sm.DCCurrent,
                        dcenergy/3600.0,
                        sm.ACVoltage,
                        sm.ACCurrent,
                        sm.ACVoltage * sm.ACCurrent,
                        energy / 3600.0
                        );

                    Console.WriteLine(s);
                    if (file != null)
                    {
                        await file.WriteLineAsync(s);
                        await file.FlushAsync();
                    }

                    previous = sm;

                    sb.Clear();
                }
                else  // we didn't get a response or the message was incomplete
                {
                    ++noresponsecount;  // increment the non-response count
                    if(noresponsecount > 4)
                    {
                        _serialPort.Close();
                        await Task.Delay(1000);
                        _serialPort.Open();
                    }
                }

                if (statusMessages.Count > 2) statusMessages.RemoveAt(0);
            }

            _serialPort.Close();
            if (file != null)
            {

                file.Close();
                file.Dispose();
            }

        }

        static void TestEncoding(IConsole console)
        {
            byte[] buf = StatusMessage.BuildRequest(appSettings.radio, appSettings.inverters[0]);
            foreach (byte b in buf)
            {
                Console.Write(b.ToString("X2"));

            }
            //Console.WriteLine(string.Join("",buf.Select(i => string.Format("X2",i))));
        }

        private static void _serialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            int cnt, btr;
            byte bt;
            btr = _serialPort.BytesToRead;

            StatusMessage sm = statusMessages[^1]; // get the last message in the list

            //Console.WriteLine("Bytes to read: {0}", btr);
            for (cnt = 0; cnt < btr; cnt++)
            {
                bt = (byte)_serialPort.ReadByte();
                //Console.Write(bt.ToString("X2"));
                if (!sm.ReceiveComplete) sm.AddByte(bt);
            }


        }

        static void doListPorts(bool list, IConsole c)
        {
            ListPorts();
        }
        static void ListPorts()
        {
            string[] ports = SerialPort.GetPortNames();
            Console.WriteLine("The following serial ports were found:");             // Display each port name to the console.
            foreach (string port in ports)
            {
                Console.WriteLine(port);
            }
            //Console.ReadLine();        
        }
        static void WritePort()
        {

            if (_serialPort == null) return;

            //byte[] buf = new byte[]
            //{
            //    0x43, 0xC0  , 0x10  , 0x88  , 0x0   , 0x0   , 0x55  , 0x0   , 0x4   , 0x14  , 0x0   , 0x0   , 0x0   , 0x0   , 0x8
            //};



            //_serialPort.Write(buf, 0, 15);

        }

        static void ReadPort()
        {
            try
            {
                string message = _serialPort.ReadLine();
                Console.WriteLine(message);
            }
            catch (TimeoutException) {
                Console.WriteLine("Read timed out");

            }
        }

        static async Task SetupPowerHub()
        {
            PowerHub = new HubConnectionBuilder()
                 .WithAutomaticReconnect()
                 .WithUrl(appSettings.rsignalhost)
                 .Build();

            PowerHub.Closed += async (error) =>
            {
                await Task.Delay(new Random().Next(0, 5) * 1000);
                await StartPowerHub(tokenPowerHub, 10000);
            };

            //PowerHub.On("RequestFullStatus", OnRequestFullStatus);
            //PowerHub.On("SendCommand",
            //    (string command) =>
            //    { CommandProcessor(command); });
        }

        //private async Task OnRequestFullStatus()
        //{
        //    await IrrigationHub.SendAsync("SendFullStatus", "{ \"status\": \"full status\"}");
        //}

        private static async Task<bool> StartPowerHub(CancellationToken token, int retrymillis)
        {

            while (true)
            {
                try
                {
                    await PowerHub.StartAsync(token);
                    return true;
                }
                catch when (token.IsCancellationRequested)
                {
                    return false;
                }
                catch
                {
                    await Task.Delay(retrymillis);
                }
            }
        }


        private static async Task GetSunriseSunset()
        {

            SunriseSunsetResults ? res = await GetSunriseSunsetData();

            if (res != null) Console.WriteLine("Sunrise: {0:HH:mm:ss}\nSunset: {1:HH:mm:ss}", res.results.sunrise, res.results.sunset);

        }
    

        private static async Task<SunriseSunsetResults?> GetSunriseSunsetData()
        {
            SunriseSunsetResults res = null;
            string data = null;

            HttpClient client = new HttpClient() { Timeout = TimeSpan.FromSeconds(30) };
            //string data = await client.GetStringAsync("https://api.sunrise-sunset.org/json?lat=-26.145457&lng=27.969062&date=today&formatted=0");
            try
            {
                //data = await client.GetStringAsync(appSettings.sunrisesunseturl);
                data = await client.GetStringAsync("https://api.sunrise-sunset.org/json?lat=-26.145457&lng=27.969062&date=today&formatted=0");
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception getting sunrise/sunset data: {0}", e.ToString());
            }

            if (data != null)
            {
                try
                {
                    res = JsonConvert.DeserializeObject<SunriseSunsetResults>(data);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Exception in json conversion: {0}", e.ToString());
                    System.Diagnostics.Debug.WriteLine("Exception in json conversion: {0}", e.ToString());
                }
            }

            return res;
        }
    }

    // get status:     0x43, 0xC0  , 0x10  , 0x88  , 0x0   , 0x0   , 0x55  , 0x0   , 0x4   , 0x14  , 0x0   , 0x0   , 0x0   , 0x00   , 0x8
    // turn on:        0x43, 0xC1  , 0x10  , 0x88  , 0x0   , 0x0   , 0x55  , 0x0   , 0x4   , 0x14  , 0x0   , 0x0   , 0x0   , 0x01   , 0x0A
    // turn off:        0x43, 0xC1  , 0x10  , 0x88  , 0x0   , 0x0   , 0x55  , 0x0   , 0x4   , 0x14  , 0x0   , 0x0   , 0x0   , 0x02   , 0x0B
    // reboot:          0x43, 0xC1  , 0x10  , 0x88  , 0x0   , 0x0   , 0x55  , 0x0   , 0x4   , 0x14  , 0x0   , 0x0   , 0x0   , 0x03   , 0x0C
    // powergrade 91%:  0x43, 0xC3  , 0x10  , 0x88  , 0x0   , 0x0   , 0x55  , 0x0   , 0x4   , 0x14  , 0x0   , 0x0   , 0x0   , 0x5B   , 0x66
    // powergrade 51%:  0x43, 0xC3  , 0x10  , 0x88  , 0x0   , 0x0   , 0x55  , 0x0   , 0x4   , 0x14  , 0x0   , 0x0   , 0x0   , 0x33   , 0x3E
    // powergrade 10%:  0x43, 0xC3  , 0x10  , 0x88  , 0x0   , 0x0   , 0x55  , 0x0   , 0x4   , 0x14  , 0x0   , 0x0   , 0x0   , 0x0A   , 0x15

    // last byte is checksum.  Add all previous 14 bytes and take the LSB of the sum as the 15th byte in the message
    // Message structure:
    // 01: 0x43 - start
    // 02: 0xCx - command - C0 = status, C1 = set powerstate, C2 = ?, C3 = set powergrade
    // 03: 0x10 - databox ID upper e.g. 1088
    // 04: 0x88 - databox ID lower
    // 05: 0x00
    // 06: 0x00
    // 07: 0x55 - inverter ID MSB e.g. 55000414
    // 08: 0x00 
    // 09: 0x04
    // 10: 0x14 - inverter ID LSB
    // 11: 0x00
    // 12: 0x00
    // 13: 0x00
    // 14: 0x00 - data
    // 15: 0x08 - checksum
    // 

    class SunriseSunsetData
    {

    }
}
