using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

using System.Net.Http.Headers;
using System.CommandLine;


namespace Softwaremess.CLI;
class Program
{
    private static ILogger logger;
    private static PublicClientApplicationOptions appConfiguration = null;
    private static IConfiguration configuration;
    private static string authority;
    private static IPublicClientApplication app;

    private static string? accessToken;
    private static RESTService myService;

    public class RESTService
    {
        public string BaseURL { get; set;  }
        public RESTService() { }
    }

    static async Task<int> Main(string[] args)
    {
        Microsoft.Extensions.Logging.LogLevel logLevel = Microsoft.Extensions.Logging.LogLevel.Information;
        foreach (var arg in args) { if (arg.Equals("--debug")) { logLevel = Microsoft.Extensions.Logging.LogLevel.Debug; Console.WriteLine("Switch to debug"); break; } }
        logger = CreateLogger(logLevel);
        logger.LogDebug($"{nameof(Main)} Logger created.");

        SetConfiguration();
        InitApp();
        await PrepareCache();
        //Scope depends on Azure AD App Registration settings
        string[] scopes = new[] { "api://<YOUR_AAD_CLIENT_ID>/user_impersonation" };  // Client ID of your API provider
        AuthenticationResult result;
        
        var debugOption = new Option<bool>(
           name: "--debug",
           description: "Enable debug logs"
           );

        var fileOption = new Option<FileInfo?>(
            name: "--file",
            description: "An option whose argument is parsed as a FileInfo",
            isDefault: true,
            parseArgument: result =>
            {
                if (result.Tokens.Count == 0)
                {
                    result.ErrorMessage = "File nor specified";
                    return null;
                }
                string? filePath = result.Tokens.Single().Value;
                if (!File.Exists(filePath))
                {
                    result.ErrorMessage = "File does not exist";
                    return null;
                }
                else
                {
                    return new FileInfo(filePath);
                }
            });

        var rootCommand = new RootCommand("REST CLI client");
        rootCommand.AddGlobalOption(debugOption);
        //READ
        var readCommand = new Command("read", "Read resource");
        rootCommand.AddCommand(readCommand);
        //ACCOUNT
        var accountCommand = new Command("account", "Cached accounts.");
        rootCommand.AddCommand(accountCommand);

        var accountListCommand = new Command("list", "List cached accounts");
        accountCommand.AddCommand(accountListCommand);

        var accountDeleteCommand = new Command("delete", "Delete cached accounts");
        accountCommand.AddCommand(accountDeleteCommand);
        //RESOURCE
        var uploadCommand = new Command("upload", "Upload resource");
        rootCommand.AddCommand(uploadCommand);
        uploadCommand.AddOption(fileOption);

        readCommand.SetHandler(
            async () =>
            {
                result = await AcquireToken(scopes, false);
                accessToken = result.AccessToken.ToString();
                logger.LogDebug(result.AccessToken.ToString());

                await ReadResource(@"/ping", @"application/json"); //Adapt to REST API
            }
        );

        accountListCommand.SetHandler(
            async () =>
            {
                ListAccounts();
            }
        );

        accountDeleteCommand.SetHandler(
            async () =>
            {
                ClearTokenCache();
            }
        );

        uploadCommand.SetHandler(
            async (file) =>
            {
                result = await AcquireToken(scopes, false);
                accessToken = result.AccessToken.ToString();
                logger.LogDebug(result.AccessToken.ToString());

                await UploadResource(@"/resource", file); //Adapt to REST API
            },
            fileOption);

        return await rootCommand.InvokeAsync(args);
    }

