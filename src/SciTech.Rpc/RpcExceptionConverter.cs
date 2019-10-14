using SciTech.Rpc.Client;
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
    /// NOTE! The <see cref="IRpcServerExceptionConverter.CreateFault(Exception)"/> implementation will use
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
        public RpcExceptionConverter(string faultCode) : base(faultCode)
        {
            this.exceptionCtor = typeof(TException).GetConstructor(new Type[] { typeof(string) }) 
                ?? throw new InvalidOperationException("Exception type must have a constructor accepting a message argument.");
        }

        public override TException CreateException(string message)
        {
            return (TException)this.exceptionCtor.Invoke(new object[] { message });
        }

        public override ConvertedFault? CreateFault(TException exception)
        {
            if (exception is null) throw new ArgumentNullException(nameof(exception));

            return new ConvertedFault(this.FaultCode, exception.Message);
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
    {
        protected RpcExceptionConverter()
        {
            // TODO: Move RetrieveFaultCode to a better location.
            this.FaultCode = RpcBuilderUtil.RetrieveFaultCode(typeof(TFault));

        }

        public Type ExceptionType => typeof(TException);

        public string FaultCode { get; }

        public Type? FaultDetailsType => typeof(TFault);

        public abstract TException CreateException(string message, TFault details);

        public abstract ConvertedFault? CreateFault(TException exception);

        Exception IRpcClientExceptionConverter.CreateException(string message, object? details)
        {
            if (details is TFault faultDetails)
            {
                return this.CreateException(message, faultDetails);
            }

            throw new ArgumentException($"Incorrect fault details, expected details of type '{typeof(TFault)}'.", nameof(details));
        }

        ConvertedFault? IRpcServerExceptionConverter.CreateFault(Exception exception)
        {
            return this.CreateFault((TException)exception);
        }
    }

    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false)]
    public class RpcExceptionConverterAttribute : Attribute
    {
        public RpcExceptionConverterAttribute(Type converterType, Type exceptionType)
        {
            this.ConverterType = converterType;
            this.ExceptionType = exceptionType;
        }

        public Type ConverterType { get; }

        public Type ExceptionType { get; }
    }

    /// <summary>
    /// <para>
    /// Base implementation of <see cref="IRpcServerExceptionConverter"/> and  <see cref="IRpcClientExceptionConverter"/> for
    /// exception converters that do include additional fault details.
    /// </para>
    /// <para>Derived classes must implement the abstract methods <see cref="CreateException(string, TFault)"/> and <see cref="CreateFault(TException)"/>.
    /// </para>
    /// </summary>
    /// <typeparam name="TException">Type of the exception that this converter handles.</typeparam>
    /// <typeparam name="TFault">Type of the fault details that this converter handles.</typeparam>
    public abstract class RpcExceptionConverterBase<TException> : IRpcServerExceptionConverter, IRpcClientExceptionConverter
        where TException : Exception
    {
        protected RpcExceptionConverterBase(string faultCode)
        {
            if (string.IsNullOrWhiteSpace(faultCode))
            {
                throw new ArgumentException("RpcExceptionConverter must have a fault code.", nameof(faultCode));
            }

            this.FaultCode = faultCode;
        }

        public Type ExceptionType => typeof(TException);

        public string FaultCode { get; }

        public Type? FaultDetailsType => null;

        public abstract TException CreateException(string message);

        public abstract ConvertedFault? CreateFault(TException exception);

        Exception IRpcClientExceptionConverter.CreateException(string message, object? details)
        {
            return this.CreateException(message);
        }

        ConvertedFault? IRpcServerExceptionConverter.CreateFault(Exception exception)
        {
            return this.CreateFault((TException)exception);
        }
    }

    public class RpcFaultExceptionConverter : RpcExceptionConverterBase<RpcFaultException>
    {
        public RpcFaultExceptionConverter(string faultCode) : base(faultCode)
        {

        }

        public override RpcFaultException CreateException(string message)
        {
            return new RpcFaultException(this.FaultCode, message);
        }

        public override ConvertedFault? CreateFault(RpcFaultException exception)
        {
            if (exception is null) throw new ArgumentNullException(nameof(exception));

            if (exception.FaultCode == this.FaultCode)
            {
                return new ConvertedFault(this.FaultCode, exception.Message);
            }

            return null;
        }
    }

    public sealed class RpcFaultExceptionConverter<TFault> : RpcExceptionConverter<RpcFaultException<TFault>, TFault>
        where TFault : class
    {
        public static readonly RpcFaultExceptionConverter<TFault> Default = new RpcFaultExceptionConverter<TFault>();

        public override RpcFaultException<TFault> CreateException(string message, TFault details)
        {
            return new RpcFaultException<TFault>(message, details);
        }

        public override ConvertedFault? CreateFault(RpcFaultException<TFault> exception)
        {
            if (exception is null) throw new ArgumentNullException(nameof(exception));

            return new ConvertedFault(this.FaultCode, exception.Message, exception.Fault);
        }
    }
}
