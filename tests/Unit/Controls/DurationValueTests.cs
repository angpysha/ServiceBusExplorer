using Xunit;

namespace ServiceBusExplorer.UnitTests.Controls;

public class DurationValueTests
{
    [Theory]
    [InlineData("0.00:00:00", 0)]
    [InlineData("12.03:04:05", 1047845000)]
    [InlineData("12.03:04:05.006", 1047845006)]
    [InlineData("10675199.02:48:05.477", 922337203685477)]
    public void Parse_ValidCanonicalText_RoundTrips(string text, long milliseconds)
    {
        var value = DurationValue.Parse(text);

        Assert.Equal(milliseconds, value.TotalMilliseconds);
        Assert.Equal(text, value.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1.00:00:00")]
    [InlineData("1:00:00")]
    [InlineData("1.0:00:00")]
    [InlineData("1.24:00:00")]
    [InlineData("1.00:60:00")]
    [InlineData("1.00:00:60")]
    [InlineData("1.00:00:00.1")]
    [InlineData("1.00:00:00.000")]
    [InlineData("10675199.02:48:05.478")]
    public void TryParse_NonCanonicalOrOutOfRangeText_Rejects(string text)
    {
        Assert.False(DurationValue.TryParse(text, out _));
    }

    [Fact]
    public void Create_Components_PreservesMilliseconds()
    {
        var value = DurationValue.Create(400, 3, 4, 5, 6);

        Assert.Equal(400, value.Days);
        Assert.Equal(3, value.Hours);
        Assert.Equal(4, value.Minutes);
        Assert.Equal(5, value.Seconds);
        Assert.Equal(6, value.Milliseconds);
        Assert.Equal("400.03:04:05.006", value.ToString());
    }

    [Theory]
    [InlineData(-1, 0, 0, 0, 0, "Days")]
    [InlineData(0, 24, 0, 0, 0, "Hours")]
    [InlineData(0, 0, 60, 0, 0, "Minutes")]
    [InlineData(0, 0, 0, 60, 0, "Seconds")]
    [InlineData(0, 0, 0, 0, 1000, "Milliseconds")]
    public void TryCreate_InvalidComponent_ReportsField(
        long days,
        int hours,
        int minutes,
        int seconds,
        int milliseconds,
        string expectedField)
    {
        var success = DurationValue.TryCreate(
            days,
            hours,
            minutes,
            seconds,
            milliseconds,
            out _,
            out var errors);

        Assert.False(success);
        Assert.Contains(expectedField, errors.Keys);
    }

    [Fact]
    public void TimeSpanConversion_MillisecondAlignedValue_IsLossless()
    {
        var original = TimeSpan.FromDays(400)
            + TimeSpan.FromHours(3)
            + TimeSpan.FromMinutes(4)
            + TimeSpan.FromSeconds(5)
            + TimeSpan.FromMilliseconds(6);

        var value = DurationValue.FromTimeSpan(original);

        Assert.Equal(original, value.ToTimeSpan());
    }

    [Fact]
    public void FromTimeSpan_RejectsNegativeAndSubMillisecondValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DurationValue.FromTimeSpan(TimeSpan.FromMilliseconds(-1)));
        Assert.Throws<ArgumentException>(
            () => DurationValue.FromTimeSpan(TimeSpan.FromTicks(1)));
    }

    [Fact]
    public void EditTransaction_InvalidDraft_DoesNotReplaceOriginalAndCancelRestoresIt()
    {
        var original = DurationValue.Parse("12.03:04:05.006");
        var transaction = new DurationEditTransaction(original);

        transaction.UpdateComponents("20", "24", "4", "5", "6");

        Assert.Null(transaction.Candidate);
        Assert.Contains("Hours", transaction.FieldErrors.Keys);
        Assert.False(transaction.TryCommit(out _));

        transaction.Cancel();

        Assert.Equal(original, transaction.Candidate);
        Assert.Equal(original.ToString(), transaction.PrimaryDraft);
        Assert.Empty(transaction.FieldErrors);
    }

    [Fact]
    public void EditTransaction_ValidDraft_CommitsCandidateOnlyOnRequest()
    {
        var original = DurationValue.Parse("1.00:00:00");
        var transaction = new DurationEditTransaction(original);

        transaction.UpdatePrimaryDraft("2.03:04:05.006");

        Assert.Equal(original, transaction.Original);
        Assert.Equal(DurationValue.Parse("2.03:04:05.006"), transaction.Candidate);
        Assert.True(transaction.TryCommit(out var committed));
        Assert.Equal(DurationValue.Parse("2.03:04:05.006"), committed);
    }
}
