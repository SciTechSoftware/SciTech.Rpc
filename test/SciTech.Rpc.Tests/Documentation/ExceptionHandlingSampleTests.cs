using NUnit.Framework;
using SciTech.Rpc.Client;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Server;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;

// The samples of docs/exception-handling.md, which are tested to make sure that the documentation stays correct.
namespace SciTech.Rpc.Tests.Documentation
{
    public static class ThermostatFaults
    {
        public const string CalibrationFailed = "CalibrationFailed";

        public const string DeviceAction = "DeviceAction";

        public const string UnauthorizedAccess = "UnauthorizedAccess";

        public static readonly IReadOnlyList<IRpcExceptionConverter> ExceptionConverters = new IRpcExceptionConverter[]
        {
            new RpcExceptionConverter<UnauthorizedAccessException>(UnauthorizedAccess),
        };
    }

    [DataContract]
    public class TemperatureFault
    {
        [DataMember(Order = 1)]
        public double Temperature { get; set; }
    }

    public class DeviceActionException : Exception
    {
        public DeviceActionException(string message) : base(message)
        {
        }
    }

    public class CalibrationException : Exception
    {
        public CalibrationException(string message) : base(message)
        {
        }
    }

    public class TemperatureException : Exception
    {
        public TemperatureException(string message, double temperature) : base(message)
        {
            this.Temperature = temperature;
        }

        public double Temperature { get; }
    }

    public class TemperatureExceptionConverter : RpcExceptionConverter<TemperatureException, TemperatureFault>
    {
        public TemperatureExceptionConverter() : base(includeSubTypes: true)
        {
        }

        public override TemperatureException CreateException(string message, TemperatureFault details)
            => new TemperatureException(message, details.Temperature);

        public override RpcFaultException CreateFault(TemperatureException exception)
            => new RpcFaultException<TemperatureFault>(this.FaultCode, exception.Message,
                new TemperatureFault { Temperature = exception.Temperature });
    }

    [RpcService]
    [RpcFaultConverter(ThermostatFaults.DeviceAction, typeof(DeviceActionException))]
    public interface IThermostatService
    {
        [RpcFault(typeof(TemperatureFault))]
        Task SetTargetTemperatureAsync(double temperature);

        [RpcFaultConverter(ThermostatFaults.CalibrationFailed, typeof(CalibrationException))]
        Task CalibrateAsync();

        Task StartAsync();

        [RpcFaultConverter(typeof(TemperatureExceptionConverter))]
        Task HeatAsync(double temperature);

        Task ResetAsync();
    }

    public class ThermostatService : IThermostatService
    {
        private const double MaxTemperature = 100;

        public Task SetTargetTemperatureAsync(double temperature)
        {
            if (temperature > MaxTemperature)
            {
                throw new RpcFaultException<TemperatureFault>("The temperature is too high.", new TemperatureFault { Temperature = temperature });
            }

            return Task.CompletedTask;
        }

        public Task CalibrateAsync() => throw new CalibrationException("The calibration failed.");

        public Task StartAsync() => throw new DeviceActionException("The device is busy.");

        public Task HeatAsync(double temperature) => throw new TemperatureException("The temperature can't be reached.", temperature);

        public Task ResetAsync() => throw new UnauthorizedAccessException("Not allowed to reset.");
    }

    [TestFixture(RpcConnectionType.LightweightInproc)]
#if NET
    [TestFixture(RpcConnectionType.NetGrpc)]
#endif
    public class ExceptionHandlingSampleTests : ClientServerTestsBase
    {
        public ExceptionHandlingSampleTests(RpcConnectionType connectionType)
            : base(new ProtobufRpcSerializer(), connectionType)
        {
        }

        [Test]
        public Task DeclaredFault_Should_ReachClientWithDetails() => this.RunAsync(async thermostat =>
        {
            var exception = Assert.ThrowsAsync<RpcFaultException<TemperatureFault>>(() => thermostat.SetTargetTemperatureAsync(120));
            Assert.AreEqual("The temperature is too high.", exception.Message);
            Assert.AreEqual(120, exception.Fault.Temperature);
            Assert.AreEqual(nameof(TemperatureFault), exception.FaultCode);

            await thermostat.SetTargetTemperatureAsync(20);
        });

        [Test]
        public Task ServiceAndOperationConverters_Should_ConvertExceptions() => this.RunAsync(thermostat =>
        {
            var deviceException = Assert.ThrowsAsync<DeviceActionException>(() => thermostat.StartAsync());
            Assert.AreEqual("The device is busy.", deviceException.Message);

            var calibrationException = Assert.ThrowsAsync<CalibrationException>(() => thermostat.CalibrateAsync());
            Assert.AreEqual("The calibration failed.", calibrationException.Message);
            return Task.CompletedTask;
        });

        [Test]
        public Task ConverterWithDetails_Should_ConvertException() => this.RunAsync(thermostat =>
        {
            var exception = Assert.ThrowsAsync<TemperatureException>(() => thermostat.HeatAsync(250));
            Assert.AreEqual("The temperature can't be reached.", exception.Message);
            Assert.AreEqual(250, exception.Temperature);
            return Task.CompletedTask;
        });

        [Test]
        public Task RegisteredConverter_Should_ConvertException() => this.RunAsync(thermostat =>
        {
            var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(() => thermostat.ResetAsync());
            Assert.AreEqual("Not allowed to reset.", exception.Message);
            return Task.CompletedTask;
        });

        private async Task RunAsync(Func<IThermostatService, Task> test)
        {
            var builder = new RpcServiceDefinitionsBuilder();
            builder.RegisterService<IThermostatService>();

            var (server, connection) = this.CreateServerAndConnection(builder,
                so => so.ExceptionConverters.AddRange(ThermostatFaults.ExceptionConverters),
                co => co.ExceptionConverters.AddRange(ThermostatFaults.ExceptionConverters));
            using var publishedService = server.ServicePublisher.PublishSingleton<IThermostatService>(new ThermostatService());
            server.Start();
            try
            {
                await test(connection.GetServiceSingleton<IThermostatService>());
            }
            finally
            {
                await server.ShutdownAsync();
            }
        }
    }
}
