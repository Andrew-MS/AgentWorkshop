
using System.ComponentModel;
using System.Diagnostics;
using Azure.AI.Inference;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Embeddings;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Xunit.Abstractions;

namespace Tests
{
    [Collection("TestCollection")]
    // Plugins
    public class Step5
    {
        const string ASPIRE_ENDPOINT = "http://localhost:4317";
        private readonly TestFixture _fixture;
        private readonly ITestOutputHelper _output;


        public Step5(TestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }


        [Fact]
        public async Task SimpleAgent()
        {
            _fixture.CreateKernel("SimpleAgent");
            _fixture.Rebuild();
            var agent_instructions = """
                You are a simple, helpful AI assistant designed to answer user questions and provide support. Follow these instructions:
                1. **Greet the User:** Start every conversation with a polite greeting and ask how you can help.
                2. **Understand the Query:** Read the user's question carefully. If the question is ambiguous or lacks details, ask clarifying questions.
                3. **Provide Clear Answers:** Offer concise, straightforward, and accurate responses. Use simple language and examples if necessary.
                4. **Stay On Topic:** Focus on answering the query at hand. If a question is outside your expertise, politely inform the user that you’re unable to help.
                5. **Be Friendly and Respectful:** Always maintain a courteous and supportive tone.
                6. **Verify Information:** If you’re not completely certain about an answer, mention that you’re double-checking or suggest additional resources.
                7. **End with Help:** Before ending any response, ask if the user needs further clarification or additional help.

                Your goal is to assist the user efficiently while ensuring a positive and informative interaction.
            """;
            ChatCompletionAgent agent = new()
            {
                Name = "SimpleAssistant",
                Instructions = agent_instructions,
                Kernel = _fixture.Kernel
            };

            ChatHistory history = [];
            var userQuery = "Whats the best way to learn something new?";
            ChatMessageContent message = new(AuthorRole.User, userQuery);
            history.Add(message);

            await foreach (ChatMessageContent response in agent.InvokeAsync(history).ConfigureAwait(true))
            {
                history.Add(response);
                _output.WriteLine("{0}:{1}", response.AuthorName, response.ToString());
            }
        }

        [Fact]
        public async Task SimpleAgentPlugin()
        {
            _fixture.CreateKernel("PlanningAgent");
            _fixture.Rebuild();
            var agent_instructions = """
                # Weekly Activity Planner Agent – Instructions Prompt

                ## Objective:
                Develop a weekly schedule of activities that maximizes user enjoyment while adapting to the forecasted weather conditions. The plan should seamlessly balance outdoor and indoor events based on reliable weather data.

                ## Instructions:

                1. **Data Collection:**  
                - Assume all users interests and preferences 
                - Retrieve the latest local weather forecast for each day of the upcoming week from a trusted weather source.

                2. **Weather-Based Activity Selection:**  
                - **Favorable Weather (Clear/Sunny/Mild):**  
                    - Prioritize outdoor activities such as park visits, hikes, picnics, or outdoor sports.  
                - **Unfavorable Weather (Rain, Snow, Extreme Heat/Cold):**  
                    - Focus on indoor alternatives like museums, cafes, fitness centers, or indoor cultural events.  
                - **Mixed or Uncertain Forecasts:**  
                    - Plan a combination of indoor and outdoor activities, incorporating contingency options for potential weather changes.

                3. **Daily Planning Process:**  
                - **Step 1:** For each day, analyze the specific weather forecast details (temperature, precipitation, wind conditions, etc.).  
                - **Step 2:** Match the day’s weather profile with suitable activities.  
                - **Step 3:** Schedule the primary activity when weather conditions are most favorable.  
                - **Step 4:** Include backup plans for outdoor activities that might need to shift indoors if conditions change unexpectedly.  
                - **Step 5:** Allocate transition periods between activities to account for possible delays or shifts due to weather fluctuations.

                4. **Output Format:**
                - Add a heading That lists days intil the plan
                - Under the heading add the daily forecast until the plan  
                - Present a clear, day-by-day schedule that lists:
                    - The Date in MM-DD format
                    - The weather for that day
                    - The planned activities.
                    - A brief rationale for each choice, citing weather considerations.
                    - Contingency plans or alternatives for days with unpredictable weather.

                5. **Flexibility and Updates:**  
                - Monitor real-time weather updates throughout the week and be prepared to adjust the schedule promptly.  
                - Inform the user of any significant changes or alerts due to severe weather conditions.

                6. **User Communication:**  
                - Clearly explain how weather conditions influenced each scheduling decision.  
                - Offer users the option to customize or adjust the balance of indoor vs. outdoor activities based on their preferences.

                By following these instructions, you will create a comprehensive and flexible weekly activity plan that ensures user satisfaction by always considering the prevailing weather conditions.
            """;

            ChatCompletionAgent agent = new()
            {
                Name = "PlanningAgent",
                Instructions = agent_instructions,
                Kernel = _fixture.Kernel,
                Arguments = new KernelArguments(new PromptExecutionSettings() { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() })
            };

            agent.Kernel.Plugins.Add(KernelPluginFactory.CreateFromType<Plugins.DateTimePlugin>());
            agent.Kernel.Plugins.Add(KernelPluginFactory.CreateFromType<Plugins.Weather>());

            ChatHistory history = [];
            var userQuery = """
            What should I do this weekend?
            """;
            ChatMessageContent message = new(AuthorRole.User, userQuery);
            history.Add(message);

            await foreach (ChatMessageContent response in agent.InvokeAsync(history).ConfigureAwait(true))
            {
                history.Add(response);
                _output.WriteLine("{0}:{1}", response.AuthorName, response.ToString());
            }
        }

