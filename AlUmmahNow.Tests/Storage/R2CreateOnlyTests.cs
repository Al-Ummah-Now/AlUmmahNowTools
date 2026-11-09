using System.Net;
using AlUmmahNow.Data.Storage;
using Amazon.S3;
using Amazon.S3.Model;
using Moq;

namespace AlUmmahNow.Tests.Storage;

[Trait("Category", "Unit")]
public class R2CreateOnlyTests
{
    [Fact]
    public async Task Create_only_put_sends_if_none_match_star()
    {
        var s3 = new Mock<IAmazonS3>();
        PutObjectRequest? sent = null;
        s3.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(new PutObjectResponse());

        Assert.True(await new R2Client(s3.Object).PutObjectIfAbsentAsync("videos", "id", "{}"));
        Assert.Equal("*", sent!.IfNoneMatch);
        Assert.Equal("id", sent.Key);
    }

    [Fact]
    public async Task Create_only_put_reports_an_existing_key_as_not_created()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("At least one of the pre-conditions you specified did not hold") { StatusCode = HttpStatusCode.PreconditionFailed });

        Assert.False(await new R2Client(s3.Object).PutObjectIfAbsentAsync("videos", "id", "{}"));
    }

    [Fact]
    public async Task Create_only_put_rethrows_other_errors()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("boom") { StatusCode = HttpStatusCode.InternalServerError });

        await Assert.ThrowsAsync<AmazonS3Exception>(() => new R2Client(s3.Object).PutObjectIfAbsentAsync("videos", "id", "{}"));
    }
}
