using ProtoBuf;
using SciTech.Rpc.Client;
using SciTech.Rpc.Server;
using SciTech.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests
{
    [RpcService]
    public interface IEmptyDerivedService : ISimpleService, ISimpleService2
    {
    }

    public interface INotRpcService
    {
        Task<int> AddAsync(int a, int b);
    }

    [RpcService]
    public interface IServiceWithEvents
    {
        event EventHandler<ValueChangedEventArgs> DetailedValueChanged;

        event EventHandler ValueChanged;
    }

    [RpcService]
    public interface ISimpleServiceWithEvents : ISimpleService
    {
        event EventHandler<ValueChangedEventArgs> DetailedValueChanged;

        event EventHandler ValueChanged;
    }

    [RpcService]
    public interface IServiceWithUnserializableEvent
    {
        event EventHandler<UnserializableEventArgs> UnserializableValueChanged;

    }

    [RpcService]
    public interface ISimpleService
    {
        Task<int> AddAsync(int a, int b);

        Task<double> GetValueAsync();


        Task SetValueAsync(double value);

        Task<int> SumAsync(int[] data);

        Task<int[]> GetArrayAsync(int count);
    }

    [RpcService]
    public interface IBlockingService
    {
        int Add(int a, int b);

        double Value { get; set; }
    }

    [RpcService(Name = "BlockingService")]
    public interface IBlockingServiceClient : IBlockingService, IRpcService
    {
        Task<int> AddAsync(int a, int b);

        Task<double> GetValueAsync();

        Task SetValueAsync(double value);
    }

    [RpcService]
    public interface IFaultService
    {
        /// <summary>
        /// Just a simple operation to make sure that the service is reachable.
        /// </summary>
        int Add(int a, int b);

        [RpcFault(typeof(DeclaredFault))]
        int GenerateDeclaredFault(int ignored);


        [RpcFault(typeof(DeclaredFault))]
        Task<int> GenerateAsyncDeclaredFaultAsync(bool direct);

        [RpcFault(typeof(AnotherDeclaredFault))]
        void GenerateAnotherDeclaredFault(int faultArg);


        [RpcFault(typeof(DeclaredFault))]
        [RpcFault(typeof(AnotherDeclaredFault))]
        Task GenerateAsyncAnotherDeclaredFaultAsync(bool direct);

        void GenerateNoDetailsFault();


        [RpcFault(typeof(DeclaredFault))]
        Task GenerateUndeclaredAsyncExceptionAsync(bool direct);

        [RpcFault(typeof(DeclaredFault))]
        Task<int> GenerateUndeclaredAsyncExceptionWithReturnAsync(bool direct);

        [RpcFault(typeof(DeclaredFault))]
        void GenerateUndeclaredException();

        [RpcFault(typeof(DeclaredFault))]
        int GenerateUndeclaredExceptionWithReturn();


        [RpcFault(typeof(DeclaredFault))]
        Task GenerateUndeclaredFaultExceptionAsync(bool direct);

        [RpcFault(typeof(DeclaredFault))]
        void GenerateCustomDeclaredExceptionAsync();
    }

    [RpcService(Name = "FaultService", ServerDefinitionType = typeof(IFaultService))]
    public interface IFaultServiceClient : IFaultService
    {
        Task<int> GenerateDeclaredFaultAsync(int ignored);

        [RpcFault(typeof(AnotherDeclaredFault))]
        Task GenerateAnotherDeclaredFaultAsync(int faultArg);

        int GenerateAsyncDeclaredFault(bool direct);


        [RpcFault(typeof(DeclaredFault))]
        [RpcFault(typeof(AnotherDeclaredFault))]
        void GenerateAsyncAnotherDeclaredFault(bool direct);

        [RpcFault(typeof(DeclaredFault))]
        void GenerateUndeclaredAsyncException(bool direct);

        [RpcFault(typeof(DeclaredFault))]
        int GenerateUndeclaredAsyncExceptionWithReturn(bool direct);

        [RpcFault(typeof(DeclaredFault))]
        Task GenerateUndeclaredExceptionAsync();

        [RpcFault(typeof(DeclaredFault))]
        Task<int> GenerateUndeclaredExceptionWithReturnAsync();
    }


    [RpcService]
    public interface IIncorrectFaultOpService
    {
        [RpcFault(typeof(DeclaredFault))]
        void IncorrectFaultDeclaration();
    }

    [RpcService(ServerDefinitionType = typeof(IIncorrectFaultOpService))]
    public interface IIncorrectFaultOpServiceClient
    {
        [RpcFault(typeof(AnotherDeclaredFault))]
        void IncorrectFaultDeclaration();
    }

    [RpcService]
    [RpcFault(typeof(DeclaredFault))]
    public interface IIncorrectServiceFaultService
    {
        void IncorrectFaultDeclaration();
    }

    [RpcService(ServerDefinitionType = typeof(IIncorrectServiceFaultService))]
    [RpcFault(typeof(AnotherDeclaredFault))]
    public interface IIncorrectServiceFaultServiceClient
    {
        void IncorrectFaultDeclaration();
    }

    [RpcService]
    [RpcFault(typeof(ServiceDeclaredFault))]
    [RpcFault(typeof(AnotherServiceDeclaredFault))]
    public interface IServiceFaultService
    {
        /// <summary>
        /// Just a simple operation to make sure that the service is reachable.
        /// </summary>
        int Add(int a, int b);

        [RpcFault(typeof(DeclaredFault))]
        [RpcFault(typeof(AnotherDeclaredFault))]
        void GenerateServiceFault(bool useAnotherFault, bool useServiceFault);
    }

    [RpcService(Name = "ServiceFaultService", ServerDefinitionType = typeof(IServiceFaultService))]
    [RpcFault(typeof(AnotherServiceDeclaredFault))]
    public interface IServiceFaultServiceClient : IServiceFaultService
    {

        [RpcFault(typeof(DeclaredFault))]
        [RpcFault(typeof(AnotherDeclaredFault))]
        Task GenerateServiceFaultAsync(bool useAnotherFault, bool useServiceFault);
    }

    public class ServiceFaultServiceImpl : IServiceFaultService
    {
        public int Add(int a, int b)
        {
            return a + b;
        }

        public void GenerateServiceFault(bool useAnotherFault, bool useServiceFault)
        {
            if (useAnotherFault)
            {
                if (useServiceFault)
                {
                    throw new RpcFaultException<AnotherServiceDeclaredFault>("Another service fault", new AnotherServiceDeclaredFault("This is another service fault.", 124));
                }
                else
                {
                    throw new RpcFaultException<AnotherDeclaredFault>("Another fault", new AnotherDeclaredFault("This is another fault.", 124));
                }
            }
            else
            {
                if (useServiceFault)
                {
                    throw new RpcFaultException<ServiceDeclaredFault>("Service fault", new ServiceDeclaredFault("This is a service fault."));
                }
                else
                {
                    throw new RpcFaultException<DeclaredFault>("Fault", new DeclaredFault("This is a fault."));

                }
            }
        }
    }


    public class FaultServiceImpl : IFaultService
    {
        public int Add(int a, int b)
        {
            return a + b;
        }

        public void GenerateAnotherDeclaredFault(int faultArg)
        {
            throw new RpcFaultException<AnotherDeclaredFault>("Another fault", new AnotherDeclaredFault("Another fault, with arg,", faultArg));
        }

        public async Task GenerateAsyncAnotherDeclaredFaultAsync(bool direct)
        {
            if (direct)
            {
                throw new RpcFaultException<DeclaredFault>(new DeclaredFault("This is declared, before await."));
            }

            await Task.Delay(1);

            throw new RpcFaultException<AnotherDeclaredFault>(new AnotherDeclaredFault("This is another declared, after await.", 1234));
        }

        public async Task<int> GenerateAsyncDeclaredFaultAsync(bool direct)
        {
            if (direct)
            {
                throw new RpcFaultException<DeclaredFault>(new DeclaredFault("This is declared, before await."));
            }

            await Task.Delay(1);

            throw new RpcFaultException<DeclaredFault>(new DeclaredFault("This is declared, after await."));
        }

        public void GenerateCustomDeclaredExceptionAsync()
        {
            throw new DeclaredFaultException("A custom declared fault.", "With detailed message.");
        }

        public int GenerateDeclaredFault(int ignored)
        {
            throw new RpcFaultException<DeclaredFault>(new DeclaredFault("This is declared"));
        }

        public void GenerateNoDetailsFault()
        {
            throw new RpcFaultException("NoDetailsFault", "Fault with no details");
        }


        public async Task GenerateUndeclaredAsyncExceptionAsync(bool direct)
        {
            if (direct)
            {
                throw new InvalidOperationException("Undeclared exception before await");
            }

            await Task.Delay(1).ContextFree();

            throw new InvalidOperationException("Undeclared exception after await");
        }

        public async Task<int> GenerateUndeclaredAsyncExceptionWithReturnAsync(bool direct)
        {
            if (direct)
            {
                throw new InvalidOperationException("Undeclared exception before await");
            }

            await Task.Delay(1).ContextFree();

            throw new InvalidOperationException("Undeclared exception after await");
        }

        public void GenerateUndeclaredException()
        {
            throw new InvalidOperationException("Undeclared exception");
        }

        public int GenerateUndeclaredExceptionWithReturn()
        {
            throw new InvalidOperationException("Undeclared exception");
        }

        public async Task GenerateUndeclaredFaultExceptionAsync(bool direct)
        {
            if (direct)
            {
                throw new RpcFaultException<AnotherDeclaredFault>(new AnotherDeclaredFault("Another fault, with arg,", 1));
            }

            await Task.Delay(1).ContextFree();

            throw new RpcFaultException<AnotherDeclaredFault>(new AnotherDeclaredFault("Another fault, with arg,", 2));
        }

    }

    [DataContract]
    [ProtoContract(SkipConstructor = true)]
    public class DeclaredFault
    {
        public DeclaredFault(string message)
        {
            this.Message = message;
        }

        [DataMember(Order = 1)]
        public string Message { get; private set; }
    }

    [DataContract]
    [ProtoContract(SkipConstructor = true)]
    public class AnotherDeclaredFault
    {
        public AnotherDeclaredFault(string message, int argument)
        {
            this.Message = message;
            this.Argument = argument;
        }

        [DataMember(Order = 1)]
        public string Message { get; private set; }

        [DataMember(Order = 2)]
        public int Argument { get; private set; }
    }

    [DataContract]
    [ProtoContract(SkipConstructor = true)]
    public class ServiceDeclaredFault
    {
        public ServiceDeclaredFault(string message)
        {
            this.Message = message;
        }

        [DataMember(Order = 1)]
        public string Message { get; private set; }
    }

    [DataContract]
    [ProtoContract(SkipConstructor = true)]
    public class AnotherServiceDeclaredFault
    {
        public AnotherServiceDeclaredFault(string message, int argument)
        {
            this.Message = message;
            this.Argument = argument;
        }

        [DataMember(Order = 1)]
        public string Message { get; private set; }

        [DataMember(Order = 2)]
        public int Argument { get; private set; }
    }


    [RpcService]
    public interface ISimpleService2
    {
        Task<int> SubAsync(int a, int b);
    }

    [RpcService]
    public interface IServiceProviderService
    {
        RpcObjectRef<ISimpleService> GetSimpleService();
    }


    [RpcService]
    public interface IImplicitServiceProviderService
    {
        ISimpleService FirstSimpleService { get; }

        ISimpleService GetSimpleService(int index);

        ISimpleService[] GetSimpleServices();

        IBlockingService GetBlockingService(int index);

        IBlockingService[] GetBlockingServices();

    }

    [RpcService(Name = "ImplicitServiceProviderService")]
    public interface IImplicitServiceProviderServiceClient : IImplicitServiceProviderService
    {
        new IBlockingServiceClient GetBlockingService(int index);


        Task<RpcObjectRef<IBlockingServiceClient>> GetBlockingServiceAsync(int index);

        new IBlockingServiceClient[] GetBlockingServices();

        Task<IBlockingServiceClient[]> GetBlockingServicesAsync();

        [RpcOperation(Name = "GetBlockingServices")]
        Task<RpcObjectRef<IBlockingServiceClient>[]> GetBlockingServices2Async();
    }


    [RpcService(Name = "ServiceProviderService")]
    public interface IServiceProviderServiceClient : IServiceProviderService, IRpcService
    {
        Task<RpcObjectRef<ISimpleService>> GetSimpleServiceAsync();
    }
    [RpcService]
    public interface IDeviceService
    {
        Guid DeviceAcoId { get; }
    }
    [RpcService(Name = "DeviceService")]
    public interface IDeviceServiceClient : IDeviceService, IRpcService
    {
        Task<Guid> GetDeviceAcoIdAsync();
    }

    public class DeviceServiceImpl : IDeviceService
    {
        public Guid DeviceAcoId { get; } = Guid.NewGuid();
    }

    [RpcService]
    public interface IThermostatService : IDeviceService
    {
        void ScanTo(double temp);
    }

    [RpcService(Name = "ThermostatService")]
    public interface IThermostatServiceClient : IThermostatService, IDeviceServiceClient, IRpcService
    {
        Task ScanToAsync(double temp);
    }

    public class ThermostatServiceImpl : DeviceServiceImpl, IThermostatService
    {
        public void ScanTo(double temp)
        {
            Console.WriteLine("Scanning");
            Thread.Sleep(10);
        }
    }

    public class DeclaredFaultException : Exception
    {
        public DeclaredFaultException(string message, string detailedMessage) : base(message)
        {
            this.DetailedMessage = detailedMessage;
        }

        public string DetailedMessage { get; }

    }


    public class DeclaredFaultException2 : Exception
    {
        public DeclaredFaultException2(string message, string detailedMessage) : base(message)
        {
            this.DetailedMessage = detailedMessage;
        }

        public string DetailedMessage { get; }

    }

    public class DeclaredFaultExceptionConverter : RpcExceptionConverter<DeclaredFaultException, DeclaredFault>
    {
        public override DeclaredFaultException CreateException(string message, DeclaredFault details)
        {
            return new DeclaredFaultException(message, details.Message);
        }

        public override ConvertedFault CreateFault(DeclaredFaultException exception)
        {
            return new ConvertedFault(this.FaultCode, exception.Message, new DeclaredFault(exception.DetailedMessage));
        }
    }

    public class DeclaredFaultExceptionConverter2 : RpcExceptionConverter<DeclaredFaultException2, DeclaredFault>
    {
        public override DeclaredFaultException2 CreateException(string message, DeclaredFault details)
        {
            return new DeclaredFaultException2(message, details.Message);
        }

        public override ConvertedFault CreateFault(DeclaredFaultException2 exception)
        {
            return new ConvertedFault(this.FaultCode, exception.Message, new DeclaredFault(exception.DetailedMessage));
        }
    }

    public class TestServiceWithEventsImpl : ISimpleServiceWithEvents
    {
        private EventHandler<ValueChangedEventArgs> detailedValueChanged;

        private double value;

        private EventHandler valueChanged;

        public event EventHandler<ValueChangedEventArgs> DetailedValueChanged

        {
            add { this.detailedValueChanged = (EventHandler<ValueChangedEventArgs>)Delegate.Combine(this.detailedValueChanged, value); }
            remove { this.detailedValueChanged = (EventHandler<ValueChangedEventArgs>)Delegate.Remove(this.detailedValueChanged, value); }

        }

        public event EventHandler ValueChanged
        {
            add { this.valueChanged = (EventHandler)Delegate.Combine(this.valueChanged, value); }
            remove { this.valueChanged = (EventHandler)Delegate.Remove(this.valueChanged, value); }
        }

        public bool HasDetailedValueChangedHandler => this.detailedValueChanged != null;

        public bool HasValueChangedHandler => this.valueChanged != null;

        public Task<int> AddAsync(int a, int b)
        {
            return Task.FromResult(a + b);
        }

        public Task<double> GetValueAsync()
        {
            return Task.FromResult(this.value);
        }


        public async Task SetValueAsync(double value)
        {
            var oldValue = this.value;
            this.value = value;
            this.valueChanged?.Invoke(this, EventArgs.Empty);
            this.detailedValueChanged?.Invoke(this, new ValueChangedEventArgs((int)this.value, (int)oldValue));
            await Task.Delay(20);

            //return Task.CompletedTask;
        }

        public async Task<int> SumAsync(int[] data)
        {
            return await Task.Run(() =>
            {
                if (data == null)
                {
                    throw new ArgumentNullException(nameof(data));
                }

                int sum = data.Sum();

                return sum;
            });
        }

        public Task<int[]> GetArrayAsync(int count)
        {
            int[] data = new int[count];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = i;
            }

            return Task.FromResult(data);
        }
    }

    public class TestSimpleServiceImpl : ISimpleService
    {
        protected double value;

        public Task<int> AddAsync(int a, int b)
        {
            return Task.FromResult(a + b);
        }

        public async Task GenerateUndeclaredExceptionAsync(bool direct)
        {
            if (direct)
            {
                throw new InvalidOperationException("Undeclared exception before await");
            }

            await Task.Delay(1).ContextFree();

            throw new InvalidOperationException("Undeclared exception after await");
        }

        public Task<int[]> GetArrayAsync(int count)
        {
            int[] data = new int[count];
            for(int i=0; i < data.Length;i++)
            {
                data[i] = i;
            }

            return Task.FromResult(data);
        }

        public Task<double> GetValueAsync()
        {
            return Task.FromResult(this.value);
        }

        public Task SetValueAsync(double value)
        {
            this.value = value;
            return Task.CompletedTask;
        }

        public async Task<int> SumAsync(int[] data)
        {
            return await Task.Run(() =>
            {
                if (data == null)
                {
                    throw new ArgumentNullException(nameof(data));
                }

                int sum = data.Sum();

                return sum;
            });
        }
    }


    public class TestBlockingServiceImpl : IBlockingService
    {
        protected double value;

        internal int nBlockingGetValue;
        internal int nBlockingSetValue;
        internal int nBlockingAdd;


        public double Value
        {
            get { nBlockingGetValue++; return this.value; }
            set { this.nBlockingSetValue++; this.value = value; }
        }

        public int Add(int a, int b)
        {
            this.nBlockingAdd++;
            return a + b;
        }

    }

    public class TestBlockingSimpleServiceImpl : TestSimpleServiceImpl, IBlockingService
    {
        internal int nBlockingGetValue;
        internal int nBlockingSetValue;
        internal int nBlockingAdd;


        public double Value
        {
            get { nBlockingGetValue++; return this.value; }
            set { this.nBlockingSetValue++; this.value = value; }
        }

        public int Add(int a, int b)
        {
            this.nBlockingAdd++;
            return a + b;
        }

    }

    public class ServiceProviderServiceImpl : IServiceProviderService, IDisposable
    {
        ScopedObject<RpcObjectRef<ISimpleService>> simpleServiceScope;

        public ServiceProviderServiceImpl(IRpcServicePublisher publisher)
        {
            this.simpleServiceScope = publisher.PublishInstance<ISimpleService>(new TestSimpleServiceImpl());
        }

        public void Dispose()
        {
            this.simpleServiceScope.Dispose();
        }

        public RpcObjectRef<ISimpleService> GetSimpleService()
        {
            return simpleServiceScope.Value;
        }
    }

    public class ImplicitServiceProviderServiceImpl : IImplicitServiceProviderService, IDisposable
    {
        ScopedObject<RpcObjectRef<ISimpleService>>[] simpleServiceScopes;
        ScopedObject<RpcObjectRef<IBlockingService>>[] blockingServiceScopes;

        ISimpleService[] serviceImpls = new ISimpleService[] {
            new TestSimpleServiceImpl(),
            new TestSimpleServiceImpl()
        };

        IBlockingService[] blockingServiceImpls = new IBlockingService[] {
            new TestBlockingSimpleServiceImpl(),
            new TestBlockingSimpleServiceImpl()
        };

        public ImplicitServiceProviderServiceImpl(IRpcServicePublisher publisher)
        {
            this.simpleServiceScopes = Array.ConvertAll(serviceImpls,
                s => publisher.PublishInstance<ISimpleService>(s));
            this.blockingServiceScopes = Array.ConvertAll(blockingServiceImpls,
                s => publisher.PublishInstance<IBlockingService>(s));
        }

        public void Dispose()
        {
            foreach (var scope in this.simpleServiceScopes)
            {
                scope.Dispose();
            }

            foreach (var scope in this.blockingServiceScopes)
            {
                scope.Dispose();
            }
        }

        public ISimpleService FirstSimpleService => this.serviceImpls[0];

        public ISimpleService GetSimpleService(int index)
        {
            return serviceImpls[index];
        }

        public ISimpleService[] GetSimpleServices()
        {
            return this.serviceImpls;
        }

        public IBlockingService GetBlockingService(int index)
        {
            return blockingServiceImpls[index];
        }

        public IBlockingService[] GetBlockingServices()
        {
            return this.blockingServiceImpls;
        }
    }


    public class AutoPublishServiceProviderServiceImpl : IImplicitServiceProviderService
    {
        ISimpleService[] serviceImpls = new ISimpleService[] {
            new TestSimpleServiceImpl(),
            new TestSimpleServiceImpl()
        };

        IBlockingService[] blockingServiceImpls = new IBlockingService[] {
            new TestBlockingSimpleServiceImpl(),
            new TestBlockingSimpleServiceImpl()
        };

        public AutoPublishServiceProviderServiceImpl()
        {
        }

        public ISimpleService FirstSimpleService => this.serviceImpls[0];


        public ISimpleService GetSimpleService(int index)
        {
            return serviceImpls[index];
        }

        public ISimpleService[] GetSimpleServices()
        {
            return serviceImpls;
        }

        public IBlockingService GetBlockingService(int index)
        {
            return blockingServiceImpls[index];
        }

        public IBlockingService[] GetBlockingServices()
        {
            return this.blockingServiceImpls;
        }
    }


    public class UnserializableEventArgs
    {
        public UnserializableEventArgs(double someValue)
        {
            this.SomeValue = someValue;
        }

        public double SomeValue { get; private set; }
    }

    [DataContract]
    [ProtoContract(SkipConstructor = true)]
    public class ValueChangedEventArgs //: EventArgs
    {
        public ValueChangedEventArgs(int newValue, int oldValue)
        {
            this.NewValue = newValue;
            this.OldValue = oldValue;
        }

        [DataMember(Order = 1)]
        public int NewValue { get; private set; }

        [DataMember(Order = 2)]
        public int OldValue { get; private set; }
    }


    public static class TestTaskExtensions
    {
        public static bool IsCompletedSuccessfully(this Task task)
        {
            return task.Status == TaskStatus.RanToCompletion;
        }


    }

}
