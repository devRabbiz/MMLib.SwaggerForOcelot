﻿using Kros.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MMLib.SwaggerForOcelot.Configuration;
using MMLib.SwaggerForOcelot.Transformation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Extensions;

namespace MMLib.SwaggerForOcelot.Middleware
{
    /// <summary>
    /// Swagger for Ocelot middleware.
    /// This middleware generate swagger documentation from downstream services for SwaggerUI.
    /// </summary>
    public class SwaggerForOcelotMiddleware
    {
        #pragma warning disable IDE0052
        private readonly RequestDelegate _next;
        #pragma warning restore IDE0052

        private readonly IOptions<List<ReRouteOptions>> _reRoutes;
        private readonly Lazy<Dictionary<string, SwaggerEndPointOptions>> _swaggerEndPoints;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ISwaggerJsonTransformer _transformer;
        private readonly SwaggerForOcelotUIOptions _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="SwaggerForOcelotMiddleware"/> class.
        /// </summary>
        /// <param name="next">The next delegate.</param>
        /// <param name="options">The options.</param>
        /// <param name="reRoutes">The Ocelot ReRoutes configuration.</param>
        /// <param name="swaggerEndPoints">The swagger end points.</param>
        /// <param name="httpClientFactory">The HTTP client factory.</param>
        /// <param name="transformer">The SwaggerJsonTransformer</param>
        public SwaggerForOcelotMiddleware(
            RequestDelegate next,
            SwaggerForOcelotUIOptions options,
            IOptions<List<ReRouteOptions>> reRoutes,
            IOptions<List<SwaggerEndPointOptions>> swaggerEndPoints,
            IHttpClientFactory httpClientFactory,
            ISwaggerJsonTransformer transformer)
        {
            _transformer = Check.NotNull(transformer, nameof(transformer));
            _next = Check.NotNull(next, nameof(next));
            _reRoutes = Check.NotNull(reRoutes, nameof(reRoutes));
            Check.NotNull(swaggerEndPoints, nameof(swaggerEndPoints));
            _httpClientFactory = Check.NotNull(httpClientFactory, nameof(httpClientFactory));
            _options = options;

            _swaggerEndPoints = new Lazy<Dictionary<string, SwaggerEndPointOptions>>(()
                => swaggerEndPoints.Value.ToDictionary(p => $"/{p.KeyToPath}", p => p));
        }

        /// <summary>
        /// Invokes the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        public async Task Invoke(HttpContext context)
        {
            (string Url, SwaggerEndPointOptions EndPoint) = GetEndPoint(context.Request.Path);
            HttpClient httpClient = _httpClientFactory.CreateClient();
            AddHeaders(httpClient);
            string content = await httpClient.GetStringAsync(Url);
            string hostName = EndPoint.HostOverride ?? context.Request.Host.Value.RemoveSlashFromEnd();
            IEnumerable<ReRouteOptions> reRouteOptions = _reRoutes.Value
                .ExpandConfig(EndPoint)
                .GroupByPaths();

            content = _transformer.Transform(content, reRouteOptions, hostName);
            content = await ReconfigureUpstreamSwagger(context, content);

            await context.Response.WriteAsync(content);
        }

        private async Task<string> ReconfigureUpstreamSwagger(HttpContext context, string swaggerJson)
        {
            if (_options.ReConfigureUpstreamSwaggerJson != null && _options.ReConfigureUpstreamSwaggerJsonAsync != null)
            {
                throw new Exception(
                    "Both ReConfigureUpstreamSwaggerJson and ReConfigureUpstreamSwaggerJsonAsync cannot have a value. Only use one method.");
            }

            if (_options.ReConfigureUpstreamSwaggerJson != null)
            {
                return _options.ReConfigureUpstreamSwaggerJson(context, swaggerJson);
            }

            if (_options.ReConfigureUpstreamSwaggerJsonAsync != null)
            {
                return await _options.ReConfigureUpstreamSwaggerJsonAsync(context, swaggerJson);
            }

            return swaggerJson;
        }

        private void AddHeaders(HttpClient httpClient)
        {
            if (_options.DownstreamSwaggerHeaders is null)
            {
                return;
            }

            foreach (KeyValuePair<string, string> kvp in _options.DownstreamSwaggerHeaders)
            {
                httpClient.DefaultRequestHeaders.Add(kvp.Key, kvp.Value);
            }
        }

        /// <summary>
        /// Get Url and Endpoint from path
        /// </summary>
        /// <param name="path"></param>
        /// <returns>
        /// The Url of a specific version and <see cref="SwaggerEndPointOptions"/>.
        /// </returns>
        private (string Url, SwaggerEndPointOptions EndPoint) GetEndPoint(string path)
        {
            (string Version, string Key) endPointInfo = GetEndPointInfo(path);
            SwaggerEndPointOptions endPoint = _swaggerEndPoints.Value[$"/{endPointInfo.Key}"];
            string url = endPoint.Config.FirstOrDefault(x => x.Version == endPointInfo.Version)?.Url;
            return (url, endPoint);
        }

        /// <summary>
        /// Get url and version from Path
        /// </summary>
        /// <param name="path"></param>
        /// <returns>
        /// Version and the key of End point
        /// </returns>
        private (string Version, string Key) GetEndPointInfo(string path)
        {
            string[] keys = path.Split('/');
            return (keys[1], keys[2]);
        }
    }
}
