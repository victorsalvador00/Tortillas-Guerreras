using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TG.Api.IntegrationTests.Infraestructura;

public static class ExtensionesHttp
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<T> LeerAsync<T>(this HttpResponseMessage respuesta)
    {
        var texto = await respuesta.Content.ReadAsStringAsync();
        if (!respuesta.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {(int)respuesta.StatusCode}: {texto}");
        }

        return JsonSerializer.Deserialize<T>(texto, Json)!;
    }

    public static async Task<T> PostOkAsync<T>(this HttpClient cliente, string url, object? cuerpo = null)
    {
        var respuesta = await cliente.PostAsJsonAsync(url, cuerpo ?? new { }, Json);
        return await respuesta.LeerAsync<T>();
    }

    public static async Task<T> PutOkAsync<T>(this HttpClient cliente, string url, object cuerpo)
    {
        var respuesta = await cliente.PutAsJsonAsync(url, cuerpo, Json);
        return await respuesta.LeerAsync<T>();
    }

    public static async Task<T> GetOkAsync<T>(this HttpClient cliente, string url)
    {
        var respuesta = await cliente.GetAsync(url);
        return await respuesta.LeerAsync<T>();
    }

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient cliente, string url, object? cuerpo = null) =>
        cliente.PostAsJsonAsync(url, cuerpo ?? new { }, Json);

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient cliente, string url, object cuerpo) =>
        cliente.PutAsJsonAsync(url, cuerpo, Json);

    /// <summary>Verifica estado HTTP y código de negocio del <c>ProblemDetails</c>.</summary>
    public static async Task<JsonObject> DebeFallarAsync(this HttpResponseMessage respuesta, HttpStatusCode estado,
        string? codigo = null)
    {
        var texto = await respuesta.Content.ReadAsStringAsync();
        ((int)respuesta.StatusCode).ShouldBe((int)estado, texto);
        var json = string.IsNullOrEmpty(texto) ? new JsonObject() : JsonNode.Parse(texto)!.AsObject();
        if (codigo is not null)
        {
            json["codigo"]?.GetValue<string>().ShouldBe(codigo, texto);
        }

        return json;
    }
}
