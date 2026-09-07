using System.Windows;
using System.Windows.Controls;
using HaelpMi.InstallCreator.Controls;
using Xunit;

namespace HaelpMi.InstallCreator.Tests;

public class LicenseTierPickerTests
{
    [Fact]
    public void NoTierSelectedByDefault()
    {
        StaFact.Run(() =>
        {
            var picker = new LicenseTierPicker();
            Assert.Null(picker.SelectedTier);
            Assert.Null(picker.UserLimit);
        });
    }

    [Fact]
    public void ComboBoxListsAllSixTiers()
    {
        StaFact.Run(() =>
        {
            var picker = new LicenseTierPicker();
            var comboBox = (ComboBox)picker.FindName("TierComboBox")!;
            var tiers = comboBox.ItemsSource!.Cast<LicenseTierOption>().Select(o => o.Tier).ToList();
            Assert.Equal(new[] { LicenseTier.Trial, LicenseTier.S, LicenseTier.M, LicenseTier.L, LicenseTier.XL, LicenseTier.Custom }, tiers);
        });
    }

    [Theory]
    [InlineData(LicenseTier.Trial, 10)]
    [InlineData(LicenseTier.S, 25)]
    [InlineData(LicenseTier.M, 75)]
    [InlineData(LicenseTier.L, 150)]
    [InlineData(LicenseTier.XL, null)]
    public void SettingSelectedTier_UpdatesUserLimit(LicenseTier tier, int? expectedLimit)
    {
        StaFact.Run(() =>
        {
            var picker = new LicenseTierPicker { SelectedTier = tier };
            Assert.Equal(expectedLimit, picker.UserLimit);
        });
    }

    [Fact]
    public void SettingSelectedTier_SelectsMatchingComboBoxItem()
    {
        StaFact.Run(() =>
        {
            var picker = new LicenseTierPicker { SelectedTier = LicenseTier.L };
            var comboBox = (ComboBox)picker.FindName("TierComboBox")!;
            Assert.Equal(LicenseTier.L, ((LicenseTierOption)comboBox.SelectedItem).Tier);
        });
    }

    [Fact]
    public void SelectingComboBoxItem_UpdatesSelectedTierAndUserLimit()
    {
        StaFact.Run(() =>
        {
            var picker = new LicenseTierPicker();
            var comboBox = (ComboBox)picker.FindName("TierComboBox")!;
            comboBox.SelectedItem = comboBox.ItemsSource!.Cast<LicenseTierOption>().Single(o => o.Tier == LicenseTier.XL);

            Assert.Equal(LicenseTier.XL, picker.SelectedTier);
            Assert.Null(picker.UserLimit);
        });
    }

    [Fact]
    public void ClearingSelectedTier_ClearsUserLimitAndComboBoxSelection()
    {
        StaFact.Run(() =>
        {
            var picker = new LicenseTierPicker { SelectedTier = LicenseTier.S };
            picker.SelectedTier = null;

            var comboBox = (ComboBox)picker.FindName("TierComboBox")!;
            Assert.Null(picker.UserLimit);
            Assert.Null(comboBox.SelectedItem);
        });
    }

    [Fact]
    public void SelectingCustom_ShowsLimitTextBoxAndUserLimitStaysNullUntilTyped()
    {
        StaFact.Run(() =>
        {
            var picker = new LicenseTierPicker { SelectedTier = LicenseTier.Custom };
            var textBox = (TextBox)picker.FindName("CustomLimitTextBox")!;

            Assert.Equal(Visibility.Visible, textBox.Visibility);
            Assert.Null(picker.UserLimit);
        });
    }

    [Fact]
    public void TypingIntoCustomLimitTextBox_UpdatesUserLimit()
    {
        StaFact.Run(() =>
        {
            var picker = new LicenseTierPicker { SelectedTier = LicenseTier.Custom };
            var textBox = (TextBox)picker.FindName("CustomLimitTextBox")!;

            textBox.Text = "2";

            Assert.Equal(2, picker.UserLimit);
        });
    }

    [Fact]
    public void TypingInvalidCustomLimit_LeavesUserLimitNull()
    {
        StaFact.Run(() =>
        {
            var picker = new LicenseTierPicker { SelectedTier = LicenseTier.Custom };
            var textBox = (TextBox)picker.FindName("CustomLimitTextBox")!;

            textBox.Text = "0";

            Assert.Null(picker.UserLimit);
        });
    }

    [Fact]
    public void SelectingNonCustomTier_HidesLimitTextBox()
    {
        StaFact.Run(() =>
        {
            var picker = new LicenseTierPicker { SelectedTier = LicenseTier.Custom };
            var textBox = (TextBox)picker.FindName("CustomLimitTextBox")!;

            picker.SelectedTier = LicenseTier.M;

            Assert.Equal(Visibility.Collapsed, textBox.Visibility);
            Assert.Equal(75, picker.UserLimit);
        });
    }
}
