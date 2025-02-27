
using System.ComponentModel;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using Xunit.Abstractions;

namespace Tests
{
    [Collection("TestCollection")]
    // Plugins
    public class Step4
    {
        private readonly TestFixture _fixture;
        private readonly ITestOutputHelper _output;


        public Step4(TestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }


        [Fact]
        public async Task IngestData()
        {
            var vectorStore = _fixture.VectorStore();
            var collection = vectorStore.GetCollection<string, SampleRag>("Sample");
            await collection.CreateCollectionIfNotExistsAsync();
            SampleRag[] dataToIngest = {
                new (){Key="1", Content="To fix a room issues reboot it", Url="help.com"},
                new (){Key="2", Content="Check the network connection", Url="network.com"},
                new (){Key="3", Content="Ensure the device is powered on", Url="power.com"},
                new (){Key="4", Content="Update the software to the latest version", Url="update.com"},
                new (){Key="5", Content="Contact support if the issue persists", Url="support.com"},
                new (){Key="6", Content="A Microsoft Teams Room is a Square Box", Url="support.com"}
            }; 

            var ingestTasks = dataToIngest.Select(async entry => {
                entry.Embedding =  await _fixture.EmbeddingService().GenerateEmbeddingAsync(entry.Content).ConfigureAwait(false);
                return entry;
            });

            await Task.WhenAll(ingestTasks);
            var upsertedKeysTasks = dataToIngest.Select(entry=>collection.UpsertAsync(entry));
            await Task.WhenAll(upsertedKeysTasks);

            var sampleRecord = await collection.GetAsync("1",new(){IncludeVectors=true});
            var compareto = dataToIngest[0];
            Assert.NotNull(sampleRecord);
            Assert.Equal(sampleRecord.Content,compareto.Content);
            Assert.Equal(1536,sampleRecord.Embedding.Length);
            _output.WriteLine(sampleRecord.Content);
        }

        [Fact]
        public async Task SearchWithEmbedding()
        {
            var vectorStore = _fixture.VectorStore();
            var collection = vectorStore.GetCollection<string, SampleRag>("Sample");
            var searchTerm = "What is an Teams Room?";
            var searchEmbedding = await _fixture.EmbeddingService().GenerateEmbeddingAsync(searchTerm);
            var searchResults = await collection.VectorizedSearchAsync(
                searchEmbedding,
                new(){
                    Top = 3,
                    // you can also filter here for performance, but excluded for simplicity
                }
            );
            await searchResults.Results.ForEachAsync(result =>{
                _output.WriteLine("Content:{0}",result.Record.Content);
                _output.WriteLine("Score:{0}",result.Score);
            });
    
        }

        [Fact]
        public async Task SearchWithEmbeddingRAG()
        {
            var vectorStore = _fixture.VectorStore();
            var collection = vectorStore.GetCollection<string, SampleRag>("Sample");
            var SearchPhrase = "What is an Teams Room?";
            var searchEmbedding = await _fixture.EmbeddingService().GenerateEmbeddingAsync(SearchPhrase);
            var searchResults = await collection.VectorizedSearchAsync(
                searchEmbedding,
                new(){
                    Top = 3,
                    // you can also filter here for performance, but excluded for simplicity
                }
            );
            await searchResults.Results.ForEachAsync(result =>{
                _output.WriteLine("Content:{0}",result.Record.Content);
                _output.WriteLine("Score:{0}",result.Score);
            });

            ///////////////////////
            
            var bestResult = await searchResults.Results.FirstAsync();
            var prompt = 
                $"""
                <SYSTEM>
                Answer the users question only with information available in the context
                If information is not available in the context, answer you dont know.
                Cite the source using the URL in brackets like [hello.com]
                <CONTEXT>
                CONTENT:{bestResult.Record.Content}
                URL:{bestResult.Record.Url}
                <USER>
                {SearchPhrase}
                """;
            
            var generatedAnswer = await _fixture.Kernel.InvokePromptAsync(prompt);
            _output.WriteLine("Question:{0}",SearchPhrase);
            _output.WriteLine("Answer:{0}",generatedAnswer.ToString());
        }


        internal sealed class SampleRag{
            [VectorStoreRecordKey]
            public required string Key {get;set;}
            [VectorStoreRecordData]
            public required string Content {get;set;}
            [VectorStoreRecordData]
            public required string Url {get;set;}
            [VectorStoreRecordVector(Dimensions: 1536)]
            public ReadOnlyMemory<float> Embedding { get; set; }
        } 
    

    }
}
