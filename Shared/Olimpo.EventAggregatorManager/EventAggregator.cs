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

        this._logger.LogInformation("EventAggregator.Subscribe: {SubscriberType}, HashCode={HashCode}",
            subscriberType.Name, subscriber.GetHashCode());

        // If a subscriber of this type already exists, replace it
        if (this._subscribersList.ContainsKey(subscriberType))
        {
            this._logger.LogWarning("Replacing existing subscriber: {SubscriberType}", subscriberType.Name);
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
        this._logger.LogDebug("Publishing message {MessageType} | {Message}", message.GetType().Name, message.ToString());
        this._logger.LogDebug("EventAggregator has {SubscriberCount} subscribers: {Subscribers}",
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

        this._logger.LogDebug("Found {HandlerCount} async handlers for {MessageType}", asyncHandlers.Count, typeof(T).Name);

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