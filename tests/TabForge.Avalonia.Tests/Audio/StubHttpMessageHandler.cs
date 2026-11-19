using System;
using System.Net.Http;
using System.Threading;

namespace TabForge.Avalonia.Tests;

internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) => answer(request, cancel);
}
