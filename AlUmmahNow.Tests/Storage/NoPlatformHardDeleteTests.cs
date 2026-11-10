using System.Linq;
using System.Reflection;
using AlUmmahNow.Data.Storage;

namespace AlUmmahNow.Tests.Storage;

/// <summary>
/// Admin deletes of published content (Bunny Stream videos, Bunny Storage files, R2 records) are soft deletes owned by
/// the platform: admin-api marks the item deleted and the platform's cleanup purge is the only code path that removes
/// the external copies. The tools only publish, so the clients they share must not offer a way to hard-delete
/// platform content.
/// </summary>
public class NoPlatformHardDeleteTests
{
    private static readonly string[] PlatformNamespaces =
    {
        "AlUmmahNow.Data.Storage",
        "AlUmmahNow.Data.Video",
        "AlUmmahNow.Data.Repository",
    };

    private static readonly string[] DeleteVerbs = { "Delete", "Purge", "Remove", "Destroy" };

    [Fact]
    public void Platform_clients_expose_no_delete_operations()
    {
        var offenders = typeof(IR2Client).Assembly.GetTypes()
            .Where(t => t.Namespace is not null && PlatformNamespaces.Contains(t.Namespace))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && DeleteVerbs.Any(v => m.Name.StartsWith(v, System.StringComparison.Ordinal)))
                .Select(m => $"{t.FullName}.{m.Name}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Storage_interfaces_have_no_delete_members()
    {
        Assert.DoesNotContain(typeof(IR2Client).GetMethods(), m => m.Name.Contains("Delete"));
        Assert.DoesNotContain(typeof(IStorageClient<object>).GetMethods(), m => m.Name.Contains("Delete"));
    }
}
