using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SmartInverterConnectionService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private AppSettings appSettings;
        private SerialPort serialPort;
        private StatusMessage currentMessage;
        private FileStream stateFile = null;
        private ServiceState serviceState = null;
        private Dictionary<string, Inverter> inverters;
        DateTime loadtime = DateTime.Now;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {

            CancellationTokenSource cts = new CancellationTokenSource();
            DateTime sunset = DateTime.Now.Date + TimeSpan.FromHours(18.5);
            DateTime sunrise= DateTime.Now.Date + TimeSpan.FromHours(6.5);
            ReadLoopResult res;
            DateTime lastday = DateTime.Now.Date - TimeSpan.FromHours(24);
            

            // load the configuration
            BuildConfiguration();
            string port = appSettings.serialport;

            // set up the serial port
            serialPort = new SerialPort(port, 9600)
            {
                // Set the read/write timeouts
                ReadTimeout = 1500,
                WriteTimeout = 1500,
                StopBits = StopBits.One
            };
            serialPort.DataReceived += SerialPort_DataReceived;

            // create the dictionary of inverters
            inverters = new Dictionary<string, Inverter>();
            foreach (string inv in appSettings.inverters)
            {
                inverters.Add(inv, new Inverter() { Id = inv }) ;
            }


            // open the state file
            try
            {
                stateFile = new FileStream(appSettings.logfilepath + "servicestate.json", FileMode.OpenOrCreate, FileAccess.ReadWrite);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e.ToString());
            }
            await ReadLastState();
            
            while (!stoppingToken.IsCancellationRequested)
            {
                //_logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                //await Task.Delay(1000, stoppingToken);

                if(DateTime.Now.Date != lastday)  // we have rolled over to a new day
                {
                    SunriseSunsetResults ssr = await GetSunriseSunsetData(stoppingToken);
                    if (ssr != null)
                    {
                        sunrise = ssr.results.sunrise;
                        sunset = ssr.results.sunset;
                    }
                    lastday = DateTime.Now.Date;

                    _logger.LogInformation(string.Format($"Logging will run from {sunrise:HH:mm:ss} to {sunset:HH:mm:ss}"));

                    if (serviceState != null) serviceState.TotalEnergy = 0.0;  // reset the total energy for the day
                }

                

                while ((sunrise < DateTime.Now) && (DateTime.Now < sunset))
                {
                    res = await DoReadLoop(sunset, stoppingToken);
                    _logger.LogInformation(string.Format($"Returned from ReadLoop with {res}"));
                }

                await Task.Delay(1000); // wait a second before going around again
            }

            
        }

        async Task ReadLastState()
        {
            if (stateFile == null) return;
            StreamReader sr = new StreamReader(stateFile);
            string json = await sr.ReadToEndAsync();
            ServiceState inputstate = null;
            if (json.Length > 10)
            {
                try
                {
                    inputstate = JsonConvert.DeserializeObject<ServiceState>(json);
                }
                catch (Exception e) { _logger.LogWarning(e.ToString()); }
            }
            if(inputstate!= null)
            {
                // check if the last recorded state was today.  If so, set the current state
                if (inputstate.StateTime.Date == DateTime.Now.Date)
                {
                    serviceState = inputstate;
                    serviceState.LoadTime = loadtime;
                }
                else // create a new servicestate and set the load time
                {
                    serviceState = new ServiceState() { LoadTime = loadtime };
                }
            }
            else // create a new servicestate and set the load time
            {
                serviceState = new ServiceState() { LoadTime = loadtime };
            }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            int cnt, btr;
            byte bt;
            btr = serialPort.BytesToRead;

            StatusMessage sm = currentMessage; // get the last message in the list

            //Console.WriteLine("Bytes to read: {0}", btr);
            for (cnt = 0; cnt < btr; cnt++)
            {
                bt = (byte)serialPort.ReadByte();
                //Console.Write(bt.ToString("X2"));
                if (!sm.ReceiveComplete) sm.AddByte(bt);
            }
        }

        private void BuildConfiguration()
        {
            appSettings = new AppSettings();
            ConfigurationBuilder cb = new ConfigurationBuilder();
            cb.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            var config = cb.Build();
            ConfigurationBinder.Bind(config.GetSection("AppSettings"), appSettings);
        }

        private async Task<ReadLoopResult> DoReadLoop(DateTime loopuntil, CancellationToken stoppingToken)
        {
            ReadLoopResult res = ReadLoopResult.EndTimeReached;

            if (serviceState == null) return ReadLoopResult.NullServiceState;
                 
            
            byte[] inbuf = null;
            StatusMessage sm = null ;
            int period = appSettings.readperiod;
            //int noresponsecount = 0;
            //StringBuilder sb = new StringBuilder();
            //double energy = 0.0;
            //double dcenergy = 0.0;
            //StatusMessage? previous = null;
            System.IO.StreamWriter? file = null;

            // open the serial port
            if (serialPort != null) serialPort.Open();
            else return ReadLoopResult.NullSerialPort;

            // open the logfile if needed
            if (appSettings.writeoutputtologfile)
            {
                string path = appSettings.logfilepath + DateTime.Now.Date.ToString("yyMMdd") + ".log";
                try
                {
                    file = new System.IO.StreamWriter(path, true);
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine("Error opening log file: " + e.ToString());
                    Console.WriteLine("Error opening log file: " + e.ToString());
                }
            }


            while (DateTime.Now < loopuntil && !stoppingToken.IsCancellationRequested)
            {

                // cycle through each inverter
                foreach (Inverter inv in inverters.Values)
                {
                    byte[] buf = StatusMessage.BuildRequest(appSettings.radio, inv.Id);  // todo - make it handle multiple inverters

                    // request data from the inverter 
                    sm = new StatusMessage() { StatusTime = DateTime.Now };
                    currentMessage = sm; // needed for the serial data received routine
                    inv.CurrentMessage = sm;
                    serialPort.Write(buf, 0, 15); ;
                    //Thread.Sleep(period);
                    await Task.Delay(period);

                    // if we have a message and it is complete and it's valid
                    if ((sm != null) && sm.ReceiveComplete && sm.IsValidMessage)
                    {
                        inv.NoResponseCount = 0; // reset the non-response count

                        inbuf = sm.Rawdata;
                        //foreach (byte b in inbuf)
                        //{
                        //    sb.Append(b.ToString("X2"));
                        //}

                        serviceState.TotalEnergy += inv.CalculateEnergy();


                        string s = string.Format(
                            "{0:HH:mm:ss}\t{1}\t{2}\t{3}\t{4:0.00}\t{5:0.00}\t{6}\t{7}\t{8:0.00}\t{9:0.00}\t{10:0.00}",
                            sm.StatusTime,
                            inv.Id,
                            sm.DCVoltage,
                            sm.DCCurrent,
                            sm.DCVoltage * sm.DCCurrent,
                            inv.TotalDCEnergy / 3600.0,
                            sm.ACVoltage,
                            sm.ACCurrent,
                            sm.ACVoltage * sm.ACCurrent,
                            inv.TotalACEnergy / 3600.0,
                            serviceState.TotalEnergy / 3600.0
                            ) ;

                        //Console.WriteLine(s);
                        if (file != null)
                        {
                            await file.WriteLineAsync(s);
                            await file.FlushAsync();
                        }

                        if (stateFile != null)
                        {
                            serviceState.StateTime = DateTime.Now;
                            serviceState.LastMessage = sm;
                            await serviceState.SaveToFileAsync(stateFile);
                        }

                        inv.LastMessage = sm;

                        //sb.Clear();
                    }
                    else  // we didn't get a response or the message was incomplete
                    {
                        ++inv.NoResponseCount;  // increment the non-response count
                        if (inv.NoResponseCount > 4)
                        {
                            inv.ReadError = true;
                            break;  // only gets us out of the foreach loop - and moves us to the next radio
                        }
                    }
                } // foreach

                // check if we are timing out on each inverter, if so, break
                bool error = false;
                foreach(Inverter invv in inverters.Values)
                {
                    error &= invv.ReadError; 
                }
                if(error)
                {
                    res = ReadLoopResult.RetryLimitExceeded;
                    break;  
                }

            } // while

            serialPort.Close();
            if (file != null)
            {
                file.Close();
                file.Dispose();
            }

            return res;
        }


        private async Task<SunriseSunsetResults?> GetSunriseSunsetData(CancellationToken stoppingToken)
        {
            SunriseSunsetResults res = null;
            string data = null;
            string tday = DateTime.Now.ToString("yyyy-MM-dd");
            string request = string.Format($"https://api.sunrise-sunset.org/json?lat={appSettings.latitude}&lng={appSettings.longitude}&date={tday}&formatted=0");
            _logger.LogInformation(request);

            HttpClient client = new HttpClient() { Timeout = TimeSpan.FromSeconds(30) };
            //string data = await client.GetStringAsync("https://api.sunrise-sunset.org/json?lat=-26.145457&lng=27.969062&date=today&formatted=0");
            try
            {
                //data = await client.GetStringAsync(appSettings.sunrisesunseturl);
                //data = await client.GetStringAsync("https://api.sunrise-sunset.org/json?lat=-26.145457&lng=27.969062&date=today&formatted=0");
                data = await client.GetStringAsync(request);
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

    enum ReadLoopResult { EndTimeReached, ResponseTimeout, RetryLimitExceeded, NullSerialPort,
        NullServiceState
    }
}
