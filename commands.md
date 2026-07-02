Here are the dotnet CLI commands you can use to build and run the projects in this solution:

1. Build the Entire Solution
To build all projects (Core, Infrastructure, WebAPI, WebUI, and Tests) at once:

powershell
dotnet build
2. Run the WebAPI Backend
To run the WebAPI backend project:

powershell
dotnet run --project SkypointShopifyPlugin.WebAPI
3. Run the Blazor WebUI Frontend
To run the Blazor WebUI frontend project:

powershell
dotnet run --project SkypointShopifyPlugin.WebUI
4. Running Both Concurrently
Since they run on different ports (configured in their respective launchSettings.json or centrally controlled via .env), you can open two terminal windows in the root folder (d:\Office\skynet\SkypointShopifyPlugin) and run:

Terminal 1: dotnet run --project SkypointShopifyPlugin.WebAPI
Terminal 2: dotnet run --project SkypointShopifyPlugin.WebUI