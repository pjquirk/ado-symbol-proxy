namespace AADSymbolProxy
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Formatters;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Net.Http.Headers;

    public static class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) => Host.CreateDefaultBuilder(args).ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<Startup>());
    }

    public sealed class Startup
    {
        public const string AdoClientId = "ado";

        public void ConfigureServices(IServiceCollection services)
        {
            var token = Environment.GetEnvironmentVariable("ADO_PAT");
            var base64PatString = $"Basic {Convert.ToBase64String(Encoding.ASCII.GetBytes($"{string.Empty}:{token}"))}";

            // WPA and other tools don't like redirects, so follow all redirects
            services.AddHttpClient(AdoClientId, configureClient =>
            {
                configureClient.BaseAddress = new Uri("https://artifacts.dev.azure.com/mseng/_apis/symbol/symsrv/");
                configureClient.DefaultRequestHeaders.Add("Authorization", base64PatString);
                configureClient.DefaultRequestHeaders.Add("SymbolChecksumValidationSupported", "1");
            }).ConfigurePrimaryHttpMessageHandler(provider => new SocketsHttpHandler { AllowAutoRedirect = true, UseCookies = false });

            services.AddControllers();
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();
            app.UseEndpoints(endpoints => endpoints.MapControllers());
        }
    }

    public class OctetStreamOutputFormatter : IOutputFormatter
    {
        private readonly string etag;

        public OctetStreamOutputFormatter(string etag)
        {
            this.etag = etag;
        }

        public bool CanWriteResult(OutputFormatterCanWriteContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return context.Object is MemoryStream;
        }

        public async Task WriteAsync(OutputFormatterWriteContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            using (var value = ((MemoryStream)context.Object))
            {
                var response = context.HttpContext.Response;

                if (context.ContentType != null)
                {
                    response.ContentType = context.ContentType.ToString();
                }
                response.ContentLength = value.Length;
                response.Headers["Accept-Ranges"] = "bytes";
                response.Headers["ETag"] = etag;
                await value.CopyToAsync(response.Body);
            }
        }
    }

    [Route("api/[controller]")]
    [ApiController]
    public sealed class SymbolsController : ControllerBase
    {
        private readonly IHttpClientFactory clientFactory;

        public SymbolsController(IHttpClientFactory clientFactory)
        {
            this.clientFactory = clientFactory;
        }

        [HttpGet]
        [Route("download/{filename}/{key}/{filename2}")]
        public async Task<IActionResult> Symbols(string filename, string key, string filename2)
        {
            var client = clientFactory.CreateClient(Startup.AdoClientId);

            using (HttpResponseMessage response = await client.GetAsync(filename + "/" + key + "/" + filename2, HttpCompletionOption.ResponseHeadersRead))
            {
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    // Just pull the PDB into memory and then return it
                    var contentLength = response.Content.Headers.ContentLength.Value;
                    var source = await response.Content.ReadAsStreamAsync();
                    var memoryStream = new MemoryStream((int)contentLength);
                    var etag = response.Headers.ETag.Tag;
                    await source.CopyToAsync(memoryStream);
                    memoryStream.Position = 0;

                    return new OkObjectResult(memoryStream)
                    {
                        ContentTypes = new MediaTypeCollection { new MediaTypeHeaderValue("application/octet-stream") },
                        Formatters = new FormatterCollection<IOutputFormatter>() {  new OctetStreamOutputFormatter(etag) }
                    };
                }
                else
                {
                    var output = await response.Content.ReadAsStringAsync();
                    Console.WriteLine(output);
                }
            }

            return new NotFoundResult();
        }
    }
}