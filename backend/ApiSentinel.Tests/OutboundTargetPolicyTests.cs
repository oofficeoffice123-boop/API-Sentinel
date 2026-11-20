using System.Net;
using ApiSentinel.Api.Security;

namespace ApiSentinel.Tests;

public sealed class OutboundTargetPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.1.1")]
    [InlineData("198.18.0.1")]
    [InlineData("192.88.99.1")]
    [InlineData("192.0.2.1")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("64:ff9b::a00:1")]
    [InlineData("2002:7f00:1::")]
    [InlineData("2001:1ff::1")]
    [InlineData("2001:db8::1")]
    [InlineData("3fff::1")]
    [InlineData("3fff:0fff:ffff::1")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    public void Blocks_local_or_private_addresses(string value) => Assert.True(OutboundTargetPolicy.IsBlocked(IPAddress.Parse(value)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    public void Allows_public_addresses(string value) => Assert.False(OutboundTargetPolicy.IsBlocked(IPAddress.Parse(value)));

    [Theory]
    [InlineData("3ff0::1")]
    [InlineData("3ffe::1")]
    [InlineData("3fff:1000::1")]
    public void Does_not_overblock_adjacent_ipv6_unicast_space(string value) => Assert.False(OutboundTargetPolicy.IsBlocked(IPAddress.Parse(value)));

    [Fact]
    public void Blocks_private_ipv4_mapped_ipv6_addresses() => Assert.True(OutboundTargetPolicy.IsBlocked(IPAddress.Parse("::ffff:192.168.1.5")));
}