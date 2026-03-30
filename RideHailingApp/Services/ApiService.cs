using System.Net.Http;
using System.Net.Http.Json;

public class ApiService
{
    private readonly HttpClient _httpClient;

    public ApiService()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://10.0.2.2:5001/")
        };
    }
}
