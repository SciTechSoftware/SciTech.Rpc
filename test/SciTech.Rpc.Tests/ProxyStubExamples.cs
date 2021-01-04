//
// This file contains example implementations of gRPC proxy and stub classes.
// The classes are not actually used, they are here just to show what the dynamically generated proxy and stub
// code looks like.
//
// NOTE! This file is currently not up-to-date. It contains a mix of old and new implementations.
//
// Stub classes have been commented out, since there's a new implementation based on System.Linq.Expressions instead.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using SciTech.Rpc.Client;
using SciTech.Rpc.Client.Internal;
using SciTech.Rpc.Grpc;
using SciTech.Rpc.Grpc.Client;
using SciTech.Rpc.Grpc.Client.Internal;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Serialization;
using SciTech.Rpc.Tests;
using GrpcCore = Grpc.Core;

namespace SciTech.Rpc
{
    ///// <summary>
    ///// 
    ///// </summary>
    //public class BlockingServiceStub : GrpcStub<IBlockingService>
    //{
    //    protected BlockingServiceStub(IServiceImplProvider serviceImplProvider) : base(serviceImplProvider)
    //    {

    //    }

    //    public Task<RpcResponse<int>> Add(RpcObjectRequest<int, int> request, ServerCallContext context)
    //    {
    //        return this.CallBlockingMethod(request, context,
    //            new Func<IBlockingService, RpcObjectRequest<int, int>, int>(CallAdd));
    //    }

    //    public Task<RpcResponse> SetValue(RpcObjectRequest<double> request, ServerCallContext context)
    //    {
    //        return this.CallVoidBlockingMethod(request, context,
    //            new Action<IBlockingService, RpcObjectRequest<double>>(CallSetValue));
    //    }

    //    public Task<RpcResponse<double>> GetValue(RpcObjectRequest request, ServerCallContext context)
    //    {
    //        return this.CallBlockingMethod(request, context,
    //            new Func<IBlockingService, RpcObjectRequest, double>(CallGetValue));
    //    }


    //    private static int CallAdd(IBlockingService service, RpcObjectRequest<int, int> request)
    //    {
    //        return service.Add(request.Value1, request.Value2);
    //    }

    //    private static void CallSetValue(IBlockingService service, RpcObjectRequest<double> request)
    //    {
    //        service.Value = request.Value1;
    //    }
    //    private static double CallGetValue(IBlockingService service, RpcObjectRequest request)
    //    {
    //        return service.Value;
    //    }
    //}

    //public class SimpleServiceStub : GrpcStub<ISimpleService>
    //{
    //    protected SimpleServiceStub(IServiceImplProvider serviceImplProvider) : base(serviceImplProvider)
    //    {

    //    }

    //    public Task<RpcResponse<int>> Add(RpcObjectRequest<int, int> request, ServerCallContext context)
    //    {
    //        return this.CallMethod(request, context,
    //            new Func<ISimpleService, RpcObjectRequest<int, int>,  Task<int>>(CallAdd));
    //    }

    //    public Task<RpcResponse> SetValue(RpcObjectRequest<double> request, ServerCallContext context)
    //    {
    //        return this.CallVoidMethod(request, context,
    //            new Func<ISimpleService, RpcObjectRequest<double>, Task>(CallSetValue));
    //    }

    //    public Task<RpcResponse<double>> GetValue(RpcObjectRequest request, ServerCallContext context)
    //    {
    //        return this.CallMethod(request, context,
    //            new Func<ISimpleService, RpcObjectRequest, Task<double>>(CallGetValue));
    //    }


    //    private static Task<int> CallAdd(ISimpleService service, RpcObjectRequest<int, int> request)
    //    {
    //        return service.AddAsync(request.Value1, request.Value2);
    //    }

    //    private static Task CallSetValue(ISimpleService service, RpcObjectRequest<double> request)
    //    {
    //        return service.SetValueAsync( request.Value1);
    //    }
    //    private static Task<double> CallGetValue(ISimpleService service, RpcObjectRequest request)
    //    {
    //        return service.GetValueAsync();
    //    }
    //}

