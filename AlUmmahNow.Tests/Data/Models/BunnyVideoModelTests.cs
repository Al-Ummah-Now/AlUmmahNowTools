using Xunit;
using FluentAssertions;
using AlUmmahNow.Data.Models;
using System;
using System.Collections.Generic;

namespace AlUmmahNow.Tests.Data.Models;

/// <summary>
/// Tests for BunnyVideo model classes including enums and nested models
/// </summary>
public class BunnyVideoModelTests
{
    #region BunnyVideo Tests

    [Fact]
    public void BunnyVideo_DefaultInitialization_SetsDefaults()
    {
        // Arrange & Act
        var video = new BunnyVideo();

        // Assert
        video.Should().NotBeNull();
        video.VideoLibraryId.Should().Be(0);
        video.Guid.Should().BeNull();
        video.Title.Should().BeNull();
        video.Description.Should().Be(string.Empty);
        video.Views.Should().Be(0);
        video.IsPublic.Should().BeFalse();
        video.Length.Should().Be(0);
        video.Status.Should().Be(VideoStatus.Created);
        video.HasMP4Fallback.Should().BeFalse();
        video.JitEncodingEnabled.Should().BeFalse();
    }

    [Fact]
    public void BunnyVideo_WithValues_StoresCorrectly()
    {
        // Arrange & Act
        var video = new BunnyVideo
        {
            VideoLibraryId = 12345,
            Guid = "test-guid-123",
            Title = "Test Video",
            Description = "Test Description",
            DateUploaded = DateTime.UtcNow,
            Views = 1000,
            IsPublic = true,
            Length = 120,
            Status = VideoStatus.Finished,
            Framerate = 30.0,
            Rotation = 90,
            Width = 1920,
            Height = 1080,
            AvailableResolutions = "720p,1080p",
            ThumbnailCount = 5,
            EncodeProgress = 100,
            StorageSize = 1024000,
            HasMP4Fallback = true,
            CollectionId = "collection-123",
            ThumbnailFileName = "thumb.jpg",
            AverageWatchTime = 60,
            TotalWatchTime = 3600,
            Category = "Education"
        };

        // Assert
        video.VideoLibraryId.Should().Be(12345);
        video.Guid.Should().Be("test-guid-123");
        video.Title.Should().Be("Test Video");
        video.Description.Should().Be("Test Description");
        video.Views.Should().Be(1000);
        video.IsPublic.Should().BeTrue();
        video.Length.Should().Be(120);
        video.Status.Should().Be(VideoStatus.Finished);
        video.Framerate.Should().Be(30.0);
        video.Rotation.Should().Be(90);
        video.Width.Should().Be(1920);
        video.Height.Should().Be(1080);
        video.AvailableResolutions.Should().Be("720p,1080p");
        video.ThumbnailCount.Should().Be(5);
        video.EncodeProgress.Should().Be(100);
        video.StorageSize.Should().Be(1024000);
        video.HasMP4Fallback.Should().BeTrue();
        video.CollectionId.Should().Be("collection-123");
        video.ThumbnailFileName.Should().Be("thumb.jpg");
        video.AverageWatchTime.Should().Be(60);
        video.TotalWatchTime.Should().Be(3600);
        video.Category.Should().Be("Education");
    }

    [Fact]
    public void BunnyVideo_VideoUrl_GeneratesCorrectUrl()
    {
        // Arrange
        var video = new BunnyVideo
        {
            Guid = "test-guid-456"
        };

        // Act
        var url = video.VideoUrl;

        // Assert
        url.Should().Be("/test-guid-456/playlist.m3u8");
    }

    [Fact]
    public void BunnyVideo_WithCollections_StoresCorrectly()
    {
        // Arrange & Act
        var video = new BunnyVideo
        {
            Captions = new List<string> { "en", "ar", "es" },
            Chapters = new List<ChapterModel>
            {
                new ChapterModel { Title = "Intro", Start = 0, End = 30 },
                new ChapterModel { Title = "Content", Start = 30, End = 90 }
            },
            Moments = new List<MomentModel>
            {
                new MomentModel { Label = "Key Point", Timestamp = 45 }
            },
            MetaTags = new List<MetaTagModel>
            {
                new MetaTagModel { Property = "og:title", Value = "Test" }
            },
            TranscodingMessages = new List<TranscodingMessageModel>
            {
                new TranscodingMessageModel
                {
                    TimeStamp = DateTime.UtcNow,
                    Level = Severity.High,
                    IssueCode = IssueCodes.Code1,
                    Message = "Test message",
                    Value = "Test value"
                }
            }
        };

        // Assert
        video.Captions.Should().HaveCount(3);
        video.Chapters.Should().HaveCount(2);
        video.Moments.Should().HaveCount(1);
        video.MetaTags.Should().HaveCount(1);
        video.TranscodingMessages.Should().HaveCount(1);
    }

