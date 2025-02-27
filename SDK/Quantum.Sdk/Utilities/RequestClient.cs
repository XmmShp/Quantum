using System.Net;

namespace Quantum.Sdk.Utilities;

public class RequestOptions
{
    public List<Cookie>? Cookies { get; set; }
    public int TimeoutSeconds { get; set; } = 100;
    public bool AllowRedirects { get; set; } = false;
    public Dictionary<string, string>? Headers { get; set; }
}

public class RequestClient : HttpClient
{
    public CookieContainer CookieContainer { get; }

    private RequestClient(HttpMessageHandler handler, CookieContainer cookieContainer, RequestOptions? options = null) : base(handler)
    {
        CookieContainer = cookieContainer;
        Timeout = TimeSpan.FromSeconds(options?.TimeoutSeconds ?? 100);

        if (options?.Cookies is not null)
        {
            foreach (var cookie in options.Cookies)
            {
                cookieContainer.Add(cookie);
            }
        }

        DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.");

        // ReSharper disable once InvertIf
        if (options?.Headers is not null)
        {
            foreach (var header in options.Headers)
            {
                DefaultRequestHeaders.Add(header.Key, header.Value);
            }
        }
    }

    public static RequestClient Create(RequestOptions? options = null)
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            UseCookies = true,
            AllowAutoRedirect = options?.AllowRedirects ?? true,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        return new RequestClient(handler, cookieContainer, options);
    }
}
