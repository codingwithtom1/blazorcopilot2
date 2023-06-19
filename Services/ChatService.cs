using Azure;

using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using BlazorCopilot2.Data;
using System.Buffers.Text;
using System.Reflection.Metadata;
using System.Text;

namespace BlazorCopilot2.Services
{
    public class ChatService
    {
        private readonly IConfiguration _configuration;

        private string SystemMessage = "You are a knowledge base driven chat bot. When you respond, do not mention the context of your answers.  If you do not have context provided to answer your question, do not answer it.";

        public ChatService(IConfiguration configuration)
        {
            _configuration = configuration;
        }
        public async Task<Message> GetResponse(List<Message> messagechain)
        {
            string currentmessage = messagechain.Last().Body;

            // Take the latest message from the chain, search cognitive services for relevant
            // information and then update the 

            var serviceName = _configuration.GetSection("Azure")["CognitiveSearchServiceName"];
            var indexName = _configuration.GetSection("Azure")["CognitiveSearchIndexName"];
            var apiKey = _configuration.GetSection("Azure")["CognitiveSearchAPIKey"];
            var serviceEndpointURL = _configuration.GetSection("Azure")["CognitiveSearchServiceEndpointURL"];
            Uri serviceEndpoint = new Uri(serviceEndpointURL);
            AzureKeyCredential credential = new AzureKeyCredential(apiKey);
            SearchClient searchclient = new SearchClient(serviceEndpoint, indexName, credential);
            SearchOptions searchoptions = new SearchOptions() { Size = 5 };

            // Search for relevant articles based on all the questions in the thread
            StringBuilder allquestions = new StringBuilder();
            foreach (Message message in messagechain)
            {
                if (message.IsRequest)
                {
                    allquestions.Append(message.Body);
                }
            }

            StringBuilder augmentedmessage = new StringBuilder("");
            SearchResults<KnowledgeBaseEntry> results = await searchclient.SearchAsync<KnowledgeBaseEntry>(allquestions.ToString(), searchoptions);

            augmentedmessage.Append("Answer the question in the form of 'Based on my knowledge': \r\n");
            augmentedmessage.Append(currentmessage+"\r\n");
            augmentedmessage.Append("based on following knowledge:'\r\n");


            if (results.GetResults().Count() == 0)
            {
                augmentedmessage.Append("You have no knowledge.\r\n");
            }
            else
            {
                foreach (SearchResult<KnowledgeBaseEntry> result in results.GetResults())
                {
                    augmentedmessage.Append(result.Document.Body);
                    augmentedmessage.Append(". For more info about this contact " + result.Document.Owner+" in "+result.Document.Department+"\r\n");
                }
                augmentedmessage.Append("\r\n");
            }

            // Submit message chain to OpenAI GPT-35
            OpenAIClient client = new OpenAIClient(
                new Uri(_configuration.GetSection("Azure")["OpenAIUrl"]!),
                new AzureKeyCredential(_configuration.GetSection("Azure")["OpenAIKey"]!));

            ChatCompletionsOptions options = new ChatCompletionsOptions();
            options.Temperature = (float)0.5;  // make the model a little less creative
            // than the default .7, we want it to stick to our knowledge and not speculate too much
            // 0 is low creativity, 1 is high creativity
            options.MaxTokens = 800;
            options.NucleusSamplingFactor = (float)0.95;
            options.FrequencyPenalty = 0;
            options.PresencePenalty = 0;
            options.Messages.Add(new ChatMessage(ChatRole.System, SystemMessage));
            for (int i = 0; i < messagechain.Count-1; i++)
            {
                Message msg = messagechain[i];
                if (msg.IsRequest)
                {
                    options.Messages.Add(new ChatMessage(ChatRole.User, msg.Body));
                }
                else
                {
                    options.Messages.Add(new ChatMessage(ChatRole.Assistant, msg.Body));
                }
            }
            options.Messages.Add(new ChatMessage(ChatRole.User, augmentedmessage.ToString()));

            Response<ChatCompletions> resp = await client.GetChatCompletionsAsync(
                _configuration.GetSection("Azure")["OpenAIDeploymentModel"]!,
                options);

            ChatCompletions completions = resp.Value;
            string response = completions.Choices[0].Message.Content;
            Message responseMessage = new Message(response,false);
            return responseMessage;
        }
        
    }
}
