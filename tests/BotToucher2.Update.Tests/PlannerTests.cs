using BotToucher2.Update;
using Xunit;

namespace BotToucher2.Update.Tests;

public class PlannerTests
{
    [Fact]
    public void PlanCountsOnlyAddedAndChangedBytes()
    {
        var source = new[] { new InventoryFile("app/a", 1, new string('a', 64)), new InventoryFile("app/b", 2, new string('b', 64)), new InventoryFile("engine/e", 3, new string('c', 64)) };
        var target = new[] { source[0], new InventoryFile("app/b", 4, new string('d', 64)), new InventoryFile("app/c", 5, new string('e', 64)) };
        var manifest = UpdatePlanner.CreateManifest("0.2", source, target, "0.1");
        Assert.Equal(9, manifest.RequiredBytes);
        Assert.Equal(new[] { "unchanged", "replace", "add", "delete" }, manifest.Operations.Select(x => x.Kind));
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("/app/a")]
    [InlineData("app/CON.txt")]
    [InlineData("app/foo.")]
    [InlineData("app/a:stream")]
    public void RejectsUnsafePaths(string path)
    {
        Assert.Throws<InvalidDataException>(() => UpdatePlanner.CreateManifest("0.1", null, new[] { new InventoryFile(path, 1, new string('a', 64)) }));
    }
}

public class InventoryAliasTests
{
    [Fact]
    public void RejectsFileDirectoryAliasesIgnoringCase()
    {
        var files = new[] { new InventoryFile("app/Config", 1, new string('a', 64)), new InventoryFile("app/config/child", 1, new string('b', 64)) };
        Assert.Throws<InvalidDataException>(() => UpdatePlanner.CreateManifest("0.1", null, files));
    }
}

public class InventoryBoundsTests
{
    [Fact]
    public void RejectsOversizedFilesBeforeHashing()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "app"));
        using var file = new FileStream(Path.Combine(root, "app", "huge"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None); file.SetLength(512L * 1024 * 1024 + 1);
        Assert.Throws<InvalidDataException>(() => UpdatePlanner.Inventory(root));
    }
}