        [Fact]
        // Works for when we have a defined plan
        public async Task MultiAgentChat()
        {
            _fixture.CreateKernel("MultiAgent");
            _fixture.Rebuild();

            var PMAgentInstruction = """
            You are a Product Manager responsible for defining software features. Your tasks:
            - Understand user needs and business goals.
            - Write a clear feature description.
            - Define success metrics.
            - Ensure feasibility for design and development with designer and Dev agents and make sure they each contribute
            - You have final approver authority, once everyone contributes create a consolidated plan and say approve

            When given a feature request, respond with:
            1. A short description of the feature.
            2. The key user problem it solves.
            3. Business impact.
            4. Success metrics.
            5. Any additional considerations for design and development.
            """;

            var DesignerAgentInstruction = """
            You are a UX/UI Designer responsible for creating user-friendly interfaces. Your tasks:
            - Understand the feature requirements from the PM.
            - Propose wireframe ideas in text form.
            - Describe key UI elements and interactions.
            - Ensure accessibility and usability.

            When given a feature request, respond with:
            1. A brief explanation of the design approach.
            2. Key UI elements and layout considerations.
            3. Interaction details (e.g., toggles, transitions).
            4. Accessibility concerns.
            """;

            var DeveloperAgentInstruction = """
            You are a Software Developer responsible for implementing features. Your tasks:
            - Understand the feature and design specifications.
            - Assess technical feasibility.
            - Outline the best implementation approach.
            - Identify potential challenges.

            When given a feature request, respond with:
            1. The recommended tech stack.
            2. A high-level implementation plan.
            3. Any potential technical issues.
            4. Estimated time for development.
            """;

            ChatCompletionAgent pmagent = new()
            {
                Name = "PMAgent",
                Instructions = PMAgentInstruction,
                Kernel = _fixture.Kernel
            };

            ChatCompletionAgent designagent = new()
            {
                Name = "DesignAgent",
                Instructions = DesignerAgentInstruction,
                Kernel = _fixture.Kernel
            };

            ChatCompletionAgent devagent = new()
            {
                Name = "DeveloperAgent",
                Instructions = DeveloperAgentInstruction,
                Kernel = _fixture.Kernel
            };

            AgentGroupChat chat =
            new(pmagent, devagent, designagent)
            {
                ExecutionSettings =
                    new()
                    {
                        // Here a TerminationStrategy subclass is used that will terminate when
                        // an assistant message contains the term "approve".
                        TerminationStrategy =
                            new ApprovalTerminationStrategy()
                            {
                                // Only the pm agent can approve
                                Agents = [pmagent],
                                // Limit total number of turns
                                MaximumIterations = 10,
                            }
                    }
            };

            var userRequest =
            """
            Feature Request: "We need to add a Dark Mode feature to our mobile app."

            Agents, work together to refine the idea.
            """;
            ChatMessageContent input = new ChatMessageContent(AuthorRole.User, userRequest);
            chat.AddChatMessage(input);
            await foreach (ChatMessageContent response in chat.InvokeAsync())
            {
                _output.WriteLine("####################################################");
                _output.WriteLine("AUTHOR: {0}", response.AuthorName);
                _output.WriteLine("####################################################");
                _output.WriteLine(response.ToString());
                _output.WriteLine("####################################################");
            }

            _output.WriteLine("COMPLETE in {0} turns");
            Console.WriteLine($"\n[IS COMPLETED: {chat.IsComplete}]");
        }


