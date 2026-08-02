using Azure.Messaging.ServiceBus;
using ServiceBusExplorer.Services;
using Xunit;

namespace ServiceBusExplorer.UnitTests.Messaging;

public class MessageSourceRoutingTests
{
    [Theory]
    [InlineData(MessageSource.Active, SubQueue.None)]
    [InlineData(MessageSource.DeadLetter, SubQueue.DeadLetter)]
    [InlineData(MessageSource.TransferDeadLetter, SubQueue.TransferDeadLetter)]
    public void Map_MapsEveryExplicitSource(MessageSource source, SubQueue expected)
    {
        Assert.Equal(expected, MessageSourceMapper.Map(source));
    }

    [Fact]
    public void Map_RejectsUnknownSource()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MessageSourceMapper.Map((MessageSource)int.MaxValue));
    }
}
