﻿using SciTech.Rpc.Client;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Server;
using System;
using System.Reflection;

namespace SciTech.Rpc
{
    /// <summary>
    /// <para>
    /// Default implementation of <see cref="IRpcServerExceptionConverter"/> and  <see cref="IRpcClientExceptionConverter"/>
    /// which converts exceptions based on the <typeparamref name="TException"/> type argument, the specified faultCode,
    /// and the message provided to <see cref="CreateException(string)"/>.
    /// </para>
    /// <para>
    /// <b>NOTE! </b>The <see cref="IRpcServerExceptionConverter.CreateFault(Exception)"/> implementation will use
    /// the message from the exception. Make sure that this message doesn't include any sensitive information.
    /// </para>
    /// </summary>
    /// <remarks>
    /// If <see cref="Exception.Message"/> may contain sensitive information, don't use this class as the server side
    /// exception converter. Instead make a custom implementation of <see cref="IRpcServerExceptionConverter"/>.
    /// </remarks>
    /// <typeparam name="TException">Type of the exception that this converter handles.</typeparam>
    public class RpcExceptionConverter<TException> : RpcExceptionConverterBase<TException>
        where TException : Exception
    {
        private ConstructorInfo exceptionCtor;

        /// <summary>
        /// Creates a <see cref="RpcExceptionConverter{TException}"/> associated with the specified <paramref name="faultCode"/>.
        /// </summary>
        /// <param name="faultCode"></param>
        public RpcExceptionConverter(string faultCode, bool includeSubTypes) : base(faultCode, includeSubTypes)
        {
            this.exceptionCtor = typeof(TException).GetConstructor(new Type[] { typeof(string) })
                ?? throw new InvalidOperationException("Exception type must have a constructor accepting a message argument.");
        }

        public override TException CreateException(string message)
        {
            return (TException)this.exceptionCtor.Invoke(new object[] { message });
        }

        public override RpcFaultException? CreateFault(TException exception)
        {
            if (exception is null) throw new ArgumentNullException(nameof(exception));

            return new RpcFaultException(this.FaultCode, exception.Message);
        }
    }

    /// <summary>
    /// <para>
    /// Base implementation of <see cref="IRpcServerExceptionConverter"/> and  <see cref="IRpcClientExceptionConverter"/> for
    /// exception converters that include fault details.
    /// </para>
    /// <para>Derived classes must implement the abstract methods <see cref="CreateException(string, TFault)"/> and <see cref="CreateFault(TException)"/>.
    /// </para>
    /// </summary>
    /// <typeparam name="TException">Type of the exception that this converter handles.</typeparam>
    /// <typeparam name="TFault">Type of the fault details that this converter handles.</typeparam>
    public abstract class RpcExceptionConverter<TException, TFault> : IRpcServerExceptionConverter, IRpcClientExceptionConverter
        where TException : Exception
        where TFault : class
    {
        protected RpcExceptionConverter(bool includeSubTypes) : this(null, includeSubTypes)
        {
        }

        protected RpcExceptionConverter(string? faultCode, bool includeSubTypes)
        {
            // TODO: Move RetrieveFaultCode to a better location.
            this.FaultCode = faultCode ?? RpcBuilderUtil.RetrieveFaultCode(typeof(TFault));
            this.IncludeSubTypes = includeSubTypes;
        }

        public Type ExceptionType => typeof(TException);

        public string FaultCode { get; }

        public Type? FaultDetailsType => typeof(TFault);

        public bool IncludeSubTypes { get; }

        public abstract TException CreateException(string message, TFault details);

        public abstract RpcFaultException? CreateFault(TException exception);


        RpcFaultException? IRpcServerExceptionConverter.CreateFault(Exception exception)
        {
            if (exception is TException typedException)
            {
                if (this.IncludeSubTypes || typedException.GetType().Equals(typeof(TException)))
                {
                    return this.CreateFault((TException)exception);
                }
            }

            return null;
        }

        Exception? IRpcClientExceptionConverter.TryCreateException(RpcFaultException faultException)
        {
            if (faultException is RpcFaultException<TFault> typedException )
            {
                return this.CreateException(typedException.Message, typedException.Fault);
            }

            return null;
        }
    }

