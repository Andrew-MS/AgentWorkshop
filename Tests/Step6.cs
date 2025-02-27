
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Azure.AI.Inference;
using Microsoft.Extensions.DependencyInjection;
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
    public class Step6
    {
        const string ASPIRE_ENDPOINT = "http://localhost:4317";
        private readonly TestFixture _fixture;
        private readonly ITestOutputHelper _output;


        public Step6(TestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }


        [Fact]
        // Works for when we have a defined plan
        public async Task ChatWithSemanticOrchestration()
        {
            _fixture.CreateKernel("ChatWithSemanticOrchestration");
            _fixture.Builder.Plugins.AddFromType<Plugins.Weather>();
            _fixture.Builder.Plugins.AddFromType<Plugins.DateTimePlugin>();


            // Create and add logger, which will output messages to test detail summary window.
            var lf = LoggerFactory.Create(l =>
            {
                l.AddProvider(new XUnitLoggerProvider(_output));
                l.SetMinimumLevel(LogLevel.Trace);
            });
            var logger = lf.CreateLogger<Step6>();

            _fixture.Builder.Services.AddSingleton<ILogger>(logger);

            // Add filters with logging.
            _fixture.Builder.Services.AddSingleton<IFunctionInvocationFilter, FunctionInvocationLoggingFilter>();
            _fixture.Builder.Services.AddSingleton<IPromptRenderFilter, PromptRenderLoggingFilter>();
            _fixture.Builder.Services.AddSingleton<IAutoFunctionInvocationFilter, AutoFunctionInvocationLoggingFilter>();

            _fixture.Rebuild();

            KernelFunction getWeather = _fixture.Kernel.Plugins.GetFunction("Weather", "get_weather");
            KernelFunction getCurrentTime = _fixture.Kernel.Plugins.GetFunction("DateTimePlugin", "get_datetime");
            KernelFunction getDateDifference = _fixture.Kernel.Plugins.GetFunction("DateTimePlugin", "get_date_difference");

            var EventIdeasAgentName = "EventIdeasAgent";
            var WeatherAgentName = "WeatherAgent";
            var SchedulingAgentName = "SchedulingAgent";
            var ResponderAgentName = "ResponderAgent";
            var PlannerAgentName = "PlannerAgent";


            var EventIdeasAgentDescription = "The agent that generates event ideas";
            var WeatherAgentDescription = "The agent that provides weather information and forecasts";
            var SchedulingAgentDescription = "The agent that takes ideas and weather information and creates a schedule";
            var ResponderAgentDescription = "The agent that summarizes a final response to the user's original inquiry by collecting responses from the OTHER agents";
            var PlannerAgentDescription = "The agent that creates a plan for the users inquiry by integrating the responses from the other agents";
            // Agent instructions

            var EventsIdeasAgentInstruction = """
            You are the Events Ideas Agent. Your role is to generate creative, engaging, and practical event ideas tailored to the user's needs. When a user requests event ideas, you should:
            - Ask clarifying questions if details such as location, audience, budget, or theme are not provided.
            - Consider context (e.g., indoor/outdoor settings, seasonal aspects, target demographics) when proposing ideas.
            - Provide a diverse range of event concepts, explaining why each might be successful.
            - Offer additional tips on planning and executing the event if requested.
            Your responses should be imaginative yet realistic, ensuring that each idea is actionable.
            """;

            var WeatherAgentInstruction = """
            You are the Weather Agent. Your responsibility is to provide accurate, up-to-date weather information and forecasts. When a user inquires about the weather, you should:
            - Confirm the location and time period if not specified.
            - Present the current weather conditions, including temperature, precipitation, wind speed, and any relevant alerts.
            - Offer a forecast for the upcoming hours or days, as applicable.
            - If necessary, ask for additional details to ensure the accuracy of the information.
            Your answers should be concise, clear, and helpful for planning purposes.
            """;

            var SchedulingAgentInstruction = """
            You are the Scheduling Agent. Your task is to help users organize their calendars and manage appointments efficiently, integrating inputs from both the Weather Agent and the Events Ideas Agent. When a scheduling request is made, you should:
            - Gather all relevant details, such as dates, times, event types
            - If the user has left out any details decide for them and work with the weather and event ideas agent. 
            - Collaborate with the Weather Agent to incorporate current and forecasted weather conditions.
            - Consult the Events Ideas Agent to identify suitable event types based on the user's context.
            - Identify and resolve potential scheduling conflicts.
            - Propose an optimized schedule that balances time management, weather considerations, and the appropriateness of event types.
            Your responses should be structured, detailed, and aimed at creating a feasible schedule that considers both environmental factors and event requirements.
            """;

            var ResponderAgentInstruction = """
            You are a Summariser Agent. You receive information from multiple OTHER agents and need to compile a final response to the user.\n

            Provide a coherent and helpful response to the user by combining the information from the agents. \n
            - You **MUST** use only information provided in the chat history. Do NOT use any external sources or functions or your own knowledge \n
            - You **MUST** assume that the information provided by other agents is not visible to the end user. \n
            - You **MUST** formulate the full answer based on the information from other agents. \n
            - You **MUST** check the original USER question, scan the chat history, and summarize the coherent response \n
            
            The response MUST NOT be the original question, or generic statement. 
            
            It **MUST** be specific to the user's inquiry and provide a clear and concise answer.

            FINAL RESPONSE:
            """;

            var PlannerAgentInstruction = """
            You are the Planner Agent. Your role is to coordinate and delegate tasks to the appropriate agents.
            DO NOT generate your own detailed responses or directly call functions.
            Instead, review the information provided by other agents (such as weather updates, event ideas, and scheduling details) and integrate these into a final plan.
            If any required information is missing, instruct or delegate to the appropriate agent to obtain it.
            """;

            ChatCompletionAgent plannerAgent = new()
            {
                Name = "PlannerAgent",
                Instructions = PlannerAgentInstruction,
                Description = "Creates a plan for the other agents to follow to gather all information needed for responder agent",
                Kernel = _fixture.Kernel
            };

            ChatCompletionAgent eventsIdeasAgent = new()
            {
                Name = "EventIdeasAgent",
                Instructions = EventsIdeasAgentInstruction,
                Description = EventIdeasAgentDescription,
                Kernel = _fixture.Kernel
            };

            ChatCompletionAgent weatherAgent = new()
            {
                Name = "WeatherAgent",
                Instructions = WeatherAgentInstruction,
                Description = WeatherAgentDescription,
                Kernel = _fixture.Kernel,
                Arguments = new KernelArguments(new PromptExecutionSettings()
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(
                        functions: [getWeather, getCurrentTime, getDateDifference]
                    )
                })

            };
            // weatherAgent.Kernel.Plugins.AddFromType<Plugins.Weather>();
            ChatCompletionAgent schedulingAgent = new()
            {
                Name = "SchedulingAgent",
                Instructions = SchedulingAgentInstruction,
                Description = SchedulingAgentDescription,
                Kernel = _fixture.Kernel,
                Arguments = new KernelArguments(new PromptExecutionSettings()
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(
                        functions: [getCurrentTime, getDateDifference]
                    )
                })
            };

            ChatCompletionAgent responderAgent = new()
            {
                Name = "ResponderAgent",
                Instructions = ResponderAgentInstruction,
                Description = ResponderAgentDescription,
                Kernel = _fixture.Kernel
            };

            // Create list to easily iterate over agents
            List<Tuple<string, string, ChatCompletionAgent>> agents = new()
            {
                new(EventIdeasAgentName, EventIdeasAgentDescription, eventsIdeasAgent),
                new(WeatherAgentName, WeatherAgentDescription, weatherAgent),
                new(SchedulingAgentName, SchedulingAgentDescription, schedulingAgent),
                new(ResponderAgentName, ResponderAgentDescription, responderAgent),
                new(PlannerAgentName, PlannerAgentDescription, plannerAgent)
            };
            string agentListString = string.Join(Environment.NewLine, agents.Select(a => a.Item1).ToList());

            // Limit history used for selection and termination to the most recent message.
            ChatHistoryTruncationReducer strategyReducer = new(1);

            // Create a prompt function for termination this time
           KernelFunction terminationFunction = AgentGroupChat.CreatePromptFunctionForStrategy(
            """
            Determine if the plan is complete and the user's inquiry is fully addressed.
            Only respond with a single word: yes if and only if:
            - The WeatherAgent, EventIdeasAgent, and SchedulingAgent have all provided their contributions,
            - AND the final response has been synthesized by the ResponderAgent.
            Otherwise, respond with any other text.
            
            History:
            {{$history}}
            """,
            safeParameterNames: "history");

            // Create another prompt function to have more control over agent selections
            var selectionFunction = AgentGroupChat.CreatePromptFunctionForStrategy(
            $$$"""
            You are the next speaker selector.
            
            - You MUST return ONLY the agent name from the list of available agents below.
            - Do NOT select the PlannerAgent until you have confirmed that the WeatherAgent, EventIdeasAgent, and SchedulingAgent have provided their contributions.
            - Return the name of the agent who has not yet contributed sufficiently to the plan.
            
            # AVAILABLE AGENTS

            EventIdeasAgent: Generates event ideas.
            WeatherAgent: Provides weather updates.
            SchedulingAgent: Organizes scheduling details.
            PlannerAgent: Coordinates and integrates inputs (do not select until other agents have contributed).
            ResponderAgent: Summarizes the final plan.
            
            # CHAT HISTORY

            {{$history}}
            """,
            safeParameterNames: ["history", "agentnames", "agents"]);
            _fixture.Kernel.PromptRenderFilters.Add(new PromptFilter(_output));
            AgentGroupChat chat =
            new([responderAgent, schedulingAgent, weatherAgent, eventsIdeasAgent])
            {
                LoggerFactory = LoggerFactory.Create(l => l.AddProvider(new XUnitLoggerProvider(_output))),
                ExecutionSettings =
                    new()
                    {
                        // Here KernelFunctionTerminationStrategy will terminate
                        // when the PM has given their approval.
                        TerminationStrategy =
                            new KernelFunctionTerminationStrategy(terminationFunction, _fixture.Kernel)
                            {
                                // Only the responder may approve.
                                Agents = [plannerAgent],
                                MaximumIterations = 10,
                                HistoryVariableName= "history"
                            }
                        ,
                        // Here a KernelFunctionSelectionStrategy selects agents based on a prompt function.
                        SelectionStrategy =
                            new KernelFunctionSelectionStrategy(selectionFunction, _fixture.Kernel)
                            {
                                // // Always start with the writer agent.
                                InitialAgent = plannerAgent,
                                // Returns the entire result value as a string. Default to responder
                                ResultParser = (result) => result.GetValue<string>() ?? responderAgent.Name,
                                // The prompt variable name for the history argument.
                                HistoryVariableName = "history",
                                // Save tokens by not including the entire history in the prompt
                                // HistoryReducer = strategyReducer,
                                // Only include the agent names and not the message content
                                EvaluateNameOnly = false,
                                AgentsVariableName = "agents"
                            }

                    }
            };



            var userRequest =
           """
            Make me a plan for the weekend come up with event ideas, its completely up to you dont ask me any followup questions.
            """;


            ChatMessageContent input = new ChatMessageContent(AuthorRole.User, userRequest);
            chat.AddChatMessage(input);
            await foreach (ChatMessageContent response in chat.InvokeAsync().ConfigureAwait(true))
            {
                _output.WriteLine("####################################################");
                _output.WriteLine("AUTHOR: {0}", response.AuthorName);
                _output.WriteLine("####################################################");
                _output.WriteLine(response.ToString());

            }

            _output.WriteLine("COMPLETE in {0} turns", chat.GetChatMessagesAsync().CountAsync().Result);
            Console.WriteLine($"\n[IS COMPLETED: {chat.IsComplete}]");
        }

        private sealed class ApprovalTerminationStrategy : TerminationStrategy
        {
            // Terminate when the final message contains the term "approve"
            protected override Task<bool> ShouldAgentTerminateAsync(Agent agent, IReadOnlyList<ChatMessageContent> history, CancellationToken cancellationToken)
                => Task.FromResult(history[history.Count - 1].AuthorName == "ResponderAgent");
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


        /// <summary>
        /// Filter which logs an information available during function invocation such as:
        /// Function name, arguments, execution settings, result, duration, token usage.
        /// </summary>
        private sealed class FunctionInvocationLoggingFilter(ILogger logger) : IFunctionInvocationFilter
        {
            public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
            {
                long startingTimestamp = Stopwatch.GetTimestamp();

                logger.LogInformation("Function {FunctionName} invoking.", context.Function.Name);


                if (context.Arguments.Count > 0)
                {
                    logger.LogTrace("Function arguments: {Arguments}", JsonSerializer.Serialize(context.Arguments));
                }

                if (logger.IsEnabled(LogLevel.Information) && context.Arguments.ExecutionSettings is not null)
                {
                    logger.LogInformation("Execution settings: {Settings}", JsonSerializer.Serialize(context.Arguments.ExecutionSettings));
                }

                try
                {
                    await next(context).ConfigureAwait(true);

                    logger.LogInformation("Function {FunctionName} succeeded.", context.Function.Name);

                    if (context.IsStreaming)
                    {
                        // Overriding the result in a streaming scenario enables the filter to stream chunks 
                        // back to the operation's origin without interrupting the data flow.
                        var enumerable = context.Result.GetValue<IAsyncEnumerable<StreamingChatMessageContent>>();
                        context.Result = new FunctionResult(context.Result, ProcessFunctionResultStreamingAsync(enumerable!));
                    }
                    else
                    {
                        ProcessFunctionResult(context.Result);
                    }
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Function failed. Error: {Message}", exception.Message);
                    throw;
                }
                finally
                {
                    if (logger.IsEnabled(LogLevel.Information))
                    {
                        TimeSpan duration = new((long)((Stopwatch.GetTimestamp() - startingTimestamp) * (10_000_000.0 / Stopwatch.Frequency)));

                        // Capturing the duration in seconds as per OpenTelemetry convention for instrument units:
                        // More information here: https://opentelemetry.io/docs/specs/semconv/general/metrics/#instrument-units
                        logger.LogInformation("Function completed. Duration: {Duration}s", duration.TotalSeconds);
                    }
                }
            }

            private void ProcessFunctionResult(FunctionResult functionResult)
            {
                string? result = functionResult.GetValue<string>();
                object? usage = functionResult.Metadata?["Usage"];

                if (!string.IsNullOrWhiteSpace(result))
                {
                    logger.LogTrace("Function result: {Result}", result);
                }

                if (logger.IsEnabled(LogLevel.Information) && usage is not null)
                {
                    logger.LogInformation("Usage: {Usage}", JsonSerializer.Serialize(usage));
                }
            }

            private async IAsyncEnumerable<StreamingChatMessageContent> ProcessFunctionResultStreamingAsync(IAsyncEnumerable<StreamingChatMessageContent> data)
            {
                object? usage = null;

                var stringBuilder = new StringBuilder();

                await foreach (var item in data.ConfigureAwait(true))
                {
                    yield return item;

                    if (item.Content is not null)
                    {
                        stringBuilder.Append(item.Content);
                    }

                    usage = item.Metadata?["Usage"];
                }

                var result = stringBuilder.ToString();

                if (!string.IsNullOrWhiteSpace(result))
                {
                    logger.LogTrace("Function result: {Result}", result);
                }

                if (logger.IsEnabled(LogLevel.Information) && usage is not null)
                {
                    logger.LogInformation("Usage: {Usage}", JsonSerializer.Serialize(usage));
                }
            }
        }

        /// <summary>
        /// Filter which logs an information available during prompt rendering such as rendered prompt.
        /// </summary>
        private sealed class PromptRenderLoggingFilter(ILogger logger) : IPromptRenderFilter
        {
            public async Task OnPromptRenderAsync(PromptRenderContext context, Func<PromptRenderContext, Task> next)
            {
                await next(context).ConfigureAwait(true);

                logger.LogTrace("Rendered prompt: {Prompt}", context.RenderedPrompt);
            }
        }

        /// <summary>
        /// Filter which logs an information available during automatic function calling such as:
        /// Chat history, number of functions to call, which functions to call and their arguments.
        /// </summary>
        private sealed class AutoFunctionInvocationLoggingFilter(ILogger logger) : IAutoFunctionInvocationFilter
        {
            public async Task OnAutoFunctionInvocationAsync(AutoFunctionInvocationContext context, Func<AutoFunctionInvocationContext, Task> next)
            {
                if (logger.IsEnabled(LogLevel.Trace))
                {
                    logger.LogTrace("ChatHistory: {ChatHistory}", JsonSerializer.Serialize(context.ChatHistory));
                }

                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("Function count: {FunctionCount}", context.FunctionCount);
                }

                var functionCalls = FunctionCallContent.GetFunctionCalls(context.ChatHistory.Last()).ToList();

                if (logger.IsEnabled(LogLevel.Trace))
                {
                    functionCalls.ForEach(functionCall
                        => logger.LogTrace(
                            "Function call requests: {PluginName}-{FunctionName}({Arguments})",
                            functionCall.PluginName,
                            functionCall.FunctionName,
                            JsonSerializer.Serialize(functionCall.Arguments)));
                }

                await next(context).ConfigureAwait(true);
            }
        }

        private sealed class PromptFilter(ITestOutputHelper output) : IPromptRenderFilter
        {
            private readonly ITestOutputHelper _output = output;

            public async Task OnPromptRenderAsync(PromptRenderContext context, Func<PromptRenderContext, Task> next)
            {
                this._output.WriteLine($"###########Rendering prompt for {context.Function.Name}#################");

                await next(context).ConfigureAwait(true);

                this._output.WriteLine($"###########Rendered prompt:{Environment.NewLine} {context.RenderedPrompt}");
            }
        }


        private sealed class FunctionFilter(ITestOutputHelper output) : IFunctionInvocationFilter
        {
            private readonly ITestOutputHelper _output = output;

            public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
            {
                this._output.WriteLine($"############Invoking {context.Function.Name}");

                await next(context).ConfigureAwait(true);




            }
        }
    }

}
