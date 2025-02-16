# Abstract

EventAggregator is part of the Olimpo developer suite that contains several tools that were created to help developers.
The event aggregator is a common pattern to decouple the several modules. Instead of having a hard relation between compoenents, with the EventAggregator patter the dependency is to a message that is exchange bettween them.


# How to use?
## The subscriber
The object that should be listening for the event should inject the *IEventAggreagator* interface and add this to the constructor

```
    eventAggregator.Subscribe(this);
```

Need also to implement the interface *IHandle<TMessage>* or *IHandleAsync<TMessage>* and that method will be called when a message is plublished.


## The publisher
To publish a message, it's necessary to inject the *IEventAggregator* interface and this is the command do publish a message:

```
    await eventAggregator.PublishAsync(new GlobalEvent());
```