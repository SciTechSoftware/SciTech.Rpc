using SciTech.Rpc;
using SciTech.Rpc.Authorization;
using SciTech.Rpc.Client;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Ticketer
{
    [RpcService]
    public interface ITicketerService
    {
        int AvailableTickets { get; }

        bool BuyTickets(int count);

        [RpcAuthorize]
        event EventHandler<TicketPurchaseEventArgs> TicketsPurchased;
    }
    
    [DataContract]
    public class TicketPurchaseEventArgs : EventArgs
    {
        public TicketPurchaseEventArgs() { }

        public TicketPurchaseEventArgs(string user, int ticketCount)
        {
            this.User = user;
            this.TicketCount = ticketCount;
        }

        [DataMember(Order = 1)]
        public string User { get; set; } = "";

        [DataMember(Order = 2)]
        public int TicketCount { get; set; }
    }

    [RpcService(ServerDefinitionType =typeof(ITicketerService))]
    public interface ITicketerServiceClient : ITicketerService, IRpcProxy
    {
        Task<int> GetAvailableTicketsAsync();

        Task<bool> BuyTicketsAsync(int count);
    }

}
