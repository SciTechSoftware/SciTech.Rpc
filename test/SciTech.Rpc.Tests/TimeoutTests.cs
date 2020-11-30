using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SciTech.Rpc.Client;
using SciTech.Rpc.Server;
using SciTech.Rpc.Serialization;

namespace SciTech.Rpc.Tests
{
    public abstract class TimeoutTests : ClientServerTestsBase
    {
        protected TimeoutTests(RpcConnectionType connectionType) : base(new ProtobufRpcSerializer(), connectionType)
        {

        }

        [Test]
        public async Task TooSlowExecution_ShouldThrowException()
        {
            var definitionBuilder = new RpcServiceDefinitionsBuilder();
            definitionBuilder.RegisterService<ITimeoutTestService>();

            var (host, connection) = this.CreateServerAndConnection(definitionBuilder,
                configClientOptions: options =>
                {
                    options.CallTimeout = TimeSpan.FromMilliseconds(300);
                });
            host.Start();
            try
            {
                using (var publishedInstanceScope = host.ServicePublisher.PublishInstance<ITimeoutTestService>(new TestTimeoutServiceImpl()))
                {
                    var timeoutService = connection.GetServiceInstance<ITimeoutTestServiceClient>(publishedInstanceScope.Value);

                    Assert.ThrowsAsync<TimeoutException>(() => timeoutService.AsyncAddWithDelayAsync(3, 4, TimeSpan.FromMilliseconds(400), default).DefaultTimeout());
                    Assert.ThrowsAsync<TimeoutException>(() => timeoutService.AddWithDelayAsync(3, 4, TimeSpan.FromMilliseconds(400)).DefaultTimeout());
                    Assert.Throws<TimeoutException>(() => timeoutService.AddWithDelay(3, 4, TimeSpan.FromMilliseconds(400)));
                    Assert.Throws<TimeoutException>(() => timeoutService.AsyncAddWithDelay(3, 4, TimeSpan.FromMilliseconds(400)));
                }
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }

        [Test]
        public async Task FastExecution_ShouldReturn()
        {
            var definitionBuilder = new RpcServiceDefinitionsBuilder();
            definitionBuilder.RegisterService<ITimeoutTestService>();

            var (host, connection) = this.CreateServerAndConnection(definitionBuilder,
                configClientOptions: options =>
                {
                    options.CallTimeout = TimeSpan.FromMilliseconds(300);
                });
            host.Start();
            try
            {
                using (var publishedInstanceScope = host.ServicePublisher.PublishInstance<ITimeoutTestService>(new TestTimeoutServiceImpl()))
                {
                    var timeoutService = connection.GetServiceInstance<ITimeoutTestServiceClient>(publishedInstanceScope.Value);

                    var res = await timeoutService.AddWithDelayAsync(3, 4, TimeSpan.FromMilliseconds(50)).DefaultTimeout();
                    Assert.AreEqual(3 + 4, res);

                    res = await timeoutService.AsyncAddWithDelayAsync(5, 6, TimeSpan.FromMilliseconds(50), default).DefaultTimeout();
                    Assert.AreEqual(5 + 6, res);

                    res = timeoutService.AsyncAddWithDelay(7, 8, TimeSpan.FromMilliseconds(50));
                    Assert.AreEqual(7 + 8, res);

                    res = timeoutService.AddWithDelay(9, 10, TimeSpan.FromMilliseconds(50));
                    Assert.AreEqual(9 + 10, res);
                }
            }
            finally
            {
                await host.ShutdownAsync();
            }
        }

    }
}
