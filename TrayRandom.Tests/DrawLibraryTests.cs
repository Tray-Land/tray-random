using System.Text.Json;
using TrayRandom.Draws;

namespace TrayRandom.Tests;

[TestClass]
public sealed class DrawLibraryTests
{
    private DirectoryInfo _directory = null!;
    private string _path = null!;

    [TestInitialize]
    public void CreateDrawFolder()
    {
        _directory = Directory.CreateTempSubdirectory();
        _path = Path.Combine(_directory.FullName, "draws.json");
    }

    [TestCleanup]
    public void DeleteDrawFolder() => _directory.Delete(recursive: true);

    [TestMethod]
    public async Task SaveListAsync_AddsAndEditsNamedListsAcrossReopen()
    {
        DrawLibrary library = new(_path);
        await library.LoadAsync();
        SavedDrawList first = await library.SaveListAsync(null, "  First list  ", "A\nB");
        SavedDrawList second = await library.SaveListAsync(null, "Second list", "C\nD | 6");
        SavedDrawList updated = await library.SaveListAsync(first.Id, "Renamed list", "A\nE");

        DrawLibrary reopened = new(_path);
        await reopened.LoadAsync();

        Assert.AreEqual(first.Id, updated.Id);
        CollectionAssert.AreEqual(new[] { updated, second }, reopened.Lists.ToArray());
    }

    [TestMethod]
    public async Task SaveListAsync_DuplicateName_LeavesExistingListsUnchanged()
    {
        DrawLibrary library = new(_path);
        await library.LoadAsync();
        SavedDrawList list = await library.SaveListAsync(null, "Players", "A\nB");

        var error = await Assert.ThrowsExactlyAsync<DrawValidationException>(() => library.SaveListAsync(null, " players ", "C"));

        Assert.AreEqual(DrawError.DuplicateListName, error.Error);
        CollectionAssert.AreEqual(new[] { list }, library.Lists.ToArray());
    }

    [TestMethod]
    public async Task SaveListAsync_WhenWriteFails_KeepsThePreviousListOnDiskAndInMemory()
    {
        DrawLibrary library = new(_path);
        await library.LoadAsync();
        SavedDrawList original = await library.SaveListAsync(null, "Original list", "A\nB");
        using FileStream blocker = File.Open(_path + ".tmp", FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        await Assert.ThrowsExactlyAsync<IOException>(() => library.SaveListAsync(original.Id, "New name", "C\nD"));

        DrawLibrary reopened = new(_path);
        await reopened.LoadAsync();
        Assert.AreEqual(original, library.Lists.Single());
        Assert.AreEqual(original, reopened.Lists.Single());
    }

    [TestMethod]
    public async Task RunAsync_ReopenAfterListDeletion_KeepsOriginalResultsAndNewestFirst()
    {
        DrawLibrary library = new(_path);
        await library.LoadAsync();
        SavedDrawList list = await library.SaveListAsync(null, "Players", "A\nB");
        DrawRun first = await library.RunAsync(list.Name, list.Entries, DrawMode.SecretSanta, 0);
        await library.SaveListAsync(list.Id, "Renamed", "C\nD");
        DrawRun second = await library.RunAsync("", "E\nF\nG", DrawMode.Groups, 2);
        await library.DeleteListAsync(list.Id);

        DrawLibrary reopened = new(_path);
        await reopened.LoadAsync();

        Assert.IsEmpty(reopened.Lists);
        CollectionAssert.AreEqual(new[] { second.Id, first.Id }, reopened.Runs.Select(run => run.Id).ToArray());
        Assert.AreEqual("Players", reopened.Runs[1].ListName);
        Assert.IsNull(reopened.Runs[0].ListName);
        Assert.AreEqual(first.Mode, reopened.Runs[1].Mode);
        Assert.AreEqual(first.RanAt, reopened.Runs[1].RanAt);
        CollectionAssert.AreEqual(first.Lines, reopened.Runs[1].Lines);
        CollectionAssert.AreEqual(second.Lines, reopened.Runs[0].Lines);
    }

    [TestMethod]
    public async Task DeleteRunAsync_Reopen_RemovesOnlyTheSelectedRun()
    {
        DrawLibrary library = new(_path);
        await library.LoadAsync();
        DrawRun keep = await library.RunAsync("List", "A\nB", DrawMode.Ranking, 0);
        DrawRun delete = await library.RunAsync("List", "A\nB", DrawMode.Winner, 0);
        await library.DeleteRunAsync(delete.Id);

        DrawLibrary reopened = new(_path);
        await reopened.LoadAsync();

        Assert.HasCount(1, reopened.Runs);
        Assert.AreEqual(keep.Id, reopened.Runs[0].Id);
    }

    [TestMethod]
    public async Task RunAsync_InvalidInput_DoesNotAddHistory()
    {
        DrawLibrary library = new(_path);
        await library.LoadAsync();
        await library.RunAsync("List", "A", DrawMode.Winner, 0);

        await Assert.ThrowsExactlyAsync<DrawValidationException>(() => library.RunAsync("List", "A", DrawMode.SecretSanta, 0));

        DrawLibrary reopened = new(_path);
        await reopened.LoadAsync();
        Assert.HasCount(1, reopened.Runs);
    }

    [TestMethod]
    public async Task LoadAndSaveAsync_OverlappingHistoryAndListOperations_PreserveBothChanges()
    {
        DrawLibrary library = new(_path);
        await library.LoadAsync();
        DrawRun oldRun = await library.RunAsync("Old", "A\nB", DrawMode.Winner, 0);

        Task<DrawRun> draw = library.RunAsync("New", "C\nD", DrawMode.Ranking, 0);
        Task delete = library.DeleteRunAsync(oldRun.Id);
        Task<SavedDrawList> save = library.SaveListAsync(null, "New", "C\nD");
        await Task.WhenAll(draw, delete, save, library.LoadAsync());

        DrawLibrary reopened = new(_path);
        await reopened.LoadAsync();
        Assert.AreEqual(await save, reopened.Lists.Single());
        Assert.AreEqual((await draw).Id, reopened.Runs.Single().Id);
    }

    [TestMethod]
    [DataRow("{")]
    [DataRow("""{"Lists":null,"Runs":[]}""")]
    [DataRow("""{"Lists":[],"Runs":[null]}""")]
    public async Task LoadAsync_InvalidFile_ReportsFailureWithoutReplacingIt(string json)
    {
        await File.WriteAllTextAsync(_path, json);
        DrawLibrary library = new(_path);

        await Assert.ThrowsExactlyAsync<JsonException>(library.LoadAsync);

        Assert.AreEqual(json, await File.ReadAllTextAsync(_path));
    }
}
