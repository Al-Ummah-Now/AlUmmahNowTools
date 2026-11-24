using System.Globalization;
using AlUmmahNow.ToolsCore.Social;
using AlUmmahNow.VisualImporterUno.Shell;
using AlUmmahNow.VisualImporterUno.ViewModels;
using FluentAssertions;
using Microsoft.UI.Xaml;
using Xunit;

namespace AlUmmahNow.VisualImporterUno.Tests;

[Collection("CultureSensitive")]
public sealed class SocialSourcesViewModelTests
{
    private static void WithCulture(string cultureName, Action test)
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(cultureName);
            test();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    private static ImportSource MakeSource(
        string id = "isrc_1",
        string network = SourceNetworks.Mastodon,
        string schedule = SourceSchedules.Weekly,
        bool enabled = true,
        string? lastJobId = "job_1",
        string? lastError = null) =>
        new(id, "ch_1", network, "ahmad@mastodon.social", "Mastodon posts", "recorded", null, schedule, enabled, null, null, lastJobId, lastError, null, null);

    private static SyncJob MakeJob(string status, string? lastError = null, string id = "job_1") =>
        new(id, "import.source-sync", status, 6, 6, null, lastError, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);

    private static TakeoutFile MakeTakeoutFile(
        string id = "tkf_1",
        string name = "Friday khutbah [d7Vn2Wp8RbY].mp4",
        string state = TakeoutStates.Matched,
        string? matchedBy = "video_id",
        string? note = null) =>
        new(id, "isrc_yt", name, 4096, "d7Vn2Wp8RbY", "Friday khutbah", null, state, matchedBy, note, "d7Vn2Wp8RbY",
            new TakeoutCandidate("d7Vn2Wp8RbY", "Friday khutbah", "https://www.youtube.com/watch?v=d7Vn2Wp8RbY", DateTimeOffset.UtcNow),
            null, null);

    [Fact]
    public void Failed_sync_job_hides_Retry_and_drops_Retry_copy_when_uploads_are_off()
    {
        WithCulture("en-US", () =>
        {
            var source = MakeSource(lastError: "source_unreachable: The source could not be read (http_503)");
            var failedJob = MakeJob("failed", lastError: "source_unreachable: The source could not be read (http_503)");

            var row = new SourceRow(source.Id) { UploadsOn = true };
            row.Update(source, failedJob, null);

            row.IsFailed.Should().BeTrue();
            row.CanRetryJob.Should().BeTrue();
            row.CanSync.Should().BeTrue();
            row.Status.Should().Be(Loc.KeepLastWords(Loc.Get("Social_StatusFailed")));
            row.JobStatus.Should().Be(Loc.KeepLastWords(Loc.Format("SocialJob_failed", 6, 6)));

            // Switching uploads off hides Retry on the failed job too, and drops "Retry" from card & drawer status
            row.UploadsOn = false;

            row.CanRetryJob.Should().BeFalse();
            row.CanSync.Should().BeFalse();
            row.Status.Should().Be(Loc.KeepLastWords(Loc.Get("Social_StatusFailedQuiet")));
            row.JobStatus.Should().Be(Loc.KeepLastWords(Loc.Format("SocialJob_failedQuiet", 6, 6)));
            row.Status.Should().NotContain("Retry");
            row.JobStatus.Should().NotContain("Retry");
        });
    }

    [Fact]
    public void Cancelled_sync_job_also_hides_Retry_and_uses_quiet_copy_when_uploads_are_off()
    {
        WithCulture("en-US", () =>
        {
            var source = MakeSource(schedule: SourceSchedules.Manual);
            var cancelledJob = MakeJob("cancelled");

            var row = new SourceRow(source.Id) { UploadsOn = false };
            row.Update(source, cancelledJob, null);

            row.IsCancelled.Should().BeTrue();
            row.CanRetryJob.Should().BeFalse();
            row.CanSync.Should().BeFalse();
            row.Status.Should().Be(Loc.KeepLastWords(Loc.Get("Social_StatusCancelledQuiet")));
            row.JobStatus.Should().Be(Loc.KeepLastWords(Loc.Get("SocialJob_cancelledQuiet")));
        });
    }

