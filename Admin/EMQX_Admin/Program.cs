using MQTTnet;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using System;
using System.Text.Json;
using System.Threading.Tasks;
namespace EMQX_Admin
{
    internal class Program
    {
        public class TelemetryData
        {
            public string AdminName { get; set; }
            public string Status { get; set; }
            public DateTime TimeStamp { get; set; }
        }
        public async Task EMQX_Admin()
        {
            IManagedMqttClient _mqttClient;
            var factory = new MqttFactory();
            _mqttClient = factory.CreateManagedMqttClient();
            var BROKER_HOST = "localhost";  // to be replaced with Dashboard IP or Docker Internal IP
            var WS_PORT = 8081;
            var WS_PATH = "/mqtt";
            var USERNAME = "Admin";
            var PASSWORD = "Public";

            var options = new MqttClientOptionsBuilder()
                .WithClientId("Admin")
                .WithWebSocketServer(o => o.WithUri($"ws://{BROKER_HOST}:{WS_PORT}{WS_PATH}"))
                .WithCredentials(USERNAME, PASSWORD)
                .WithCleanSession()
                .Build();

            var managedOptions = new ManagedMqttClientOptionsBuilder()
            .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
            .WithClientOptions(options)
            .Build();

            _mqttClient.ApplicationMessageReceivedAsync += e =>
            {
                Console.WriteLine($"Received: {System.Text.Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment)} on {e.ApplicationMessage.Topic}");
                return Task.CompletedTask;
            };
            await _mqttClient.StartAsync(managedOptions);
            Console.WriteLine("EMQX Admin Connected to EMQX via WebSocket...");

            await _mqttClient.SubscribeAsync("topic/#");

            var messageData = new TelemetryData
            {
                AdminName = "Admin",
                Status = "Active",
                TimeStamp = DateTime.Now,
            };
            var jsonPayload = JsonSerializer.Serialize(messageData);
            var message = new MqttApplicationMessageBuilder()
                .WithTopic("topic/Sensors/Data")
                .WithPayload(jsonPayload)   
                .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce)
                .Build();

            

            await _mqttClient.EnqueueAsync(message);
            Console.WriteLine($"Publishec JSON Message : {jsonPayload}"); 
            await Task.Delay(Timeout.Infinite); 

         


        }
    }
}
