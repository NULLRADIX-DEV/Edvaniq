using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Edvaniq.Services.Content.Api;

// Uploading a file into a learning goal. The body streams straight into the file store, nothing of it is buffered in
// memory or in a temp folder. What the content turns out not to be is deleted again before the answer goes out.
internal static class FileUploadEndpoint
{
    // The file plus room for the multipart frame around it.
    public const long MaxBodyLength = FileCheck.MaxLength + (64 * 1024);

    public const string NoFile = "Es ist keine Datei angekommen. Wähle eine Datei aus und lade sie noch einmal hoch.";

    public const string GoalNotFound = "Dieses Lernziel gibt es nicht. Wähle eines deiner Lernziele aus.";

    public const string Unavailable = "Die Datei ließ sich gerade nicht speichern. Versuch es gleich noch einmal.";

    public static RouteGroupBuilder MapFileUpload(this RouteGroupBuilder documents)
    {
        documents.MapPost("/files", UploadAsync);
        return documents;
    }

    // Without learningGoalId the file goes into the default goal, as a pasted text does.
    private static async Task<IResult> UploadAsync(
        HttpContext context,
        Guid? learningGoalId,
        ILearningGoals goals,
        IFiles files,
        IDocuments documents,
        IAiBudget budget,
        IPageQuota quota,
        ICurrentUser user,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        if (request.ContentLength > MaxBodyLength)
        {
            return TooLarge();
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = MaxBodyLength;
        }

        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var type)
            || !type.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || HeaderUtilities.RemoveQuotes(type.Boundary).Value is not { Length: > 0 } boundary)
        {
            return Rejected(NoFile);
        }

        if (await BudgetRefusal.OfAsync(budget, cancellationToken) is { } refused)
        {
            return refused;
        }

        // The pages of a file are only known once the worker has read it, so here only a quota that is used up.
        if (await QuotaEndpoints.RefusalAsync(quota, user, pages: null, time, cancellationToken) is { } overQuota)
        {
            return overQuota;
        }

        // The goal before the body: a foreign goal or no Planning, and not a byte is stored.
        LearningGoalInfo goal;
        try
        {
            if (learningGoalId is not { } id)
            {
                goal = await goals.GetDefaultAsync(cancellationToken);
            }
            else if (await goals.FindAsync(id, cancellationToken) is { } found)
            {
                goal = found;
            }
            else
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: GoalNotFound);
            }
        }
        catch (LearningGoalUnavailableException)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: Unavailable);
        }

        StoredFile stored;
        FileFormat format;
        string fileName;
        try
        {
            var reader = new MultipartReader(boundary, request.Body);
            ContentDispositionHeaderValue? disposition = null;
            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(cancellationToken)) is not null)
            {
                if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out disposition) && disposition.IsFileDisposition())
                {
                    break;
                }
            }

            if (section is null || disposition is null)
            {
                return Rejected(NoFile);
            }

            fileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value ?? "";
            if (FileCheck.FormatOf(fileName) is not { } claimed)
            {
                return Rejected(FileCheck.Unsupported);
            }

            format = claimed;
            stored = await files.SaveAsync(new LimitedStream(section.Body, FileCheck.MaxLength), cancellationToken);
        }
        catch (FileTooLargeException)
        {
            return TooLarge();
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge();
        }

        if (await CheckAsync(files, stored, format, cancellationToken) is { } reason)
        {
            await files.DeleteAsync(stored.Id, CancellationToken.None);
            return Rejected(reason);
        }

        Document document;
        try
        {
            document = await documents.UploadAsync(goal, fileName, stored.Id, format, cancellationToken);
        }
        catch
        {
            await files.DeleteAsync(stored.Id, CancellationToken.None);
            throw;
        }

        return Results.Created($"/documents/{document.Id}", DocumentResponse.From(document));
    }

    // Why the stored content can't stay, or null if it can.
    private static async Task<string?> CheckAsync(IFiles files, StoredFile stored, FileFormat format, CancellationToken cancellationToken)
    {
        if (stored.Length == 0)
        {
            return FileCheck.Empty;
        }

        await using var content = await files.OpenAsync(stored.Id, cancellationToken);
        return content is null ? FileCheck.Mismatch : FileCheck.Refusal(content, format);
    }

    private static IResult TooLarge() =>
        Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: FileCheck.TooLarge);

    // 422 with the message at the field, like a refused text, so the client shows it as it is.
    private static IResult Rejected(string message) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]> { ["file"] = [message] },
            statusCode: StatusCodes.Status422UnprocessableEntity);
}

internal sealed class FileTooLargeException() : Exception("The file is larger than allowed.");

// Passes the reads on and throws once more than the limit has come through, so a file too large never gets stored
// whole. FileStore deletes what it wrote so far.
internal sealed class LimitedStream(Stream inner, long limit) : Stream
{
    private long read;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => read;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await inner.ReadAsync(buffer, cancellationToken));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int Count(int count)
    {
        read += count;
        return read > limit ? throw new FileTooLargeException() : count;
    }
}