using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrayRandom.Draws;

public sealed record SavedDrawList(Guid Id, string Name, string Entries);

public sealed record DrawRun(Guid Id, string? ListName, DrawMode Mode, DateTimeOffset RanAt, DrawLine[] Lines);

public sealed class DrawLibrary(string filePath)
{
    private readonly SemaphoreSlim _gate = new(1);
    private DrawData? _data;

    public IReadOnlyList<SavedDrawList> Lists => Data.Lists;

    public IReadOnlyList<DrawRun> Runs => Data.Runs;

    private DrawData Data => _data ?? throw new InvalidOperationException("Load the draw library before using it.");

    public async Task LoadAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _data ??= await ReadAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SavedDrawList> SaveListAsync(Guid? id, string name, string entries)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            string trimmedName = name.Trim();
            if (trimmedName.Length == 0)
            {
                throw new DrawValidationException(DrawError.MissingListName);
            }

            if (Lists.Any(list => list.Id != id && string.Equals(list.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new DrawValidationException(DrawError.DuplicateListName);
            }

            ListDraw.ParseEntries(entries);
            SavedDrawList list = new(id ?? Guid.NewGuid(), trimmedName, entries);
            SavedDrawList[] lists = Lists.Any(item => item.Id == id)
                ? Lists.Select(item => item.Id == id ? list : item).ToArray()
                : [.. Lists, list];
            await SaveAsync(Data with { Lists = lists }).ConfigureAwait(false);
            return list;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task DeleteListAsync(Guid id) =>
        UpdateAsync(data => data with { Lists = data.Lists.Where(list => list.Id != id).ToArray() });

    public async Task<DrawRun> RunAsync(string name, string entries, DrawMode mode, int groupCount)
    {
        DrawLine[] lines = ListDraw.Run(entries, mode, groupCount);
        DrawRun run = new(Guid.NewGuid(), string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            mode, DateTimeOffset.UtcNow, lines);
        await UpdateAsync(data => data with { Runs = [run, .. data.Runs] }).ConfigureAwait(false);
        return run;
    }

    public Task DeleteRunAsync(Guid id) =>
        UpdateAsync(data => data with { Runs = data.Runs.Where(run => run.Id != id).ToArray() });

    private async Task<DrawData> ReadAsync()
    {
        try
        {
            await using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            DrawData? data = await JsonSerializer.DeserializeAsync(stream, DrawJsonContext.Default.DrawData).ConfigureAwait(false);
            if (data is null ||
                data.Lists.Any(list => list is null || list.Id == Guid.Empty || string.IsNullOrWhiteSpace(list.Name)) ||
                data.Runs.Any(run => run is null || !IsValidRun(run)))
            {
                throw new JsonException("The draw library contains invalid data.");
            }

            return data;
        }
        catch (FileNotFoundException)
        {
            return new DrawData([], []);
        }
    }

    private async Task UpdateAsync(Func<DrawData, DrawData> update)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await SaveAsync(update(Data)).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsValidRun(DrawRun run) =>
        run.Id != Guid.Empty && Enum.IsDefined(run.Mode) && run.Lines.Length > 0 &&
        run.Lines.All(line => line is not null && !string.IsNullOrWhiteSpace(line.Entry) &&
            (run.Mode != DrawMode.Groups || line.Group is > 0) &&
            (run.Mode != DrawMode.SecretSanta || !string.IsNullOrWhiteSpace(line.Recipient)));

    private async Task SaveAsync(DrawData data)
    {
        string tempPath = filePath + ".tmp";
        await using (FileStream stream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, data, DrawJsonContext.Default.DrawData).ConfigureAwait(false);
        }

        File.Move(tempPath, filePath, overwrite: true);
        _data = data;
    }
}

internal sealed record DrawData(SavedDrawList[] Lists, DrawRun[] Runs);

[JsonSourceGenerationOptions(
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(DrawData))]
internal sealed partial class DrawJsonContext : JsonSerializerContext;
