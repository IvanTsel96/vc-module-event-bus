using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using VirtoCommerce.EventBusModule.Core.Extensions;
using VirtoCommerce.EventBusModule.Core.Models;
using VirtoCommerce.EventBusModule.Core.Services;
using VirtoCommerce.Platform.Core.Bus;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Events;
using VirtoCommerce.Platform.Core.Exceptions;

namespace VirtoCommerce.EventBusModule.Data.Services
{
    public class DefaultEventBusSubscriptionsManager : IEventBusSubscriptionsManager
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly IHandlerRegistrar _eventHandlerRegistrar;
        private readonly RegisteredEventService _registeredEventService;
        private readonly ISubscriptionSearchService _subscriptionSearchService;

        public DefaultEventBusSubscriptionsManager(IHandlerRegistrar eventHandlerRegistrar,
            RegisteredEventService registeredEventService,
            ISubscriptionService subscriptionService,
            ISubscriptionSearchService subscriptionSearchService)
        {
            _eventHandlerRegistrar = eventHandlerRegistrar;
            _registeredEventService = registeredEventService;
            _subscriptionService = subscriptionService;
            _subscriptionSearchService = subscriptionSearchService;
        }

        #region Subcription

        public virtual async Task<SubscriptionInfo> SaveSubscriptionAsync(SubscriptionRequest request)
        {
            SubscriptionInfo result = null;
            if (CheckEvents(request.EventIds))
            {
                result = request.ToModel();
                await _subscriptionService.SaveChangesAsync(new[] { result });
            }

            return result;
        }

        #endregion Subcription

        #region HandleEvent

        public virtual void RegisterEvents()
        {
            var allEvents = _registeredEventService.GetAllEvents();

            foreach (var @event in allEvents)
            {
                InvokeHandler(@event.Value, _eventHandlerRegistrar);
            }
        }


        protected virtual async Task HandleEvent(DomainEvent domainEvent, CancellationToken cancellationToken)
        {
            var eventId = domainEvent.GetType().FullName;
            var criteria = new SubscriptionSearchCriteria()
            {
                EventIds = new[] { eventId },
                Skip = 0,
                Take = int.MaxValue,
            };
            var searchResult = await _subscriptionSearchService.SearchAsync(criteria);

            if (searchResult.TotalCount > 0)
            {
                var objectEvent = domainEvent.GetEntityWithInterface<IEntity>()
                                             .Select(x => new { objectId = x.Id, objectType = x.GetType().FullName })
                                             .ToArray();

                //TODO call eventBusProvider
            }
        }

        private void InvokeHandler(Type eventType, IHandlerRegistrar registrar)
        {
            var registerExecutorMethod = registrar
                .GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(mi => mi.Name == nameof(IHandlerRegistrar.RegisterHandler))
                .Where(mi => mi.IsGenericMethod)
                .Where(mi => mi.GetGenericArguments().Length == 1)
                .Single(mi => mi.GetParameters().Length == 1)
                .MakeGenericMethod(eventType);

            Func<DomainEvent, CancellationToken, Task> del = (x, token) =>
            {
                return HandleEvent(x, token);
            };

            registerExecutorMethod.Invoke(registrar, new object[] { del });            
        }

        #endregion HandleEvent


        private bool CheckEvents(string[] eventIds)
        {
            var allEvents = _registeredEventService.GetAllEvents();
            if (eventIds.All(x => allEvents.Keys.Contains(x)))
            {
                return true;
            }
            else
            {
                var notRegisteredEvents = eventIds.Where(e => !allEvents.Keys.Any(all => all == e));
                throw new PlatformException($"The events are not registered: {string.Join(",", notRegisteredEvents)}");
            }
        }
    }
}
