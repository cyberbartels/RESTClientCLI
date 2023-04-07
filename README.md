# REST API client CLI
A console app to invoke a REST API with OAuth authentication. The idea is to provide a Command Line Interface as a client app. 

The code is just a sample which must be adapted to the problem at hand. As a starting point it leans heavily on Microsft technology. 
Parsing the user input and obtaining the OAuth token are tasks that a solved with client libraries. 

## Tutorials that served as starting point
- The CLI is based on [System.Commandline](https://learn.microsoft.com/en-us/dotnet/standard/commandline/get-started-tutorial)
- Authentication uses the [MSAL Library](https://github.com/Azure-Samples/ms-identity-dotnet-desktop-tutorial/tree/master/2-TokenCache)
- The HTTP Client is taken from [this tutorial](https://learn.microsoft.com/en-us/dotnet/csharp/tutorials/console-webapiclient)

Mix it all together ...