#tool "nuget:?package=GitVersion.CommandLine"
#tool "nuget:?package=xunit.runner.console&version=2.2.0"

#addin "Cake.FileHelpers"

var target          = Argument("target", "Default");
var configuration   = Argument("configuration", "Release");
var artifactsDir    = Directory("./artifacts");
var solution        = "./src/Aranea.HttpMessageHandler.sln";
GitVersion versionInfo = null;



Task("Clean")
    .Does(() =>
{
    CleanDirectory(artifactsDir);
});

Task("SetVersionInfo")
    .IsDependentOn("Clean")
    .Does(() =>
{
    versionInfo = GitVersion(new GitVersionSettings {
        RepositoryPath = "."
    });
});

Task("RestorePackages")
    .IsDependentOn("SetVersionInfo")
    .Does(() =>
{
    DotNetCoreRestore(solution);
});

Task("Build")
    .IsDependentOn("RestorePackages")
    .Does(() =>
{
    MSBuild(solution, new MSBuildSettings 
    {
        Verbosity = Verbosity.Minimal,
        ToolVersion = MSBuildToolVersion.VS2017,
        Configuration = configuration,
        ArgumentCustomization = args => args.Append("/p:SemVer=" + versionInfo.NuGetVersionV2)
    });
});


Task("RunTests")
    .IsDependentOn("Build")
    .Does(() =>
{
        var settings =  new DotNetCoreTestSettings 
        { 
            
        };
        DotNetCoreTest("./src/Aranea.HttpMessageHandler.Tests/Aranea.HttpMessageHandler.Tests.csproj", settings);
});

Task("MovePackages")
    .IsDependentOn("RunTests")
    .Does(() =>
{
    var files = GetFiles("./src/Aranea.HttpMessageHandler/**/*.nupkg");
    MoveFiles(files, "./artifacts");
    DeleteFile("./artifacts/Aranea.HttpMessageHandler.1.0.0.nupkg");
});


Task("NuGetPublish")
    .IsDependentOn("MovePackages")
    .Does(() =>
    {
         
        var APIKey = EnvironmentVariable("NUGETAPIKEY");
        var packages = GetFiles("./artifacts/*.nupkg");
        NuGetPush(packages, new NuGetPushSettings {
            Source = "https://www.nuget.org/api/v2/package",
            ApiKey = APIKey
        });
    });

Task("Default")
    .IsDependentOn("RunTests")
    .IsDependentOn("NuGetPublish");

RunTarget(target);