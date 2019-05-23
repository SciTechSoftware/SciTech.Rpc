using ProtoBuf;
using SciTech.Rpc;
using SciTech.Rpc.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Mailer
{
    public enum Reason
    {
        Received,
        Forwarded
    }

    [ProtoContract(SkipConstructor =true)]
    public class MailboxMessage
    {
        public MailboxMessage(int newCount, int forwardedCount, Reason reason)
        {
            this.New = newCount;
            this.Forwarded = forwardedCount;
            this.Reason = reason;
        }

        [ProtoMember(1)]
        public int New { get; set; }

        [ProtoMember(2)]
        public int Forwarded { get; set; }

        [ProtoMember(3)]
        public Reason Reason { get; set; }
    }

    [RpcService]
    public interface IMailBoxManagerService
    {
        IMailboxService GetMailbox(string mailboxName);
    }

    [RpcService(ServerDefinitionType = typeof(IMailBoxManagerService))]
    public interface IMailBoxManagerServiceClient : IMailBoxManagerService, IRpcService
    {
        Task<IMailboxServiceClient> GetMailboxAsync(string mailboxName);
    }

    [RpcService]

    public interface IMailboxService
    {
        void ForwardMail();

        event EventHandler<MailboxMessage> MailReceieved;

        event EventHandler<MailboxMessage> MailForwarded;
    }

    [RpcService(ServerDefinitionType =typeof(IMailboxService))]
    public interface IMailboxServiceClient : IMailboxService, IRpcService, IDisposable
    {
        Task ForwardMailAsync();

    }
}
