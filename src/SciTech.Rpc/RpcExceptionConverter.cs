using SciTech.Rpc.Client;
using SciTech.Rpc.Internal;
using SciTech.Rpc.Server;
using System;
using System.Reflection;

namespace SciTech.Rpc
{
    /// <summary>
    /// Base interface for exceptions converters that implement both <see cref="IRpcServerExceptionConverter"/> and <see cref="IRpcClientExceptionConverter"/>
    /// <para>This interrface includes no additional functionality, but can make it more convenient to handle exceptions converters that should
    /// be added to both <see cref="RpcClientOptions"/> and <see cref="RpcServerOptions"/>.</para>
    /// </summary>
    public interface IRpcExceptionConverter : IRpcServerExceptionConverter, IRpcClientExceptionConverter
    {
    }

    /// <summary>
    /// <para>
    /// Default implementation of <see cref="IRpcServerExceptionConverter"/> and  <see cref="IRpcClientExceptionConverter"/>
    /// which converts exceptions based on the <typeparamref name="TException"/> type argument, the specified faultCode,
    /// and the message provided to <see cref="CreateException(string)"/>.
    /// </para>
    /// <para>
    /// <b>NOTE! </b>The <see cref="IRpcServerExceptionConverter.TryCreateFault(Exception)"/> implementation will use
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
        /// <param name="faultCode">FaultCode that is associated with the converted fault.</param>
        /// <param name="includeSubTypes">Indicates whether sub types of <typeparamref name="TException"/> should be converted.
        /// <c>true</c> by default.</param>
        public RpcExceptionConverter(string faultCode, bool includeSubTypes = true) : base(faultCode, includeSubTypes)
        {
            this.exceptionCtor = typeof(TException).GetConstructor(new Type[] { typeof(string) })
                ?? throw new InvalidOperationException("Exception type must have a constructor accepting a message argument.");
        }

        public override TException CreateException(string message)
        {
            return (TException)this.exceptionCtor.Invoke(new object[] { message });
        }

        public override RpcFaultException CreateFault(TException exception)
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
        /// <summary>
        /// Initializes a <see cref="RpcExceptionConverter{TException,TFault}"/> associated a fault code retrieved from the <typeparamref name="TException"/>
        /// type parameter.
        /// </summary>
        /// <param name="includeSubTypes">Indicates whether sub types of <typeparamref name="TException"/> should be converted.
        /// <c>true</c> by default.</param>
        protected RpcExceptionConverter(bool includeSubTypes) : this(null, includeSubTypes)
        {
        }

        /// <summary>
        /// Initializes a <see cref="RpcExceptionConverter{TException,TFault}"/> associated with the specified <paramref name="faultCode"/>.
        /// </summary>
        /// <param name="faultCode">FaultCode that is associated with the converted fault.</param>
        /// <param name="includeSubTypes">Indicates whether sub types of <typeparamref name="TException"/> should be converted.
        /// <c>true</c> by default.</param>
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

        public abstract RpcFaultException CreateFault(TException exception);

        public RpcFaultException? TryCreateFault(Exception exception)
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

        public Exception? TryCreateException(RpcFaultException faultException)
        {
            if (faultException is RpcFaultException<TFault> typedException)
            {
                return this.CreateException(typedException.Message, typedException.Fault);
            }

            return null;
        }
    }

    /// <summary>
    /// <para>
    /// Base implementation of <see cref="IRpcServerExceptionConverter"/> and  <see cref="IRpcClientExceptionConverter"/> for
    /// exception converters that do not include additional fault details.
    /// </para>
    /// <para>Derived classes must implement the abstract methods <see cref="CreateException(string)"/> and <see cref="CreateFault(TException)"/>.
    /// </para>
    /// </summary>
    /// <typeparam name="TException">Type of the exception that this converter handles.</typeparam>
    public abstract class RpcExceptionConverterBase<TException> : IRpcExceptionConverter
        where TException : Exception
    {
        /// <param name="faultCode">FaultCode that is associated with the converted fault.</param>
        /// <param name="includeSubTypes">Indicates whether sub types of <typeparamref name="TException"/> should be converted.
        /// <c>true</c> by default.</param>
        protected RpcExceptionConverterBase(string faultCode, bool includeSubTypes = true)
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

        public abstract RpcFaultException CreateFault(TException exception);

        public RpcFaultException? TryCreateFault(Exception exception)
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

        public Exception? TryCreateException(RpcFaultException faultException)
        {
            if (faultException is null) throw new ArgumentNullException(nameof(faultException));

            if (faultException.FaultCode == this.FaultCode)
            {
                return this.CreateException(faultException.Message);
            }

            return null;
        }
    }
}