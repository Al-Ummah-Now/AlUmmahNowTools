using AlUmmahNow.Data.Video;

namespace AlUmmahNow.Tests.Video;

[Trait("Category", "Unit")]
public class ThumbnailShapeTests
{
    [Theory]
    [InlineData(1280, 720, 1920, 1080)]   // 16:9 thumbnail, 16:9 video
    [InlineData(720, 1280, 1080, 1920)]   // 9:16 both
    [InlineData(1080, 1080, 720, 720)]    // 1:1 both
    [InlineData(1280, 696, 1280, 720)]    // odd encode, 3.4% off
    [InlineData(1280, 720, 1280, 696)]    // same, the other way round
    public void Thumbnails_within_five_percent_of_the_video_shape_match(int tw, int th, int vw, int vh)
    {
        Assert.True(ThumbnailShape.Matches(tw, th, vw, vh));
        Assert.Equal(ThumbnailDecision.KeepSource, ThumbnailShape.Choose(true, (tw, th), (vw, vh)));
    }

    [Theory]
    [InlineData(1280, 720, 720, 1280)]    // 16:9 YouTube art on a 9:16 video
    [InlineData(640, 480, 1080, 1080)]    // 4:3 art on a 1:1 video
    [InlineData(480, 360, 1920, 1080)]    // hqdefault (4:3, black bars) on a 16:9 video
    [InlineData(640, 480, 1280, 720)]     // sddefault on a 16:9 video
    [InlineData(1280, 720, 1080, 1080)]   // 16:9 art on a 1:1 video
    public void Thumbnails_of_another_shape_are_replaced_by_a_frame(int tw, int th, int vw, int vh)
    {
        Assert.False(ThumbnailShape.Matches(tw, th, vw, vh));
        var decision = ThumbnailShape.Choose(true, (tw, th), (vw, vh));
        Assert.Equal(ThumbnailDecision.FrameShapeMismatch, decision);
        Assert.True(decision.UsesFrame());
    }

    [Fact]
    public void The_tolerance_is_five_percent_of_the_ratio()
    {
        // 16:9 = 1.7778. 1280x683 is 1.874 (5.4% off); 1280x700 is 1.829 (2.9% off).
        Assert.False(ThumbnailShape.Matches(1280, 683, 16, 9));
        Assert.True(ThumbnailShape.Matches(1280, 700, 16, 9));
    }

    [Fact]
    public void A_missing_thumbnail_uses_a_frame()
    {
        Assert.Equal(ThumbnailDecision.FrameNoThumbnail, ThumbnailShape.Choose(false, null, (1920, 1080)));
        Assert.Equal(ThumbnailDecision.FrameNoThumbnail, ThumbnailShape.Choose(false, null, null));
        Assert.True(ThumbnailDecision.FrameNoThumbnail.UsesFrame());
    }

    [Fact]
    public void An_unreadable_thumbnail_uses_a_frame_and_an_unreadable_video_keeps_the_source()
    {
        Assert.Equal(ThumbnailDecision.FrameUnreadableThumbnail, ThumbnailShape.Choose(true, null, (1920, 1080)));
        Assert.Equal(ThumbnailDecision.KeepSourceUnverified, ThumbnailShape.Choose(true, (1280, 720), null));
        Assert.False(ThumbnailDecision.KeepSourceUnverified.UsesFrame());
    }

    [Fact]
    public void Missing_or_zero_sizes_never_match()
    {
        Assert.False(ThumbnailShape.Matches(0, 720, 1280, 720));
        Assert.False(ThumbnailShape.Matches(1280, 720, 1280, 0));
    }

    [Theory]
    [InlineData(1920, 1080, 90, 1080, 1920)]   // phone video recorded landscape, shown portrait
    [InlineData(1920, 1080, -90, 1080, 1920)]
    [InlineData(1920, 1080, 270, 1080, 1920)]
    [InlineData(1920, 1080, 180, 1920, 1080)]
    [InlineData(1920, 1080, 0, 1920, 1080)]
    [InlineData(1920, 1080, 450, 1080, 1920)]
    public void Rotation_metadata_gives_the_displayed_size(int w, int h, double rotation, int dw, int dh)
    {
        Assert.Equal((dw, dh), ThumbnailShape.DisplaySize(w, h, rotation));
    }

    [Fact]
    public void A_rotated_phone_video_counts_as_vertical()
    {
        var shown = ThumbnailShape.DisplaySize(1920, 1080, 90);
        Assert.Equal(ThumbnailDecision.FrameShapeMismatch, ThumbnailShape.Choose(true, (1280, 720), shown));
        Assert.Equal(ThumbnailDecision.KeepSource, ThumbnailShape.Choose(true, (720, 1280), shown));
    }

    [Theory]
    [InlineData(1920, 1080, 1280, 720)]
    [InlineData(720, 1280, 720, 1280)]     // already 1280 long
    [InlineData(1080, 1920, 720, 1280)]
    [InlineData(1080, 1080, 1080, 1080)]    // never upscaled
    [InlineData(2160, 2160, 1280, 1280)]
    [InlineData(640, 360, 640, 360)]       // never upscaled
    [InlineData(3840, 1600, 1280, 534)]    // 2.4:1, even sides
    public void Frames_keep_the_video_shape_with_a_1280_long_edge(int vw, int vh, int fw, int fh)
    {
        var (w, h) = ThumbnailShape.FrameSize(vw, vh);
        Assert.Equal((fw, fh), (w, h));
        Assert.True(ThumbnailShape.Matches(w, h, vw, vh));
        Assert.Equal(0, w % 2);
        Assert.Equal(0, h % 2);
    }
}
