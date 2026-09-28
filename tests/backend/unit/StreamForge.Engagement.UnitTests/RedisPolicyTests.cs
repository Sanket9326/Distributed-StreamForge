using StreamForge.Engagement.Api.Infrastructure.Redis;

namespace StreamForge.Engagement.UnitTests;

public sealed class RedisPolicyTests
{
    private static string Root()
    {
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        while (path is not null && !File.Exists(Path.Combine(path.FullName, "StreamForge.slnx"))) path = path.Parent;
        return path?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    [Fact]
    public void ApplicationRedisAccess_IsConfinedToOwningScriptExecutors()
    {
        foreach (var owner in new[] { "gateway", "services/identity", "services/engagement" })
        {
            var directory = Path.Combine(Root(), "src/backend", owner);
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                    !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                    Path.GetFileName(path) != "LuaScriptExecutor.cs"))
            {
                var source = File.ReadAllText(file);
                Assert.DoesNotContain(".GetDatabase(", source);
                Assert.DoesNotContain(".ScriptEvaluate", source);
                Assert.DoesNotContain(".PingAsync(", source);
                Assert.DoesNotContain("IDatabase ", source);
            }
        }
    }

    [Fact]
    public void EveryEngagementLuaFile_IsPackagedAndReadScriptsAreFlagged()
    {
        var resources = typeof(LuaScriptExecutor).Assembly.GetManifestResourceNames();
        var folder = Path.Combine(Root(), "src/backend/services/engagement/Infrastructure/Redis/Scripts");
        foreach (var file in Directory.GetFiles(folder, "*.lua"))
            Assert.Single(resources, resource => resource.EndsWith(".Scripts." + Path.GetFileName(file)));
        foreach (var name in new[] { "ready", "read-string", "reaction-read", "summary-read", "comment-read", "subscription-status", "subscription-page",
            "history-read", "history-page", "history-retry-stats" })
            Assert.StartsWith("#!lua flags=no-writes", File.ReadAllText(Path.Combine(folder, name + ".lua")));
    }
}
