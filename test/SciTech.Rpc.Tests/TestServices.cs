using ProtoBuf;
using SciTech.Rpc.Client;
using SciTech.Rpc.Server;
using SciTech.Threading;
using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SciTech.Rpc.Tests
{
    [RpcService]
    public interface IBlockingService
    {
        double Value { get; set; }

        [RpcOperation(AllowInlineExecution = true)]
        int Add(int a, int b);
    }

    [RpcService(Name = "BlockingService")]
    public interface IBlockingServiceClient : IBlockingService, IRpcService
    {
        Task<int> AddAsync(int a, int b);

        Task<double> GetValueAsync();

        Task SetValueAsync(double value);
    }

    [RpcService]
    public interface IDeclaredFaultsService
    {
        /// <summary>
        /// Just a simple operation to make sure that the service is reachable.
        /// </summary>
        int Add(int a, int b);

        [RpcFault(typeof(AnotherDeclaredFault))]
        void GenerateAnotherDeclaredFault(int faultArg);

        [RpcFault(typeof(DeclaredFault))]
        [RpcFault(typeof(AnotherDeclaredFault))]
        Task GenerateAsyncAnotherDeclaredFaultAsync(bool direct);

        [RpcFault(typeof(DeclaredFault))]
        Task<int> GenerateAsyncDeclaredFaultAsync(bool direct);

        [RpcFault(typeof(DeclaredFault))]
        void GenerateCustomDeclaredExceptionAsync();

        [RpcFault(typeof(DeclaredFault))]
        int GenerateDeclaredFault(int ignored);

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

    [RpcService]
    public interface IEmptyDerivedService : ISimpleService, ISimpleService2
    {
    }

    [RpcService(Name = "DeclaredFaultsService", ServerDefinitionType = typeof(IDeclaredFaultsService))]
    public interface IFaultServiceClient : IDeclaredFaultsService
    {
        [RpcFault(typeof(AnotherDeclaredFault))]
        Task GenerateAnotherDeclaredFaultAsync(int faultArg);

        [RpcFault(typeof(DeclaredFault))]
        [RpcFault(typeof(AnotherDeclaredFault))]
        void GenerateAsyncAnotherDeclaredFault(bool direct);

        int GenerateAsyncDeclaredFault(bool direct);

        Task<int> GenerateDeclaredFaultAsync(int ignored);

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
    public interface IImplicitServiceProviderService
    {
        ISimpleService FirstSimpleService { get; }

        IBlockingService GetBlockingService(int index);

        IBlockingService[] GetBlockingServices();

        ISimpleService GetSimpleService(int index);

        ISimpleService[] GetSimpleServices();
    }

    [RpcService(Name = "ImplicitServiceProviderService")]
    public interface IImplicitServiceProviderServiceClient : IImplicitServiceProviderService
    {
        new IBlockingServiceClient GetBlockingService(int index);

        Task<RpcObjectRef<IBlockingServiceClient>> GetBlockingServiceAsync(int index);

        new IBlockingServiceClient[] GetBlockingServices();

        [RpcOperation(Name = "GetBlockingServices")]
        Task<RpcObjectRef<IBlockingServiceClient>[]> GetBlockingServices2Async();

        Task<IBlockingServiceClient[]> GetBlockingServicesAsync();
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

    public interface INotRpcService
    {
        Task<int> AddAsync(int a, int b);
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

    [RpcService]
    public interface IServiceProviderService
    {
        RpcObjectRef<ISimpleService> GetSimpleService();
    }

    [RpcService(Name = "ServiceProviderService")]
    public interface IServiceProviderServiceClient : IServiceProviderService, IRpcService
    {
        Task<RpcObjectRef<ISimpleService>> GetSimpleServiceAsync();
    }

    [RpcService]
    public interface IServiceWithEvents
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

        Task<int[]> GetArrayAsync(int count);

        Task<double> GetValueAsync();

        Task SetValueAsync(double value);

        Task<int> SumAsync(int[] data);
    }

    [RpcService]
    public interface ISimpleService2
    {
        Task<int> SubAsync(int a, int b);
    }

    [RpcService]
    public interface ISimpleServiceWithEvents : ISimpleService
    {
        event EventHandler<ValueChangedEventArgs> DetailedValueChanged;

        event EventHandler ValueChanged;
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

    [RpcService]
    public interface ITimeoutTestService
    {
        int AddWithDelay(int a, int b, TimeSpan delay);

        Task<int> AsyncAddWithDelayAsync(int a, int b, TimeSpan delay, CancellationToken cancellationToken);
    }

    [RpcService(ServerDefinitionType = typeof(ITimeoutTestService))]
    public interface ITimeoutTestServiceClient : ITimeoutTestService
    {
        Task<int> AddWithDelayAsync(int a, int b, TimeSpan delay);

        int AsyncAddWithDelay(int a, int b, TimeSpan delay);
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

        [DataMember(Order = 2)]
        public int Argument { get; private set; }

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

        [DataMember(Order = 2)]
        public int Argument { get; private set; }

        [DataMember(Order = 1)]
        public string Message { get; private set; }
    }

    public class AutoPublishServiceProviderServiceImpl : IImplicitServiceProviderService
    {
        private IBlockingService[] blockingServiceImpls = new IBlockingService[] {
            new TestBlockingSimpleServiceImpl(),
            new TestBlockingSimpleServiceImpl()
        };

        private ISimpleService[] serviceImpls = new ISimpleService[] {
            new TestSimpleServiceImpl(),
            new TestSimpleServiceImpl()
        };

        public AutoPublishServiceProviderServiceImpl()
        {
        }

        public ISimpleService FirstSimpleService => this.serviceImpls[0];

        public IBlockingService GetBlockingService(int index)
        {
            return this.blockingServiceImpls[index];
        }

        public IBlockingService[] GetBlockingServices()
        {
            return this.blockingServiceImpls;
        }

        public ISimpleService GetSimpleService(int index)
        {
            return this.serviceImpls[index];
        }

        public ISimpleService[] GetSimpleServices()
        {
            return this.serviceImpls;
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

    public class DeviceServiceImpl : IDeviceService
    {
        public Guid DeviceAcoId { get; } = Guid.NewGuid();
    }

    public class FaultServiceImpl : IDeclaredFaultsService
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

    public class ImplicitServiceProviderServiceImpl : IImplicitServiceProviderService, IDisposable
    {
        private IBlockingService[] blockingServiceImpls = new IBlockingService[] {
            new TestBlockingSimpleServiceImpl(),
            new TestBlockingSimpleServiceImpl()
        };

        private ScopedObject<RpcObjectRef<IBlockingService>>[] blockingServiceScopes;

        private ISimpleService[] serviceImpls = new ISimpleService[] {
            new TestSimpleServiceImpl(),
            new TestSimpleServiceImpl()
        };

        private ScopedObject<RpcObjectRef<ISimpleService>>[] simpleServiceScopes;

        public ImplicitServiceProviderServiceImpl(IRpcServicePublisher publisher)
        {
            this.simpleServiceScopes = Array.ConvertAll(this.serviceImpls,
                s => publisher.PublishInstance<ISimpleService>(s));
            this.blockingServiceScopes = Array.ConvertAll(this.blockingServiceImpls,
                s => publisher.PublishInstance<IBlockingService>(s));
        }

        public ISimpleService FirstSimpleService => this.serviceImpls[0];

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

        public IBlockingService GetBlockingService(int index)
        {
            return this.blockingServiceImpls[index];
        }

        public IBlockingService[] GetBlockingServices()
        {
            return this.blockingServiceImpls;
        }

        public ISimpleService GetSimpleService(int index)
        {
            return this.serviceImpls[index];
        }

        public ISimpleService[] GetSimpleServices()
        {
            return this.serviceImpls;
        }
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

    public class ServiceProviderServiceImpl : IServiceProviderService, IDisposable
    {
        private ScopedObject<RpcObjectRef<ISimpleService>> simpleServiceScope;

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
            return this.simpleServiceScope.Value;
        }
    }

    public class TestBlockingServiceImpl : IBlockingService
    {
        internal int nBlockingAdd;

        internal int nBlockingGetValue;

        internal int nBlockingSetValue;

        protected double value;

        public double Value
        {
            get { this.nBlockingGetValue++; return this.value; }
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
        internal int nBlockingAdd;

        internal int nBlockingGetValue;

        internal int nBlockingSetValue;

        public double Value
        {
            get { this.nBlockingGetValue++; return this.value; }
            set { this.nBlockingSetValue++; this.value = value; }
        }

        public int Add(int a, int b)
        {
            this.nBlockingAdd++;
            return a + b;
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

        public Task<int[]> GetArrayAsync(int count)
        {
            int[] data = new int[count];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = i;
            }

            return Task.FromResult(data);
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
            for (int i = 0; i < data.Length; i++)
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

    public static class TestTaskExtensions
    {
        public static bool IsCompletedSuccessfully(this Task task)
        {
            return task.Status == TaskStatus.RanToCompletion;
        }
    }

    public class TestTimeoutServiceImpl : ITimeoutTestService
    {
        public int AddWithDelay(int a, int b, TimeSpan delay)
        {
            Thread.Sleep(delay);
            return a + b;
        }

        public async Task<int> AsyncAddWithDelayAsync(int a, int b, TimeSpan delay, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return a + b;
        }
    }

    public class ThermostatServiceImpl : DeviceServiceImpl, IThermostatService
    {
        public void ScanTo(double temp)
        {
            Console.WriteLine("Scanning");
            Thread.Sleep(10);
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
}
