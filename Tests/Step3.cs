
using System.ComponentModel;
using Microsoft.SemanticKernel;
using Xunit.Abstractions;

namespace Tests
{
    [Collection("TestCollection")]
    // Plugins
    public class Step3
    {
        private readonly TestFixture _fixture;
        private readonly ITestOutputHelper _output;


        public Step3(TestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [Fact]
        public async Task LLMOnlyCalculations()
        {

            var prompt =
            """
            How many days until christmas?
            """;
            var response = (await _fixture.Kernel.InvokePromptAsync(prompt)).ToString();
            _output.WriteLine(response);

        }

        [Fact]
        public async Task CalculationsUsingPluginAssistance()
        {
            _fixture.Builder.Plugins.AddFromType<DateTimePlugin>();
            _fixture.Rebuild();

            var prompt =
            """
            How many days until christmas? Current time is {{DateTimePlugin.get_datetime}}
            """;
            var response = (await _fixture.Kernel.InvokePromptAsync(prompt)).ToString();
            _output.WriteLine(response);
        }

        [Fact]
        // Plugins are selected via completions without planning
        public async Task CalculationsUsingPluginAssistanceAutoSelection()
        {
            _fixture.CreateKernel("CalculationsUsingPluginAssistanceAutoSelection");
            _fixture.Builder.Plugins.AddFromType<DateTimePlugin>();
            _fixture.Rebuild();

            PromptExecutionSettings settings = new() { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() };

            var prompt =
            """
            How many days until christmas?
            """;
            var response = (await _fixture.Kernel.InvokePromptAsync(prompt, new(settings))).ToString();
            _output.WriteLine(response);
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
            public static int DaysBetweenDates(string start, string end) => (DateTime.Parse(start) - DateTime.Parse(end)).Days;
        }

    }
}
