
using Microsoft.SemanticKernel;
using Xunit.Abstractions;

namespace Tests
{
    [Collection("TestCollection")]
    public class Step2
    {
        private readonly TestFixture _fixture;
        private readonly ITestOutputHelper _output;


        public Step2(TestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [Fact]
        public async Task SimpleChat()
        {
            List<string> history = new List<string>();
            int maxResponse = 10;
            string contextprompt = "INSTRUCTIONS: Only respond as the next user, do not write messages as multiple users";
            history.Add(contextprompt);
            string userone = "Hi Im an AI, are you an AI?";
            while (maxResponse>0)
            {
                if(string.IsNullOrEmpty(userone)){
                    userone = (await this._fixture.Kernel.InvokePromptAsync(string.Join(System.Environment.NewLine, history))).ToString();
                }
                _output.WriteLine(userone);
                _output.WriteLine("-------------------------");
                history.Add("User1: " + userone);
                var usertwo = (await this._fixture.Kernel.InvokePromptAsync(string.Join(System.Environment.NewLine, history))).ToString();
                history.Add("User2: " + usertwo);
                _output.WriteLine(usertwo);
                _output.WriteLine("-------------------------");
                userone = "";
                maxResponse--;
            }
           
        }
    }
}
