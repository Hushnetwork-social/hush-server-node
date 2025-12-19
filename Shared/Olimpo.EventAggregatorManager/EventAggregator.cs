using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Olimpo;

public class EventAggregator : IEventAggregator
{
    private readonly IDictionary<Type, object> _subscribersList = new Dictionary<Type, object>();
    private readonly ILogger<EventAggregator> _logger;

    public EventAggregator(ILogger<EventAggregator> logger)
    {
        this._logger = logger;
    }

    public void Subscribe(object subscriber)
    {
        var subscriberType = subscriber.GetType();

        // If a subscriber of this type already exists, replace it
        if (this._subscribersList.ContainsKey(subscriberType))
        {
            this._subscribersList[subscriberType] = subscriber;
        }
        else
        {
            this._subscribersList.Add(subscriberType, subscriber);
        }
    }

    public void Unsubscribe(object subscriber)
    {
        var subscriberType = subscriber.GetType();
        if (this._subscribersList.ContainsKey(subscriberType))
        {
            this._subscribersList.Remove(subscriberType);
        }
    }

    public void Unsubscribe<T>() where T : class
    {
        var subscriberType = typeof(T);
        if (this._subscribersList.ContainsKey(subscriberType))
        {
            this._subscribersList.Remove(subscriberType);
        }
    }

    public Task PublishAsync<T>(T message) where T : class
    {
        this._logger.LogInformation("Publishing message {0} | {1}", message.GetType().Name, message.ToString());
        this._logger.LogInformation("EventAggregator has {0} subscribers: {1}",
            this._subscribersList.Count,
            string.Join(", ", this._subscribersList.Keys.Select(k => k.Name)));

        foreach (var handler in this._subscribersList.Select(x => x.Value).OfType<IHandle<T>>().ToList())
        {
            handler.Handle(message);
        }

        var asyncHandlers = this._subscribersList
            .ToList()
            .Select(x => x.Value)
            .OfType<IHandleAsync<T>>()
            .ToList();

        this._logger.LogInformation("Found {0} async handlers for {1}", asyncHandlers.Count, typeof(T).Name);

        var handlers = asyncHandlers
            .Select(s => s.HandleAsync(message))
            .Where(t => t.Status != TaskStatus.RanToCompletion)
            .ToList();

        if (handlers.Any())
        {
            return Task.WhenAll(handlers);
        }

        return Task.CompletedTask;
    }
}