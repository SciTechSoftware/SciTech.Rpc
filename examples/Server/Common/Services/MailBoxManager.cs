using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Mailer
{
    public class MailBoxManager : IMailBoxManagerService
    {
        private readonly MailQueueRepository mailQueueRepository;

        ConcurrentDictionary<string, MailBox> mailBoxes = new ConcurrentDictionary<string, MailBox>();

        public MailBoxManager(MailQueueRepository mailQueueRepository)
        {
            this.mailQueueRepository = mailQueueRepository;
        }

        public IMailboxService GetMailbox(string mailboxName)
        {
            // The returned mailbox will be auto-published and made available to the client through an RpcObjectRef.
            // Note. This will not work on a load-balanced service, since all calls on the returned
            // IMailBoxService must be made to this process. Better support for load-balanced services is 
            // being investigated.
            return this.mailBoxes.GetOrAdd(mailboxName, name => new MailBox(mailQueueRepository.GetMailQueue(mailboxName)));
        }
    }

    public class MailBox : IMailboxService, IDisposable
    {
        private MailQueue mailQueue;

        public MailBox( MailQueue mailQueue)
        {
            this.mailQueue = mailQueue;
            this.mailQueue.Changed += this.MailQueue_Changed;
        }

        private Task MailQueue_Changed((int totalCount, int forwardedCount, Reason reason) arg)
        {
            var msg = new MailboxMessage(arg.totalCount - arg.forwardedCount, arg.forwardedCount, arg.reason);
            switch ( arg.reason )
            {
                case Reason.Received:
                    this.MailReceieved?.Invoke(this, msg);
                    break;
                case Reason.Forwarded:
                    this.MailForwarded?.Invoke(this, msg);
                    break;
            }

            return Task.CompletedTask;
        }

        public event EventHandler<MailboxMessage> MailReceieved;

        public event EventHandler<MailboxMessage> MailForwarded;

        public void ForwardMail()
        {
            this.mailQueue.TryForwardMail( out var mail);
        }

        public void Dispose()
        {
            mailQueue.Changed -= this.MailQueue_Changed;
        }
    }
}
