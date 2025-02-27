
using Microsoft.SemanticKernel;
using Xunit.Abstractions;

namespace Tests
{
    [Collection("TestCollection")]
    public class Step1
    {
        private readonly TestFixture _fixture;
        private readonly ITestOutputHelper _output;


        public Step1(TestFixture fixture,ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [Fact]
        public async Task TestConnectionAsync()
        {
            // Submit prompt to LLM
            var prompt = 
            """
            What color is the Sky?
            """;
            var response = (await this._fixture.Kernel.InvokePromptAsync(prompt)).ToString();
            _output.WriteLine(response);
        }
    }
}
