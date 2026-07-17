using Xunit;

namespace ServiceBusExplorer.UnitTests.Controls;

public class DurationConstraintTests
{
    [Fact]
    public void Validate_ValueWithinRange_ReturnsNoError()
    {
        var constraint = new DurationConstraint(
            "Queue LockDuration",
            DurationValue.Parse("0.00:00:05"),
            DurationValue.Parse("0.05:00:00"));

        Assert.Null(constraint.Validate(DurationValue.Parse("0.00:01:00")));
    }

    [Theory]
    [InlineData("0.00:00:04")]
    [InlineData("0.05:00:01")]
    public void Validate_ValueOutsideRange_NamesPropertyAndDoesNotClamp(string text)
    {
        var value = DurationValue.Parse(text);
        var constraint = new DurationConstraint(
            "Queue LockDuration",
            DurationValue.Parse("0.00:00:05"),
            DurationValue.Parse("0.05:00:00"));

        var error = constraint.Validate(value);

        Assert.Contains("Queue LockDuration", error);
        Assert.Equal(text, value.ToString());
    }

    [Fact]
    public void EditTransaction_ContextFailureRetainsRepresentableCandidate()
    {
        var original = DurationValue.Parse("0.00:01:00");
        var candidate = DurationValue.Parse("400.00:00:00");
        var constraint = new DurationConstraint(
            "Subscription LockDuration",
            Maximum: DurationValue.Parse("0.05:00:00"));
        var transaction = new DurationEditTransaction(original, constraint);

        transaction.UpdatePrimaryDraft(candidate.ToString());

        Assert.Equal(candidate, transaction.Candidate);
        Assert.Contains("Subscription LockDuration", transaction.ContextError);
        Assert.False(transaction.TryCommit(out _));
    }
}
