# Handler pooling

A connection pool belongs to a handler, and the handler is keyed to an origin: scheme, host, and port. Two handlers pointed at the same origin still have two pools. Two origins on one handler have two pools.

## Factory rotation

`SetHandlerLifetime` tells `IHttpClientFactory` how long a handler pipeline may be handed to new `HttpClient` wrappers. The platform requires that value to be at least one second. This repo uses 120 seconds for the order API.

The sequence is:

1. `CreateClient` builds a pipeline if the current one is missing or expired, then returns a new wrapper around it.
2. Another `CreateClient` during that window returns another wrapper around the same pipeline.
3. After `HandlerLifetime`, a later `CreateClient` receives a new pipeline.
4. The earlier wrapper still sends through the pipeline it was given. Expiration does not swap that reference.
5. Disposing the wrapper releases the factory's tracking entry. It does not dispose the handler while other wrappers still reference it, and it does not dispose an unexpired handler just because one wrapper went away.

`FactoryLifetimeTests` covers steps 2 through 5 with a fake primary handler. `LoopbackPoolingTests` covers the TCP consequence: several factory wrappers, disposed between calls, still produce one accepted connection for sequential HTTP/1.1 requests.

## Typed and named registrations

`inventory-typed` and `inventory-named` are separate `AddHttpClient` registrations. They both add `InstanceIdHandler` outside a `SocketsHttpHandler`. The delegating handler's id is created when the pipeline is built, so it stays constant for the life of that pipeline and changes when the factory builds the next one.

`GET /api/demo/named/wrappers/{sku}` calls `CreateClient` twice. The response reports `wrappersAreSameInstance: false`. While the current pipeline is active, the two handler instance ids match.

## Singleton capture

Typed clients are registered as transient. `OrderService` is scoped, so a request scope gets a wrapper and then releases it.

This is the shape to avoid:

```csharp
builder.Services.AddSingleton<CapturingSingleton>();

public sealed class CapturingSingleton
{
    public CapturingSingleton(FactoryInventoryClient client) => Client = client;
    public FactoryInventoryClient Client { get; }
}
```

The singleton constructor runs once, so it holds one wrapper. `HandlerLifetime` can rotate the pipeline issued to new clients, and this singleton continues to use the original pipeline. The test `Singleton_that_captures_a_typed_client_keeps_its_handler_after_rotation` is that case. It is not registered in the running order API.

## Cookies

`CookieHandlerExample` registers a factory client whose primary handler lambda constructs a new `CookieContainer` for each pipeline. Callers that share a pipeline share that container. After rotation, a new pipeline has an empty container, so cookies collected by the previous pipeline are not sent by new wrappers.

A wrapper that still holds the old pipeline still sends the cookies stored in that pipeline's container. Rotation does not wipe a container that an existing client is using.

If cookies must survive rotation, create one `CookieContainer` outside the lambda and assign that same instance to every handler the lambda returns. The order and inventory calls in this sample set `UseCookies` to false. They do not depend on a cookie jar.

## Request headers

`InventoryHttp` puts `X-Correlation-Id` on the outgoing `HttpRequestMessage`. `DefaultRequestHeaders` is used only for the stable `User-Agent`, and only while the client is being constructed. Concurrent requests on the long-lived client and on a factory wrapper each carry one correlation id. The parallel test records those ids from a fake handler.