    #endregion

    #region VideoStatus Enum Tests

    [Theory]
    [InlineData(VideoStatus.Created, 0)]
    [InlineData(VideoStatus.Uploaded, 1)]
    [InlineData(VideoStatus.Processing, 2)]
    [InlineData(VideoStatus.Transcoding, 3)]
    [InlineData(VideoStatus.Finished, 4)]
    [InlineData(VideoStatus.Error, 5)]
    [InlineData(VideoStatus.UploadFailed, 6)]
    [InlineData(VideoStatus.JitSegmenting, 7)]
    [InlineData(VideoStatus.JitPlaylistsCreated, 8)]
    public void VideoStatus_EnumValues_AreCorrect(VideoStatus status, int expected)
    {
        // Assert
        ((int)status).Should().Be(expected);
    }

    #endregion

    #region ChapterModel Tests

    [Fact]
    public void ChapterModel_WithValues_StoresCorrectly()
    {
        // Arrange & Act
        var chapter = new ChapterModel
        {
            Title = "Introduction",
            Start = 0,
            End = 60
        };

        // Assert
        chapter.Title.Should().Be("Introduction");
        chapter.Start.Should().Be(0);
        chapter.End.Should().Be(60);
    }

    #endregion

    #region MomentModel Tests

    [Fact]
    public void MomentModel_DefaultInitialization_SetsDefaults()
    {
        // Arrange & Act
        var moment = new MomentModel();

        // Assert
        moment.Label.Should().Be(string.Empty);
        moment.Timestamp.Should().BeNull();
    }

    [Fact]
    public void MomentModel_WithValues_StoresCorrectly()
    {
        // Arrange & Act
        var moment = new MomentModel
        {
            Label = "Important Scene",
            Timestamp = 120
        };

        // Assert
        moment.Label.Should().Be("Important Scene");
        moment.Timestamp.Should().Be(120);
    }

    #endregion

    #region MetaTagModel Tests

    [Fact]
    public void MetaTagModel_WithValues_StoresCorrectly()
    {
        // Arrange & Act
        var metaTag = new MetaTagModel
        {
            Property = "og:description",
            Value = "Test Description"
        };

        // Assert
        metaTag.Property.Should().Be("og:description");
        metaTag.Value.Should().Be("Test Description");
    }

    #endregion

    #region TranscodingMessageModel Tests

    [Fact]
    public void TranscodingMessageModel_WithValues_StoresCorrectly()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;

        // Act
        var message = new TranscodingMessageModel
        {
            TimeStamp = timestamp,
            Level = Severity.Medium,
            IssueCode = IssueCodes.Code2,
            Message = "Transcoding warning",
            Value = "Warning value"
        };

        // Assert
        message.TimeStamp.Should().Be(timestamp);
        message.Level.Should().Be(Severity.Medium);
        message.IssueCode.Should().Be(IssueCodes.Code2);
        message.Message.Should().Be("Transcoding warning");
        message.Value.Should().Be("Warning value");
    }

    #endregion

    #region Severity Enum Tests

    [Theory]
    [InlineData(Severity.Low)]
    [InlineData(Severity.Medium)]
    [InlineData(Severity.High)]
    public void Severity_EnumValues_AreDefined(Severity severity)
    {
        // Assert
        Enum.IsDefined(typeof(Severity), severity).Should().BeTrue();
    }

    #endregion

    #region IssueCodes Enum Tests

    [Theory]
    [InlineData(IssueCodes.Code1)]
    [InlineData(IssueCodes.Code2)]
    [InlineData(IssueCodes.Code3)]
    public void IssueCodes_EnumValues_AreDefined(IssueCodes code)
    {
        // Assert
        Enum.IsDefined(typeof(IssueCodes), code).Should().BeTrue();
    }

    #endregion
}
