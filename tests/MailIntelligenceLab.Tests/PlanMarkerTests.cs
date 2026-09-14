using MailIntelligenceLab.Planning;
using Xunit;

namespace MailIntelligenceLab.Tests;

public class PlanMarkerTests
{
    [Fact]
    public void Apply_sets_the_action_and_leaves_every_other_column_unchanged()
    {
        var row = new ActionPlanRowBuilder().From("news@example.com").Named("Newsletter").WithMessageCount(40).Build();

        var result = PlanMarker.Apply([row], [new PlanMark("news@example.com", "delete")]);

        Assert.Empty(result.Errors);
        Assert.Equal(row with { Action = "delete" }, Assert.Single(result.Rows));
    }

    [Fact]
    public void Apply_returns_rows_not_named_by_any_mark_unchanged()
    {
        var marked = new ActionPlanRowBuilder().From("news@example.com").Build();
        var untouched = new ActionPlanRowBuilder().From("shop@example.com").WithAction("permanent-delete").Build();

        var result = PlanMarker.Apply([marked, untouched], [new PlanMark("news@example.com", "delete")]);

        Assert.Empty(result.Errors);
        Assert.Equal(untouched, result.Rows[1]);
    }

    [Fact]
    public void Apply_clears_an_existing_mark_when_the_action_is_blank()
    {
        var row = new ActionPlanRowBuilder().From("news@example.com").WithAction("delete").Build();

        var result = PlanMarker.Apply([row], [new PlanMark("news@example.com", "")]);

        Assert.Empty(result.Errors);
        Assert.Equal("", Assert.Single(result.Rows).Action);
    }

    [Fact]
    public void Apply_trims_whitespace_around_the_action()
    {
        var row = new ActionPlanRowBuilder().From("news@example.com").Build();

        var result = PlanMarker.Apply([row], [new PlanMark("news@example.com", "  delete  ")]);

        Assert.Empty(result.Errors);
        Assert.Equal("delete", Assert.Single(result.Rows).Action);
    }

    [Fact]
    public void Apply_matches_the_sender_address_regardless_of_case()
    {
        var row = new ActionPlanRowBuilder().From("news@example.com").Build();

        var result = PlanMarker.Apply([row], [new PlanMark("News@Example.COM", "delete")]);

        Assert.Empty(result.Errors);
        Assert.Equal("delete", Assert.Single(result.Rows).Action);
    }

    [Fact]
    public void Apply_rejects_an_unknown_sender_and_returns_no_rows()
    {
        var row = new ActionPlanRowBuilder().From("news@example.com").Build();

        var result = PlanMarker.Apply(
            [row],
            [new PlanMark("news@example.com", "delete"), new PlanMark("missing@example.com", "delete")]);

        Assert.Contains(result.Errors, error => error.Contains("Unknown sender"));
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Apply_reports_every_unknown_sender_not_just_the_first()
    {
        var row = new ActionPlanRowBuilder().From("news@example.com").Build();

        var result = PlanMarker.Apply(
            [row],
            [new PlanMark("first@example.com", "delete"), new PlanMark("second@example.com", "delete")]);

        Assert.Contains(result.Errors, error => error.Contains("first@example.com"));
        Assert.Contains(result.Errors, error => error.Contains("second@example.com"));
    }

    [Fact]
    public void Apply_rejects_an_action_it_does_not_recognise_and_returns_no_rows()
    {
        var row = new ActionPlanRowBuilder().From("news@example.com").Build();

        var result = PlanMarker.Apply([row], [new PlanMark("news@example.com", "remove")]);

        Assert.Contains(result.Errors, error => error.Contains("Unrecognized action"));
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Apply_rejects_two_marks_for_the_same_sender()
    {
        var row = new ActionPlanRowBuilder().From("news@example.com").Build();

        var result = PlanMarker.Apply(
            [row],
            [new PlanMark("news@example.com", "delete"), new PlanMark("NEWS@example.com", "")]);

        Assert.Contains(result.Errors, error => error.Contains("Duplicate mark"));
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Apply_with_no_marks_returns_the_rows_unchanged()
    {
        var rows = new[]
        {
            new ActionPlanRowBuilder().From("news@example.com").Build(),
            new ActionPlanRowBuilder().From("shop@example.com").WithAction("delete").Build()
        };

        var result = PlanMarker.Apply(rows, []);

        Assert.Empty(result.Errors);
        Assert.Equal(rows, result.Rows);
    }
}