    //[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false)]
    //public class RpcExceptionConverterAttribute : Attribute
    //{
    //    public RpcExceptionConverterAttribute(Type converterType, Type exceptionType)
    //    {
    //        this.ConverterType = converterType;
    //        this.ExceptionType = exceptionType;
    //    }

    //    public Type ConverterType { get; }

    //    public Type ExceptionType { get; }
    //}

    /// <summary>
    /// <para>
    /// Base implementation of <see cref="IRpcServerExceptionConverter"/> and  <see cref="IRpcClientExceptionConverter"/> for
    /// exception converters that do include additional fault details.
    /// </para>
    /// <para>Derived classes must implement the abstract methods <see cref="CreateException(string)"/> and <see cref="CreateFault(TException)"/>.
    /// </para>
    /// </summary>
    /// <typeparam name="TException">Type of the exception that this converter handles.</typeparam>
    public abstract class RpcExceptionConverterBase<TException> : IRpcServerExceptionConverter, IRpcClientExceptionConverter
        where TException : Exception
    {
        protected RpcExceptionConverterBase(string faultCode, bool includeSubTypes)
        {
            if (string.IsNullOrWhiteSpace(faultCode))
            {
                throw new ArgumentException("RpcExceptionConverter must have a fault code.", nameof(faultCode));
            }

            this.FaultCode = faultCode;
            this.IncludeSubTypes = includeSubTypes;
        }

        public Type ExceptionType => typeof(TException);

        public string FaultCode { get; }

        public Type? FaultDetailsType => null;

        public bool IncludeSubTypes { get; }

        public abstract TException CreateException(string message);

        public abstract RpcFaultException? CreateFault(TException exception);

        RpcFaultException? IRpcServerExceptionConverter.CreateFault(Exception exception)
        {
            if (exception is TException typedException)
            {
                if (this.IncludeSubTypes || typedException.GetType().Equals(typeof(TException)))
                {
                    return this.CreateFault((TException)exception);
                }
            }

            return null;
        }

        Exception? IRpcClientExceptionConverter.TryCreateException(RpcFaultException faultException)
        {
            if( faultException.FaultCode == this.FaultCode)
            {
                return this.CreateException(faultException.FaultCode);
            }

            return null;
        }
    }

    public class RpcFaultExceptionConverter : RpcExceptionConverterBase<RpcFaultException>
    {
        public RpcFaultExceptionConverter(string faultCode) : base(faultCode, false)
        {
        }

        public override RpcFaultException CreateException(string message)
        {
            return new RpcFaultException(this.FaultCode, message);
        }

        public override RpcFaultException? CreateFault(RpcFaultException exception)
        {
            if (exception is null) throw new ArgumentNullException(nameof(exception));

            if (exception.FaultCode == this.FaultCode)
            {
                return new RpcFaultException(this.FaultCode, exception.Message);
            }

            return null;
        }
    }

    public sealed class RpcFaultExceptionConverter<TFault> : RpcExceptionConverter<RpcFaultException<TFault>, TFault>
        where TFault : class
    {
        public static readonly RpcFaultExceptionConverter<TFault> Default = new RpcFaultExceptionConverter<TFault>();

        public RpcFaultExceptionConverter(string faultCode) : base(faultCode, false)
        {
        }

        public RpcFaultExceptionConverter() : base(false)
        {
        }

        public override RpcFaultException<TFault> CreateException(string message, TFault details)
        {
            return new RpcFaultException<TFault>(message, details);
        }

        public override RpcFaultException? CreateFault(RpcFaultException<TFault> exception)
        {
            if (exception is null) throw new ArgumentNullException(nameof(exception));

            return new RpcFaultException<TFault>(exception.Message, exception.Fault);
        }
    }
}