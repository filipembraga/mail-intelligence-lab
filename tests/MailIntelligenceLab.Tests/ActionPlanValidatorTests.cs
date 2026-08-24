using MailIntelligenceLab.Models;
using MailIntelligenceLab.Planning;
using Xunit;

namespace MailIntelligenceLab.Tests;

public class ActionPlanValidatorTests
{
    // A plan row is built here with optional arguments rather than a builder:
    // every test names the two or three fields it cares about and no more.
    private static ActionPlanRow Row(
        string address = "someone@example.com",
        string action = "",
        int messageCount = 1,
        long attachmentBytes = 0) =>
        new(
            SenderAddress: address,
            SenderName: "Someone",
            MessageCount: messageCount,
            MessagesWithAttachmentsCount: 0,
            AttachmentFileCount: 0,
            TotalAttachmentSizeMB: attachmentBytes / 1024 / 1024,
            TotalAttachmentSizeBytes: attachmentBytes,
            AverageAgeYears: 1,
            OldestReceivedDate: "2010-01-01",
            NewestReceivedDate: "2024-01-01",
            Action: action);

    [Fact]
    public void Validate_accepts_a_plan_with_no_marked_rows()
    {
        var result = ActionPlanValidator.Validate([Row(), Row(address: "other@example.com")]);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.TotalRows);
        Assert.Equal(0, result.RowsMarkedForDeletion);
    }

    [Fact]
    public void Validate_rejects_duplicate_senders_even_when_the_case_differs()
    {
        var result = ActionPlanValidator.Validate(
        [
            Row(address: "Someone@Example.com"),
            Row(address: "someone@example.com")
        ]);

        Assert.Contains(result.Errors, error => error.Contains("Duplicate sender"));
    }

    [Fact]
    public void Validate_rejects_an_action_it_does_not_recognise()
    {
        var result = ActionPlanValidator.Validate([Row(action: "remove")]);

        Assert.Contains(result.Errors, error => error.Contains("Unrecognized action"));
    }

    [Fact]
    public void Validate_rejects_a_marked_sender_no_Graph_filter_can_resolve()
    {
        var result = ActionPlanValidator.Validate([Row(address: "(unknown)", action: "delete")]);

        Assert.Contains(result.Errors, error => error.Contains("cannot be resolved"));
    }

    [Fact]
    public void Validate_treats_a_whitespace_only_action_as_keep()
    {
        // A spreadsheet leaves stray spaces behind. That must mean keep, not error.
        var result = ActionPlanValidator.Validate([Row(action: "   ")]);

        Assert.Empty(result.Errors);
        Assert.Equal(0, result.RowsMarkedForDeletion);
    }

    [Fact]
    public void Validate_counts_permanent_deletions_separately_from_deletions()
    {
        var result = ActionPlanValidator.Validate(
        [
            Row(address: "a@example.com", action: "delete", messageCount: 10),
            Row(address: "b@example.com", action: "permanent-delete", messageCount: 5),
            Row(address: "c@example.com", messageCount: 999)
        ]);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.RowsMarkedForDeletion);
        Assert.Equal(1, result.RowsMarkedForPermanentDeletion);
        Assert.Equal(15, result.MessagesTargeted);
    }

    [Fact]
    public void Validate_sums_targeted_bytes_only_for_marked_rows()
    {
        var result = ActionPlanValidator.Validate(
        [
            Row(address: "a@example.com", action: "delete", attachmentBytes: 2_000),
            Row(address: "b@example.com", attachmentBytes: 8_000)
        ]);

        Assert.Equal(2_000, result.BytesTargeted);
    }
}