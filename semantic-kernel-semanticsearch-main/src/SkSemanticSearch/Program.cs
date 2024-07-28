using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.AI.OpenAI;
using Microsoft.SemanticKernel.Connectors.Memory.Sqlite;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.Plugins.Memory;
using SkSemanticSearch;
#pragma warning disable IDE0005 // Using directive is unnecessary.
using UglyToad.PdfPig;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Collections.Generic;
using System.Threading.Tasks;
#pragma warning restore IDE0005 // Using directive is unnecessary.

const string COLLECTION = "documentation";

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", false)
    .AddUserSecrets<Program>()
    .Build();

var index = !File.Exists("index.db");
var store = await SqliteMemoryStore.ConnectAsync("index.db");

var memory = new MemoryBuilder()
    .WithOpenAITextEmbeddingGenerationService("text-embedding-ada-002",
        config["OpenAi:ApiKey"] ?? throw new ArgumentNullException(nameof(config), "OpenAi:ApiKey is null."))
    .WithMemoryStore(store)
    .Build();

var kernel = Kernel.Builder
    .WithOpenAIChatCompletionService("gpt-3.5-turbo-16k",
        config["OpenAi:ApiKey"] ?? throw new ArgumentNullException(nameof(config), "OpenAi:ApiKey is null."))
    .Build();

kernel.ImportSemanticFunctionsFromDirectory("plugins", "qa");
var chatHistory = new ChatHistory();

if (index)
{
   // Console.WriteLine("index not found");
    await Index();
}

while (true)
{
    Console.Write("Enter a question or press enter to quit: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
    {
        Console.WriteLine("Bye 👋");
        break;
    }

   
    var answer = await Answer(input, chatHistory);
    Console.WriteLine($"\nAI -> : {answer}\n");

    // Add the bot's answer to the chat history
    chatHistory.AddUserMessage(input);
    chatHistory.AddBotMessage(answer);
}


async Task<string> Answer(string question, ChatHistory chatHistory)
{
    // Retrieve relevant context information
    var results = await memory.SearchAsync(COLLECTION, question, limit: 3).ToListAsync();
    var contextText = results.Any() ? string.Join("\n", results.Select(r => r.Metadata.Text)) : "No context found for this question.";

    // Include chat history in the context
    var historyText = chatHistory.GetHistory();
    var variables = new ContextVariables(question)
    {
        ["context"] = contextText,
        ["history"] = historyText
    };

    // Execute the function with the context variables
    var result = await kernel.RunAsync(variables, kernel.Functions.GetFunction("qa", "answer"));
    //Console.WriteLine(historyText);
    return result.GetValue<string>() ?? "I don't know";
}



async Task Index()
{
    Console.WriteLine("Indexing urls and PDFs");
    var urls = config.GetSection("urls").Get<IndexUrl[]>() ?? Array.Empty<IndexUrl>();
    var pdfs = config.GetSection("pdfs").Get<IndexPdf[]>() ?? Array.Empty<IndexPdf>();

    using var client = new HttpClient();

    foreach (var url in urls)
    {
        Console.WriteLine($"Indexing {url.Url}");
        await IndexUrl(client, url.Url, url.Selector);
    }

    foreach (var pdf in pdfs)
    {
        Console.WriteLine($"Indexing PDF {pdf.FilePath}");
        await IndexPdf(pdf.FilePath);
    }

    Console.WriteLine("Indexing done");
}

async Task IndexUrl(HttpClient client, string url, string contentSelector)
{
    var content = await client.GetStringAsync(url);
    var title = string.Empty;

    if (!string.IsNullOrWhiteSpace(contentSelector))
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(content);
        var mainElement = doc.DocumentNode.SelectSingleNode(contentSelector);
        title = mainElement.SelectSingleNode("//h1")?.InnerText ?? "Untitled";
        content = Cleanup(mainElement.InnerText);
    }

    await memory.SaveInformationAsync(COLLECTION, content, url, title);

    static string Cleanup(string content) => content.Replace("\t", "").Replace("\r\n\r\n", "");
}

async Task IndexPdf(string filePath)
{
    using var pdfDocument = PdfDocument.Open(filePath);
    var content = string.Empty;
    var title = Path.GetFileNameWithoutExtension(filePath);

    foreach (var page in pdfDocument.GetPages())
    {
        content += page.Text;
    }

    var chunks = SplitIntoChunks(content, maxTokens: 500); // Adjust maxTokens as needed
    var i = 1; 
    foreach (var chunk in chunks)
    {
        await memory.SaveInformationAsync(COLLECTION, chunk, i.ToString(), title);
        i++;
    }
}

List<string> SplitIntoChunks(string text, int maxTokens)
{
    var words = text.Split(' ');
    var chunks = new List<string>();
    var currentChunk = new List<string>();

    foreach (var word in words)
    {
        currentChunk.Add(word);
        var tokenCount = GetTokenCount(string.Join(" ", currentChunk));
        if (tokenCount > maxTokens)
        {
            currentChunk.RemoveAt(currentChunk.Count - 1);
            chunks.Add(string.Join(" ", currentChunk));
            currentChunk = new List<string> { word };
        }
    }

    if (currentChunk.Count > 0)
    {
        chunks.Add(string.Join(" ", currentChunk));
    }

    return chunks;
}

int GetTokenCount(string text)
{
    // This is a simplified estimation. For accurate token counting, use a tokenizer specific to the model.
    return text.Split(' ').Length;
}




record IndexUrl(string Url, string Selector);
record IndexPdf(string FilePath);
