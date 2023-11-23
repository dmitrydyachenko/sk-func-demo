using HandlebarsDotNet;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.AI.OpenAI;
using System.Text.RegularExpressions;

public class Program {

    public static async Task Main(string[] args) {
        await Demo();
    }

    public static async Task Demo() 

    {

        // Get the configuration from the appsettings.json file
        IConfigurationBuilder builder = new ConfigurationBuilder().AddJsonFile("appsettings.json").AddUserSecrets<Program>();
        IConfigurationRoot configuration = builder.Build();
        string cfgDeployment = configuration["AIService:Models:Completion"] ?? "";
        string cfgKey = configuration["AIService:Key"] ?? "";
        string cfgEndpoint = configuration["AIService:Endpoint"] ?? "";
        string cfgTokenLimit = configuration["AIService:TokenLimit"] ?? "";
        if(cfgKey.Length == 0 || cfgEndpoint.Length == 0 || cfgDeployment.Length == 0) {
            Console.WriteLine("Please ensure you have set the correct values in appsettings.json / user secrets.");
            return;
        }


        // Build the Semantic Kernel
        var skBuilder = new KernelBuilder();
        skBuilder.WithAzureOpenAIChatCompletionService(
            cfgDeployment,
            cfgEndpoint,
            cfgKey
        );
        var kernel = skBuilder.Build();


        // Add Skills (Now called 'Functions' in newest SK)
        var functionDirectory = Path.Combine(Directory.GetCurrentDirectory(), "skFunctions");
        var funcsDataTypes = kernel.ImportSemanticFunctionsFromDirectory(functionDirectory, "DataTypes");
        var funcsDateQueries = kernel.ImportSemanticFunctionsFromDirectory(functionDirectory, "DateQueries");


        // DEFINE USER ASK!
        Console.Clear();
        Console.WriteLine("Welcome to the SK Demo!\n");
        Console.WriteLine("Ask a question or press enter to use the default ('What incident occured last year which Joe Bloggs was involved in?')");
        string? userAsk = Console.ReadLine();
        if(userAsk == null || userAsk.Length == 0) {
            userAsk = "What incident occured last year which Joe Bloggs was involved in?";
        }



        // Begin Steps....
        Console.Clear();
        Console.WriteLine("Demo Begins...\n");
        Console.WriteLine("The user asks: " + userAsk + "\n\n");
        Console.WriteLine("The first call is to the 'GetArchiveDataType' SK Function. This function asks GPT to return the user asks with some modifications:-");
        Console.WriteLine("  1. It will determine the data type(s) the user is asking for... e.g. Audit, Action, Incident, Event...");
        Console.WriteLine("  2. It will ensure names are wrapped in double quotes");
        Console.WriteLine("  3. It will ensure a name is represented as 'Surname, Firstname' in addition to 'Firstname Surname'\n\nPress enter to continue...");
        Console.ReadLine();
        Console.Clear();

        // Call the first SK function to get the data type(s) the user is asking for
        // Run some regex to get the data type(s) from the result and make an odata query
        // Run some regex to remove the data type(s) from the result and make a search term
        Console.WriteLine("Calling the first SK function (GetArchiveDataType) with the User Ask:\n" + userAsk + "\n\n");
        var result1 = await kernel.RunAsync(userAsk, funcsDataTypes["GetArchiveDataType"]);
        Console.WriteLine("The result of the first call is:\n\n" + result1 + "\n");
        Console.WriteLine("... That's a good start. We'll pass this result to a custom C# function to build an odata query, and another to remove the data types from the string.\n");
        Console.WriteLine("We now have a good search term and filter to pass to ACS:-");
        string odataQuery = BuildDataTypeFilter(result1.ToString());
        string userAskNoDataTypes = RemoveDataTypesFromString(result1.ToString());
        Console.WriteLine("ACS Filter: "  + odataQuery);
        Console.WriteLine("ACS Search Term: " +  userAskNoDataTypes + "\n\n");

        // Call the second SK function (Date)
        Console.WriteLine("Now that we know what data types the user is asking for, we can create an odata query for that too using our second SK function:-");
        Console.WriteLine("We'll pass this into the second SK function: " + result1 + "\n\n");
        Console.WriteLine("Press enter to continue...");
        Console.ReadLine();
        Console.Clear();
        Console.WriteLine("Calling the second SK function (DateQueries) with the User Ask:\n" + result1.ToString() + "\n\n");
        var result2 = await kernel.RunAsync(result1.ToString(), funcsDateQueries["GetDateOdata"]);
        Console.WriteLine("The result of this call is:\n\n" + result2 + "\n");
        
        var newOdataQuery = odataQuery.Length > 0 ? odataQuery + " and " + result2.ToString() : result2.ToString();

        Console.WriteLine("This is the ACS search we'd use:-");
        Console.WriteLine("Search: " + userAskNoDataTypes);
        Console.WriteLine("Filter: " + newOdataQuery + "\n\n");
        Console.WriteLine("Wondering why the conclusion was reached regarding the date filter? Change the last line in ./DateQueries/GetDateOdata/skprompt.txt from 'DEBUG OFF' to 'DEBUG ON' and run again!\n\n");

    }

    private static string BuildDataTypeFilter(string input)
    {
        // This pattern matches any characters that follow "Data Type:" and are wrapped in parentheses.
        var pattern = @"\((Data Type: ([^\)]+))\)";
        var matches = Regex.Matches(input, pattern);

        // Check if there are any matches; if not, return String.Empty.
        if (matches.Count == 0)
            return string.Empty;

        // Build the output string.
        var output = "";
        for (int i = 0; i < matches.Count; i++)
        {
            if (i > 0)
                output += " or "; // Add "or" if there are multiple data types.

            // The second group in the match contains the actual data type value.
            output += $"(Type eq '{matches[i].Groups[2].Value}')";
        }

        return output;
    }

    private static string RemoveDataTypesFromString(string input)
    {
        // This pattern matches any characters that follow "Data Type:" and are wrapped in parentheses.
        var pattern = @"\((Data Type: ([^\)]+))\)";

        // Remove all data types from input string and save as output
        var output = Regex.Replace(input, pattern, string.Empty);

        return output;
    }
}