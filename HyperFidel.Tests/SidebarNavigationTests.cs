using HyperFidel;
using Xunit;

namespace HyperFidel.Tests;

/// <summary>Checks the sidebar rule that decides when a repeated click restores the top-level view.</summary>
public sealed class SidebarNavigationTests
{
    /// <summary>A repeated click restores the view only while settings or a drill-down is active.</summary>
    /// <param name="settingsOpen">Whether the embedded settings view is open.</param>
    /// <param name="albumFilterId">Active album filter, if any.</param>
    /// <param name="artistFilterId">Active artist filter, if any.</param>
    /// <param name="expected">Whether the top-level view must be restored.</param>
    [Theory]
    [InlineData(true, null, null, true)]
    [InlineData(false, 1L, null, true)]
    [InlineData(false, null, 1L, true)]
    [InlineData(false, null, null, false)]
    public void ShouldRestoreTopLevelView_OnlyWhenSettingsOrDrilldownIsActive(
        bool settingsOpen, long? albumFilterId, long? artistFilterId, bool expected) =>
        Assert.Equal(expected, MainWindow.ShouldRestoreTopLevelView(settingsOpen, albumFilterId, artistFilterId));
}
