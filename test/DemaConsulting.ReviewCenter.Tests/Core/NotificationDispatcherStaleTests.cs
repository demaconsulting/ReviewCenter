// Copyright (c) DEMA Consulting
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests for <see cref="NotificationDispatcher"/>: a result that was prepared for an address or a mode
///     that changed meanwhile is discarded and never recorded, and a mail is sent only to the address that is
///     confirmed at the moment of the send.
/// </summary>
public class NotificationDispatcherStaleTests
{
    /// <summary>
    ///     The address a replacement confirmation gives ann.
    /// </summary>
    private const string NewAddress = "ann.new@example.com";

    /// <summary>
    ///     Makes a world in which ann has a mail due: her window started and has elapsed.
    /// </summary>
    /// <returns>The world, ready for the pass that sends.</returns>
    private static async Task<NotificationDispatcherWorld> ArrangeDueAsync()
    {
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        await world.PassAsync();
        world.Advance(NotificationDispatcherWorld.DefaultWindow);
        return world;
    }

    /// <summary>
    ///     Test that no mail goes to an address that was replaced after the recipients were listed.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_AddressReplacedBeforeSend_NothingSent()
    {
        // Arrange: ann confirms a new address right after the pass listed her old one
        var world = await ArrangeDueAsync();
        var annId = world.IdOf("ann");
        world.Store.AfterListRecipients = () =>
        {
            world.Store.AfterListRecipients = null;
            world.Store.ReplaceConfirmedAddress(annId, NewAddress);
        };

        // Act
        var result = await world.PassAsync();

        // Assert: the old address got nothing and nothing was recorded
        Assert.Equal(new NotificationPassResult(0, 0, 0), result);
        Assert.Empty(world.Sender.Sent);
        Assert.Empty(world.Store.Log);
        Assert.Empty(world.Store.NotifiedOf(annId));
    }

    /// <summary>
    ///     Test that the mail that was held back goes to the new address in the next pass.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_AddressReplacedBeforeSend_NextPassMailsNewAddress()
    {
        // Arrange: the first pass drops its result because the address was replaced
        var world = await ArrangeDueAsync();
        var annId = world.IdOf("ann");
        world.Store.AfterListRecipients = () =>
        {
            world.Store.AfterListRecipients = null;
            world.Store.ReplaceConfirmedAddress(annId, NewAddress);
        };
        await world.PassAsync();

        // Act
        var result = await world.PassAsync();

        // Assert: the mail is sent, to the new address only
        Assert.Equal(new NotificationPassResult(1, 0, 0), result);
        Assert.Equal(NewAddress, Assert.Single(world.Sender.Sent).ToAddress);
    }

    /// <summary>
    ///     Test that a mail that was sent to an address replaced during the send is not recorded.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_AddressReplacedDuringSend_NothingRecorded()
    {
        // Arrange: ann confirms a new address while the mail is on its way
        var world = await ArrangeDueAsync();
        var annId = world.IdOf("ann");
        world.Sender.OnSend = _ => world.Store.ReplaceConfirmedAddress(annId, NewAddress);

        // Act
        await world.PassAsync();

        // Assert: neither the items nor the send log hold the stale result
        Assert.Empty(world.Store.Log);
        Assert.Empty(world.Store.NotifiedOf(annId));
    }

    /// <summary>
    ///     Test that a mode that was switched off and on during the send does not keep the stale result.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_ModeFlippedDuringSend_NothingRecorded()
    {
        // Arrange: ann switches the e-mail off and on again while the mail is on its way
        var world = await ArrangeDueAsync();
        var annId = world.IdOf("ann");
        world.Sender.OnSend = _ =>
        {
            world.Sender.OnSend = null;
            world.Store.SwitchMode(annId, NotificationMode.None);
            world.Store.SwitchMode(annId, NotificationMode.Every);
        };

        // Act
        await world.PassAsync();

        // Assert: the result of the earlier mode is not recorded
        Assert.Empty(world.Store.Log);
        Assert.Empty(world.Store.NotifiedOf(annId));
    }

    /// <summary>
    ///     Test that a mode that was switched off before the send keeps the mail from being sent.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_ModeSwitchedOffBeforeSend_NothingSent()
    {
        // Arrange: ann switches the e-mail off right after the pass listed her
        var world = await ArrangeDueAsync();
        var annId = world.IdOf("ann");
        world.Store.AfterListRecipients = () =>
        {
            world.Store.AfterListRecipients = null;
            world.Store.SwitchMode(annId, NotificationMode.None);
        };

        // Act
        await world.PassAsync();

        // Assert
        Assert.Empty(world.Sender.Sent);
    }
}
