﻿using Aguacongas.IdentityServer.Store;
using Aguacongas.IdentityServer.Store.Entity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Aguacongas.IdentityServer.Admin.Http.Store
{
    [SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters", Justification = "No globalization")]
    public class AdminStore<T> : IAdminStore<T> where T : class, IEntityId
    {
        private readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
        {
            IgnoreNullValues = true,
            IgnoreReadOnlyProperties = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,            
        };
        private readonly Task<HttpClient> _httpClientFactory;
        private readonly ILogger<AdminStore<T>> _logger;
        private readonly string _baseUri;

        [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Paths shoul be to lower")]
        public AdminStore(Task<HttpClient> httpClientFactory, ILogger<AdminStore<T>> logger)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _baseUri = $"/{typeof(T).Name}".ToLowerInvariant();
        }

        public async Task<T> CreateAsync(T entity, CancellationToken cancellationToken = default)
        {
            var httpClient = await _httpClientFactory
                .ConfigureAwait(false);
            using (var content = new StringContent(SerializeEntity(entity), Encoding.UTF8, "application/json"))
            {
                using (var response = await httpClient.PostAsync(GetUri(httpClient, _baseUri), content, cancellationToken)
                    .ConfigureAwait(false))
                {
                    await EnsureSuccess(response).ConfigureAwait(false);
                    return await DeserializeResponse(response)
                        .ConfigureAwait(false);
                }
            }
        }

        public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            var httpClient = await _httpClientFactory
                .ConfigureAwait(false);

            using (var response = await httpClient.DeleteAsync(GetUri(httpClient, $"{_baseUri}/{id}"), cancellationToken)
                .ConfigureAwait(false))
            {

                await EnsureSuccess(response)
                    .ConfigureAwait(false);
            }
        }

        public async Task<T> GetAsync(string id, GetRequest request, CancellationToken cancellationToken = default)
        {
            var httpClient = await _httpClientFactory
                .ConfigureAwait(false);

            using (var response = await httpClient.GetAsync(GetUri(httpClient, $"{_baseUri}/{id}?expand={request?.Expand}"), cancellationToken)
                .ConfigureAwait(false))
            {
                await EnsureSuccess(response)
                    .ConfigureAwait(false);

                return await DeserializeResponse(response)
                    .ConfigureAwait(false);
            } 
        }

        [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Request parameters should be to lower")]
        public async Task<PageResponse<T>> GetAsync(PageRequest request, CancellationToken cancellationToken = default)
        {
            request = request ?? new PageRequest();

            var dictionary = typeof(PageRequest)
                .GetProperties()
                .Where(p => p.GetValue(request) != null)
                .ToDictionary(p => p.Name.ToLowerInvariant(), p => p.GetValue(request).ToString());

            var httpClient = await _httpClientFactory
                .ConfigureAwait(false);

            using (var response = await httpClient.GetAsync(GetUri(httpClient, QueryHelpers.AddQueryString(_baseUri, dictionary)), cancellationToken)
                .ConfigureAwait(false))
            {

                await EnsureSuccess(response)
                    .ConfigureAwait(false);

                return await DeserializeResponse<PageResponse<T>>(response)
                    .ConfigureAwait(false);
            }
        }
        public async Task<T> UpdateAsync(T entity, CancellationToken cancellationToken = default)
        {
            entity = entity ?? throw new ArgumentNullException(nameof(entity));

            var httpClient = await _httpClientFactory
                .ConfigureAwait(false);

            using (var content = new StringContent(SerializeEntity(entity), Encoding.UTF8, "application/json"))
            {
                using (var response = await httpClient.PutAsync(GetUri(httpClient, $"{_baseUri}/{entity.Id}"), content, cancellationToken)
                    .ConfigureAwait(false))
                {

                    await EnsureSuccess(response).ConfigureAwait(false);
                    return await DeserializeResponse(response)
                        .ConfigureAwait(false);
                }
            }
        }

        public async Task<IEntityId> CreateAsync(IEntityId entity, CancellationToken cancellationToken = default)
        {
            return await CreateAsync(entity as T, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<IEntityId> UpdateAsync(IEntityId entity, CancellationToken cancellationToken = default)
        {
            return await UpdateAsync(entity as T, cancellationToken)
                .ConfigureAwait(false);
        }

        private string SerializeEntity(T entity)
        {
            return JsonSerializer.Serialize(entity, _jsonSerializerOptions);
        }

        private async Task EnsureSuccess(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync()
                    .ConfigureAwait(false);
                _logger.LogError("Error response received {@Response}", content);

                if (content != null &&
                    (response.StatusCode == HttpStatusCode.BadRequest ||
                     response.StatusCode == HttpStatusCode.Conflict))
                {
                    var details = JsonSerializer.Deserialize<ProblemDetails>(content);
                    throw new ProblemException
                    {
                        Details = details
                    };
                }
                response.EnsureSuccessStatusCode();
            }
        }

        private Task<T> DeserializeResponse(HttpResponseMessage response)
        {
            return DeserializeResponse<T>(response);
        }

        private async Task<TResponse> DeserializeResponse<TResponse>(HttpResponseMessage response)
        {
            var content = await response.Content.ReadAsStringAsync()
                .ConfigureAwait(false);
            _logger.LogDebug("Response received {@Response}", content);
            return JsonSerializer.Deserialize<TResponse>(content, _jsonSerializerOptions);
        }

        private Uri GetUri(HttpClient httpClient, string uri)
        {
            var baseAddress = httpClient.BaseAddress.ToString();
            if (baseAddress.EndsWith("/", StringComparison.Ordinal))
            {
                baseAddress = baseAddress.Substring(0, baseAddress.Length - 1);
            }
            
            var result = new Uri($"{baseAddress}{uri}");
            _logger.LogInformation($"Request {result}");
            return result;
        }
    }
}