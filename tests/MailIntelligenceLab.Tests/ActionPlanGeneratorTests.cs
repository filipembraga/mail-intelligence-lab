using MailIntelligenceLab.Planning;
using Xunit;

namespace MailIntelligenceLab.Tests;

public class ActionPlanGeneratorTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("delete", true)]
    [InlineData("DELETE", true)]
    [InlineData("  delete  ", true)]
    [InlineData("permanent-delete", true)]
    [InlineData("Permanent-Delete", true)]
    [InlineData("remove", false)]
    [InlineData("delete ", true)]
    [InlineData("deletee", false)]
    public void IsActionable_recognises_only_the_two_destructive_values(string? action, bool expected)
    {
        Assert.Equal(expected, ActionPlanGenerator.IsActionable(action));
    }

    [Theory]
    [InlineData("permanent-delete", true)]
    [InlineData("PERMANENT-DELETE", true)]
    [InlineData("delete", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsPermanentDelete_separates_the_unrecoverable_action(string? action, bool expected)
    {
        Assert.Equal(expected, ActionPlanGenerator.IsPermanentDelete(action));
    }

    [Theory]
    [InlineData("someone@example.com", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("(unknown)", false)]
    [InlineData("/o=ExchangeLabs/ou=Exchange Administrative Group", false)]
    [InlineData("/O=EXCHANGELABS", false)]
    public void IsResolvable_accepts_only_addresses_a_Graph_filter_can_match(string? address, bool expected)
    {
        Assert.Equal(expected, ActionPlanGenerator.IsResolvable(address));
    }

    [Fact]
    public void Generate_merges_rows_differing_only_in_case()
    {
        var report = new[]
        {
            new SenderReportRowBuilder().From("Someone@Example.com").WithMessageCount(40).Build(),
            new SenderReportRowBuilder().From("someone@example.com").WithMessageCount(2).Build()
        };

        var result = ActionPlanGenerator.Generate(report);

        var row = Assert.Single(result.Rows);
        Assert.Equal(1, result.MergedByCase);
        Assert.Equal(42, row.MessageCount);
    }

    [Fact]
    public void Generate_writes_the_merged_address_in_lower_case()
    {
        var report = new[]
        {
            new SenderReportRowBuilder().From("Someone@Example.com").Build()
        };

        var result = ActionPlanGenerator.Generate(report);

        Assert.Equal("someone@example.com", Assert.Single(result.Rows).SenderAddress);
    }

    [Fact]
    public void Generate_sums_attachment_weight_across_merged_rows()
    {
        var report = new[]
        {
            new SenderReportRowBuilder().From("A@example.com").WithAttachmentBytes(1_000).Build(),
            new SenderReportRowBuilder().From("a@example.com").WithAttachmentBytes(2_500).Build()
        };

        var result = ActionPlanGenerator.Generate(report);

        Assert.Equal(3_500, Assert.Single(result.Rows).TotalAttachmentSizeBytes);
    }

    [Fact]
    public void Generate_excludes_senders_no_Graph_filter_can_resolve()
    {
        var report = new[]
        {
            new SenderReportRowBuilder().From("real@example.com").Build(),
            new SenderReportRowBuilder().From("(unknown)").Build(),
            new SenderReportRowBuilder().From("/o=ExchangeLabs/ou=Group").Build()
        };

        var result = ActionPlanGenerator.Generate(report);

        Assert.Equal(2, result.ExcludedAsUnresolvable);
        Assert.Equal("real@example.com", Assert.Single(result.Rows).SenderAddress);
    }

    [Fact]
    public void Generate_weights_average_age_by_message_count()
    {
        // 400 messages averaging 10 years, 4 messages averaging 1 year.
        // Weighted: (10*400 + 1*4) / 404 = 9.9 -> 10.
        // A plain average of the two averages would give 5.5 -> 6.
        var report = new[]
        {
            new SenderReportRowBuilder().From("A@example.com").WithMessageCount(400).WithAverageAgeYears(10).Build(),
            new SenderReportRowBuilder().From("a@example.com").WithMessageCount(4).WithAverageAgeYears(1).Build()
        };

        var result = ActionPlanGenerator.Generate(report);

        Assert.Equal(10, Assert.Single(result.Rows).AverageAgeYears);
    }

    [Fact]
    public void Generate_leaves_the_action_column_blank()
    {
        var report = new[]
        {
            new SenderReportRowBuilder().From("someone@example.com").Build()
        };

        var result = ActionPlanGenerator.Generate(report);

        Assert.False(ActionPlanGenerator.IsActionable(Assert.Single(result.Rows).Action));
    }

    [Fact]
    public void Generate_orders_rows_by_attachment_weight_descending()
    {
        var report = new[]
        {
            new SenderReportRowBuilder().From("small@example.com").WithAttachmentBytes(10).Build(),
            new SenderReportRowBuilder().From("big@example.com").WithAttachmentBytes(9_000).Build()
        };

        var result = ActionPlanGenerator.Generate(report);

        Assert.Equal("big@example.com", result.Rows[0].SenderAddress);
    }
}