    private static ILogger CreateLogger(Microsoft.Extensions.Logging.LogLevel logLevel)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .AddFilter("Microsoft", Microsoft.Extensions.Logging.LogLevel.Warning)
                .AddFilter("System", Microsoft.Extensions.Logging.LogLevel.Warning)
                .AddFilter("Softwaremess.CLI.Program", logLevel)
                .AddSimpleConsole(options =>
                {
                    options.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
                    options.UseUtcTimestamp = false;
                    options.IncludeScopes = true;
                    options.SingleLine = true;
                    options.TimestampFormat = "yyyy-MM-ddTHHmmssz ";
                })
                .AddSimpleConsole();
        });
        ILogger logger = loggerFactory.CreateLogger<Program>();
        return logger;
    }
    private static async Task PrepareCache()
    {
        logger.LogDebug($"{nameof(PrepareCache)}");
        // Building StorageCreationProperties
        var storageProperties =
             new StorageCreationPropertiesBuilder(CacheSettings.CacheFileName, CacheSettings.CacheDir, appConfiguration.ClientId)
             .WithLinuxKeyring(
                 CacheSettings.LinuxKeyRingSchema,
                 CacheSettings.LinuxKeyRingCollection,
                 CacheSettings.LinuxKeyRingLabel,
                 CacheSettings.LinuxKeyRingAttr1,
                 CacheSettings.LinuxKeyRingAttr2)
             .WithMacKeyChain(
                 CacheSettings.KeyChainServiceName,
                 CacheSettings.KeyChainAccountName)
             .Build();

        // This hooks up the cross-platform cache into MSAL
        var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
        cacheHelper.RegisterCache(app.UserTokenCache);
    }

    private static void InitApp()
    {
        logger.LogDebug($"{nameof(InitApp)}");
        // Building the AAD authority, https://login.microsoftonline.com/<tenant>
        authority = string.Concat(appConfiguration.Instance, appConfiguration.TenantId);

        // Building a public client application
        app = PublicClientApplicationBuilder.Create(appConfiguration.ClientId)
                                                .WithAuthority(authority)
                                                .WithRedirectUri(appConfiguration.RedirectUri)
                                                .Build();
    }

    private static void SetConfiguration()
    {
        logger.LogDebug($"{nameof(SetConfiguration)}");
        // Using appsettings.json as our configuration settings
        var builder = new ConfigurationBuilder()
            .SetBasePath(System.IO.Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json");

        configuration = builder.Build();

        // Configure local console app
        // Loading PublicClientApplicationOptions from the values set on appsettings.json
        appConfiguration = configuration.Get<PublicClientApplicationOptions>();

        //Configure target service to invoke
        myService = configuration.Get<RESTService>();
        logger.LogDebug($"Target service base URL is {myService.BaseURL}");
    }

    private static async Task<AuthenticationResult> AcquireToken(string[] scopes, bool useEmbaddedView)
    {
        logger.LogDebug($"{nameof(AcquireToken)}");
        AuthenticationResult result;
        try
        {
            var accounts = await app.GetAccountsAsync();

            // Try to acquire an access token from the cache. If an interaction is required, 
            // MsalUiRequiredException will be thrown.

            result = await app.AcquireTokenSilent(scopes, accounts.FirstOrDefault())
                        .ExecuteAsync();
        }
        catch (MsalUiRequiredException)
        {
            logger.LogDebug($"{nameof(AcquireToken)} MsalUiRequiredException");
            // Acquiring an access token interactively. MSAL will cache it so we can use AcquireTokenSilent
            // on future calls.
            result = await app.AcquireTokenInteractive(scopes)
                        .WithUseEmbeddedWebView(useEmbaddedView)
                        .ExecuteAsync();
        }

        return result;
    }

    private static async Task ReadResource(string path, string mediaType)
    {
        logger.LogDebug($"{nameof(ReadResource)} with {nameof(path)} {path}, {nameof(mediaType)} {mediaType}");
        UriBuilder uriBuilder = new UriBuilder(myService.BaseURL + path);
       
        using HttpClient client = new();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(mediaType));
        client.DefaultRequestHeaders.Add("User-Agent", ".NET Foundation Repository Reporter");
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

        string url = uriBuilder.ToString();
        logger.LogDebug($"GET {url}");
        var json = await client.GetStringAsync(url);

        Console.WriteLine(json) ;
    }

    private static async Task UploadResource(string path, FileInfo file)
    {
        logger.LogDebug($"{nameof(UploadResource)} with {nameof(path)} {path}, {nameof(file)} {file.FullName}");
        UriBuilder uriBuilder = new UriBuilder(myService.BaseURL + path);

        using HttpClient client = new();
        client.DefaultRequestHeaders.Accept.Clear();
        
        client.DefaultRequestHeaders.Add("User-Agent", ".NET Foundation Repository Reporter");
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

        string fileContent = File.ReadAllText(file.FullName);
        HttpContent content = new StringContent(fileContent);
        string url = uriBuilder.ToString();
        logger.LogDebug($"POST {url}");
        HttpResponseMessage result = await client.PostAsync(url, content);

        Console.WriteLine(result.StatusCode);
        Console.WriteLine(await result.Content.ReadAsStringAsync());
    }

    private static async void ListAccounts()
    {
        logger.LogDebug($"{nameof(ListAccounts)}");
        
        var accounts2 = await app.GetAccountsAsync().ConfigureAwait(false);
        if (!accounts2.Any())
        {
            Console.WriteLine("No accounts were found in the cache.");
            Console.Write(Environment.NewLine);
        }

        foreach (var acc in accounts2)
        {
            Console.WriteLine($"Account for {acc.Username}");
            Console.Write(Environment.NewLine);
        }
    }

    private static async void ClearTokenCache()
    {
        logger.LogDebug($"{nameof(ClearTokenCache)}");
     
        var accounts3 = await app.GetAccountsAsync().ConfigureAwait(false);
        foreach (var acc in accounts3)
        {
            Console.WriteLine($"Removing account for {acc.Username}");
            Console.Write(Environment.NewLine);
            await app.RemoveAsync(acc).ConfigureAwait(false);
        }
    }
}