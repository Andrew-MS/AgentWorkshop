using Xunit;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Net.Http;
                using System.Threading.Tasks;
                using System.Text.Json;

using Azure.Identity;
using OpenAI.Assistants;
using Azure.AI.Projects;
using System.ComponentModel;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;

using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.Redis;
using Microsoft.SemanticKernel.Embeddings;
using StackExchange.Redis;
using System.Linq;




using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Collections.Specialized;

[CollectionDefinition("TestCollection")]
public class TestCollection : ICollectionFixture<TestFixture>
{
}

public class TestFixture : IDisposable
{
    const string AZURE_OPENAI_ENDPOINT = "https://tdmintdeveusaoai.openai.azure.com/";
    const string MODEL_DEPLOYMENT = "gpt-4o";
    const string MODEL_EMBEDDING = "text-embedding-3-large";
    const string ASPIRE_ENDPOINT = "http://localhost:4317";
    const string REDIS_ENDPOINT = "localhost:6379";
    
    public Kernel Kernel { get => _kernel; }
    public IKernelBuilder Builder {get => _builder;}
    public ILoggerFactory LoggerFact {get => _loggerFactory;}
    private RedisVectorStore _vectorStore;

    private Kernel _kernel;
    private IKernelBuilder _builder;
    private ILoggerFactory _loggerFactory;

    private AzureOpenAITextEmbeddingGenerationService _embeddingService;
    public TestFixture()
    {
        CreateKernel("Default");
    }
    public void CreateKernel(string service){
        _builder = Kernel.CreateBuilder();
        var cred = new DefaultAzureCredential();
        var cliCred = new AzureCliCredential();
        _builder.AddAzureOpenAIChatCompletion(
            MODEL_DEPLOYMENT,
            AZURE_OPENAI_ENDPOINT
            ,
             cliCred
        );
        _builder.AddAzureOpenAITextEmbeddingGeneration(
            MODEL_EMBEDDING
        );

        AzureOpenAITextEmbeddingGenerationService embeddingSvc = new(
            MODEL_EMBEDDING,
            new(new Uri(AZURE_OPENAI_ENDPOINT), cred)
             , null, null, 1536
        );

        _embeddingService = embeddingSvc;



        var resourceBuilder = ResourceBuilder
            .CreateDefault()
            .AddService(service);

        // Enable model diagnostics with sensitive data.
        AppContext.SetSwitch("Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnosticsSensitive", true);
         AppContext.SetSwitch("Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnostics", true);

        var traceProvider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .AddSource("Microsoft.SemanticKernel*")
            .AddSource("*")
            .AddOtlpExporter(options => options.Endpoint = new Uri(ASPIRE_ENDPOINT))
            .Build();

        // var meterProvider = Sdk.CreateMeterProviderBuilder()
        //     .SetResourceBuilder(resourceBuilder)
        //     .AddMeter("Microsoft.SemanticKernel*")
        //     .AddOtlpExporter(options => options.Endpoint = new Uri(endpoint))
        //     .Build();

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            // Add OpenTelemetry as a logging provider
            builder.AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(resourceBuilder);
                options.AddOtlpExporter(options => options.Endpoint = new Uri(ASPIRE_ENDPOINT));
                // Format log messages. This is default to false.
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
                options.ParseStateValues = true;
            });
            builder.SetMinimumLevel(LogLevel.Trace);
        });

        _builder.Services.AddSingleton(_loggerFactory);

        this._kernel = _builder.Build();

    }
    public void Rebuild(){
        _kernel = _builder.Build();
    }
    
    public RedisVectorStore VectorStore(){
        if(_vectorStore == null){
            _vectorStore = new RedisVectorStore(ConnectionMultiplexer.Connect(REDIS_ENDPOINT).GetDatabase());
        }
        return _vectorStore;
    } 

    public AzureOpenAITextEmbeddingGenerationService EmbeddingService(){
        return _embeddingService;
    }

    public void Dispose()
    {
        
    }

    
}

public static class Plugins{
        public class Weather{
            const string baseurl = "https://api.open-meteo.com/v1/forecast?latitude=47.6062&longitude=-122.3321&hourly=temperature_2m,precipitation_probability,precipitation&temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch&timezone=America%2FLos_Angeles&temporal_resolution=hourly_1&forecast_days=";

            [KernelFunction("get_weather")]
            [Description("Gets the forecast for a certain amount of days in the future")]
            public async Task<WeatherData[]> GetWeatherForDay(int days = 1){
                // Call baseurl
                
                var data = new WeatherData[24];
                HttpClient client = new HttpClient();
                HttpResponseMessage response = await client.GetAsync(baseurl+$"{days}").ConfigureAwait(true);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(true);

                var weatherData = JsonSerializer.Deserialize<WeatherResponse>(responseBody);

                for(int i = 0;i<24;i++){
                    data[i].timestamp = DateTime.Parse(weatherData.hourly.time[i]);
                    data[i].temperature = weatherData.hourly.temperature_2m[i];
                    data[i].precipitation = weatherData.hourly.precipitation[i];
                    data[i].precipitation_probability = weatherData.hourly.precipitation_probability[i];
                }
                
                return data;
            }

            public class WeatherResponse
            {
                public required Hourly hourly { get; set; }

                public class HourlyUnits
                {
                    public required string time { get; set; }
                    public required string temperature_2m { get; set; }
                    public required string precipitation_probability { get; set; }
                    public required string precipitation { get; set; }
                }

                public class Hourly
                {
                    public required List<string> time { get; set; }
                    public required List<double> temperature_2m { get; set; }
                    public required List<int> precipitation_probability { get; set; }
                    public required List<double> precipitation { get; set; }
                }
            }

            public struct WeatherData{
                public DateTime timestamp;
                public double precipitation;
                public int precipitation_probability;
                public double temperature;
            }
        }

        public class DateTimePlugin
        {
            // First function from CalculationsUsingPluginAssistance
            [KernelFunction("get_datetime")]
            [Description("Retrieves the current datetime in UTC, should always be used to answer questions regarding current time.")]
            public static string GetCurrentUtcTime() => DateTime.UtcNow.ToString("R");

            // Second plugin from CalculationsUsingPluginAssistanceAutoSelection
            [KernelFunction("get_date_difference")]
            [Description("Returns the days between two dates in yyyy-MM-DD format")]
            public static string DaysBetweenDates(string start, string end){
                var days = (DateTime.Parse(end) - DateTime.Parse(start)).Days;
                return days.ToString(); 
            }
        }
    }