    //public class ServiceWithEventsStub : GrpcStub<IServiceWithEvents>
    //{
    //    // TODO:
    //    protected ServiceWithEventsStub(IServiceImplProvider serviceImplProvider) : base(serviceImplProvider)
    //    {

    //    }

    //    private static void AddDetailedValueChangedHandler(IServiceWithEvents service, EventHandler<ValueChangedEventArgs> handler)
    //    {
    //        service.DetailedValueChanged += handler;
    //    }

    //    private static void RemoveDetailedValueChangedHandler(IServiceWithEvents service, EventHandler<ValueChangedEventArgs> handler)
    //    {
    //        service.DetailedValueChanged -= handler;
    //    }

    //    public Task BeginDetailedValueChanged( RpcObjectEventRequest request, GrpcCore.IServerStreamWriter<ValueChangedEventArgs> repsonseStream, GrpcCore.ServerCallContext context )
    //    {
    //        return this.BeginEventProducer(request, repsonseStream, context,
    //            AddDetailedValueChangedHandler,
    //            RemoveDetailedValueChangedHandler);
    //    }
    //}


    //public class DeviceServiceStub : GrpcStub<IDeviceService>
    //{
    //    public DeviceServiceStub(IServiceImplProvider serviceImplProvider) : base(serviceImplProvider)
    //    {

    //    }

    //    public Task<RpcResponse<Guid>> GetDeviceAcoId(RpcObjectRequest request, GrpcCore.ServerCallContext context)
    //    {
    //        return this.CallBlockingMethod(request, context, new Func<IDeviceService, RpcObjectRequest, Guid>(Callget_DeviceAcoId));
    //    }

    //    private static Guid Callget_DeviceAcoId(IDeviceService service, RpcObjectRequest request)
    //    {
    //        return service.DeviceAcoId;
    //    }
    //}

    //public class DeviceServiceStub_Builder
    //{
    //    public (GrpcCore.Method<RpcObjectRequest, RpcResponse<Guid>>, GrpcCore.UnaryServerMethod<RpcObjectRequest, RpcResponse<Guid>>)
    //        Create_DeviceServiceStub_DeviceAcoId_Method(IServiceImplProvider serviceImplProvider)
    //    {
    //        var callMethodInfo = typeof(DeviceServiceStub).GetMethod("GetDeviceAcoId");
    //        var serviceStub = new DeviceServiceStub(serviceImplProvider);

    //        var handler = (GrpcCore.UnaryServerMethod<RpcObjectRequest, RpcResponse<Guid>>)callMethodInfo.CreateDelegate(typeof(GrpcCore.UnaryServerMethod<RpcObjectRequest, RpcResponse<Guid>>), serviceStub);

    //        var grpcMethodDef = GrpcMethodDefinitionGenerator.CreateMethodDefinition<RpcObjectRequest, RpcResponse<Guid>>(MethodType.Unary, "DeviceService", "GetDeviceAcoId", new ProtobufSerializer());
    //        return (grpcMethodDef, handler);

    //    }
    //}

    public static class AnonymousProxyExpressionsClass
    {
        //internal static readonly Func<IRpcSerializer, GrpcProxyMethod[]> BlockingServiceProxyMethodsCreator =
        //    (serializer) =>
        //    {
        //        GrpcProxyMethod[] proxyMethods = new GrpcProxyMethod[] {
        //            GrpcProxyBase.CreateMethodDef<RpcObjectRequest<int, int>, RpcResponse<int>>(RpcMethodType.Unary,
        //                "SciTech.Rpc.BlockingService",
        //                "Add",
        //                null,
        //                null),

        //            GrpcProxyBase.CreateMethodDef<RpcObjectRequest, RpcResponse<double>>(RpcMethodType.Unary,
        //                "SciTech.Rpc.BlockingService",
        //                "GetValue",
        //                serializer,
        //                null),

        //            GrpcProxyBase.CreateMethodDef<RpcObjectRequest<double>, RpcResponse>(RpcMethodType.Unary,
        //                "SciTech.Rpc.BlockingService",
        //                "SetValue",
        //                null,
        //                null),