        [Fact]
        // Works for when we have a defined plan
        public async Task MultiAgentChatWithPlanner()
        {
            _fixture.CreateKernel("MultiAgent");
            _fixture.Rebuild();

            var PMAgentInstruction = """
            You are a Product Manager responsible for defining software features. Your tasks:
            - Understand user needs and business goals.
            - Write a clear feature description.
            - Define success metrics.
            - Ensure feasibility for design and development with designer and Dev agents and make sure they each contribute
            - You have final approver authority, once everyone contributes create a consolidated plan and say approve

            When given a feature request, respond with:
            1. A short description of the feature.
            2. The key user problem it solves.
            3. Business impact.
            4. Success metrics.
            5. Any additional considerations for design and development.
            """;

            var DesignerAgentInstruction = """
            You are a UX/UI Designer responsible for creating user-friendly interfaces. Your tasks:
            - Understand the feature requirements from the PM.
            - Propose wireframe ideas in text form.
            - Describe key UI elements and interactions.
            - Ensure accessibility and usability.

            When given a feature request, respond with:
            1. A brief explanation of the design approach.
            2. Key UI elements and layout considerations.
            3. Interaction details (e.g., toggles, transitions).
            4. Accessibility concerns.
            """;

            var DeveloperAgentInstruction = """
            You are a Software Developer responsible for implementing features. Your tasks:
            - Understand the feature and design specifications.
            - Assess technical feasibility.
            - Outline the best implementation approach.
            - Identify potential challenges.

            When given a feature request, respond with:
            1. The recommended tech stack.
            2. A high-level implementation plan.
            3. Any potential technical issues.
            4. Estimated time for development.
            """;

            ChatCompletionAgent pmagent = new()
            {
                Name = "PMAgent",
                Instructions = PMAgentInstruction,
                Kernel = _fixture.Kernel
            };

            ChatCompletionAgent designagent = new()
            {
                Name = "DesignAgent",
                Instructions = DesignerAgentInstruction,
                Kernel = _fixture.Kernel
            };

            ChatCompletionAgent devagent = new()
            {
                Name = "DeveloperAgent",
                Instructions = DeveloperAgentInstruction,
                Kernel = _fixture.Kernel
            };

            // Limit history used for selection and termination to the most recent message.
            ChatHistoryTruncationReducer strategyReducer = new(1);

            // Create a prompt function for termination this time
            KernelFunction terminationFunction =
            AgentGroupChat.CreatePromptFunctionForStrategy(
                """
                Determine if the plan is complete.  If so, respond with a single word: yes

                History:
                {{$history}}
                """,
                safeParameterNames: "history");

            // Create another prompt function to have more control over agent selections
            KernelFunction selectionFunction =
            AgentGroupChat.CreatePromptFunctionForStrategy(
                $$$"""
                Determine which participant takes the next turn in a conversation based on the the most recent participant.
                State only the name of the participant to take the next turn.
                No participant should take more than one turn in a row.
                
                Choose only from these participants:
                - PMAgent
                - DesignAgent
                - DeveloperAgent
                
                Always follow these rules when selecting the next participant:
                - After PMAgent, it is DesignAgent's turn.
                - After DesignAgent, it is DeveloperAgent's turn.
                - After DeveloperAgent, it is PMAgent's turn.

                History:
                {{$history}}
                """,
                safeParameterNames: "history");



            AgentGroupChat chat =
            new(pmagent, devagent, designagent)
            {LoggerFactory = LoggerFactory.Create(l=>l.AddProvider(new XUnitLoggerProvider(_output))),
                ExecutionSettings =
                    new()
                    {
                        // Here KernelFunctionTerminationStrategy will terminate
                        // when the PM has given their approval.
                        TerminationStrategy =
                            new KernelFunctionTerminationStrategy(terminationFunction, _fixture.Kernel)
                            {
                                // Only the art-director may approve.
                                Agents = [pmagent],
                                // Customer result parser to determine if the response is "yes"
                                ResultParser = (result) => result.GetValue<string>()?.Contains("yes", StringComparison.OrdinalIgnoreCase) ?? false,
                                // The prompt variable name for the history argument.
                                HistoryVariableName = "history",
                                // Limit total number of turns
                                MaximumIterations = 10,
                                // Save tokens by not including the entire history in the prompt
                                HistoryReducer = strategyReducer,
                            },
                        // Here a KernelFunctionSelectionStrategy selects agents based on a prompt function.
                        SelectionStrategy =
                            new KernelFunctionSelectionStrategy(selectionFunction, _fixture.Kernel)
                            {
                                // Always start with the writer agent.
                                InitialAgent = pmagent,
                                // Returns the entire result value as a string. Default to PM
                                ResultParser = (result) => result.GetValue<string>() ?? "PMAgent",
                                // The prompt variable name for the history argument.
                                HistoryVariableName = "history",
                                // Save tokens by not including the entire history in the prompt
                                HistoryReducer = strategyReducer,
                                // Only include the agent names and not the message content
                                EvaluateNameOnly = true,
                            },
                    }
            };

            var userRequest =
            """
            We need to add a Dark Mode feature to our mobile app
            """;
            ChatMessageContent input = new ChatMessageContent(AuthorRole.User, userRequest);
            chat.AddChatMessage(input);
            await foreach (ChatMessageContent response in chat.InvokeAsync())
            {
                _output.WriteLine("####################################################");
                _output.WriteLine("AUTHOR: {0}", response.AuthorName);
                _output.WriteLine("####################################################");
                _output.WriteLine(response.ToString());
                _output.WriteLine("####################################################");
            }

            _output.WriteLine("COMPLETE in {0} turns");
            Console.WriteLine($"\n[IS COMPLETED: {chat.IsComplete}]");
        }

        private sealed class ApprovalTerminationStrategy : TerminationStrategy
        {
            // Terminate when the final message contains the term "approve"
            protected override Task<bool> ShouldAgentTerminateAsync(Agent agent, IReadOnlyList<ChatMessageContent> history, CancellationToken cancellationToken)
                => Task.FromResult(history[history.Count - 1].Content?.Contains("approve", StringComparison.OrdinalIgnoreCase) ?? false);
        }

        public class XUnitLoggerProvider : ILoggerProvider
        {
            private readonly ITestOutputHelper _output;

            public XUnitLoggerProvider(ITestOutputHelper output)
            {
                _output = output;
            }

            public ILogger CreateLogger(string categoryName) =>
                new XUnitLogger(categoryName, _output);

            public void Dispose() { }
        }

        public class XUnitLogger : ILogger
        {
            private readonly string _categoryName;
            private readonly ITestOutputHelper _output;

            public XUnitLogger(string categoryName, ITestOutputHelper output)
            {
                _categoryName = categoryName;
                _output = output;
            }

            public IDisposable BeginScope<TState>(TState state) => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter)
            {
                _output.WriteLine($"{logLevel}: {_categoryName} - {formatter(state, exception)}");
            }
        }

    }
}
