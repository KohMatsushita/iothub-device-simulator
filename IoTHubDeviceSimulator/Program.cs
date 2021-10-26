using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.Loader;
using Microsoft.Azure.Devices.Client;

namespace IoTHubDeviceSimulator
{
    class Program
    {
        private static string _connectionString;
        private static uint _sendInterval;
        private static bool _logging = false;
        private static DeviceClient _deviceClient;
        private static Random _random = new Random();
        static async Task Main(string[] args)
        {
            // parse args
            var rootCommand = new RootCommand("Azure IoT Hub device simulation app.")
            {
                new Option<string>(
                    aliases: new string[] { "--connectionString", "-c" },
                    description: "Connection string of IoT Hub device."
                ),
                new Option<uint>(
                    aliases: new string[] { "--sendInterval", "-s" },
                    description: "Telemetry send interval."
                ),
                new Option<bool>(
                    aliases: new string[] { "--logging", "-l" },
                    description: "Output logs."
                )
            };
            rootCommand.Handler = CommandHandler.Create<string, uint, bool>(ConvertArgs);
            await rootCommand.InvokeAsync(args);

            // check args
            if(string.IsNullOrEmpty(_connectionString))
            {
                Console.WriteLine("Connection string must be setted.");
                return;
            }
            if(_sendInterval < 1000)
            {
                Console.WriteLine("Send interval is too short (over 1000).");
                return;
            }

            // connect to IoT Hub
            try
            {
                _deviceClient = DeviceClient.CreateFromConnectionString(_connectionString);
                await _deviceClient.OpenAsync();
            }
            catch(Exception e)
            {
                Console.WriteLine($"Connection error:{e}");
                return;
            }

            var timer = new System.Timers.Timer(_sendInterval);
            timer.Elapsed += async (sender, e) => {
                var telemetry = new Telemetry
                {
                    Temperature = _random.NextDouble() * 40,
                    Humidity = _random.NextDouble() * 100
                };
                var messageStr = JsonSerializer.Serialize(telemetry);
                var message = new Message(Encoding.UTF8.GetBytes(messageStr));
                message.MessageId = Guid.NewGuid().ToString();
                message.ContentType = "application/json";
                message.ContentEncoding = "utf-8";
                if(_logging)
                {
                    Console.WriteLine($"body:{messageStr}, messageId:{message.MessageId}");
                }
                try
                {
                    await _deviceClient.SendEventAsync(message);
                }
                catch(Exception ex)
                {
                    Console.WriteLine($"Telemetry send error:{ex}");
                }
            };
            timer.Start();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            await WhenCancelled(cts.Token);

            timer.Stop();
            timer.Dispose();
        }

        private static void ConvertArgs(string connectionString, uint sendInterval = 5000, bool logging = false)
        {
            _connectionString = connectionString;
            _sendInterval = sendInterval;
            _logging = logging;
        }

        private static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

    }
}
