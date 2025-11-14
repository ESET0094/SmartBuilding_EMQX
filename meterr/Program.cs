using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using System;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EMQX_SmartMeter
{
    internal class Program
    {
        // ------------------ DATA MODELS ------------------

        public class SmartMeterData
        {
            public string DeviceID { get; set; }
            public double Voltage { get; set; }
            public double Current { get; set; }
            public double Power { get; set; }
            public double EnergyConsumed { get; set; }
            public DateTime TimeStamp { get; set; }
        }

        public class CommandMessage
        {
            public string Username { get; set; }
            public string Status { get; set; }
            public string Command { get; set; }
            public DateTime TimeStamp { get; set; }
        }

        public class BillData
        {
            public string DeviceID { get; set; }
            public double TotalEnergy { get; set; }
            public double TariffRate { get; set; }
            public double BillAmount { get; set; }
            public DateTime GeneratedAt { get; set; }
        }

        // ------------------ GLOBAL VARIABLES ------------------

        static bool meterOn = false;
        static bool publishing = false;
        static double totalEnergyConsumed = 0;
        static string DEVICE_ID = "SM-001";

        static double threshold = 0;
        static double limit = 0;
        static bool emailSent = false;

        // ------------------ MAIN ------------------

        static async Task Main()
        {
            var factory = new MqttFactory();
            var mqttClient = factory.CreateManagedMqttClient();

            const string BROKER = "172.16.103.22";
            const int WS_PORT = 8083;
            const string WS_PATH = "/mqtt";

            const string USERNAME = "SmartMeter";
            const string PASSWORD = "Public";

            // MQTT client setup
            var options = new MqttClientOptionsBuilder()
                .WithClientId("SmartMeterClient")
                .WithWebSocketServer(o => o.WithUri($"ws://{BROKER}:{WS_PORT}{WS_PATH}"))
                .WithCredentials(USERNAME, PASSWORD)
                .WithCleanSession()
                .Build();

            var managedOptions = new ManagedMqttClientOptionsBuilder()
                .WithClientOptions(options)
                .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
                .Build();

            // Incoming message handler
            mqttClient.ApplicationMessageReceivedAsync += async e =>
            {
                string topic = e.ApplicationMessage.Topic;
                string payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

                Console.WriteLine($"\n📩 Received Message on {topic}: {payload}");

                try
                {
                    var cmdMsg = JsonSerializer.Deserialize<CommandMessage>(payload);
                    if (cmdMsg == null)
                        return;

                    // ------------------ IGNORE SMART METER'S OWN ACK ------------------
                    if (cmdMsg.Username == "SmartMeter")
                        return;

                    string cmd = cmdMsg.Command?.Trim().ToLower() ?? "";

                    // ------------------ THRESHOLD INPUT ------------------
                    if (topic.Equals("topic/Smartmeter/UserInput", StringComparison.OrdinalIgnoreCase))
                    {
                        if (double.TryParse(cmdMsg.Command, out threshold))
                        {
                            limit = threshold * 0.90;
                            totalEnergyConsumed = 0;
                            emailSent = false;

                            Console.WriteLine($"📏 Threshold set = {threshold} | Limit = {limit}");

                            await SendAckAsync(mqttClient, $"Threshold set to {threshold} kWh");
                        }
                        else
                        {
                            await SendAckAsync(mqttClient, "❌ Invalid threshold input");
                        }

                        return;
                    }

                    // ------------------ COMMANDS ------------------
                    if (topic.Equals("topic/Smartmeter/Commands", StringComparison.OrdinalIgnoreCase))
                    {
                        switch (cmd)
                        {
                            case "on":
                                meterOn = true;
                                publishing = false;
                                totalEnergyConsumed = 0;
                                emailSent = false;

                                await SendAckAsync(mqttClient, "Smart Meter turned ON");
                                break;

                            case "off":
                                meterOn = false;
                                publishing = false;

                                await SendAckAsync(mqttClient, "Smart Meter turned OFF");
                                break;

                            case "view meter readings":
                                if (!meterOn)
                                {
                                    await SendAckAsync(mqttClient, "⚠️ Cannot view readings — meter is OFF");
                                    return;
                                }

                                publishing = true;
                                await SendAckAsync(mqttClient, "Started viewing meter readings");
                                break;

                            case "generate bill":
                                if (!meterOn)
                                {
                                    await SendAckAsync(mqttClient, "⚠️ Cannot generate bill — meter is OFF");
                                    return;
                                }

                                double tariffRate = 5.25;
                                double billAmount = Math.Round(totalEnergyConsumed * tariffRate, 2);

                                var bill = new BillData
                                {
                                    DeviceID = DEVICE_ID,
                                    TotalEnergy = Math.Round(totalEnergyConsumed, 2),
                                    TariffRate = tariffRate,
                                    BillAmount = billAmount,
                                    GeneratedAt = DateTime.Now
                                };

                                string billJson = JsonSerializer.Serialize(bill, new JsonSerializerOptions { WriteIndented = true });

                                var billMsg = new MqttApplicationMessageBuilder()
                                    .WithTopic("topic/Smartmeter/Bill")
                                    .WithPayload(billJson)
                                    .Build();

                                await mqttClient.EnqueueAsync(billMsg);
                                await SendAckAsync(mqttClient, $"Bill generated: ₹{billAmount:F2}");
                                break;

                            default:
                                await SendAckAsync(mqttClient, $"⚠ Unknown command: {cmd}");
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error processing message: {ex.Message}");
                }
            };

            await mqttClient.StartAsync(managedOptions);
            await mqttClient.SubscribeAsync("topic/Smartmeter/#");

            Console.WriteLine("📡 Smart Meter connected to broker...");
            Console.WriteLine("💡 Waiting for dashboard commands...");

            // ------------------ BACKGROUND LOOP ------------------
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    if (meterOn && publishing)
                        await PublishMeterDataAsync(mqttClient);

                    await Task.Delay(4000);
                }
            });

            await Task.Delay(Timeout.Infinite);
        }

        // ------------------ PUBLISH DATA ------------------

        static async Task PublishMeterDataAsync(IManagedMqttClient mqttClient)
        {
            Random r = new Random();

            double voltage = Math.Round(220 + (r.NextDouble() * 20 - 10), 2);
            double current = Math.Round(5 + (r.NextDouble() * 5), 2);
            double power = Math.Round(voltage * current, 2);

            totalEnergyConsumed += Math.Round(power / 1000.0, 3);

            if (limit > 0 && totalEnergyConsumed >= limit && !emailSent)
            {
                SendEmailAlert(totalEnergyConsumed, threshold, limit);
                emailSent = true;
            }

            var data = new SmartMeterData
            {
                DeviceID = DEVICE_ID,
                Voltage = voltage,
                Current = current,
                Power = power,
                EnergyConsumed = totalEnergyConsumed,
                TimeStamp = DateTime.Now
            };

            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });

            var msg = new MqttApplicationMessageBuilder()
                .WithTopic("topic/Smartmeter/Data")
                .WithPayload(json)
                .Build();

            await mqttClient.EnqueueAsync(msg);

            Console.WriteLine($"📤 Reading → {totalEnergyConsumed:F3} kWh");
        }

        // ------------------ EMAIL ALERT ------------------

        static void SendEmailAlert(double energy, double threshold, double limit)
        {
            try
            {
                MailMessage mail = new MailMessage();
                mail.From = new MailAddress("formestudy624@gmail.com");
                mail.To.Add("somkumar225@gmail.com");

                mail.Subject = "⚠ Smart Meter Usage Alert";
                mail.Body =
                    $"Your energy usage reached 90% of the threshold.\n\n" +
                    $"Energy Used: {energy:F2} kWh\n" +
                    $"Threshold: {threshold:F2} kWh\n" +
                    $"Limit (90%): {limit:F2} kWh\n\n" +
                    $"Please recharge soon.";

                var smtp = new SmtpClient("smtp.gmail.com")
                {
                    Port = 587,
                    Credentials = new NetworkCredential("formestudy624@gmail.com", "etsb xvvd vuaw nfjr"),
                    EnableSsl = true
                };

                smtp.Send(mail);
                Console.WriteLine("📧 Email sent successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Email sending error: {ex.Message}");
            }
        }

        // ------------------ ACK message ------------------

        static async Task SendAckAsync(IManagedMqttClient mqttClient, string text)
        {
            var ack = new
            {
                Username = "SmartMeter",
                Status = "ACK",
                Command = text,
                TimeStamp = DateTime.Now
            };

            string json = JsonSerializer.Serialize(ack);

            var msg = new MqttApplicationMessageBuilder()
                .WithTopic("topic/Smartmeter/Commands")   // same topic—but filtered by Username
                .WithPayload(json)
                .Build();

            await mqttClient.EnqueueAsync(msg);
        }
    }
}
