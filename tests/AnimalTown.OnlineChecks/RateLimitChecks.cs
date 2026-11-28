using AnimalTown.Online;

static class RateLimitChecks
{
    internal static async Task Run(string origin)
    {
        if(!new Uri(origin).IsLoopback)throw new Exception("Request-limit qualification uses the disposable loopback backend");
        using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var handler=new HttpClientHandler{MaxConnectionsPerServer=8};
        using var api=new HttpClient(handler){BaseAddress=new Uri(origin),Timeout=TimeSpan.FromSeconds(5)};
        // More than two windows' capacity makes a boundary crossing harmless.
        // Only eight requests may be in flight; no accounts or saves change.
        var responses=await Task.WhenAll(Enumerable.Range(0,181).Select(_=>api.GetAsync("/health",stop.Token)));
        int limited=0;
        try
        {
            foreach(var response in responses)
            {
                if((int)response.StatusCode==429)
                {
                    limited++;
                    string? header=response.Headers.TryGetValues("Retry-After",out var values)?values.Single():null;
                    var retry=new OnlineHttpRetry();
                    if(!retry.TryWait(429,header,out int delay)||delay<1000||delay>60000)
                        throw new Exception("The active HTTP request limiter must return a bounded Retry-After that the console accepts");
                }
                else if(!response.IsSuccessStatusCode)throw new Exception("Unexpected health response during request-limit qualification");
            }
            if(limited==0)throw new Exception("The production per-address HTTP request limit was not enforced");
        }
        finally{foreach(var response in responses)response.Dispose();}
        Console.WriteLine("Production HTTP request limit and console Retry-After contract passed.");
    }
}
