using System.IO.Compression;
using System.Text;
using SkiaSharp;

namespace ReadNest.IntegrationTests.Infrastructure;

/// <summary>Small, valid (and deliberately invalid) files built in memory for upload tests. Shared with the unit tests.</summary>
public static class TestFiles
{
    /// <summary>A minimal PDF with the given number of pages.</summary>
    public static byte[] Pdf(int pages = 3)
    {
        var objects = new List<string> { "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>" };
        var pageIds = new List<int>();
        var pagesId = 2 + (pages * 2);
        for (var i = 1; i <= pages; i++)
        {
            var text = $"BT /F1 24 Tf 72 700 Td (Page {i}) Tj ET";
            objects.Add($"<< /Length {text.Length} >>\nstream\n{text}\nendstream");
            var contentId = objects.Count;
            objects.Add($"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 1 0 R >> >> /Contents {contentId} 0 R >>");
            pageIds.Add(objects.Count);
        }

        objects.Add($"<< /Type /Pages /Kids [{string.Join(' ', pageIds.Select(id => id + " 0 R"))}] /Count {pages} >>");
        objects.Add($"<< /Type /Catalog /Pages {pagesId} 0 R >>");

        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(builder.Length);
            builder.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }

        var xref = builder.Length;
        builder.Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            builder.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        builder.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root ").Append(objects.Count).Append(" 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    /// <summary>A minimal EPUB 3. Optionally with a script in the first chapter, which readers must never run.</summary>
    public static byte[] Epub(int chapters = 2, string mimetype = "application/epub+zip", bool includeContainer = true, bool withScript = false)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "mimetype", mimetype, CompressionLevel.NoCompression);
            if (includeContainer)
            {
                Write(zip, "META-INF/container.xml",
                    "<?xml version=\"1.0\"?><container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>");
            }

            var items = string.Concat(Enumerable.Range(1, chapters).Select(i => $"<item id=\"c{i}\" href=\"c{i}.xhtml\" media-type=\"application/xhtml+xml\"/>"));
            var spine = string.Concat(Enumerable.Range(1, chapters).Select(i => $"<itemref idref=\"c{i}\"/>"));
            Write(zip, "OEBPS/content.opf",
                "<?xml version=\"1.0\"?><package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"id\"><metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:identifier id=\"id\">urn:uuid:test</dc:identifier><dc:title>Test</dc:title><dc:language>en</dc:language></metadata>" +
                $"<manifest>{items}</manifest><spine>{spine}</spine></package>");
            for (var i = 1; i <= chapters; i++)
            {
                var script = withScript && i == 1 ? "<script>window.parent.pwned = true;</script>" : string.Empty;
                Write(zip, $"OEBPS/c{i}.xhtml", $"<?xml version=\"1.0\"?><html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>Chapter {i}</title>{script}</head><body><h1>Chapter {i}</h1><p>Text.</p></body></html>");
            }
        }

        return buffer.ToArray();
    }

    /// <summary>A ZIP archive that is not an EPUB.</summary>
    public static byte[] PlainZip()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "readme.txt", "hello");
        }

        return buffer.ToArray();
    }

    public static byte[] Image(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(198, 113, 57));
            using var paint = new SKPaint { Color = new SKColor(122, 138, 94) };
            canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 3f, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    /// <summary>A PNG header that claims a 30000 x 30000 canvas (a decompression bomb) followed by almost no data.</summary>
    public static byte[] PngBomb()
    {
        using var buffer = new MemoryStream();
        buffer.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header, 30_000);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 30_000);
        header[8] = 8;
        header[9] = 2;
        WriteChunk(buffer, "IHDR", header);
        WriteChunk(buffer, "IDAT", [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]);
        WriteChunk(buffer, "IEND", []);
        return buffer.ToArray();
    }

    public static byte[] Svg() => Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");

    private static void Write(ZipArchive zip, string name, string content, CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(name, level);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crc = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32([.. typeBytes, .. data]));
        stream.Write(crc);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
