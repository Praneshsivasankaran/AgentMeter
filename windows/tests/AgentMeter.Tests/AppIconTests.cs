namespace AgentMeter.Tests;

public sealed class AppIconTests
{
    [Theory]
    [InlineData(16)] [InlineData(20)] [InlineData(24)] [InlineData(32)]
    [InlineData(48)] [InlineData(64)] [InlineData(128)] [InlineData(256)]
    public void EmbeddedRaspberryGaugeHasNativeSizeAndTransparentMargins(int size)
    {
        // System.Drawing's Icon loader treats the ICO 256px zero-dimension byte
        // as zero when selecting among frames; Explorer uses the valid PNG frame.
        // Validate that frame directly and exercise native tray-sized loading too.
        using var bitmap = ReadFrame(size);
        Assert.Equal(new Size(size, size), bitmap.Size);
        if (size < 256)
        {
            using var icon = AppIcon.Load(size);
            Assert.Equal(new Size(size, size), icon.Size);
        }
        Assert.Equal(0, bitmap.GetPixel(0, 0).A);
        var left = bitmap.GetPixel((int)(size * .23), (int)(size * .55));
        var right = bitmap.GetPixel((int)(size * .77), (int)(size * .55));
        Assert.True(left.B > left.G);
        Assert.True(right.R > right.G);
        Assert.True(right.R > right.B);

    }

    private static Bitmap ReadFrame(int size)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("AgentMeter.Assets.Llumi.ico")!;
        using var reader = new BinaryReader(stream);
        Assert.Equal(0, reader.ReadUInt16()); Assert.Equal(1, reader.ReadUInt16());
        var count = reader.ReadUInt16();
        for (var index = 0; index < count; index++)
        {
            stream.Position = 6 + index * 16;
            var width = reader.ReadByte(); var height = reader.ReadByte();
            stream.Position += 6;
            var length = reader.ReadUInt32(); var offset = reader.ReadUInt32();
            if ((width == 0 ? 256 : width) != size || (height == 0 ? 256 : height) != size) continue;
            stream.Position = offset;
            using var png = new MemoryStream(reader.ReadBytes(checked((int)length)));
            using var frame = new Bitmap(png);
            return new Bitmap(frame);
        }
        throw new InvalidOperationException($"Missing {size}px icon frame.");
    }
}