    [Fact]
    public void Matched_takeout_files_use_quiet_label_and_hide_upload_actions_when_uploads_are_off()
    {
        WithCulture("en-US", () =>
        {
            var matched = MakeTakeoutFile("tkf_1", "Lesson.mp4", TakeoutStates.Matched, "video_id");
            var incomplete = MakeTakeoutFile("tkf_2", "Incomplete.mp4", TakeoutStates.Failed, null, note: "upload_incomplete");
            var report = new TakeoutReport(new[] { matched, incomplete }, new Dictionary<string, int> { ["matched"] = 1, ["failed"] = 1 }, "job_t");
            var failedTakeoutJob = new SyncJob("job_t", "import.takeout-match", "failed", 1, 6, null, "upload_incomplete: pending", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);

            var row = new SourceRow("isrc_yt") { UploadsOn = true };
            row.SetTakeout(report, failedTakeoutJob);

            // Uploads on: shows "Matched, ready to upload" and "choose again to upload" hint (no local path yet)
            var matchedRow = row.TakeoutFiles.Single(f => f.Id == "tkf_1");
            var incompleteRow = row.TakeoutFiles.Single(f => f.Id == "tkf_2");
            matchedRow.StateText.Should().Be(Loc.Commas(new[] { Loc.Get("TakeoutState_matched"), Loc.Get("TakeoutMatchedBy_video_id") }));
            incompleteRow.Note.Should().Be(Loc.KeepLastWords(Loc.Get("TakeoutNote_upload_incomplete")));
            row.HasUploadTakeoutHint.Should().BeTrue();
            row.TakeoutJobStatus.Should().Contain(Loc.KeepLastWords(Loc.Get("TakeoutJob_failed")));

            // Remember local path so Upload button would be enabled when uploads are on
            row.RememberTakeoutPaths(new[] { new TakeoutFileInput("Lesson.mp4", 4096, "d7Vn2Wp8RbY", "Lesson", null, "video/mp4") { Path = "/Takeout/Lesson.mp4" } });
            row.SetTakeout(report, failedTakeoutJob);
            row.CanUploadTakeout.Should().BeTrue();

            // Switching uploads off updates matched rows to quiet label, hides upload button & hint, and updates failed job copy
            row.UploadsOn = false;

            matchedRow = row.TakeoutFiles.Single(f => f.Id == "tkf_1");
            incompleteRow = row.TakeoutFiles.Single(f => f.Id == "tkf_2");
            row.CanUploadTakeout.Should().BeFalse();
            row.HasUploadTakeoutHint.Should().BeFalse();
            row.UploadTakeoutHint.Should().BeEmpty();
            matchedRow.StateText.Should().Be(Loc.Commas(new[] { Loc.Get("TakeoutState_matchedQuiet"), Loc.Get("TakeoutMatchedBy_video_id") }));
            incompleteRow.Note.Should().Be(Loc.KeepLastWords(Loc.Get("TakeoutNote_upload_incompleteQuiet")));
            row.TakeoutJobStatus.Should().Contain(Loc.KeepLastWords(Loc.Get("TakeoutJob_failedQuiet")));
            row.TakeoutJobStatus.Should().Contain(Loc.KeepLastWords(Loc.Get("SocialReason_upload_incompleteQuiet")));
        });
    }

    [Fact]
    public void Matched_takeout_files_use_Arabic_comma_when_uploads_are_off_in_Arabic()
    {
        WithCulture("ar", () =>
        {
            var matched = MakeTakeoutFile("tkf_1", "تفسير سورة الكهف.mp4", TakeoutStates.Matched, "video_id");
            var quiet = new TakeoutFileRow(matched, uploadsOn: false);
            var active = new TakeoutFileRow(matched, uploadsOn: true);

            quiet.StateText.Should().Be(Loc.Get("TakeoutState_matchedQuiet") + "، " + Loc.Get("TakeoutMatchedBy_video_id"));
            active.StateText.Should().Be(Loc.Get("TakeoutState_matched") + "، " + Loc.Get("TakeoutMatchedBy_video_id"));
            quiet.StateText.Should().NotBe(active.StateText);
        });
    }

    [Fact]
    public void Added_message_and_upload_incomplete_reason_drop_hidden_actions_when_uploads_are_off()
    {
        WithCulture("en-US", () =>
        {
            SocialSourcesViewModel.AddedMessage("Feed", uploadsOn: true).Should().Be(Loc.GetWithDetail("Social_Added", "Feed"));
            SocialSourcesViewModel.AddedMessage("Feed", uploadsOn: false).Should().Be(Loc.GetWithDetail("Social_AddedQuiet", "Feed"));

            SourceRow.ReasonText("upload_incomplete: file pending", uploadsOn: true).Should().Be(Loc.Get("SocialReason_upload_incomplete"));
            SourceRow.ReasonText("upload_incomplete: file pending", uploadsOn: false).Should().Be(Loc.Get("SocialReason_upload_incompleteQuiet"));
        });
    }

    [Fact]
    public void Original_url_alignment_is_right_in_Arabic_and_left_in_English()
    {
        var item = new ImportSourceItem("413", "text", "posted", null, "post_413", null, "https://t.me/ahmadalazhari_lessons/413", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var file = MakeTakeoutFile();
        var candidate = new TakeoutCandidate("Hr5Nc2Vb7Qa", "أسئلة وأجوبة", "https://www.youtube.com/watch?v=Hr5Nc2Vb7Qa", DateTimeOffset.UtcNow);

        WithCulture("en-US", () =>
        {
            var itemRow = new SourceItemRow(item);
            var candidateRow = new TakeoutCandidateRow(candidate, new TakeoutFileRow(file));

            itemRow.OriginalAlignment.Should().Be(TextAlignment.Left);
            candidateRow.OriginalAlignment.Should().Be(TextAlignment.Left);
        });

        WithCulture("ar", () =>
        {
            var itemRow = new SourceItemRow(item);
            var candidateRow = new TakeoutCandidateRow(candidate, new TakeoutFileRow(file));

            itemRow.OriginalAlignment.Should().Be(TextAlignment.Right);
            candidateRow.OriginalAlignment.Should().Be(TextAlignment.Right);
        });
    }
}
