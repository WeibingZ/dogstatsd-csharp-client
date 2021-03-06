using NUnit.Framework;
using StatsdClient;
using Tests.Utils;
using Mono.Unix;
using System.Text;
using System.Net.Sockets;
using System;

#if !OS_WINDOWS
namespace Tests
{
    [TestFixture]
    public class StatsdUnixDomainSocketTest
    {
        private TemporaryPath _temporaryPath;

        [SetUp]
        public void Setup()
        {
            _temporaryPath = new TemporaryPath();
        }

        [TearDown]
        public void TearDown()
        {
            _temporaryPath.Dispose();
        }

        public enum HostnameProvider
        {
            Environment,
            Property
        }

        [TestCase(HostnameProvider.Property)]
        [TestCase(HostnameProvider.Environment)]
        public void SendSingleMetric(HostnameProvider hostnameProvider)
        {
            using (var service = CreateService(_temporaryPath, hostnameProvider))
            {
                using (var socket = CreateSocketServer(_temporaryPath))
                {
                    var metric = "gas_tank.level";
                    var value = 0.75;
                    service.Gauge(metric, value);
                    Assert.AreEqual($"{metric}:{value}|g", ReadFromServer(socket));
                }
            }
        }

        [Test]
        public void SendSplitMetrics()
        {
            using (var statdUds = new StatsdUnixDomainSocket(StatsdUnixDomainSocket.UnixDomainSocketPrefix + _temporaryPath.Path, 25))
            {
                using (var socket = CreateSocketServer(_temporaryPath))
                {
                    var statd = new Statsd(statdUds);
                    var messageCount = 7;

                    for (int i = 0; i < messageCount; ++i)
                        statd.Add("title" + i, "text");
                    Assert.AreEqual(messageCount, statd.Commands.Count);

                    statd.Send();

                    var response = ReadFromServer(socket);
                    for (int i = 0; i < messageCount; ++i)
                        Assert.True(response.Contains("title" + i));
                }
            }
        }

        // Use a timeout in case Gauge become blocking
        [Test, Timeout(30000)]
        public void CheckNotBlockWhenServerNotReadMessage()
        {
            var tags = new string[] { new string('A', 100) };

            using (var service = CreateService(_temporaryPath))
            {
                // We are sending several Gauge to make sure there is no buffer
                // that can make service.Gauge blocks after several calls.
                for (int i = 0; i < 1000; ++i)
                    service.Gauge("metric" + i, 42, 1, tags);
                // If the code go here that means we do not block.
            }
        }

        static DogStatsdService CreateService(
                    TemporaryPath temporaryPath,
                    HostnameProvider hostnameProvider = HostnameProvider.Property)
        {
            var serverName = StatsdUnixDomainSocket.UnixDomainSocketPrefix + temporaryPath.Path;
            var dogstatsdConfig = new StatsdConfig{ StatsdMaxUnixDomainSocketPacketSize = 1000 };

            switch (hostnameProvider)
            {
                case HostnameProvider.Property: dogstatsdConfig.StatsdServerName = serverName; break;
                case HostnameProvider.Environment: 
                {
                    Environment.SetEnvironmentVariable(StatsdConfig.DD_AGENT_HOST_ENV_VAR, serverName); 
                    break;
                }
            }

            var dogStatsdService = new DogStatsdService();
            dogStatsdService.Configure(dogstatsdConfig);

            return dogStatsdService;
        }

        static Socket CreateSocketServer(TemporaryPath temporaryPath)
        {
            var endPoint = new UnixEndPoint(temporaryPath.Path);
            var server = new Socket(AddressFamily.Unix, SocketType.Dgram, ProtocolType.IP);
            server.Bind(endPoint);

            return server;
        }

        static string ReadFromServer(Socket socket)
        {
            var builder = new StringBuilder();
            var buffer = new byte[8096];

            while (socket.Available > 0)
            {
                var count = socket.Receive(buffer);
                var chars = System.Text.Encoding.UTF8.GetChars(buffer, 0, count);
                builder.Append(chars);
            }

            return builder.ToString();
        }
    }
}
#endif