        //            GrpcProxyBase.CreateMethodDef<RpcObjectRequest<double>, RpcResponse>(RpcMethodType.Unary,
        //                "SciTech.Rpc.FaultService",
        //                "GeneratedDeclaredFault",
        //                null,
        //                new RpcClientFaultHandler(null, new IRpcClientExceptionConverter[]
        //                {
        //                    RpcFaultExceptionConverter<DeclaredFault>.Default
        //                })),

        //            GrpcProxyBase.CreateMethodDef<RpcObjectRequest<double>, RpcResponse>(RpcMethodType.Unary,
        //                "SciTech.Rpc.FaultService",
        //                "GenerateAsyncDeclaredFault",
        //                null,
        //                new RpcClientFaultHandler(null, new IRpcClientExceptionConverter[]
        //                {
        //                    RpcFaultExceptionConverter<DeclaredFault>.Default
        //                })),

        //            GrpcProxyBase.CreateMethodDef<RpcObjectRequest<double>, RpcResponse>(RpcMethodType.Unary,
        //                "SciTech.Rpc.FaultService",
        //                "GenerateAsyncConvertedFault",
        //                null,
        //                new RpcClientFaultHandler(null, new IRpcClientExceptionConverter[]
        //                {
        //                    new RpcFaultExceptionConverter("UnauthorizedAccessError")
        //                })),

        //            GrpcProxyBase.CreateMethodDef<RpcObjectRequest, ValueChangedEventArgs>(
        //                RpcMethodType.EventAdd,
        //                "SciTech.Rpc.ServiceWithEvents",
        //                "BeginDetailedValueChanged",
        //                null,
        //                null),


        //            GrpcProxyBase.CreateMethodDef<RpcObjectRequest, EventArgs>(
        //                RpcMethodType.EventAdd,
        //                "SciTech.Rpc.ServiceWithEvents",
        //                "BeginValueChanged",
        //                null,
        //                null),

        //            GrpcProxyBase.CreateMethodDef<RpcObjectRequest, EventArgs>(
        //                RpcMethodType.EventAdd,
        //                "SciTech.Rpc.ServiceWithEvents",
        //                "BeginValueChanged",
        //                null,
        //                null),

        //            GrpcProxyBase.CreateMethodDef<RpcObjectRequest, RpcResponse<RpcObjectRef>>(RpcMethodType.Unary,
        //                "SciTech.Rpc.ServiceProviderService",
        //                "GetSimpleService",
        //                null,
        //                null)};
        //        return proxyMethods;
        //    };
    }

    //public sealed class BlockingServiceProxy : GrpcProxyBase, IBlockingServiceClient, IFaultService, IServiceWithEvents, IServiceProviderServiceClient
    //{
    //    private static readonly GrpcProxyMethod __Method_SciTech_Rpc_BlockingService_Add;

    //    private static readonly GrpcProxyMethod __Method_SciTech_Rpc_ServiceWithEvents_BeginDetailedValueChanged;



    //    static BlockingServiceProxy()
    //    {
    //        __Method_SciTech_Rpc_BlockingService_Add =
    //            CreateMethodDef<RpcObjectRequest<int, int>, RpcResponse<int>>(
    //                RpcMethodType.Unary,
    //                "SciTech.Rpc.BlockingService",
    //                "Add",
    //                null,
    //                null);

    //        __Method_SciTech_Rpc_ServiceWithEvents_BeginDetailedValueChanged =
    //            CreateMethodDef<RpcObjectRequest, ValueChangedEventArgs>(
    //                RpcMethodType.EventAdd,
    //                "SciTech.Rpc.ServiceWithEvents",
    //                "BeginDetailedValueChanged",
    //                null,
    //                null);

    //    }

    //    public BlockingServiceProxy(GrpcProxyArgs proxyArgs, GrpcProxyMethod[] proxyMethods) 
    //        : base(proxyArgs, proxyMethods)
    //    {
    //    }

    //    event EventHandler<ValueChangedEventArgs> IServiceWithEvents.DetailedValueChanged
    //    {
    //        add
    //        {
    //            this.AddEventHandlerAsync<EventHandler<ValueChangedEventArgs>, ValueChangedEventArgs>(value, 5);
    //        }
    //        remove
    //        {
    //            this.RemoveEventHandlerAsync<EventHandler<ValueChangedEventArgs>, ValueChangedEventArgs>(value, 5);
    //        }
    //    }

