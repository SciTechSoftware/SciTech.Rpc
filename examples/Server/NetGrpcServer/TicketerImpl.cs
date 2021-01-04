using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NetGrpcServer;
using System;
using System.Linq;
using Ticketer;

namespace NetGrpcServer
{
    public class TicketerImpl : ITicketerService
    {
        private readonly TicketRepository ticketRepository;

        private IHttpContextAccessor contextAccessor;

        public TicketerImpl(TicketRepository ticketRepository, IHttpContextAccessor contextAccessor)
        {
            this.ticketRepository = ticketRepository;
            this.contextAccessor = contextAccessor;
        }

        public int AvailableTickets => this.ticketRepository.GetAvailableTickets();

        /// <summary>
        /// Authorize attribute already applied on service definition.
        /// </summary>
        public event EventHandler<TicketPurchaseEventArgs> TicketsPurchased;

        [Authorize]
        public bool BuyTickets(int count)
        {
            var user = this.contextAccessor.HttpContext?.User?.Identity?.Name ?? "";
            bool success = this.ticketRepository.BuyTickets(user, count);
            if( success)
            {
                this.TicketsPurchased?.Invoke(this, new TicketPurchaseEventArgs(user, count));
            }

            return success;
        }
    }
}
