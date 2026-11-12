using System.IO.Compression;

using Edvaniq.Services.Content.Domain;

using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace Edvaniq.Services.Content.Infrastructure.Files;

// PdfPig's filters, with limits on what a file may unpack to. A few MB of packed zeros unpack to gigabytes, and PdfPig
// holds a stream whole in memory. So a Flate stream is first inflated into nothing to count its size, and only read for
// real if it stays below streamLimit, which bounds the memory. The budget bounds all unpacking of the file together, and
// with it the time. One instance per file, never shared.
public sealed class BoundedPdfFilters(long streamLimit, long budget, CancellationToken cancellationToken) : IFilterProvider
{
    private readonly IFilterProvider inner = DefaultFilterProvider.Instance;
    private long left = budget;

    public IReadOnlyList<IFilter> GetFilters(DictionaryToken dictionary) => Wrap(inner.GetFilters(dictionary));

    public IReadOnlyList<IFilter> GetNamedFilters(IReadOnlyList<NameToken> names) => Wrap(inner.GetNamedFilters(names));

    public IReadOnlyList<IFilter> GetAllFilters() => Wrap(inner.GetAllFilters());

    private IReadOnlyList<IFilter> Wrap(IReadOnlyList<IFilter> filters) => [.. filters.Select(filter => new Bounded(filter, this))];

    // Takes what an unpacked stream costs, or refuses the file when the budget is gone.
    private void Spend(long length)
    {
        cancellationToken.ThrowIfCancellationRequested();
        left -= length;
        if (left < 0 || length > streamLimit)
        {
            throw new FileRefusedException(FileCheck.PackedTooTightly);
        }
    }

    private long Allowed => Math.Min(streamLimit, left);

    private sealed class Bounded(IFilter filter, BoundedPdfFilters owner) : IFilter
    {
        public bool IsSupported => filter.IsSupported;

        public Memory<byte> Decode(Memory<byte> input, DictionaryToken streamDictionary, IFilterProvider filterProvider, int filterIndex)
        {
            if (filter is FlateFilter && InflatedLength(input, owner.Allowed + 1) > owner.Allowed)
            {
                throw new FileRefusedException(FileCheck.PackedTooTightly);
            }

            var output = filter.Decode(input, streamDictionary, filterProvider, filterIndex);
            owner.Spend(output.Length);
            return output;
        }
    }

    // How many bytes the zlib data unpacks to, counted up to stopAt, nothing kept. Data that isn't valid counts as
    // nothing: PdfPig's own filter decides what to make of it.
    public static long InflatedLength(Memory<byte> input, long stopAt)
    {
        if (input.Length < 2)
        {
            return 0;
        }

        var buffer = new byte[81920];
        long total = 0;
        try
        {
            // zlib's two header bytes first, as PdfPig skips them too.
            using var packed = new MemoryStream(input[2..].ToArray(), writable: false);
            using var inflate = new DeflateStream(packed, CompressionMode.Decompress);
            int read;
            while (total < stopAt && (read = inflate.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
            }
        }
        catch (InvalidDataException)
        {
        }

        return total;
    }
}