    //    event EventHandler IServiceWithEvents.ValueChanged
    //    {
    //        add
    //        {
    //            this.AddEventHandlerAsync<EventHandler, EventArgs>(value, 6);
    //        }

    //        remove
    //        {
    //            this.RemoveEventHandlerAsync<EventHandler, EventArgs>(value, 6);
    //        }
    //    }

    //    double IBlockingService.Value
    //    {
    //        get => this.CallUnaryMethod<RpcObjectRequest, double, double>(this.proxyMethods[1], new RpcObjectRequest(this.objectId), null);
    //        set => this.CallUnaryVoidMethod(this.proxyMethods[2], new RpcObjectRequest<double>(this.objectId, value));
    //    }

    //    int IBlockingService.Add(int a, int b)
    //    {
    //        return this.CallUnaryMethod<RpcObjectRequest<int, int>, int, int>(__Method_SciTech_Rpc_BlockingService_Add, new RpcObjectRequest<int, int>(this.objectId, a, b), null);
    //    }

    //    Task<int> IBlockingServiceClient.AddAsync(int a, int b)
    //    {
    //        return this.CallUnaryMethodAsync<RpcObjectRequest<int, int>, int, int>(__Method_SciTech_Rpc_BlockingService_Add, new RpcObjectRequest<int, int>(this.objectId, a, b), null, CancellationToken.None  );
    //    }

    //    Task<double> IBlockingServiceClient.GetValueAsync()
    //    {
    //        return this.CallUnaryMethodAsync<RpcObjectRequest, double, double>(this.proxyMethods[1], new RpcObjectRequest(this.objectId), null, CancellationToken.None);
    //    }

    //    Task IBlockingServiceClient.SetValueAsync(double value)
    //    {
    //        return this.CallUnaryVoidMethodAsync(this.proxyMethods[2], new RpcObjectRequest<double>(this.objectId, value), CancellationToken.None);
    //    }

    //    // DUMMY!
    //    Task LegacySetValueAsync(int value)
    //    {
    //        var methodDef = this.proxyMethods[2];
    //        return null;// this.CallUnaryVoidMethodAsync(methodDef, new RpcObjectRequest<double>(this.objectId, (double)methodDef.RequestConverter[0](value)));
    //    }

    //    int IFaultService.Add(int a, int b)
    //    {
    //        return a + b;
    //    }

    //    int IFaultService.GenerateDeclaredFault(int ignored)
    //    {
    //        return this.CallUnaryMethod<RpcObjectRequest<int>, int, int>(this.proxyMethods[3], new RpcObjectRequest<int>(this.objectId, ignored), null);
    //    }

    //    Task<int> IFaultService.GenerateAsyncDeclaredFaultAsync(bool direct)
    //    {
    //        return this.CallUnaryMethodAsync<RpcObjectRequest<bool>, int, int>(this.proxyMethods[4], new RpcObjectRequest<bool>(this.objectId, direct), null, CancellationToken.None);
    //    }

    //    Task<RpcObjectRef<ISimpleService>> IServiceProviderServiceClient.GetSimpleServiceAsync()
    //    {
    //        return this.CallUnaryMethodAsync<RpcObjectRequest, RpcObjectRef, RpcObjectRef<ISimpleService>>(
    //            this.proxyMethods[-1],
    //            new RpcObjectRequest(this.objectId),
    //            ServiceRefConverter<ISimpleService>.Default, 
    //            CancellationToken.None);
    //    }

    //    RpcObjectRef<ISimpleService> IServiceProviderService.GetSimpleService()
    //    {
    //        throw new NotImplementedException();
    //    }

    //    public void GenerateAnotherDeclaredFault(int faultArg)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    public Task GenerateAsyncAnotherDeclaredFaultAsync(bool direct)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    public void GenerateNoDetailsFault()
    //    {
    //        throw new NotImplementedException();
    //    }

    //    public Task GenerateUndeclaredExceptionAsync(bool direct)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    public Task GenerateUndeclaredFaultExceptionAsync(bool direct)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    public void GenerateCustomDeclaredExceptionAsync()
    //    {
    //        throw new NotImplementedException();
    //    }
    //}
}
